using Google.Apis.Gmail.v1.Data;
using PIM.Sync.Google;

namespace PIM.Sync.Google.Tests;

public class GmailMapperTests
{
    private const string AccountId = "test-account";

    [Fact]
    public void ToEmailHeader_BasicMessage_MapsCorrectly()
    {
        var msg = CreateMessage(
            id: "msg1",
            from: "Alice Smith <alice@example.com>",
            to: "bob@example.com",
            cc: "carol@example.com",
            subject: "Test Subject",
            date: "Mon, 15 Jan 2024 10:30:00 +0000",
            labels: ["INBOX", "UNREAD"],
            snippet: "This is a preview..."
        );

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal("msg1", result.MessageId);
        Assert.Equal(AccountId, result.AccountId);
        Assert.Equal("INBOX", result.FolderId);
        Assert.Equal("Test Subject", result.Subject);
        Assert.Equal("alice@example.com", result.FromAddress);
        Assert.Equal("Alice Smith", result.FromDisplayName);
        Assert.Single(result.ToAddresses);
        Assert.Equal("bob@example.com", result.ToAddresses[0]);
        Assert.Single(result.CcAddresses);
        Assert.Equal("carol@example.com", result.CcAddresses[0]);
        Assert.False(result.IsRead);
        Assert.False(result.IsFlagged);
        Assert.Equal("This is a preview...", result.PlainTextSnippet);
    }

    [Fact]
    public void ToEmailHeader_ReadAndStarred_MapsFlags()
    {
        var msg = CreateMessage(
            id: "msg2",
            from: "sender@example.com",
            labels: ["INBOX", "STARRED"]
        );

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.True(result.IsRead);
        Assert.True(result.IsFlagged);
    }

    [Fact]
    public void ToEmailHeader_SentFolder_MapsFolderId()
    {
        var msg = CreateMessage(id: "msg3", from: "me@example.com", labels: ["SENT"]);

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal("SENT", result.FolderId);
    }

    [Fact]
    public void ToEmailHeader_DisplayNameInQuotes_ParsesCorrectly()
    {
        var msg = CreateMessage(
            id: "msg4",
            from: "\"Smith, Alice\" <alice@example.com>"
        );

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal("alice@example.com", result.FromAddress);
        Assert.Equal("Smith, Alice", result.FromDisplayName);
    }

    [Fact]
    public void ToEmailHeader_BareEmail_ParsesAddress()
    {
        var msg = CreateMessage(id: "msg5", from: "alice@example.com");

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal("alice@example.com", result.FromAddress);
        Assert.Equal("", result.FromDisplayName);
    }

    [Fact]
    public void ToEmailHeader_MultipleToAndCc_ParsesAll()
    {
        var msg = CreateMessage(
            id: "msg6",
            from: "sender@example.com",
            to: "alice@example.com, bob@example.com",
            cc: "carol@example.com, dave@example.com"
        );

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal(2, result.ToAddresses.Count);
        Assert.Contains("alice@example.com", result.ToAddresses);
        Assert.Contains("bob@example.com", result.ToAddresses);
        Assert.Equal(2, result.CcAddresses.Count);
        Assert.Contains("carol@example.com", result.CcAddresses);
        Assert.Contains("dave@example.com", result.CcAddresses);
    }

    [Fact]
    public void ToEmailHeader_MissingHeaders_DefaultsToEmpty()
    {
        var msg = new Message
        {
            Id = "msg7",
            LabelIds = ["INBOX"],
            Payload = new MessagePart { Headers = [] },
            Snippet = null,
        };

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal("msg7", result.MessageId);
        Assert.Equal("", result.Subject);
        Assert.Equal("", result.FromAddress);
        Assert.Equal("", result.FromDisplayName);
        Assert.Empty(result.ToAddresses);
        Assert.Empty(result.CcAddresses);
        Assert.Null(result.PlainTextSnippet);
    }

    [Fact]
    public void ToEmailHeader_WithAttachment_ExtractsMetadata()
    {
        var msg = CreateMessage(id: "msg8", from: "sender@example.com");
        msg.Payload.Parts =
        [
            new MessagePart
            {
                MimeType = "text/plain",
                Body = new MessagePartBody { Data = "dGVzdA==" },
            },
            new MessagePart
            {
                Filename = "report.pdf",
                MimeType = "application/pdf",
                Body = new MessagePartBody
                {
                    AttachmentId = "att1",
                    Size = 12345,
                },
            },
        ];

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.Single(result.Attachments);
        Assert.Equal("report.pdf", result.Attachments[0].Filename);
        Assert.Equal("application/pdf", result.Attachments[0].ContentType);
        Assert.Equal(12345, result.Attachments[0].SizeBytes);
    }

    [Fact]
    public void ToEmailHeader_DateParsing_HandlesStandardFormat()
    {
        var msg = CreateMessage(
            id: "msg9",
            from: "sender@example.com",
            date: "2024-06-15T14:30:00+02:00"
        );

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal(2024, result.Date.Year);
        Assert.Equal(6, result.Date.Month);
        Assert.Equal(15, result.Date.Day);
    }

    [Fact]
    public void ParseAddressList_EmptyString_ReturnsEmpty()
    {
        var result = GmailMapper.ParseAddressList("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseAddressList_SingleAddress_ReturnsList()
    {
        var result = GmailMapper.ParseAddressList("Alice <alice@example.com>");
        Assert.Single(result);
        Assert.Equal("alice@example.com", result[0]);
    }

    [Fact]
    public void ParseAddressList_MultipleAddresses_ParsesAll()
    {
        var result = GmailMapper.ParseAddressList(
            "Alice <alice@example.com>, bob@example.com, Carol <carol@example.com>");

        Assert.Equal(3, result.Count);
        Assert.Equal("alice@example.com", result[0]);
        Assert.Equal("bob@example.com", result[1]);
        Assert.Equal("carol@example.com", result[2]);
    }

    [Fact]
    public void ToEmailHeader_DraftFolder_MapsFolderId()
    {
        var msg = CreateMessage(id: "msg10", from: "me@example.com", labels: ["DRAFT"]);

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal("DRAFT", result.FolderId);
    }

    [Fact]
    public void ToEmailHeader_CustomLabel_FallsBackToLabel()
    {
        var msg = CreateMessage(id: "msg11", from: "me@example.com", labels: ["Label_123"]);

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal("Label_123", result.FolderId);
    }

    [Fact]
    public void ToEmailHeader_NestedAttachments_ExtractsAll()
    {
        var msg = CreateMessage(id: "msg12", from: "sender@example.com");
        msg.Payload.Parts =
        [
            new MessagePart
            {
                MimeType = "multipart/mixed",
                Parts =
                [
                    new MessagePart
                    {
                        Filename = "doc.pdf",
                        MimeType = "application/pdf",
                        Body = new MessagePartBody { AttachmentId = "att1", Size = 100 },
                    },
                    new MessagePart
                    {
                        Filename = "image.png",
                        MimeType = "image/png",
                        Body = new MessagePartBody { AttachmentId = "att2", Size = 200 },
                    },
                ],
            },
        ];

        var result = GmailMapper.ToEmailHeader(msg, AccountId);

        Assert.Equal(2, result.Attachments.Count);
        Assert.Equal("doc.pdf", result.Attachments[0].Filename);
        Assert.Equal("image.png", result.Attachments[1].Filename);
    }

    private static Message CreateMessage(
        string id,
        string from = "",
        string? to = null,
        string? cc = null,
        string? subject = null,
        string? date = null,
        string[]? labels = null,
        string? snippet = null)
    {
        var headers = new List<MessagePartHeader>();
        if (!string.IsNullOrEmpty(from))
            headers.Add(new MessagePartHeader { Name = "From", Value = from });
        if (to is not null)
            headers.Add(new MessagePartHeader { Name = "To", Value = to });
        if (cc is not null)
            headers.Add(new MessagePartHeader { Name = "Cc", Value = cc });
        if (subject is not null)
            headers.Add(new MessagePartHeader { Name = "Subject", Value = subject });
        if (date is not null)
            headers.Add(new MessagePartHeader { Name = "Date", Value = date });

        return new Message
        {
            Id = id,
            LabelIds = labels ?? ["INBOX"],
            Payload = new MessagePart { Headers = headers },
            Snippet = snippet,
        };
    }
}
