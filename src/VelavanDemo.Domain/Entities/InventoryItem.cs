using VelavanDemo.Domain.Common;

namespace VelavanDemo.Domain.Entities;

public class InventoryItem : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
    public int QuantityOnHand { get; set; }
    public int ReservedQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
