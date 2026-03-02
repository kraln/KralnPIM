namespace PIM.Core.Models;

public sealed record AttachmentInfo(
    string Filename,
    string ContentType,
    long SizeBytes
);
