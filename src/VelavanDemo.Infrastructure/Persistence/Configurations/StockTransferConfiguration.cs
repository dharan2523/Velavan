using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class StockTransferConfiguration : BaseEntityConfiguration<StockTransfer>
{
    public override void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        base.Configure(builder);

        builder.ToTable("StockTransfers", table =>
        {
            table.HasCheckConstraint("CK_StockTransfers_DifferentWarehouses", "[FromWarehouseId] <> [ToWarehouseId]");
        });

        builder.Property(transfer => transfer.TransferNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(transfer => transfer.TransferNumber)
            .IsUnique();

        builder.Property(transfer => transfer.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(transfer => transfer.CompletedAtUtc)
            .HasColumnType("datetime2");

        builder.Property(transfer => transfer.Notes)
            .HasMaxLength(1000);

        builder.HasIndex(transfer => transfer.Status);

        builder.HasOne(transfer => transfer.FromWarehouse)
            .WithMany(warehouse => warehouse.OutgoingTransfers)
            .HasForeignKey(transfer => transfer.FromWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(transfer => transfer.ToWarehouse)
            .WithMany(warehouse => warehouse.IncomingTransfers)
            .HasForeignKey(transfer => transfer.ToWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
