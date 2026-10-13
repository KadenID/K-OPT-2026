using System.Security.Cryptography;
using System.Text;

namespace Kopt2026.Language;

public sealed record SourceDocument(string Text, string EncodingName, string Hash)
{
    public static SourceDocument FromText(string text, string encodingName = "UTF-8") =>
        new(text.Replace("\r\n", "\n").Replace('\r', '\n'), encodingName,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));

    public static SourceDocument FromBytes(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding;
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble())) encoding = new UTF8Encoding(true, true);
        else
        {
            try { _ = new UTF8Encoding(false, true).GetString(bytes); encoding = new UTF8Encoding(false, true); }
            catch (DecoderFallbackException) { encoding = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
        }
        var text = encoding.GetString(bytes);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        return FromText(text, encoding.CodePage == 949 ? "CP949" : "UTF-8");
    }
}
