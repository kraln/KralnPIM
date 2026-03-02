using PIM.Sync.Google;

namespace PIM.Sync.Google.Tests;

public class HtmlToTextConverterTests
{
    [Fact]
    public void Convert_PlainText_PassesThrough()
    {
        var result = HtmlToTextConverter.Convert("Hello, World!");
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void Convert_BasicParagraphs_StripsTagsPreservesText()
    {
        var html = "<p>First paragraph.</p><p>Second paragraph.</p>";
        var result = HtmlToTextConverter.Convert(html);
        Assert.Contains("First paragraph.", result);
        Assert.Contains("Second paragraph.", result);
    }

    [Fact]
    public void Convert_DivElements_ExtractsText()
    {
        var html = "<div>Section one</div><div>Section two</div>";
        var result = HtmlToTextConverter.Convert(html);
        Assert.Contains("Section one", result);
        Assert.Contains("Section two", result);
    }

    [Fact]
    public void Convert_LinkPreservation_ConvertsToMarkdown()
    {
        var html = "<a href=\"https://example.com\">Click here</a>";
        var result = HtmlToTextConverter.Convert(html);
        Assert.Equal("[Click here](https://example.com)", result);
    }

    [Fact]
    public void Convert_LinkWithSameTextAsHref_OutputsUrlOnly()
    {
        var html = "<a href=\"https://example.com\">https://example.com</a>";
        var result = HtmlToTextConverter.Convert(html);
        Assert.Equal("https://example.com", result);
    }

    [Fact]
    public void Convert_ImageWithAltText_ExtractsAltInBrackets()
    {
        var html = "<img src=\"photo.jpg\" alt=\"A cat\" />";
        var result = HtmlToTextConverter.Convert(html);
        Assert.Equal("[A cat]", result);
    }

    [Fact]
    public void Convert_ImageWithoutAlt_RemovesCleanly()
    {
        var html = "<p>Before</p><img src=\"photo.jpg\" /><p>After</p>";
        var result = HtmlToTextConverter.Convert(html);
        Assert.Contains("Before", result);
        Assert.Contains("After", result);
        Assert.DoesNotContain("img", result);
        Assert.DoesNotContain("photo.jpg", result);
    }

    [Fact]
    public void Convert_ScriptTag_RemovesCompletely()
    {
        var html = "<p>Visible</p><script>alert('xss');</script><p>Also visible</p>";
        var result = HtmlToTextConverter.Convert(html);
        Assert.Contains("Visible", result);
        Assert.Contains("Also visible", result);
        Assert.DoesNotContain("alert", result);
        Assert.DoesNotContain("script", result);
    }

    [Fact]
    public void Convert_StyleTag_RemovesCompletely()
    {
        var html = "<style>.foo { color: red; }</style><p>Content</p>";
        var result = HtmlToTextConverter.Convert(html);
        Assert.Equal("Content", result);
    }

    [Fact]
    public void Convert_HtmlEntities_Decodes()
    {
        var html = "<p>A &amp; B &lt; C</p>";
        var result = HtmlToTextConverter.Convert(html);
        Assert.Equal("A & B < C", result);
    }

    [Fact]
    public void Convert_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", HtmlToTextConverter.Convert(""));
    }

    [Fact]
    public void Convert_NullString_ReturnsEmpty()
    {
        Assert.Equal("", HtmlToTextConverter.Convert(null!));
    }

    [Fact]
    public void Convert_WhitespaceOnly_ReturnsEmpty()
    {
        Assert.Equal("", HtmlToTextConverter.Convert("   \n\t  "));
    }

    [Fact]
    public void Convert_ComplexEmail_HandlesAllFeatures()
    {
        var html = """
            <html>
            <head><style>body { font-family: Arial; }</style></head>
            <body>
                <p>Hello <b>World</b>,</p>
                <p>Check out <a href="https://example.com">this link</a>.</p>
                <img src="logo.png" alt="Company Logo" />
                <script>trackPageView();</script>
                <p>Thanks!</p>
            </body>
            </html>
            """;

        var result = HtmlToTextConverter.Convert(html);

        Assert.Contains("Hello", result);
        Assert.Contains("World", result);
        Assert.Contains("[this link](https://example.com)", result);
        Assert.Contains("[Company Logo]", result);
        Assert.Contains("Thanks!", result);
        Assert.DoesNotContain("trackPageView", result);
        Assert.DoesNotContain("font-family", result);
    }

    [Fact]
    public void Convert_MultipleBlankLines_CollapsedToOne()
    {
        var html = "<p>Line 1</p>\n\n\n\n<p>Line 2</p>";
        var result = HtmlToTextConverter.Convert(html);

        // Should not have more than one consecutive empty line
        Assert.DoesNotContain("\n\n\n", result);
    }

    [Fact]
    public void Convert_NestedTags_ExtractsDeepText()
    {
        var html = "<div><span><b><i>Deep text</i></b></span></div>";
        var result = HtmlToTextConverter.Convert(html);
        Assert.Equal("Deep text", result);
    }
}
