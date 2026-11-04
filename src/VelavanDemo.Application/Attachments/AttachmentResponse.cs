using VelavanDemo.Domain.Enums;

namespace VelavanDemo.Application.Attachments;

public sealed record AttachmentResponse(
    int Id,
    AttachmentKind Kind,
    string OriginalFileName,
    string ContentType,
    string FileExtension,
    long SizeBytes,
    int? Width,
    int? Height,
    DateTime CreatedAtUtc);
