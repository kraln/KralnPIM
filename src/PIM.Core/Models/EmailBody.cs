namespace PIM.Core.Models;

public sealed record EmailBody(
    string MessageId,
    string PlainTextContent
);
