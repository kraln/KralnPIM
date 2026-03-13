using System.Text;
using Google.Apis.Gmail.v1.Data;

namespace PIM.Sync.Google.Tests;

public class BodyDecodingTests
{
    public BodyDecodingTests()
    {
        GoogleMailProvider.EnsureCodePages();
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');

    private static MessagePart MakePart(byte[] rawBytes, string charset, string mimeType = "text/plain")
    {
        return new MessagePart
        {
            MimeType = mimeType,
            Body = new MessagePartBody { Data = ToBase64Url(rawBytes) },
            Headers = new[]
            {
                new MessagePartHeader { Name = "Content-Type", Value = $"{mimeType}; charset={charset}" }
            }
        };
    }

    // --- GetCharset ---

    [Fact]
    public void GetCharset_ReturnsUtf8_WhenNoHeaders()
    {
        var part = new MessagePart { Headers = null };
        Assert.Equal("utf-8", GoogleMailProvider.GetCharset(part));
    }

    [Fact]
    public void GetCharset_ReturnsUtf8_WhenNoContentTypeHeader()
    {
        var part = new MessagePart
        {
            Headers = new[] { new MessagePartHeader { Name = "Subject", Value = "test" } }
        };
        Assert.Equal("utf-8", GoogleMailProvider.GetCharset(part));
    }

    [Theory]
    [InlineData("text/plain; charset=windows-1252", "windows-1252")]
    [InlineData("text/plain; charset=\"iso-8859-1\"", "iso-8859-1")]
    [InlineData("text/html; charset=UTF-8", "UTF-8")]
    [InlineData("text/plain; charset=us-ascii", "us-ascii")]
    [InlineData("text/plain; charset=\"Windows-1251\"", "Windows-1251")]
    [InlineData("text/plain; charset=iso-8859-15; format=flowed", "iso-8859-15")]
    public void GetCharset_ParsesCharsetFromContentType(string contentType, string expected)
    {
        var part = new MessagePart
        {
            Headers = new[]
            {
                new MessagePartHeader { Name = "Content-Type", Value = contentType }
            }
        };
        Assert.Equal(expected, GoogleMailProvider.GetCharset(part));
    }

    [Fact]
    public void GetCharset_ReturnsUtf8_WhenNoCharsetInContentType()
    {
        var part = new MessagePart
        {
            Headers = new[]
            {
                new MessagePartHeader { Name = "Content-Type", Value = "text/plain" }
            }
        };
        Assert.Equal("utf-8", GoogleMailProvider.GetCharset(part));
    }

    // --- DecodePartBody with various charsets ---

    [Fact]
    public void DecodePartBody_Utf8()
    {
        var text = "Hello, world! Ünïcödé";
        var part = MakePart(Encoding.UTF8.GetBytes(text), "utf-8");
        Assert.Equal(text, GoogleMailProvider.DecodePartBody(part));
    }

    [Fact]
    public void DecodePartBody_Windows1252()
    {
        // Windows-1252 has characters like curly quotes and em-dash
        var enc = Encoding.GetEncoding("windows-1252");
        // \x93 = left double quote, \x94 = right double quote, \x97 = em-dash
        var raw = new byte[] { 0x93, 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x94, 0x97, 0x77, 0x6F, 0x72, 0x6C, 0x64 };
        var expected = enc.GetString(raw); // "\u201CHello\u201D\u2014world"

        var part = MakePart(raw, "windows-1252");
        Assert.Equal(expected, GoogleMailProvider.DecodePartBody(part));
    }

    [Fact]
    public void DecodePartBody_Iso8859_1()
    {
        var enc = Encoding.GetEncoding("iso-8859-1");
        var text = "café résumé naïve";
        var raw = enc.GetBytes(text);

        var part = MakePart(raw, "iso-8859-1");
        Assert.Equal(text, GoogleMailProvider.DecodePartBody(part));
    }

    [Fact]
    public void DecodePartBody_Iso8859_15()
    {
        // ISO-8859-15 includes the Euro sign at 0xA4
        var enc = Encoding.GetEncoding("iso-8859-15");
        var raw = new byte[] { 0x50, 0x72, 0x69, 0x63, 0x65, 0x3A, 0x20, 0xA4, 0x31, 0x30 }; // "Price: €10"
        var expected = enc.GetString(raw);

        var part = MakePart(raw, "iso-8859-15");
        Assert.Equal(expected, GoogleMailProvider.DecodePartBody(part));
    }

    [Fact]
    public void DecodePartBody_Windows1251_Cyrillic()
    {
        var enc = Encoding.GetEncoding("windows-1251");
        // "Привет" in Windows-1251
        var raw = new byte[] { 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2 };
        var expected = enc.GetString(raw);

        var part = MakePart(raw, "windows-1251");
        Assert.Equal(expected, GoogleMailProvider.DecodePartBody(part));
    }

    [Fact]
    public void DecodePartBody_UsAscii()
    {
        var text = "Plain ASCII text";
        var raw = Encoding.ASCII.GetBytes(text);

        var part = MakePart(raw, "us-ascii");
        Assert.Equal(text, GoogleMailProvider.DecodePartBody(part));
    }

    [Fact]
    public void DecodePartBody_QuotedCharset()
    {
        // charset="windows-1252" (with quotes in Content-Type)
        var enc = Encoding.GetEncoding("windows-1252");
        var raw = new byte[] { 0x93, 0x74, 0x65, 0x73, 0x74, 0x94 }; // "test" in curly quotes
        var expected = enc.GetString(raw);

        var part = new MessagePart
        {
            MimeType = "text/plain",
            Body = new MessagePartBody { Data = ToBase64Url(raw) },
            Headers = new[]
            {
                new MessagePartHeader
                {
                    Name = "Content-Type",
                    Value = "text/plain; charset=\"windows-1252\""
                }
            }
        };
        Assert.Equal(expected, GoogleMailProvider.DecodePartBody(part));
    }

    [Fact]
    public void DecodePartBody_DefaultsToUtf8_WhenNoCharset()
    {
        var text = "No charset specified";
        var raw = Encoding.UTF8.GetBytes(text);

        var part = new MessagePart
        {
            MimeType = "text/plain",
            Body = new MessagePartBody { Data = ToBase64Url(raw) },
            Headers = new[]
            {
                new MessagePartHeader { Name = "Content-Type", Value = "text/plain" }
            }
        };
        Assert.Equal(text, GoogleMailProvider.DecodePartBody(part));
    }

    // --- DecodeBase64UrlBytes ---

    [Fact]
    public void DecodeBase64UrlBytes_HandlesUrlSafeChars()
    {
        // Standard base64 uses +/ ; base64url uses -_
        var original = new byte[] { 0xFF, 0xFE, 0xFD };
        var base64Url = Convert.ToBase64String(original).Replace('+', '-').Replace('/', '_');

        var decoded = GoogleMailProvider.DecodeBase64UrlBytes(base64Url);
        Assert.Equal(original, decoded);
    }
}
