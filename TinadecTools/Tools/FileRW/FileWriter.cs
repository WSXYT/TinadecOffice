using System.Text;
using System.Text.Json.Serialization;
using NLog;
using TinadecTools.Abstractions;

namespace TinadecTools.Tools.FileRW;

public sealed class HashedLineContent
{
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("hash")] public string Hash { get; set; } = string.Empty;
}

public sealed class ReplaceLinesParams
{
    [JsonPropertyName("filepath")] public string FilePath { get; set; } = string.Empty;
    [JsonPropertyName("start_row")] public int StartRow { get; set; }
    [JsonPropertyName("end_row")] public int EndRow { get; set; }
    [JsonPropertyName("content")] public List<HashedLineContent> Content { get; set; } = new();
}

public sealed class FileHashMutationParams
{
    [JsonPropertyName("filepath")] public string FilePath { get; set; } = string.Empty;
    [JsonPropertyName("start_offset")] public long StartOffset { get; set; }
    [JsonPropertyName("length")] public long Length { get; set; }
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("file_hash")] public string FileHash { get; set; } = string.Empty;
}

public sealed class InsertLineParams
{
    [JsonPropertyName("filepath")] public string FilePath { get; set; } = string.Empty;
    [JsonPropertyName("line_number")] public int LineNumber { get; set; }
    [JsonPropertyName("position")] public string Position { get; set; } = "after";
    [JsonPropertyName("content")] public List<string> Content { get; set; } = new();
    [JsonPropertyName("file_hash")] public string FileHash { get; set; } = string.Empty;
}

public sealed class DeleteLineParams
{
    [JsonPropertyName("filepath")] public string FilePath { get; set; } = string.Empty;
    [JsonPropertyName("start_row")] public int StartRow { get; set; }
    [JsonPropertyName("end_row")] public int EndRow { get; set; }
    [JsonPropertyName("file_hash")] public string FileHash { get; set; } = string.Empty;
}

public sealed class FileMutationResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("file_hash")] public string FileHash { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(HashedLineContent))]
[JsonSerializable(typeof(ReplaceLinesParams))]
[JsonSerializable(typeof(FileHashMutationParams))]
[JsonSerializable(typeof(InsertLineParams))]
[JsonSerializable(typeof(DeleteLineParams))]
[JsonSerializable(typeof(FileMutationResponse))]
internal partial class FileWriterJsonContext : JsonSerializerContext;

public static class FileWriter
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    [ToolFunction("replace_lines")]
    public static async ValueTask<FileMutationResponse> ReplaceLinesAsync(
        ReplaceLinesParams args,
        CancellationToken cancellationToken)
    {
        return await MutateAsync(args.FilePath, "replace_lines", async slot =>
        {
            var startLine = ToZeroBasedLine(args.StartRow, nameof(args.StartRow));
            var endLine = ToZeroBasedLine(args.EndRow, nameof(args.EndRow));
            if (startLine > endLine)
                throw new ArgumentException("start_row must be less than or equal to end_row.");

            var expectedCount = endLine - startLine + 1;
            if (args.Content.Count != expectedCount)
                throw new ArgumentException($"content count {args.Content.Count} does not match target line count {expectedCount}.");

            var currentLines = await slot.File.ReadLines([new KeyValuePair<int, int>(startLine, endLine)])
                .ConfigureAwait(false);
            if (currentLines.Count != expectedCount)
                throw new InvalidOperationException("target lines could not be read for hash validation.");

            for (var i = 0; i < currentLines.Count; i++)
            {
                var expectedHash = args.Content[i].Hash;
                var actualLine = new LineContent(currentLines[i].Content, currentLines[i].LineNumber + 1, currentLines[i].LineLength);
                var actualHash = BuildLineHash(actualLine);
                if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
                    throw new InvalidOperationException($"REJECT line {actualLine.LineNumber}: hash mismatch, expected {expectedHash}, actual {actualHash}.");
            }

            await slot.File.ReplaceLines(startLine, endLine, args.Content.Select(line => line.Content).ToArray())
                .ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    [ToolFunction("replace_bytes")]
    public static async ValueTask<FileMutationResponse> ReplaceBytesAsync(
        FileHashMutationParams args,
        CancellationToken cancellationToken)
    {
        return await MutateWithFileHashAsync(args.FilePath, args.FileHash, "replace_bytes", async slot =>
        {
            await slot.File.ReplaceBytes(args.StartOffset, args.Length, Encoding.UTF8.GetBytes(args.Content))
                .ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    [ToolFunction("insert_bytes")]
    public static async ValueTask<FileMutationResponse> InsertBytesAsync(
        FileHashMutationParams args,
        CancellationToken cancellationToken)
    {
        return await MutateWithFileHashAsync(args.FilePath, args.FileHash, "insert_bytes", async slot =>
        {
            await slot.File.InsertBytes(args.StartOffset, Encoding.UTF8.GetBytes(args.Content))
                .ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    [ToolFunction("insert_byte")]
    public static async ValueTask<FileMutationResponse> InsertByteAsync(
        FileHashMutationParams args,
        CancellationToken cancellationToken)
    {
        return await InsertBytesAsync(args, cancellationToken).ConfigureAwait(false);
    }

    [ToolFunction("delete_bytes")]
    public static async ValueTask<FileMutationResponse> DeleteBytesAsync(
        FileHashMutationParams args,
        CancellationToken cancellationToken)
    {
        return await MutateWithFileHashAsync(args.FilePath, args.FileHash, "delete_bytes", async slot =>
        {
            await slot.File.DeleteBytes(args.StartOffset, args.Length).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    [ToolFunction("insert_line")]
    public static async ValueTask<FileMutationResponse> InsertLineAsync(
        InsertLineParams args,
        CancellationToken cancellationToken)
    {
        return await MutateWithFileHashAsync(args.FilePath, args.FileHash, "insert_line", async slot =>
        {
            var lineNumber = ToZeroBasedLine(args.LineNumber, nameof(args.LineNumber));
            if (string.Equals(args.Position, "before", StringComparison.OrdinalIgnoreCase))
            {
                await slot.File.InsertLinesBeforeLine(lineNumber, args.Content).ConfigureAwait(false);
                return;
            }

            if (string.Equals(args.Position, "after", StringComparison.OrdinalIgnoreCase))
            {
                await slot.File.InsertLinesAfterLine(lineNumber, args.Content).ConfigureAwait(false);
                return;
            }

            throw new ArgumentException("position must be 'before' or 'after'.");
        }, cancellationToken).ConfigureAwait(false);
    }

    [ToolFunction("delete_line")]
    public static async ValueTask<FileMutationResponse> DeleteLineAsync(
        DeleteLineParams args,
        CancellationToken cancellationToken)
    {
        return await MutateWithFileHashAsync(args.FilePath, args.FileHash, "delete_line", async slot =>
        {
            var startLine = ToZeroBasedLine(args.StartRow, nameof(args.StartRow));
            var endLine = ToZeroBasedLine(args.EndRow, nameof(args.EndRow));
            await slot.File.DeleteLines(startLine, endLine).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<FileMutationResponse> MutateWithFileHashAsync(
        string filePath,
        string expectedFileHash,
        string operation,
        Func<FileSlot, Task> action,
        CancellationToken cancellationToken)
    {
        return await MutateAsync(filePath, operation, async slot =>
        {
            var actualFileHash = await slot.File.ComputeFileHashAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(expectedFileHash, actualFileHash, StringComparison.Ordinal))
                throw new InvalidOperationException($"REJECT {operation}: file_hash mismatch, expected {expectedFileHash}, actual {actualFileHash}.");

            await action(slot).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<FileMutationResponse> MutateAsync(
        string filePath,
        string operation,
        Func<FileSlot, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = FileToolRuntime.ResolvePath(filePath);
            var slot = FileToolRuntime.GetFileHandle(path);
            using (await slot.RwLock.WriteLockAsync(cancellationToken).ConfigureAwait(false))
            {
                await action(slot).ConfigureAwait(false);
                var newFileHash = await slot.File.ComputeFileHashAsync(cancellationToken).ConfigureAwait(false);
                Logger.Debug("{operation}写入{path}成功，新file_hash为{fileHash}", operation, path, newFileHash);
                return new FileMutationResponse { Success = true, FileHash = newFileHash };
            }
        }
        catch (OperationCanceledException ex)
        {
            Logger.Warn(ex, "{operation}被取消，文件为{path}", operation, filePath);
            return new FileMutationResponse { Success = false, Error = $"{operation} canceled." };
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "{operation}失败，文件为{path}", operation, filePath);
            return new FileMutationResponse { Success = false, Error = ex.Message };
        }
    }

    private static int ToZeroBasedLine(int lineNumber, string parameterName)
    {
        if (lineNumber < 1)
            throw new ArgumentOutOfRangeException(parameterName, "line number must be 1 or greater.");

        return lineNumber - 1;
    }

    private static string BuildLineHash(LineContent line)
    {
        return line.LineNumber + "|" + FileHashing.ComputeLineHash(line.Content, line.LineNumber);
    }
}
