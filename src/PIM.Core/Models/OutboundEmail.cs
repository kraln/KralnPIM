namespace PIM.Core.Models;

public sealed record OutboundEmail(
    string FromAccountId,
    List<string> To,
    List<string> Cc,
    List<string> Bcc,
    string Subject,
    string PlainTextBody,
    string? InReplyToMessageId,
    string? FromDisplayName = null
);
