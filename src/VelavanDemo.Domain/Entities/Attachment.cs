using VelavanDemo.Domain.Common;
using VelavanDemo.Domain.Enums;

namespace VelavanDemo.Domain.Entities;

public class Attachment : BaseEntity
{
    public AttachmentKind Kind { get; set; }
    public string StorageProvider { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? UploadedByUserId { get; set; }
    public User? UploadedByUser { get; set; }
}
