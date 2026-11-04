using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VelavanDemo.Application.Attachments;

namespace VelavanDemo.Infrastructure.Attachments;

public sealed class LocalFileStorage : IFileStorage
{
    public const string ProviderName = "Local";

    private const int BufferSize = 81_920;

    private readonly string _rootPath;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(
        IOptions<AttachmentStorageOptions> options,
        ILogger<LocalFileStorage> logger)
    {
        var configuredRootPath = options.Value.RootPath;

        if (string.IsNullOrWhiteSpace(configuredRootPath))
        {
            throw new InvalidOperationException(
                $"Attachment storage root path '{AttachmentStorageOptions.SectionName}:RootPath' is not configured.");
        }

        _rootPath = Path.GetFullPath(
            Path.IsPathRooted(configuredRootPath)
                ? configuredRootPath
                : Path.Combine(AppContext.BaseDirectory, configuredRootPath));

        _logger = logger;
    }

    public async Task<StoredFile> SaveAsync(
        Stream content,
        string fileExtension,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        var normalizedExtension = NormalizeExtension(fileExtension);
        var now = DateTime.UtcNow;
        var storageKey = string.Join(
            '/',
            now.ToString("yyyy", CultureInfo.InvariantCulture),
            now.ToString("MM", CultureInfo.InvariantCulture),
            $"{Guid.NewGuid():N}{normalizedExtension}");

        var finalPath = ResolvePath(storageKey);
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidOperationException("Attachment storage path is invalid.");
        var temporaryPath = Path.Combine(directory, $".upload-{Guid.NewGuid():N}.tmp");

        Directory.CreateDirectory(directory);

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var completed = false;

        try
        {
            long totalBytes = 0;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var bytesRead = 0;

                while ((bytesRead = await content.ReadAsync(
                           buffer.AsMemory(0, BufferSize),
                           cancellationToken)) > 0)
                {
                    totalBytes += bytesRead;

                    if (totalBytes > maxBytes)
                    {
                        throw new AttachmentFileTooLargeException();
                    }

                    hash.AppendData(buffer, 0, bytesRead);
                    await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }

                await target.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, finalPath);
            completed = true;

            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

            _logger.LogInformation(
                "Stored attachment {StorageProvider} file with key {StorageKey} ({SizeBytes} bytes).",
                ProviderName,
                storageKey,
                totalBytes);

            return new StoredFile(ProviderName, storageKey, totalBytes, sha256);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);

            if (!completed && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePath(storageKey);

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePath(storageKey);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new InvalidOperationException("Attachment storage key is invalid.");
        }

        var relativePath = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        var rootPrefix = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Attachment storage key is invalid.");
        }

        return fullPath;
    }

    private static string NormalizeExtension(string fileExtension)
    {
        var normalized = (fileExtension ?? string.Empty).Trim().ToLowerInvariant();

        if (normalized.Length < 2 ||
            normalized.Length > 11 ||
            normalized[0] != '.' ||
            normalized[1..].Any(character => !char.IsLetterOrDigit(character)))
        {
            throw new InvalidOperationException("Attachment file extension is invalid.");
        }

        return normalized;
    }
}
