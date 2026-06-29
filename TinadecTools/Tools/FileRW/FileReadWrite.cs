using System.Buffers;
using System.Text;
using Microsoft.Win32.SafeHandles;
using NLog;
using UtfUnknown;

namespace TinadecTools.Tools.FileRW;

/// <summary>
/// 一行的索引
/// </summary>
/// <param name="LineStart">本行开始的偏移</param>
/// <param name="LineEnd">本行结束的偏移（不包含换行符）</param>
/// <param name="NextStart">下一行开始的偏移</param>
internal record struct LineSpan(
    long LineStart,
    long LineEnd,
    long NextStart
);

/// <summary>
/// 一行的内容
/// </summary>
/// <param name="Content">具体内容（不包括换行符）</param>
/// <param name="LineNumber">行号</param>
/// <param name="LineLength">本行长度</param>
public record struct LineContent(
    string Content,
    int LineNumber,
    long LineLength
);

internal class FileAccessor : IDisposable
{
    private readonly FileStream file;
    private List<LineSpan> index = new();
    private readonly string filepath;
    private readonly SafeFileHandle handle;
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private static readonly UTF8Encoding utf8NoBom = new(false);

    static FileAccessor()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 打开一个文件。
    /// </summary>
    /// <param name="filepath">文件路径</param>
    internal FileAccessor(string filepath)
    {
        this.filepath = filepath;
        file = new FileStream(filepath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete, 1024,
            FileOptions.Asynchronous);
        handle = file.SafeFileHandle;
        normalizeEncodingToUtf8();
        normalizeLineEndings();
        buildIndex();
    }

    /// <summary>
    /// 打开文件时检测全文编码，并静默保存为 UTF-8（无 BOM）。
    /// </summary>
    private void normalizeEncodingToUtf8()
    {
        if (file.Length == 0)
            return;

        if (file.Length > int.MaxValue)
            throw new IOException($"文件 {filepath} 过大，无法一次性转换为 UTF-8");

        file.Seek(0, SeekOrigin.Begin);

        var raw = new byte[file.Length];
        var offset = 0;
        while (offset < raw.Length)
        {
            var read = file.Read(raw, offset, raw.Length - offset);
            if (read == 0)
                break;

            offset += read;
        }

        if (offset != raw.Length)
            Array.Resize(ref raw, offset);

        var detection = CharsetDetector.DetectFromBytes(raw);
        var encoding = detection.Detected?.Encoding ?? Encoding.UTF8;
        var text = encoding.GetString(raw);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];

        var utf8Bytes = utf8NoBom.GetBytes(text);
        if (raw.AsSpan().SequenceEqual(utf8Bytes))
        {
            file.Seek(0, SeekOrigin.Begin);
            return;
        }

        file.SetLength(0);
        file.Seek(0, SeekOrigin.Begin);
        file.Write(utf8Bytes, 0, utf8Bytes.Length);
        file.Flush();
        file.Seek(0, SeekOrigin.Begin);
    }

    /// <summary>
    /// 打开文件时统一换行符为 LF，避免后续按字节偏移读写时混用不同换行宽度。
    /// </summary>
    private void normalizeLineEndings()
    {
        if (file.Length == 0)
            return;

        file.Seek(0, SeekOrigin.Begin);

        using var normalized = new MemoryStream();
        var changed = false;
        var previousWasCr = false;

        while (true)
        {
            var b = file.ReadByte();
            if (b == -1)
                break;

            if (previousWasCr)
            {
                normalized.WriteByte((byte)'\n');
                if (b != '\n')
                    normalized.WriteByte((byte)b);

                previousWasCr = false;
                continue;
            }

            if (b == '\r')
            {
                previousWasCr = true;
                changed = true;
                continue;
            }

            normalized.WriteByte((byte)b);
        }

        if (previousWasCr)
        {
            normalized.WriteByte((byte)'\n');
        }

        if (!changed)
        {
            file.Seek(0, SeekOrigin.Begin);
            return;
        }

        file.SetLength(0);
        file.Seek(0, SeekOrigin.Begin);
        normalized.Position = 0;
        normalized.CopyTo(file);
        file.Flush();
        file.Seek(0, SeekOrigin.Begin);
    }

    /// <summary>
    /// 手动建立行号偏移索引
    /// </summary>
    private void buildIndex()
    {
        long currentOffset = 0;
        long lineStart = 0;

        while (true)
        {
            var b = file.ReadByte();
            if (b == -1) break;

            currentOffset++;

            if (b == '\n')
            {
                index.Add(new LineSpan(lineStart, currentOffset - 1, currentOffset));
                lineStart = currentOffset;
            }
        }

        if (lineStart < file.Length) index.Add(new LineSpan(lineStart, file.Length, file.Length));
    }

    /// <summary>
    /// 读取多个行范围（每个范围包括起始和结束行）
    /// </summary>
    /// <param name="lineRanges">行范围列表，Key=起始行，Value=结束行（都包括）</param>
    /// <returns></returns>
    public async Task<List<LineContent>> ReadLines(List<KeyValuePair<int, int>> lineRanges)
    {
        var contents = new List<LineContent>();

        foreach (var range in lineRanges)
        {
            var startLine = range.Key;
            var endLine = range.Value;

            // 验证范围
            if (startLine < 0 || startLine >= index.Count)
            {
                logger.Warn($"起始行号 {startLine} 超出范围 [0, {index.Count})");
                continue;
            }

            if (endLine < 0 || endLine >= index.Count)
            {
                logger.Warn($"结束行号 {endLine} 超出范围 [0, {index.Count})");
                continue;
            }

            if (startLine > endLine)
            {
                logger.Warn($"起始行号 {startLine} 大于结束行号 {endLine}");
                continue;
            }

            // 读取这个范围内的所有行
            for (var line = startLine; line <= endLine; line++)
            {
                var span = index[line];
                var length = span.LineEnd - span.LineStart;

                if (length <= 0)
                {
                    contents.Add(new LineContent(
                        string.Empty,
                        line,
                        0));
                    continue;
                }

                var buf = ArrayPool<byte>.Shared.Rent((int)length);
                try
                {
                    var memory = buf.AsMemory(0, (int)length);
                    var bytesRead =
                        await RandomAccess.ReadAsync(handle, memory, span.LineStart, CancellationToken.None);

                    if (bytesRead < length)
                        logger.Warn($"读取 {filepath} 偏移 {span.LineStart} 预期 {length} 字节，实际 {bytesRead}");

                    var content = Encoding.UTF8.GetString(buf, 0, bytesRead);
                    contents.Add(new LineContent(
                        content,
                        line,
                        bytesRead));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }
            }
        }

        return contents;
    }

    /// <summary>
    /// 替换单个连续范围的行（原地替换，要求字节数匹配）
    /// </summary>
    /// <param name="startLine">起始行号（包括）</param>
    /// <param name="endLine">结束行号（包括）</param>
    /// <param name="content">新内容，必须包含范围内所有行</param>
    public async Task<bool> ReplaceLines(int startLine, int endLine, List<LineContent> content)
    {
        // 验证范围
        if (startLine < 0 || startLine >= index.Count)
            throw new ArgumentOutOfRangeException(nameof(startLine), $"起始行号 {startLine} 超出范围 [0, {index.Count})");

        if (endLine < 0 || endLine >= index.Count)
            throw new ArgumentOutOfRangeException(nameof(endLine), $"结束行号 {endLine} 超出范围 [0, {index.Count})");

        if (startLine > endLine)
            throw new ArgumentException($"起始行号 {startLine} 不能大于结束行号 {endLine}");

        var expectedCount = endLine - startLine + 1;
        if (content.Count != expectedCount)
            throw new ArgumentException($"内容行数 {content.Count} 与范围行数 {expectedCount} 不匹配");

        // 验证内容中的行号是否连续且匹配范围
        if (content.Any(ct => ct.LineNumber < startLine || ct.LineNumber > endLine))
            throw new ArgumentOutOfRangeException(nameof(content), "内容中的行号超出指定范围");

        // 按行号排序（防止乱序）
        var sortedContent = content.OrderBy(ct => ct.LineNumber).ToList();

        // 验证行号连续性
        for (var i = 0; i < sortedContent.Count; i++)
            if (sortedContent[i].LineNumber != startLine + i)
                throw new ArgumentException($"内容行号不连续或不匹配，预期 {startLine + i}，实际 {sortedContent[i].LineNumber}");

        //检查：换行符！如果有就REJECT
        var bad = content
            .Where(c => c.Content.Contains('\n'))
            .Select(c => c.LineNumber)
            .ToList();

        if (bad.Count > 0)
            throw new ArgumentException(
                $"Line(s) [{string.Join(", ", bad)}] contain newlines",
                nameof(content));

        // 计算长度是否一致
        long writeLength = content.Select(ct => ct.LineLength).Sum();
        long currentLength = index[endLine].LineEnd - index[startLine].LineStart + 1;

        if (writeLength == currentLength)
        {
            // OK！直接全部覆盖
            string sooooooolong = string.Join('\n', content.Select(c => c.Content));
            byte[] strByBytes = Encoding.UTF8.GetBytes(sooooooolong);
            Memory<byte> buf = strByBytes.AsMemory();
            await RandomAccess.WriteAsync(handle, buf, index[startLine].LineStart, CancellationToken.None);
        }

        return true;
    }

    public void Dispose()
    {
        file.Dispose();
    }
}
