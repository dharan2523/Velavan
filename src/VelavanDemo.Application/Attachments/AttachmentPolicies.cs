using VelavanDemo.Domain.Enums;

namespace VelavanDemo.Application.Attachments;

public sealed record AttachmentPolicy(
    AttachmentKind Kind,
    long MaxBytes,
    int MaxCount,
    IReadOnlySet<string> AllowedExtensions);

public static class AttachmentPolicies
{
    public const long ProductImageMaxBytes = 10 * 1024 * 1024;
    public const long DocumentMaxBytes = 20 * 1024 * 1024;
    public const int ProductImageMaxCount = 8;
    public const int DocumentMaxCount = 25;
    public const int MaxImageDimension = 12_000;
    public const long MaxImagePixels = 40_000_000;

    private static readonly IReadOnlySet<string> ImageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

    private static readonly IReadOnlySet<string> DocumentExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf",
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

    public static AttachmentPolicy For(AttachmentKind kind)
    {
        return kind switch
        {
            AttachmentKind.ProductImage => new AttachmentPolicy(
                kind,
                ProductImageMaxBytes,
                ProductImageMaxCount,
                ImageExtensions),
            AttachmentKind.CustomerDocument => new AttachmentPolicy(
                kind,
                DocumentMaxBytes,
                DocumentMaxCount,
                DocumentExtensions),
            AttachmentKind.OrderAttachment => new AttachmentPolicy(
                kind,
                DocumentMaxBytes,
                DocumentMaxCount,
                DocumentExtensions),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown attachment kind.")
        };
    }
}
