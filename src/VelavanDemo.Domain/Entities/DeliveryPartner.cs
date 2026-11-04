using VelavanDemo.Domain.Common;

namespace VelavanDemo.Domain.Entities;

public class DeliveryPartner : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? VehicleNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Delivery> Deliveries { get; set; } = new List<Delivery>();
}
