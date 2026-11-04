using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : BaseEntityConfiguration<Order>
{
    public override void Configure(EntityTypeBuilder<Order> builder)
    {
        base.Configure(builder);

        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint("CK_Orders_SubTotal", "[SubTotal] >= 0");
            table.HasCheckConstraint("CK_Orders_TaxAmount", "[TaxAmount] >= 0");
            table.HasCheckConstraint("CK_Orders_ShippingAmount", "[ShippingAmount] >= 0");
            table.HasCheckConstraint("CK_Orders_TotalAmount", "[TotalAmount] >= 0");
        });

        builder.Property(order => order.OrderNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(order => order.OrderNumber)
            .IsUnique();

        builder.Property(order => order.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(order => order.PaymentStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(order => order.PaymentMethod)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(order => order.SubTotal)
            .HasPrecision(18, 2);

        builder.Property(order => order.TaxAmount)
            .HasPrecision(18, 2);

        builder.Property(order => order.ShippingAmount)
            .HasPrecision(18, 2);

        builder.Property(order => order.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(order => order.PlacedAtUtc)
            .HasColumnType("datetime2")
            .IsRequired();

        builder.Property(order => order.Notes)
            .HasMaxLength(1000);

        builder.HasIndex(order => order.Status);
        builder.HasIndex(order => order.PlacedAtUtc);

        builder.HasOne(order => order.Customer)
            .WithMany(customer => customer.Orders)
            .HasForeignKey(order => order.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
