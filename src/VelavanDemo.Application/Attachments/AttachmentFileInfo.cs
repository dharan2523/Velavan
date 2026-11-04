namespace VelavanDemo.Application.Attachments;

public sealed record AttachmentFileInfo(
    string FileExtension,
    string ContentType,
    int? Width,
    int? Height);
