using VelavanDemo.Domain.Common;
using VelavanDemo.Domain.Enums;

namespace VelavanDemo.Domain.Entities;

public class Delivery : BaseEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int? DeliveryPartnerId { get; set; }
    public DeliveryPartner? DeliveryPartner { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime? PickedUpAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhoneNumber { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
