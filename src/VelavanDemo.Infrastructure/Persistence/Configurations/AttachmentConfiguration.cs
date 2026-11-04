using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VelavanDemo.Domain.Entities;

namespace VelavanDemo.Infrastructure.Persistence.Configurations;

internal sealed class AttachmentConfiguration : BaseEntityConfiguration<Attachment>
{
    public override void Configure(EntityTypeBuilder<Attachment> builder)
    {
        base.Configure(builder);

        builder.ToTable("Attachments");

        builder.Property(attachment => attachment.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(attachment => attachment.StorageProvider)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(attachment => attachment.StorageKey)
            .HasMaxLength(500)
            .IsUnicode(false)
            .IsRequired();

        builder.HasIndex(attachment => attachment.StorageKey)
            .IsUnique();

        builder.Property(attachment => attachment.OriginalFileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(attachment => attachment.ContentType)
            .HasMaxLength(100)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(attachment => attachment.FileExtension)
            .HasMaxLength(10)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(attachment => attachment.SizeBytes)
            .IsRequired();

        builder.Property(attachment => attachment.Sha256)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(attachment => attachment.Sha256);

        builder.HasIndex(attachment => attachment.Kind);
        builder.HasIndex(attachment => attachment.CreatedAtUtc);

        builder.HasOne(attachment => attachment.UploadedByUser)
            .WithMany(user => user.UploadedAttachments)
            .HasForeignKey(attachment => attachment.UploadedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
