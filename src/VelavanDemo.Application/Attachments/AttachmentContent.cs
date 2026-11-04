namespace VelavanDemo.Application.Attachments;

public sealed record AttachmentContent(
    Stream Content,
    string ContentType,
    string OriginalFileName,
    bool Inline,
    string ETag);
