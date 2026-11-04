using VelavanDemo.Domain.Common;

namespace VelavanDemo.Domain.Entities;

public class OrderAttachment : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int AttachmentId { get; set; }
    public Attachment Attachment { get; set; } = null!;
}
