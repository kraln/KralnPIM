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
        message.From.Add(MailboxAddress.Parse(email.FromAccountId));

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
        if (message.TextBody is not null)
            return message.TextBody;

        if (message.HtmlBody is not null)
            return HtmlToTextConverter.Convert(message.HtmlBody);

        return "";
    }

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
