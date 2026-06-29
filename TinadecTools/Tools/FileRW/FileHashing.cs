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
    private const string nibble_string = "ZPMQVRWSNKTXJBYH";

    private static string getHashLineDict(int index)
    {
        int high = index >> 4;
        int low = index & 0x0F;
        return nibble_string[high].ToString() + nibble_string[low];
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
        string normalized = line.Replace("\r", "").TrimEnd();
        bool match = Regex.IsMatch(normalized, @"/[\p{L}\p{N}]/u"); // 是不是有字母或者数字
        if (!match && (linenumber is null))
            throw new ArgumentNullException(nameof(linenumber));

        Debug.Assert(linenumber != null, nameof(linenumber) + " != null");
        int seed = match ? 0 : linenumber.Value;
        var hash = (int)XxHash32.HashToUInt32(Encoding.UTF8.GetBytes(line), seed);
        return getHashLineDict(hash % 256);
    }
}
