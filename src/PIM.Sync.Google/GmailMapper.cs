using System.Globalization;
using System.Text.RegularExpressions;
using Google.Apis.Gmail.v1.Data;
using PIM.Core.Models;

namespace PIM.Sync.Google;

public static partial class GmailMapper
{
    public static EmailHeader ToEmailHeader(Message msg, string accountId)
    {
        var headers = msg.Payload?.Headers ?? [];

        var from = GetHeader(headers, "From") ?? "";
        var (fromAddress, fromDisplayName) = ParseSingleAddress(from);
        var to = ParseAddressList(GetHeader(headers, "To") ?? "");
        var cc = ParseAddressList(GetHeader(headers, "Cc") ?? "");
        var subject = GetHeader(headers, "Subject") ?? "";
        var dateStr = GetHeader(headers, "Date") ?? "";
        var date = ParseDate(dateStr);

        var labels = msg.LabelIds ?? [];
        var isRead = !labels.Contains("UNREAD");
        var isFlagged = labels.Contains("STARRED");
        var folderId = DetermineFolderId(labels);

        var attachments = ExtractAttachments(msg.Payload);

        return new EmailHeader(
            MessageId: msg.Id,
            AccountId: accountId,
            FolderId: folderId,
            Subject: subject,
            FromAddress: fromAddress,
            FromDisplayName: fromDisplayName,
            ToAddresses: to,
            CcAddresses: cc,
            Date: date,
            IsRead: isRead,
            IsFlagged: isFlagged,
            PlainTextSnippet: msg.Snippet,
            Attachments: attachments
        );
    }

    public static List<string> ParseAddressList(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return [];

        return header.Split(',')
            .Select(part => ParseSingleAddress(part.Trim()).Address)
            .Where(addr => !string.IsNullOrEmpty(addr))
            .ToList();
    }

    internal static (string Address, string DisplayName) ParseSingleAddress(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ("", "");

        raw = raw.Trim();

        // Format: "Display Name <email@example.com>"
        var match = AddressRegex().Match(raw);
        if (match.Success)
            return (match.Groups[2].Value.Trim(), match.Groups[1].Value.Trim().Trim('"'));

        // Format: bare email
        if (raw.Contains('@'))
            return (raw.Trim(), "");

        return ("", raw.Trim());
    }

    private static string? GetHeader(IList<MessagePartHeader> headers, string name) =>
        headers.FirstOrDefault(h =>
            string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static DateTimeOffset ParseDate(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return DateTimeOffset.MinValue;

        // Try standard parsing first
        if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var result))
            return result;

        // Gmail sometimes includes timezone abbreviations — strip them
        var cleaned = TimezoneAbbrRegex().Replace(dateStr, "").Trim();
        if (DateTimeOffset.TryParse(cleaned, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out result))
            return result;

        return DateTimeOffset.MinValue;
    }

    private static string DetermineFolderId(IList<string> labels)
    {
        // Priority order for folder determination
        string[] folderLabels = ["INBOX", "SENT", "DRAFT", "TRASH", "SPAM"];
        foreach (var label in folderLabels)
        {
            if (labels.Contains(label))
                return label;
        }

        return labels.FirstOrDefault(l =>
            l != "UNREAD" && l != "STARRED" && l != "IMPORTANT") ?? "INBOX";
    }

    private static List<AttachmentInfo> ExtractAttachments(MessagePart? payload)
    {
        var result = new List<AttachmentInfo>();
        if (payload is null) return result;

        CollectAttachments(payload, result);
        return result;
    }

    private static void CollectAttachments(MessagePart part, List<AttachmentInfo> result)
    {
        if (!string.IsNullOrEmpty(part.Filename) && part.Body?.AttachmentId is not null)
        {
            result.Add(new AttachmentInfo(
                Filename: part.Filename,
                ContentType: part.MimeType ?? "application/octet-stream",
                SizeBytes: part.Body.Size ?? 0
            ));
        }

        if (part.Parts is null) return;
        foreach (var child in part.Parts)
            CollectAttachments(child, result);
    }

    [GeneratedRegex(@"^(.*?)\s*<(.+?)>\s*$")]
    private static partial Regex AddressRegex();

    [GeneratedRegex(@"\s*\([A-Z]{2,5}\)\s*$")]
    private static partial Regex TimezoneAbbrRegex();
}
