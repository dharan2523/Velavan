using VelavanDemo.Domain.Common;

namespace VelavanDemo.Domain.Entities;

public class ProductImage : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int AttachmentId { get; set; }
    public Attachment Attachment { get; set; } = null!;
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
}
