namespace VelavanDemo.Application.Attachments;

public interface IFileStorageInventory
{
    Task<IReadOnlyList<string>> ListStorageKeysAsync(
        CancellationToken cancellationToken = default);

    Task<long> GetTotalSizeBytesAsync(
        CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLastWriteTimeUtcAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}
