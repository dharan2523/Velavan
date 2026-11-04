using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class OrderAttachmentConfiguration : BaseEntityConfiguration<OrderAttachment>
{
    public override void Configure(EntityTypeBuilder<OrderAttachment> builder)
    {
        base.Configure(builder);

        builder.ToTable("OrderAttachments");

        builder.HasIndex(attachment => attachment.AttachmentId)
            .IsUnique();

        builder.HasIndex(attachment => attachment.OrderId);

        builder.HasOne(attachment => attachment.Order)
            .WithMany(order => order.Attachments)
            .HasForeignKey(attachment => attachment.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(attachment => attachment.Attachment)
            .WithMany()
            .HasForeignKey(attachment => attachment.AttachmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
