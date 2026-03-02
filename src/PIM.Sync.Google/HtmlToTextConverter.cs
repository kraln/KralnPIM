using HtmlAgilityPack;

namespace PIM.Sync.Google;

public static class HtmlToTextConverter
{
    public static string Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        RemoveNodes(doc, "//script");
        RemoveNodes(doc, "//style");

        ReplaceImages(doc);
        ReplaceLinks(doc);

        var text = HtmlEntity.DeEntitize(doc.DocumentNode.InnerText);
        return NormalizeWhitespace(text);
    }

    private static void RemoveNodes(HtmlDocument doc, string xpath)
    {
        var nodes = doc.DocumentNode.SelectNodes(xpath);
        if (nodes is null) return;
        foreach (var node in nodes)
            node.Remove();
    }

    private static void ReplaceImages(HtmlDocument doc)
    {
        var images = doc.DocumentNode.SelectNodes("//img");
        if (images is null) return;
        foreach (var img in images)
        {
            var alt = img.GetAttributeValue("alt", "");
            var replacement = doc.CreateTextNode(
                string.IsNullOrEmpty(alt) ? "" : $"[{alt}]");
            img.ParentNode.ReplaceChild(replacement, img);
        }
    }

    private static void ReplaceLinks(HtmlDocument doc)
    {
        var links = doc.DocumentNode.SelectNodes("//a[@href]");
        if (links is null) return;
        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", "");
            var text = link.InnerText.Trim();
            if (string.IsNullOrEmpty(href) || string.IsNullOrEmpty(text))
                continue;

            var markdown = text == href ? href : $"[{text}]({href})";
            var replacement = doc.CreateTextNode(markdown);
            link.ParentNode.ReplaceChild(replacement, link);
        }
    }

    private static string NormalizeWhitespace(string text)
    {
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .ToList();

        // Collapse multiple blank lines into one
        var result = new List<string>();
        var lastWasBlank = true;
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                if (!lastWasBlank)
                    result.Add("");
                lastWasBlank = true;
            }
            else
            {
                result.Add(line);
                lastWasBlank = false;
            }
        }

        return string.Join("\n", result).Trim();
    }
}
