using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : BaseEntityConfiguration<Role>
{
    public override void Configure(EntityTypeBuilder<Role> builder)
    {
        base.Configure(builder);

        builder.ToTable("Roles");

        builder.Property(role => role.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(role => role.Name)
            .IsUnique();

        builder.Property(role => role.Description)
            .HasMaxLength(500);

        builder.Property(role => role.IsSystemRole)
            .HasDefaultValue(false)
            .IsRequired();
    }
}
