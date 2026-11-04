namespace VelavanDemo.Application.Attachments;

public sealed record AttachmentUpload(
    Stream Content,
    string FileName,
    string? ContentType,
    long Length);
