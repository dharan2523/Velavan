using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryPartnerConfiguration : BaseEntityConfiguration<DeliveryPartner>
{
    public override void Configure(EntityTypeBuilder<DeliveryPartner> builder)
    {
        base.Configure(builder);

        builder.ToTable("DeliveryPartners");

        builder.Property(partner => partner.FullName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(partner => partner.PhoneNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(partner => partner.PhoneNumber)
            .IsUnique();

        builder.Property(partner => partner.Email)
            .HasMaxLength(256);

        builder.Property(partner => partner.VehicleNumber)
            .HasMaxLength(50);

        builder.Property(partner => partner.IsActive)
            .HasDefaultValue(true)
            .IsRequired();
    }
}
