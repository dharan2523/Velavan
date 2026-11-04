namespace VelavanDemo.Application.Attachments;

public sealed record StoredFile(
    string Provider,
    string StorageKey,
    long SizeBytes,
    string Sha256);
