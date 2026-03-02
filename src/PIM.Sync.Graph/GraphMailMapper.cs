using Microsoft.Graph.Me.SendMail;
using Microsoft.Graph.Models;
using PIM.Core.Models;
using PimAttachmentInfo = PIM.Core.Models.AttachmentInfo;

namespace PIM.Sync.Graph;

public static class GraphMailMapper
{
    public static EmailHeader ToEmailHeader(Message msg, string accountId)
    {
        var fromAddress = msg.From?.EmailAddress?.Address ?? "";
        var fromDisplayName = msg.From?.EmailAddress?.Name ?? "";

        var toAddresses = msg.ToRecipients?
            .Where(r => !string.IsNullOrEmpty(r.EmailAddress?.Address))
            .Select(r => r.EmailAddress!.Address!)
            .ToList() ?? [];

        var ccAddresses = msg.CcRecipients?
            .Where(r => !string.IsNullOrEmpty(r.EmailAddress?.Address))
            .Select(r => r.EmailAddress!.Address!)
            .ToList() ?? [];

        var date = msg.ReceivedDateTime ?? DateTimeOffset.MinValue;

        var isRead = msg.IsRead ?? false;
        var isFlagged = msg.Flag?.FlagStatus == FollowupFlagStatus.Flagged;

        var folderId = msg.ParentFolderId ?? "INBOX";

        var attachments = msg.Attachments?
            .OfType<FileAttachment>()
            .Where(a => !string.IsNullOrEmpty(a.Name))
            .Select(a => new PimAttachmentInfo(
                Filename: a.Name!,
                ContentType: a.ContentType ?? "application/octet-stream",
                SizeBytes: a.Size ?? 0
            ))
            .ToList() ?? [];

        return new EmailHeader(
            MessageId: msg.Id ?? "",
            AccountId: accountId,
            FolderId: folderId,
            Subject: msg.Subject ?? "",
            FromAddress: fromAddress,
            FromDisplayName: fromDisplayName,
            ToAddresses: toAddresses,
            CcAddresses: ccAddresses,
            Date: date,
            IsRead: isRead,
            IsFlagged: isFlagged,
            PlainTextSnippet: msg.BodyPreview,
            Attachments: attachments
        );
    }

    public static SendMailPostRequestBody ToSendRequest(OutboundEmail email)
    {
        var message = new Message
        {
            Subject = email.Subject,
            Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = email.PlainTextBody,
            },
            ToRecipients = email.To.Select(ToRecipient).ToList(),
        };

        if (email.Cc.Count > 0)
            message.CcRecipients = email.Cc.Select(ToRecipient).ToList();

        if (email.Bcc.Count > 0)
            message.BccRecipients = email.Bcc.Select(ToRecipient).ToList();

        if (email.InReplyToMessageId is not null)
        {
            message.AdditionalData = new Dictionary<string, object>
            {
                ["internetMessageHeaders"] = new[]
                {
                    new { name = "In-Reply-To", value = email.InReplyToMessageId },
                },
            };
        }

        return new SendMailPostRequestBody
        {
            Message = message,
            SaveToSentItems = true,
        };
    }

    private static Recipient ToRecipient(string address) => new()
    {
        EmailAddress = new EmailAddress { Address = address },
    };
}
