using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VelavanDemo.Application.Attachments;
using VelavanDemo.Application.Common;
using VelavanDemo.Domain.Entities;
using VelavanDemo.Domain.Enums;
using VelavanDemo.Infrastructure.Attachments;
using VelavanDemo.Infrastructure.Persistence;

namespace VelavanDemo.UnitTests.Attachments;

public sealed class AttachmentServiceStorageTests : IDisposable
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nS8AAAAASUVORK5CYII=");

    private readonly ApplicationDbContext _dbContext;

    public AttachmentServiceStorageTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"VelavanDemo.AttachmentStorageTests.{Guid.NewGuid():N}")
            .Options;

        _dbContext = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task UploadProductImage_WhenStorageWriteFails_ReturnsStorageFailure()
    {
        var product = CreateProduct();
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var service = CreateService(new ThrowingFileStorage(
            saveException: new IOException("Disk unavailable.")));

        await using var content = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        var result = await service.UploadProductImageAsync(
            product.Id,
            new AttachmentUpload(content, "image.png", "image/png", content.Length),
            uploadedByUserId: null);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationError.StorageFailure, result.Error);
        Assert.Empty(_dbContext.Attachments);
    }

    [Fact]
    public async Task GetAttachmentContent_WhenPhysicalFileIsMissing_ReturnsStorageFailure()
    {
        var attachment = new Attachment
        {
            Kind = AttachmentKind.CustomerDocument,
            StorageProvider = LocalFileStorage.ProviderName,
            StorageKey = "2026/01/missing.pdf",
            OriginalFileName = "missing.pdf",
            ContentType = "application/pdf",
            FileExtension = ".pdf",
            SizeBytes = 10,
            Sha256 = new string('a', 64)
        };

        _dbContext.Attachments.Add(attachment);
        await _dbContext.SaveChangesAsync();

        var service = CreateService(new ThrowingFileStorage(
            openException: new FileNotFoundException("File is missing.")));

        var result = await service.GetAttachmentContentAsync(attachment.Id, download: true);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationError.StorageFailure, result.Error);
    }

    [Fact]
    public async Task UploadProductImage_WhenScanIsCancelled_DeletesStoredFileAndPropagates()
    {
        var product = CreateProduct();
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var storage = new TrackingFileStorage(ValidPng);
        var service = CreateService(
            storage,
            new ThrowingUploadedFileScanner(new OperationCanceledException()));

        await using var content = new MemoryStream(ValidPng);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.UploadProductImageAsync(
                product.Id,
                new AttachmentUpload(content, "image.png", "image/png", content.Length),
                uploadedByUserId: null));

        Assert.Single(storage.DeletedKeys);
        Assert.Empty(_dbContext.Attachments);
    }

    [Fact]
    public async Task DeleteProductImage_WhenPhysicalDeleteFails_StillDeletesMetadata()
    {
        var product = CreateProduct();
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var attachment = new Attachment
        {
            Kind = AttachmentKind.ProductImage,
            StorageProvider = LocalFileStorage.ProviderName,
            StorageKey = "2026/01/image.png",
            OriginalFileName = "image.png",
            ContentType = "image/png",
            FileExtension = ".png",
            SizeBytes = ValidPng.Length,
            Sha256 = new string('a', 64),
            Width = 1,
            Height = 1
        };
        var image = new ProductImage
        {
            ProductId = product.Id,
            Attachment = attachment,
            IsPrimary = true,
            SortOrder = 0
        };

        _dbContext.ProductImages.Add(image);
        await _dbContext.SaveChangesAsync();

        var storage = new TrackingFileStorage(
            ValidPng,
            deleteException: new IOException("The physical file is locked."));
        var service = CreateService(storage);

        var result = await service.DeleteProductImageAsync(product.Id, attachment.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(_dbContext.Attachments);
        Assert.Empty(_dbContext.ProductImages);
        Assert.Single(storage.DeletedKeys);
    }

    [Fact]
    public async Task UploadProductImage_WhenDatabaseReportsUniqueConflict_DeletesStoredFile()
    {
        var databaseName = $"VelavanDemo.AttachmentConflictTests.{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        int productId;

        using (var setupContext = new ApplicationDbContext(options))
        {
            var product = CreateProduct();
            setupContext.Products.Add(product);
            await setupContext.SaveChangesAsync();
            productId = product.Id;
        }

        using var conflictContext = new UniqueConflictDbContext(options);
        var storage = new TrackingFileStorage(ValidPng);
        var service = CreateService(conflictContext, storage);

        await using var content = new MemoryStream(ValidPng);

        var result = await service.UploadProductImageAsync(
            productId,
            new AttachmentUpload(content, "image.png", "image/png", content.Length),
            uploadedByUserId: null);

        Assert.False(result.Succeeded);
        Assert.Equal(OperationError.AttachmentConflict, result.Error);
        Assert.Single(storage.DeletedKeys);
    }

    private AttachmentService CreateService(
        IFileStorage fileStorage,
        IUploadedFileScanner? fileScanner = null)
    {
        return CreateService(
            _dbContext,
            fileStorage,
            fileScanner);
    }

    private static AttachmentService CreateService(
        ApplicationDbContext dbContext,
        IFileStorage fileStorage,
        IUploadedFileScanner? fileScanner = null)
    {
        return new AttachmentService(
            dbContext,
            fileStorage,
            new EmptyFileStorageInventory(),
            new AttachmentFileInspector(),
            fileScanner ?? new NoOpUploadedFileScanner(),
            new AttachmentConcurrencyGate(),
            Options.Create(new AttachmentStorageOptions()),
            NullLogger<AttachmentService>.Instance);
    }

    private static Product CreateProduct()
    {
        return new Product
        {
            Sku = $"SKU-{Guid.NewGuid():N}",
            Name = "Attachment test product",
            Category = new Category
            {
                Name = $"Category {Guid.NewGuid():N}",
                IsActive = true
            },
            UnitPrice = 10m,
            IsActive = true
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private sealed class ThrowingUploadedFileScanner : IUploadedFileScanner
    {
        private readonly Exception _exception;

        public ThrowingUploadedFileScanner(Exception exception)
        {
            _exception = exception;
        }

        public Task<OperationResult> ScanAsync(
            Stream content,
            string fileName,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<OperationResult>(_exception);
        }
    }

    private sealed class TrackingFileStorage : IFileStorage
    {
        private readonly byte[] _content;
        private readonly Exception? _deleteException;

        public TrackingFileStorage(byte[] content, Exception? deleteException = null)
        {
            _content = content;
            _deleteException = deleteException;
        }

        public List<string> DeletedKeys { get; } = new();

        public Task<StoredFile> SaveAsync(
            Stream content,
            string fileExtension,
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            var storageKey = $"2026/01/{Guid.NewGuid():N}{fileExtension}";

            return Task.FromResult(
                new StoredFile(
                    LocalFileStorage.ProviderName,
                    storageKey,
                    _content.Length,
                    new string('a', 64)));
        }

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream>(
                new MemoryStream(_content, writable: false));
        }

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(storageKey);

            return _deleteException is null
                ? Task.CompletedTask
                : Task.FromException(_deleteException);
        }
    }

    private sealed class UniqueConflictDbContext : ApplicationDbContext
    {
        public UniqueConflictDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<int>(
                new DbUpdateException(
                    "Saving changes failed.",
                    new Exception("Cannot insert duplicate key row.")));
        }
    }

    private sealed class EmptyFileStorageInventory : IFileStorageInventory
    {
        public Task<IReadOnlyList<string>> ListStorageKeysAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        public Task<long> GetTotalSizeBytesAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }

        public Task<DateTimeOffset?> GetLastWriteTimeUtcAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DateTimeOffset?>(null);
        }
    }

    private sealed class ThrowingFileStorage : IFileStorage
    {
        private readonly Exception? _saveException;
        private readonly Exception? _openException;

        public ThrowingFileStorage(Exception? saveException = null, Exception? openException = null)
        {
            _saveException = saveException;
            _openException = openException;
        }

        public Task<StoredFile> SaveAsync(
            Stream content,
            string fileExtension,
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            if (_saveException is not null)
            {
                return Task.FromException<StoredFile>(_saveException);
            }

            return Task.FromResult(
                new StoredFile(
                    LocalFileStorage.ProviderName,
                    $"2026/01/{Guid.NewGuid():N}{fileExtension}",
                    content.Length,
                    new string('a', 64)));
        }

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            return _openException is not null
                ? Task.FromException<Stream>(_openException)
                : Task.FromResult<Stream>(new MemoryStream());
        }

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
