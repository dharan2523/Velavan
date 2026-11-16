namespace VelavanDemo.Infrastructure.Attachments;

public sealed record AttachmentReconciliationResult(
    int DeletedOrphanCount,
    int MissingFileCount);
