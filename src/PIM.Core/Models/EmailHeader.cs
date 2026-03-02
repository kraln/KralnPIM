namespace PIM.Core.Models;

public sealed record EmailHeader(
    string MessageId,
    string AccountId,
    string FolderId,
    string Subject,
    string FromAddress,
    string FromDisplayName,
    List<string> ToAddresses,
    List<string> CcAddresses,
    DateTimeOffset Date,
    bool IsRead,
    bool IsFlagged,
    string? PlainTextSnippet,
    List<AttachmentInfo> Attachments
);
