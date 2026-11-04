using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class WarehouseConfiguration : BaseEntityConfiguration<Warehouse>
{
    public override void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        base.Configure(builder);

        builder.ToTable("Warehouses");

        builder.Property(warehouse => warehouse.Code)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(warehouse => warehouse.Code)
            .IsUnique();

        builder.Property(warehouse => warehouse.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(warehouse => warehouse.Address)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(warehouse => warehouse.IsActive)
            .HasDefaultValue(true)
            .IsRequired();
    }
}
