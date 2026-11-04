using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryConfiguration : BaseEntityConfiguration<Delivery>
{
    public override void Configure(EntityTypeBuilder<Delivery> builder)
    {
        base.Configure(builder);

        builder.ToTable("Deliveries");

        builder.HasIndex(delivery => delivery.OrderId)
            .IsUnique();

        builder.Property(delivery => delivery.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(delivery => delivery.AssignedAtUtc)
            .HasColumnType("datetime2");

        builder.Property(delivery => delivery.PickedUpAtUtc)
            .HasColumnType("datetime2");

        builder.Property(delivery => delivery.DeliveredAtUtc)
            .HasColumnType("datetime2");

        builder.Property(delivery => delivery.RecipientName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(delivery => delivery.RecipientPhoneNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(delivery => delivery.DeliveryAddress)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(delivery => delivery.Notes)
            .HasMaxLength(1000);

        builder.HasIndex(delivery => delivery.Status);

        builder.HasOne(delivery => delivery.Order)
            .WithOne(order => order.Delivery)
            .HasForeignKey<Delivery>(delivery => delivery.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(delivery => delivery.DeliveryPartner)
            .WithMany(partner => partner.Deliveries)
            .HasForeignKey(delivery => delivery.DeliveryPartnerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
