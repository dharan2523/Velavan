using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class InventoryItemConfiguration : BaseEntityConfiguration<InventoryItem>
{
    public override void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        base.Configure(builder);

        builder.ToTable("InventoryItems", table =>
        {
            table.HasCheckConstraint("CK_InventoryItems_QuantityOnHand", "[QuantityOnHand] >= 0");
            table.HasCheckConstraint("CK_InventoryItems_ReservedQuantity", "[ReservedQuantity] >= 0");
            table.HasCheckConstraint("CK_InventoryItems_ReorderLevel", "[ReorderLevel] >= 0");
            table.HasCheckConstraint("CK_InventoryItems_ReservedWithinOnHand", "[ReservedQuantity] <= [QuantityOnHand]");
        });

        builder.HasIndex(item => new { item.ProductId, item.WarehouseId })
            .IsUnique();

        builder.Property(item => item.RowVersion)
            .IsRowVersion();

        builder.HasOne(item => item.Product)
            .WithMany(product => product.InventoryItems)
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.Warehouse)
            .WithMany(warehouse => warehouse.InventoryItems)
            .HasForeignKey(item => item.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
