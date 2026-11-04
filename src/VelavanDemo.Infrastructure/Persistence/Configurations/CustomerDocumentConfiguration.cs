using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class CustomerDocumentConfiguration : BaseEntityConfiguration<CustomerDocument>
{
    public override void Configure(EntityTypeBuilder<CustomerDocument> builder)
    {
        base.Configure(builder);

        builder.ToTable("CustomerDocuments");

        builder.HasIndex(document => document.AttachmentId)
            .IsUnique();

        builder.HasIndex(document => document.CustomerId);

        builder.HasOne(document => document.Customer)
            .WithMany(customer => customer.Documents)
            .HasForeignKey(document => document.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(document => document.Attachment)
            .WithMany()
            .HasForeignKey(document => document.AttachmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
