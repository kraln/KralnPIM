using Microsoft.Graph.Models;
using PIM.Core.Models;
using PIM.Sync.Graph;

namespace PIM.Sync.Graph.Tests;

public class GraphMailMapperTests
{
    private const string AccountId = "test-account";

    [Fact]
    public void ToEmailHeader_BasicMessage_MapsCorrectly()
    {
        var msg = CreateMessage(
            id: "msg1",
            subject: "Test Subject",
            fromAddress: "alice@example.com",
            fromName: "Alice Smith",
            toAddresses: ["bob@example.com"],
            ccAddresses: ["carol@example.com"],
            receivedDateTime: new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
            isRead: true,
            isFlagged: false,
            bodyPreview: "This is a preview...",
            parentFolderId: "inbox-id"
        );

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal("msg1", result.MessageId);
        Assert.Equal(AccountId, result.AccountId);
        Assert.Equal("inbox-id", result.FolderId);
        Assert.Equal("Test Subject", result.Subject);
        Assert.Equal("alice@example.com", result.FromAddress);
        Assert.Equal("Alice Smith", result.FromDisplayName);
        Assert.Single(result.ToAddresses);
        Assert.Equal("bob@example.com", result.ToAddresses[0]);
        Assert.Single(result.CcAddresses);
        Assert.Equal("carol@example.com", result.CcAddresses[0]);
        Assert.True(result.IsRead);
        Assert.False(result.IsFlagged);
        Assert.Equal("This is a preview...", result.PlainTextSnippet);
    }

    [Fact]
    public void ToEmailHeader_UnreadAndFlagged_MapsFlags()
    {
        var msg = CreateMessage(
            id: "msg2",
            fromAddress: "sender@example.com",
            isRead: false,
            isFlagged: true
        );

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.False(result.IsRead);
        Assert.True(result.IsFlagged);
    }

    [Fact]
    public void ToEmailHeader_NullFrom_DefaultsToEmpty()
    {
        var msg = new Message
        {
            Id = "msg3",
            From = null,
            Subject = "No Sender",
            ToRecipients = [],
            CcRecipients = [],
            ReceivedDateTime = DateTimeOffset.UtcNow,
            IsRead = false,
            Flag = new FollowupFlag { FlagStatus = FollowupFlagStatus.NotFlagged },
        };

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal("", result.FromAddress);
        Assert.Equal("", result.FromDisplayName);
    }

    [Fact]
    public void ToEmailHeader_NullRecipients_DefaultsToEmptyLists()
    {
        var msg = new Message
        {
            Id = "msg4",
            Subject = "No Recipients",
            ToRecipients = null,
            CcRecipients = null,
            ReceivedDateTime = DateTimeOffset.UtcNow,
            IsRead = true,
        };

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Empty(result.ToAddresses);
        Assert.Empty(result.CcAddresses);
    }

    [Fact]
    public void ToEmailHeader_MultipleRecipients_ParsesAll()
    {
        var msg = CreateMessage(
            id: "msg5",
            fromAddress: "sender@example.com",
            toAddresses: ["alice@example.com", "bob@example.com"],
            ccAddresses: ["carol@example.com", "dave@example.com"]
        );

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal(2, result.ToAddresses.Count);
        Assert.Contains("alice@example.com", result.ToAddresses);
        Assert.Contains("bob@example.com", result.ToAddresses);
        Assert.Equal(2, result.CcAddresses.Count);
        Assert.Contains("carol@example.com", result.CcAddresses);
        Assert.Contains("dave@example.com", result.CcAddresses);
    }

    [Fact]
    public void ToEmailHeader_EmptyRecipientAddress_Filtered()
    {
        var msg = new Message
        {
            Id = "msg6",
            Subject = "Mixed Recipients",
            ToRecipients =
            [
                new Recipient { EmailAddress = new EmailAddress { Address = "valid@example.com" } },
                new Recipient { EmailAddress = new EmailAddress { Address = "" } },
                new Recipient { EmailAddress = new EmailAddress { Address = null } },
            ],
            ReceivedDateTime = DateTimeOffset.UtcNow,
            IsRead = true,
        };

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Single(result.ToAddresses);
        Assert.Equal("valid@example.com", result.ToAddresses[0]);
    }

    [Fact]
    public void ToEmailHeader_DateMapping_PreservesDateTime()
    {
        var date = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.FromHours(2));
        var msg = CreateMessage(id: "msg7", fromAddress: "a@b.com", receivedDateTime: date);

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal(date, result.Date);
    }

    [Fact]
    public void ToEmailHeader_NullDate_DefaultsToMinValue()
    {
        var msg = new Message
        {
            Id = "msg8",
            ReceivedDateTime = null,
            IsRead = true,
        };

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal(DateTimeOffset.MinValue, result.Date);
    }

    [Fact]
    public void ToEmailHeader_WithFileAttachment_ExtractsMetadata()
    {
        var msg = CreateMessage(id: "msg9", fromAddress: "sender@example.com");
        msg.Attachments =
        [
            new FileAttachment
            {
                Name = "report.pdf",
                ContentType = "application/pdf",
                Size = 12345,
            },
        ];

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Single(result.Attachments);
        Assert.Equal("report.pdf", result.Attachments[0].Filename);
        Assert.Equal("application/pdf", result.Attachments[0].ContentType);
        Assert.Equal(12345, result.Attachments[0].SizeBytes);
    }

    [Fact]
    public void ToEmailHeader_MultipleAttachments_ExtractsAll()
    {
        var msg = CreateMessage(id: "msg10", fromAddress: "sender@example.com");
        msg.Attachments =
        [
            new FileAttachment { Name = "doc.pdf", ContentType = "application/pdf", Size = 100 },
            new FileAttachment { Name = "image.png", ContentType = "image/png", Size = 200 },
        ];

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal(2, result.Attachments.Count);
        Assert.Equal("doc.pdf", result.Attachments[0].Filename);
        Assert.Equal("image.png", result.Attachments[1].Filename);
    }

    [Fact]
    public void ToEmailHeader_NoAttachments_EmptyList()
    {
        var msg = CreateMessage(id: "msg11", fromAddress: "sender@example.com");
        msg.Attachments = null;

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Empty(result.Attachments);
    }

    [Fact]
    public void ToEmailHeader_NullSubject_DefaultsToEmpty()
    {
        var msg = CreateMessage(id: "msg12", fromAddress: "a@b.com");
        msg.Subject = null;

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal("", result.Subject);
    }

    [Fact]
    public void ToEmailHeader_NullBodyPreview_MapsAsNull()
    {
        var msg = CreateMessage(id: "msg13", fromAddress: "a@b.com");
        msg.BodyPreview = null;

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Null(result.PlainTextSnippet);
    }

    [Fact]
    public void ToEmailHeader_NullFlag_NotFlagged()
    {
        var msg = CreateMessage(id: "msg14", fromAddress: "a@b.com");
        msg.Flag = null;

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.False(result.IsFlagged);
    }

    [Fact]
    public void ToEmailHeader_NullParentFolderId_DefaultsToInbox()
    {
        var msg = CreateMessage(id: "msg15", fromAddress: "a@b.com");
        msg.ParentFolderId = null;

        var result = GraphMailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal("INBOX", result.FolderId);
    }

    [Fact]
    public void ToSendRequest_BasicEmail_MapsCorrectly()
    {
        var email = new OutboundEmail(
            FromAccountId: AccountId,
            To: ["alice@example.com"],
            Cc: [],
            Bcc: [],
            Subject: "Hello",
            PlainTextBody: "Hello world!",
            InReplyToMessageId: null
        );

        var result = GraphMailMapper.ToSendRequest(email);

        Assert.NotNull(result.Message);
        Assert.Equal("Hello", result.Message.Subject);
        Assert.Equal("Hello world!", result.Message.Body?.Content);
        Assert.Equal(BodyType.Text, result.Message.Body?.ContentType);
        Assert.Single(result.Message.ToRecipients!);
        Assert.Equal("alice@example.com", result.Message.ToRecipients![0].EmailAddress?.Address);
        Assert.True(result.SaveToSentItems);
    }

    [Fact]
    public void ToSendRequest_WithCcAndBcc_MapsRecipients()
    {
        var email = new OutboundEmail(
            FromAccountId: AccountId,
            To: ["to@example.com"],
            Cc: ["cc@example.com"],
            Bcc: ["bcc@example.com"],
            Subject: "Test",
            PlainTextBody: "Body",
            InReplyToMessageId: null
        );

        var result = GraphMailMapper.ToSendRequest(email);

        Assert.Single(result.Message!.CcRecipients!);
        Assert.Equal("cc@example.com", result.Message.CcRecipients![0].EmailAddress?.Address);
        Assert.Single(result.Message.BccRecipients!);
        Assert.Equal("bcc@example.com", result.Message.BccRecipients![0].EmailAddress?.Address);
    }

    [Fact]
    public void ToSendRequest_NoCcBcc_OmitsRecipientLists()
    {
        var email = new OutboundEmail(
            FromAccountId: AccountId,
            To: ["to@example.com"],
            Cc: [],
            Bcc: [],
            Subject: "Test",
            PlainTextBody: "Body",
            InReplyToMessageId: null
        );

        var result = GraphMailMapper.ToSendRequest(email);

        Assert.Null(result.Message!.CcRecipients);
        Assert.Null(result.Message.BccRecipients);
    }

    [Fact]
    public void ToSendRequest_WithInReplyTo_SetsHeader()
    {
        var email = new OutboundEmail(
            FromAccountId: AccountId,
            To: ["to@example.com"],
            Cc: [],
            Bcc: [],
            Subject: "Re: Test",
            PlainTextBody: "Reply body",
            InReplyToMessageId: "<original-msg-id@example.com>"
        );

        var result = GraphMailMapper.ToSendRequest(email);

        Assert.NotNull(result.Message!.AdditionalData);
        Assert.True(result.Message.AdditionalData.ContainsKey("internetMessageHeaders"));
    }

    private static Message CreateMessage(
        string id = "",
        string? subject = "Test",
        string fromAddress = "",
        string? fromName = null,
        string[]? toAddresses = null,
        string[]? ccAddresses = null,
        DateTimeOffset? receivedDateTime = null,
        bool isRead = true,
        bool isFlagged = false,
        string? bodyPreview = null,
        string? parentFolderId = "INBOX")
    {
        var msg = new Message
        {
            Id = id,
            Subject = subject,
            ReceivedDateTime = receivedDateTime ?? DateTimeOffset.UtcNow,
            IsRead = isRead,
            BodyPreview = bodyPreview,
            ParentFolderId = parentFolderId,
            Flag = new FollowupFlag
            {
                FlagStatus = isFlagged ? FollowupFlagStatus.Flagged : FollowupFlagStatus.NotFlagged,
            },
        };

        if (!string.IsNullOrEmpty(fromAddress))
        {
            msg.From = new Recipient
            {
                EmailAddress = new EmailAddress
                {
                    Address = fromAddress,
                    Name = fromName ?? "",
                },
            };
        }

        if (toAddresses is not null)
        {
            msg.ToRecipients = toAddresses
                .Select(a => new Recipient { EmailAddress = new EmailAddress { Address = a } })
                .ToList();
        }

        if (ccAddresses is not null)
        {
            msg.CcRecipients = ccAddresses
                .Select(a => new Recipient { EmailAddress = new EmailAddress { Address = a } })
                .ToList();
        }

        return msg;
    }
}
