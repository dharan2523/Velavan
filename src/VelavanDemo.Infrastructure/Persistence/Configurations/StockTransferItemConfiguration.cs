using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class StockTransferItemConfiguration : BaseEntityConfiguration<StockTransferItem>
{
    public override void Configure(EntityTypeBuilder<StockTransferItem> builder)
    {
        base.Configure(builder);

        builder.ToTable("StockTransferItems", table =>
        {
            table.HasCheckConstraint("CK_StockTransferItems_Quantity", "[Quantity] > 0");
        });

        builder.HasIndex(item => new { item.StockTransferId, item.ProductId })
            .IsUnique();

        builder.HasOne(item => item.StockTransfer)
            .WithMany(transfer => transfer.Items)
            .HasForeignKey(item => item.StockTransferId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.Product)
            .WithMany()
            .HasForeignKey(item => item.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
