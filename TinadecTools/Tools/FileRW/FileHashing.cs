using System.Diagnostics;
using System.IO.Hashing;
using System.Text;
using System.Text.RegularExpressions;

namespace TinadecTools.Tools.FileRW;

/// <summary>
/// 用于生成逐行双字母HASH或全文件4字母HASH的工具类
/// </summary>
internal static class FileHashing
{
    private const string NibbleString = "ZPMQVRWSNKTXJBYH";

    private static string GetHashLineDict(int index)
    {
        var high = index >> 4;
        var low = index & 0x0F;
        return NibbleString[high].ToString() + NibbleString[low];
    }

    private static string GetHashFileDict(int index)
    {
        return GetHashLineDict((index >> 8) & 0xFF) + GetHashLineDict(index & 0xFF);
    }

    /// <summary>
    /// 用于计算单行的哈希
    /// </summary>
    /// <param name="line">
    /// 这一行的内容（**必须**是UTF-8编码）
    /// </param>
    /// <param name="linenumber">
    /// 行号（可选）
    /// </param>
    /// <returns>
    /// 双字符ID
    /// </returns>
    public static string ComputeLineHash(string line, int? linenumber)
    {
        var normalized = line.Replace("\r", "").TrimEnd();
        var match = Regex.IsMatch(normalized, @"/[\p{L}\p{N}]/u"); // 是不是有字母或者数字
        if (!match && linenumber is null)
            throw new ArgumentNullException(nameof(linenumber));

        Debug.Assert(linenumber != null, nameof(linenumber) + " != null");
        var seed = match ? 0 : linenumber.Value;
        //实验性功能，如果不成就回退到原版
        var hash = XxHash32.HashToUInt32(Encoding.UTF8.GetBytes(line), linenumber.GetValueOrDefault(0));
        // var hash = (int)XxHash32.HashToUInt32(Encoding.UTF8.GetBytes(line), seed);
        return GetHashLineDict((int)(hash & 0xFF));
    }

    public static string ComputeFileHash(ReadOnlySpan<byte> content)
    {
        var hash = XxHash32.HashToUInt32(content);
        return GetHashFileDict((int)(hash & 0xFFFF));
    }
}
