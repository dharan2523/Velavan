using VelavanDemo.Domain.Common;

namespace VelavanDemo.Domain.Entities;

public class CustomerDocument : BaseEntity
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int AttachmentId { get; set; }
    public Attachment Attachment { get; set; } = null!;
}
