using VelavanDemo.Domain.Common;
using VelavanDemo.Domain.Enums;

namespace VelavanDemo.Domain.Entities;

public class StockTransfer : BaseEntity
{
    public string TransferNumber { get; set; } = string.Empty;
    public int FromWarehouseId { get; set; }
    public Warehouse FromWarehouse { get; set; } = null!;
    public int ToWarehouseId { get; set; }
    public Warehouse ToWarehouse { get; set; } = null!;
    public StockTransferStatus Status { get; set; } = StockTransferStatus.Draft;
    public DateTime? CompletedAtUtc { get; set; }
    public string? Notes { get; set; }
    public ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
}
