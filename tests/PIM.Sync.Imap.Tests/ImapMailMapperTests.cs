using MailKit;
using MimeKit;
using NSubstitute;
using PIM.Core.Models;
using PIM.Sync.Imap;

namespace PIM.Sync.Imap.Tests;

public class ImapMailMapperTests
{
    private const string AccountId = "test-account";
    private const string FolderId = "INBOX";

    // --- ToEmailHeader from IMessageSummary ---

    [Fact]
    public void ToEmailHeader_MapsFromAddress()
    {
        var summary = CreateSummary(from: "sender@example.com", fromName: "Sender Name");

        var result = ImapMailMapper.ToEmailHeader(summary, AccountId, FolderId);

        Assert.Equal("sender@example.com", result.FromAddress);
        Assert.Equal("Sender Name", result.FromDisplayName);
    }

    [Fact]
    public void ToEmailHeader_MapsToAddresses()
    {
        var summary = CreateSummary(to: ["alice@example.com", "bob@example.com"]);

        var result = ImapMailMapper.ToEmailHeader(summary, AccountId, FolderId);

        Assert.Equal(["alice@example.com", "bob@example.com"], result.ToAddresses);
    }

    [Fact]
    public void ToEmailHeader_MapsCcAddresses()
    {
        var summary = CreateSummary(cc: ["cc1@example.com", "cc2@example.com"]);

        var result = ImapMailMapper.ToEmailHeader(summary, AccountId, FolderId);

        Assert.Equal(["cc1@example.com", "cc2@example.com"], result.CcAddresses);
    }

    [Fact]
    public void ToEmailHeader_MapsUniqueIdAsMessageId()
    {
        var summary = CreateSummary(uid: 42);

        var result = ImapMailMapper.ToEmailHeader(summary, AccountId, FolderId);

        Assert.Equal("42", result.MessageId);
    }

    [Fact]
    public void ToEmailHeader_MapsFlagsRead()
    {
        var summary = CreateSummary(flags: MessageFlags.Seen);

        var result = ImapMailMapper.ToEmailHeader(summary, AccountId, FolderId);

        Assert.True(result.IsRead);
        Assert.False(result.IsFlagged);
    }

    [Fact]
    public void ToEmailHeader_MapsFlagsUnread()
    {
        var summary = CreateSummary(flags: MessageFlags.None);

        var result = ImapMailMapper.ToEmailHeader(summary, AccountId, FolderId);

        Assert.False(result.IsRead);
    }

    [Fact]
    public void ToEmailHeader_MapsFlagsFlagged()
    {
        var summary = CreateSummary(flags: MessageFlags.Flagged | MessageFlags.Seen);

        var result = ImapMailMapper.ToEmailHeader(summary, AccountId, FolderId);

        Assert.True(result.IsRead);
        Assert.True(result.IsFlagged);
    }

    [Fact]
    public void ToEmailHeader_NullEnvelope_DefaultsGracefully()
    {
        var summary = Substitute.For<IMessageSummary>();
        summary.Envelope.Returns((Envelope?)null);
        summary.UniqueId.Returns(new UniqueId(1));
        summary.Flags.Returns(MessageFlags.None);

        var result = ImapMailMapper.ToEmailHeader(summary, AccountId, FolderId);

        Assert.Equal("1", result.MessageId);
        Assert.Equal("", result.FromAddress);
        Assert.Equal("", result.FromDisplayName);
        Assert.Equal("", result.Subject);
        Assert.Empty(result.ToAddresses);
        Assert.Empty(result.CcAddresses);
    }

    // --- FromMimeMessage ---

    [Fact]
    public void FromMimeMessage_MapsSubjectAndDate()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Test", "test@example.com"));
        message.Subject = "Important Message";
        message.Date = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        message.Body = new TextPart("plain") { Text = "body" };

        var result = ImapMailMapper.FromMimeMessage(message, 100, AccountId, FolderId, MessageFlags.None);

        Assert.Equal("Important Message", result.Subject);
        Assert.Equal(new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero), result.Date);
    }

    [Fact]
    public void FromMimeMessage_MapsAddresses()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Alice", "alice@example.com"));
        message.To.Add(new MailboxAddress("Bob", "bob@example.com"));
        message.Cc.Add(new MailboxAddress("Charlie", "charlie@example.com"));
        message.Body = new TextPart("plain") { Text = "text" };

        var result = ImapMailMapper.FromMimeMessage(message, 1, AccountId, FolderId, MessageFlags.None);

        Assert.Equal("alice@example.com", result.FromAddress);
        Assert.Equal("Alice", result.FromDisplayName);
        Assert.Equal(["bob@example.com"], result.ToAddresses);
        Assert.Equal(["charlie@example.com"], result.CcAddresses);
    }

    [Fact]
    public void FromMimeMessage_MapsFlags()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Test", "test@example.com"));
        message.Body = new TextPart("plain") { Text = "text" };

        var result = ImapMailMapper.FromMimeMessage(
            message, 1, AccountId, FolderId, MessageFlags.Seen | MessageFlags.Flagged);

        Assert.True(result.IsRead);
        Assert.True(result.IsFlagged);
    }

    [Fact]
    public void FromMimeMessage_GeneratesSnippet()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Test", "test@example.com"));
        message.Body = new TextPart("plain") { Text = "Hello, this is a test body." };

        var result = ImapMailMapper.FromMimeMessage(message, 1, AccountId, FolderId, MessageFlags.None);

        Assert.Equal("Hello, this is a test body.", result.PlainTextSnippet);
    }

    // --- ToMimeMessage ---

    [Fact]
    public void ToMimeMessage_SetsRecipients()
    {
        var email = new OutboundEmail(
            FromAccountId: "sender@example.com",
            To: ["to1@example.com", "to2@example.com"],
            Cc: ["cc@example.com"],
            Bcc: ["bcc@example.com"],
            Subject: "Test",
            PlainTextBody: "body",
            InReplyToMessageId: null);

        var result = ImapMailMapper.ToMimeMessage(email);

        Assert.Equal(2, result.To.Count);
        Assert.Single(result.Cc);
        Assert.Single(result.Bcc);
        Assert.Contains(result.To.Mailboxes, m => m.Address == "to1@example.com");
        Assert.Contains(result.To.Mailboxes, m => m.Address == "to2@example.com");
        Assert.Contains(result.Cc.Mailboxes, m => m.Address == "cc@example.com");
        Assert.Contains(result.Bcc.Mailboxes, m => m.Address == "bcc@example.com");
    }

    [Fact]
    public void ToMimeMessage_SetsSubject()
    {
        var email = new OutboundEmail("sender@example.com", ["to@example.com"], [], [],
            "My Subject", "body", null);

        var result = ImapMailMapper.ToMimeMessage(email);

        Assert.Equal("My Subject", result.Subject);
    }

    [Fact]
    public void ToMimeMessage_SetsInReplyTo()
    {
        var email = new OutboundEmail("sender@example.com", ["to@example.com"], [], [],
            "Re: Original", "reply body", "<original-id@example.com>");

        var result = ImapMailMapper.ToMimeMessage(email);

        // MimeKit normalizes InReplyTo by stripping angle brackets
        Assert.Equal("original-id@example.com", result.InReplyTo);
    }

    [Fact]
    public void ToMimeMessage_SetsPlainTextBody()
    {
        var email = new OutboundEmail("sender@example.com", ["to@example.com"], [], [],
            "Subject", "Hello, world!", null);

        var result = ImapMailMapper.ToMimeMessage(email);

        Assert.IsType<TextPart>(result.Body);
        Assert.Equal("Hello, world!", ((TextPart)result.Body).Text);
    }

    // --- ExtractPlainText ---

    [Fact]
    public void ExtractPlainText_PrefersTextPlain()
    {
        var message = new MimeMessage();
        var multipart = new MultipartAlternative();
        multipart.Add(new TextPart("plain") { Text = "Plain text version" });
        multipart.Add(new TextPart("html") { Text = "<p>HTML version</p>" });
        message.Body = multipart;

        var result = ImapMailMapper.ExtractPlainText(message);

        Assert.Equal("Plain text version", result);
    }

    [Fact]
    public void ExtractPlainText_FallsBackToHtml()
    {
        var message = new MimeMessage();
        message.Body = new TextPart("html") { Text = "<p>Hello World</p>" };

        var result = ImapMailMapper.ExtractPlainText(message);

        Assert.Contains("Hello World", result);
    }

    [Fact]
    public void ExtractPlainText_EmptyMessage_ReturnsEmpty()
    {
        var message = new MimeMessage();
        message.Body = new Multipart("mixed");

        var result = ImapMailMapper.ExtractPlainText(message);

        Assert.Equal("", result);
    }

    // --- ExtractAttachments ---

    [Fact]
    public void ExtractAttachments_WithAttachment()
    {
        var message = new MimeMessage();
        var multipart = new Multipart("mixed");
        multipart.Add(new TextPart("plain") { Text = "Body" });

        var attachment = new MimePart("application", "pdf")
        {
            FileName = "report.pdf",
            Content = new MimeContent(new MemoryStream(new byte[2048])),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment)
        };
        multipart.Add(attachment);
        message.Body = multipart;

        var result = ImapMailMapper.ExtractAttachments(message);

        Assert.Single(result);
        Assert.Equal("report.pdf", result[0].Filename);
        Assert.Equal("application/pdf", result[0].ContentType);
        Assert.Equal(2048, result[0].SizeBytes);
    }

    [Fact]
    public void ExtractAttachments_NoAttachments_ReturnsEmpty()
    {
        var message = new MimeMessage();
        message.Body = new TextPart("plain") { Text = "No attachments" };

        var result = ImapMailMapper.ExtractAttachments(message);

        Assert.Empty(result);
    }

    // --- GenerateSnippet ---

    [Fact]
    public void GenerateSnippet_TruncatesLongText()
    {
        var longText = new string('a', 500);

        var result = ImapMailMapper.GenerateSnippet(longText);

        Assert.NotNull(result);
        Assert.Equal(200, result.Length);
    }

    [Fact]
    public void GenerateSnippet_ShortText_Unchanged()
    {
        var result = ImapMailMapper.GenerateSnippet("Short text");

        Assert.Equal("Short text", result);
    }

    [Fact]
    public void GenerateSnippet_Null_ReturnsNull()
    {
        var result = ImapMailMapper.GenerateSnippet(null);

        Assert.Null(result);
    }

    [Fact]
    public void GenerateSnippet_WhitespaceOnly_ReturnsNull()
    {
        var result = ImapMailMapper.GenerateSnippet("   ");

        Assert.Null(result);
    }

    // --- Helpers ---

    private static IMessageSummary CreateSummary(
        uint uid = 1,
        string? from = null,
        string? fromName = null,
        string[]? to = null,
        string[]? cc = null,
        MessageFlags flags = MessageFlags.None)
    {
        var summary = Substitute.For<IMessageSummary>();
        summary.UniqueId.Returns(new UniqueId(uid));
        summary.Flags.Returns(flags);

        var envelope = new Envelope();
        if (from is not null)
            envelope.From.Add(new MailboxAddress(fromName ?? "", from));

        if (to is not null)
        {
            foreach (var addr in to)
                envelope.To.Add(new MailboxAddress("", addr));
        }

        if (cc is not null)
        {
            foreach (var addr in cc)
                envelope.Cc.Add(new MailboxAddress("", addr));
        }

        summary.Envelope.Returns(envelope);

        return summary;
    }
}
