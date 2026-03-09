using MailKit;
using MimeKit;
using PIM.Core.Models;

namespace PIM.Sync.Imap;

public static class ImapMailMapper
{
    public static EmailHeader ToEmailHeader(IMessageSummary summary, string accountId, string folderId)
    {
        var envelope = summary.Envelope;
        var from = envelope?.From?.Mailboxes.FirstOrDefault();
        var to = envelope?.To?.Mailboxes.Select(m => m.Address).ToList() ?? [];
        var cc = envelope?.Cc?.Mailboxes.Select(m => m.Address).ToList() ?? [];
        var date = envelope?.Date ?? DateTimeOffset.MinValue;
        var subject = envelope?.Subject ?? "";

        var flags = summary.Flags ?? MessageFlags.None;
        var isRead = flags.HasFlag(MessageFlags.Seen);
        var isFlagged = flags.HasFlag(MessageFlags.Flagged);

        return new EmailHeader(
            MessageId: summary.UniqueId.Id.ToString(),
            AccountId: accountId,
            FolderId: folderId,
            Subject: subject,
            FromAddress: from?.Address ?? "",
            FromDisplayName: from?.Name ?? "",
            ToAddresses: to,
            CcAddresses: cc,
            Date: date,
            IsRead: isRead,
            IsFlagged: isFlagged,
            PlainTextSnippet: null,
            Attachments: ExtractAttachmentsFromSummary(summary)
        );
    }

    public static EmailHeader FromMimeMessage(
        MimeMessage message, uint uid, string accountId, string folderId, MessageFlags flags)
    {
        var from = message.From.Mailboxes.FirstOrDefault();
        var to = message.To.Mailboxes.Select(m => m.Address).ToList();
        var cc = message.Cc.Mailboxes.Select(m => m.Address).ToList();

        var bodyText = ExtractPlainText(message);
        var snippet = GenerateSnippet(bodyText);

        return new EmailHeader(
            MessageId: uid.ToString(),
            AccountId: accountId,
            FolderId: folderId,
            Subject: message.Subject ?? "",
            FromAddress: from?.Address ?? "",
            FromDisplayName: from?.Name ?? "",
            ToAddresses: to,
            CcAddresses: cc,
            Date: message.Date,
            IsRead: flags.HasFlag(MessageFlags.Seen),
            IsFlagged: flags.HasFlag(MessageFlags.Flagged),
            PlainTextSnippet: snippet,
            Attachments: ExtractAttachments(message)
        );
    }

    public static MimeMessage ToMimeMessage(OutboundEmail email)
    {
        var message = new MimeMessage();
        message.From.Add(email.FromDisplayName is not null
            ? new MailboxAddress(email.FromDisplayName, email.FromAccountId)
            : MailboxAddress.Parse(email.FromAccountId));

        foreach (var to in email.To)
            message.To.Add(MailboxAddress.Parse(to));
        foreach (var cc in email.Cc)
            message.Cc.Add(MailboxAddress.Parse(cc));
        foreach (var bcc in email.Bcc)
            message.Bcc.Add(MailboxAddress.Parse(bcc));

        message.Subject = email.Subject;

        if (email.InReplyToMessageId is not null)
            message.InReplyTo = email.InReplyToMessageId;

        message.Body = new TextPart("plain") { Text = email.PlainTextBody };

        return message;
    }

    public static string ExtractPlainText(MimeMessage message)
    {
        // MailKit's TextBody decodes content-transfer-encoding automatically.
        // But as a safety net, strip any residual QP artifacts.
        if (message.TextBody is not null)
            return StripQuotedPrintableArtifacts(message.TextBody);

        if (message.HtmlBody is not null)
            return HtmlToTextConverter.Convert(message.HtmlBody);

        return "";
    }

    /// <summary>
    /// Some messages arrive with QP artifacts still present (malformed senders,
    /// or edge cases in MIME parsing). Detect and clean them up.
    /// </summary>
    private static string StripQuotedPrintableArtifacts(string text)
    {
        // Quick check: if no QP soft line breaks, return as-is
        if (!text.Contains("=\r\n") && !text.Contains("=\n") && !text.Contains("=C2=") && !text.Contains("=c2="))
            return text;

        return DecodeQuotedPrintable(text);
    }

    private static string DecodeQuotedPrintable(string input)
    {
        var bytes = new List<byte>(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '=' && i + 1 < input.Length)
            {
                // Soft line break: =\r\n or =\n
                if (input[i + 1] == '\r' && i + 2 < input.Length && input[i + 2] == '\n')
                {
                    i += 2;
                    continue;
                }
                if (input[i + 1] == '\n')
                {
                    i++;
                    continue;
                }
                // Hex pair
                if (i + 2 < input.Length && IsHex(input[i + 1]) && IsHex(input[i + 2]))
                {
                    bytes.Add((byte)((HexVal(input[i + 1]) << 4) | HexVal(input[i + 2])));
                    i += 2;
                    continue;
                }
            }
            // Regular character — encode as UTF-8 bytes
            if (input[i] < 128)
            {
                bytes.Add((byte)input[i]);
            }
            else
            {
                foreach (var b in System.Text.Encoding.UTF8.GetBytes(input, i, 1))
                    bytes.Add(b);
            }
        }
        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool IsHex(char c) =>
        c is (>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f');

    private static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => 0,
    };

    public static string? GenerateSnippet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text.Length <= 200 ? text : text[..200];
    }

    public static List<AttachmentInfo> ExtractAttachments(MimeMessage message)
    {
        var result = new List<AttachmentInfo>();

        foreach (var attachment in message.Attachments.OfType<MimePart>())
        {
            var filename = attachment.FileName ?? "unknown";
            var contentType = attachment.ContentType.MimeType;

            long size = 0;
            if (attachment.Content is not null)
            {
                using var stream = new MemoryStream();
                attachment.Content.DecodeTo(stream);
                size = stream.Length;
            }

            result.Add(new AttachmentInfo(filename, contentType, size));
        }

        return result;
    }

    private static List<AttachmentInfo> ExtractAttachmentsFromSummary(IMessageSummary summary)
    {
        var result = new List<AttachmentInfo>();
        if (summary.Body is null) return result;

        CollectAttachmentsFromBody(summary.Body, result);
        return result;
    }

    private static void CollectAttachmentsFromBody(BodyPart bodyPart, List<AttachmentInfo> result)
    {
        if (bodyPart is BodyPartBasic basic && basic.IsAttachment)
        {
            result.Add(new AttachmentInfo(
                Filename: basic.FileName ?? "unknown",
                ContentType: basic.ContentType.MimeType,
                SizeBytes: basic.Octets
            ));
        }

        if (bodyPart is BodyPartMultipart multipart)
        {
            foreach (var child in multipart.BodyParts)
                CollectAttachmentsFromBody(child, result);
        }
    }
}
