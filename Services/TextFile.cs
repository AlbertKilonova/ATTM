using System.IO;
using System.Text;

namespace ATTM.Services;

/// <summary>
/// 文本读写：读的时候做编码探测（BOM → UTF-8 → GB18030），写的时候统一 UTF-8 无 BOM + LF。
/// </summary>
public static class TextFile
{
    static TextFile()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
        catch { /* 没有代码页支持就只用 UTF-8 */ }
    }

    public static string Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0) return string.Empty;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // 没有 BOM：先按严格 UTF-8 试，解不出来再按 GB18030（老 txt 常见）
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // 继续尝试 GB18030
        }

        try
        {
            var gb = Encoding.GetEncoding(54936);
            return gb.GetString(bytes);
        }
        catch (Exception)
        {
            // 代码页不可用，退回 UTF-8 宽松解码
        }

        return Encoding.UTF8.GetString(bytes);
    }

    public static void Write(string path, string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        File.WriteAllText(path, normalized, new UTF8Encoding(false));
    }
}
