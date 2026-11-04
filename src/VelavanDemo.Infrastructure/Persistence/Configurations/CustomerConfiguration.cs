using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : BaseEntityConfiguration<Customer>
{
    public override void Configure(EntityTypeBuilder<Customer> builder)
    {
        base.Configure(builder);

        builder.ToTable("Customers");

        builder.Property(customer => customer.FirstName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(customer => customer.LastName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(customer => customer.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(customer => customer.Email)
            .IsUnique();

        builder.Property(customer => customer.PhoneNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(customer => customer.AddressLine1)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(customer => customer.AddressLine2)
            .HasMaxLength(250);

        builder.Property(customer => customer.City)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(customer => customer.State)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(customer => customer.PostalCode)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(customer => customer.IsActive)
            .HasDefaultValue(true)
            .IsRequired();
    }
}
