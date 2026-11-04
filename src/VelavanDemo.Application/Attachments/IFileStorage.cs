namespace VelavanDemo.Application.Attachments;

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(
        Stream content,
        string fileExtension,
        long maxBytes,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}
