using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class ProductImageConfiguration : BaseEntityConfiguration<ProductImage>
{
    public override void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        base.Configure(builder);

        builder.ToTable("ProductImages");

        builder.HasIndex(image => image.AttachmentId)
            .IsUnique();

        builder.HasIndex(image => new { image.ProductId, image.SortOrder });

        builder.HasIndex(image => image.ProductId)
            .IsUnique()
            .HasDatabaseName("UX_ProductImages_ProductId_Primary")
            .HasFilter("[IsPrimary] = 1");

        builder.HasOne(image => image.Product)
            .WithMany(product => product.Images)
            .HasForeignKey(image => image.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(image => image.Attachment)
            .WithMany()
            .HasForeignKey(image => image.AttachmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
