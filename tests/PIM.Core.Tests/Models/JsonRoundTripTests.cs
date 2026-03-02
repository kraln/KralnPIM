using System.Text.Json;
using PIM.Core.Models;
using PIM.Core.Serialization;

namespace PIM.Core.Tests.Models;

public class JsonRoundTripTests
{
    [Fact]
    public void EmailHeader_RoundTrip()
    {
        var header = new EmailHeader(
            MessageId: "msg-001",
            AccountId: "acc-1",
            FolderId: "INBOX",
            Subject: "Test Subject",
            FromAddress: "alice@example.com",
            FromDisplayName: "Alice",
            ToAddresses: ["bob@example.com", "carol@example.com"],
            CcAddresses: ["dave@example.com"],
            Date: new DateTimeOffset(2025, 1, 6, 10, 42, 0, TimeSpan.FromHours(-5)),
            IsRead: false,
            IsFlagged: true,
            PlainTextSnippet: "Hello, this is a test...",
            Attachments: [new AttachmentInfo("doc.pdf", "application/pdf", 2400000)]
        );

        var json = JsonSerializer.Serialize(header, PimJsonContext.Default.EmailHeader);
        var deserialized = JsonSerializer.Deserialize(json, PimJsonContext.Default.EmailHeader);

        Assert.NotNull(deserialized);
        Assert.Equal(header.MessageId, deserialized.MessageId);
        Assert.Equal(header.Subject, deserialized.Subject);
        Assert.Equal(header.ToAddresses, deserialized.ToAddresses);
        Assert.Equal(header.Date, deserialized.Date);
        Assert.Equal(header.IsFlagged, deserialized.IsFlagged);
        Assert.Single(deserialized.Attachments);
        Assert.Equal("doc.pdf", deserialized.Attachments[0].Filename);
    }

    [Fact]
    public void CalendarEvent_RoundTrip()
    {
        var evt = new CalendarEvent(
            EventId: "evt-001",
            AccountId: "acc-1",
            CalendarId: "cal-1",
            Summary: "Standup",
            Description: "Daily standup meeting",
            Start: new DateTimeOffset(2025, 1, 6, 9, 0, 0, TimeSpan.FromHours(-5)),
            End: new DateTimeOffset(2025, 1, 6, 9, 15, 0, TimeSpan.FromHours(-5)),
            IsAllDay: false,
            Location: "Room A",
            Invitees: ["alice@example.com", "bob@example.com"],
            RecurrenceRule: "FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR",
            Status: EventStatus.Confirmed
        );

        var json = JsonSerializer.Serialize(evt, PimJsonContext.Default.CalendarEvent);
        var deserialized = JsonSerializer.Deserialize(json, PimJsonContext.Default.CalendarEvent);

        Assert.NotNull(deserialized);
        Assert.Equal(evt.EventId, deserialized.EventId);
        Assert.Equal(evt.Summary, deserialized.Summary);
        Assert.Equal(evt.Start, deserialized.Start);
        Assert.Equal(evt.Invitees, deserialized.Invitees);
        Assert.Equal(evt.RecurrenceRule, deserialized.RecurrenceRule);
        Assert.Equal(EventStatus.Confirmed, deserialized.Status);
    }

    [Fact]
    public void OAuthToken_RoundTrip()
    {
        var token = new OAuthToken(
            AccountId: "acc-1",
            AccessToken: "access-123",
            RefreshToken: "refresh-456",
            ExpiresAt: new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero)
        );

        var json = JsonSerializer.Serialize(token, PimJsonContext.Default.OAuthToken);
        var deserialized = JsonSerializer.Deserialize(json, PimJsonContext.Default.OAuthToken);

        Assert.NotNull(deserialized);
        Assert.Equal(token.AccountId, deserialized.AccountId);
        Assert.Equal(token.AccessToken, deserialized.AccessToken);
        Assert.Equal(token.ExpiresAt, deserialized.ExpiresAt);
    }

    [Fact]
    public void OutboundEmail_RoundTrip()
    {
        var email = new OutboundEmail(
            FromAccountId: "acc-1",
            To: ["alice@example.com"],
            Cc: ["bob@example.com"],
            Bcc: [],
            Subject: "Re: Test",
            PlainTextBody: "Thanks for the message.",
            InReplyToMessageId: "msg-001"
        );

        var json = JsonSerializer.Serialize(email, PimJsonContext.Default.OutboundEmail);
        var deserialized = JsonSerializer.Deserialize(json, PimJsonContext.Default.OutboundEmail);

        Assert.NotNull(deserialized);
        Assert.Equal(email.FromAccountId, deserialized.FromAccountId);
        Assert.Equal(email.To, deserialized.To);
        Assert.Equal(email.InReplyToMessageId, deserialized.InReplyToMessageId);
    }

    [Fact]
    public void SyncResult_EmailHeader_RoundTrip()
    {
        var result = new SyncResult<EmailHeader>(
            Upserted: [
                new EmailHeader("msg-1", "acc-1", "INBOX", "Test", "a@b.com", "A",
                    ["c@d.com"], [], DateTimeOffset.UtcNow, false, false, null, [])
            ],
            DeletedIds: ["msg-old"],
            NewSyncToken: "token-123"
        );

        var json = JsonSerializer.Serialize(result, PimJsonContext.Default.SyncResultEmailHeader);
        var deserialized = JsonSerializer.Deserialize(json, PimJsonContext.Default.SyncResultEmailHeader);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Upserted);
        Assert.Single(deserialized.DeletedIds);
        Assert.Equal("token-123", deserialized.NewSyncToken);
    }

    [Fact]
    public void EventStatus_SerializesAsString()
    {
        var evt = new CalendarEvent("e1", "a1", "c1", "Test", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            false, null, [], null, EventStatus.Tentative);

        var json = JsonSerializer.Serialize(evt, PimJsonContext.Default.CalendarEvent);
        Assert.Contains("\"Tentative\"", json);
    }

    [Fact]
    public void EmailHeader_EmptyAttachments_RoundTrip()
    {
        var header = new EmailHeader("msg-1", "acc-1", "INBOX", "Test", "a@b.com", "A",
            [], [], DateTimeOffset.UtcNow, true, false, null, []);

        var json = JsonSerializer.Serialize(header, PimJsonContext.Default.EmailHeader);
        var deserialized = JsonSerializer.Deserialize(json, PimJsonContext.Default.EmailHeader);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Attachments);
        Assert.Empty(deserialized.ToAddresses);
    }
}
