namespace VelavanDemo.Application.Attachments;

public sealed record ProductImageResponse(
    int Id,
    string OriginalFileName,
    string ContentType,
    string FileExtension,
    long SizeBytes,
    int? Width,
    int? Height,
    bool IsPrimary,
    int SortOrder,
    DateTime CreatedAtUtc);
