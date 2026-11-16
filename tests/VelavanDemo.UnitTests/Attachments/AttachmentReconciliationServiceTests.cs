using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VelavanDemo.Application.Attachments;
using VelavanDemo.Domain.Entities;
using VelavanDemo.Domain.Enums;
using VelavanDemo.Infrastructure.Attachments;
using VelavanDemo.Infrastructure.Persistence;

namespace VelavanDemo.UnitTests.Attachments;

public sealed class AttachmentReconciliationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;

    public AttachmentReconciliationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"VelavanDemo.ReconciliationTests.{Guid.NewGuid():N}")
            .Options;

        _dbContext = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task ReconcileAsync_DeletesOrphansAndReportsMissingFiles()
    {
        _dbContext.Attachments.Add(CreateAttachment("2026/01/keep.pdf"));
        _dbContext.Attachments.Add(CreateAttachment("2026/01/missing.pdf"));
        await _dbContext.SaveChangesAsync();

        var storage = new TrackingFileStorage();
        var inventory = new StaticInventory(
            "2026/01/keep.pdf",
            "2026/01/orphan.pdf");
        var gate = new AttachmentConcurrencyGate();
        var service = new AttachmentReconciliationService(
            _dbContext,
            storage,
            inventory,
            gate,
            Microsoft.Extensions.Options.Options.Create(
                new AttachmentReconciliationOptions { OrphanGracePeriodMinutes = 0 }),
            NullLogger<AttachmentReconciliationService>.Instance);

        var result = await service.ReconcileAsync();

        Assert.Equal(1, result.DeletedOrphanCount);
        Assert.Equal(1, result.MissingFileCount);
        Assert.Equal(new[] { "2026/01/orphan.pdf" }, storage.DeletedKeys);
    }

    [Fact]
    public async Task ReconcileAsync_DoesNotDeleteOrphanWithinGracePeriod()
    {
        var storage = new TrackingFileStorage();
        var inventory = new StaticInventory("2026/01/recent.pdf");
        var service = new AttachmentReconciliationService(
            _dbContext,
            storage,
            inventory,
            new AttachmentConcurrencyGate(),
            Microsoft.Extensions.Options.Options.Create(
                new AttachmentReconciliationOptions { OrphanGracePeriodMinutes = 30 }),
            NullLogger<AttachmentReconciliationService>.Instance);

        var result = await service.ReconcileAsync();

        Assert.Equal(0, result.DeletedOrphanCount);
        Assert.Empty(storage.DeletedKeys);
    }

    [Fact]
    public async Task ReconcileAsync_DeletesOrphanAfterGracePeriod()
    {
        var storage = new TrackingFileStorage();
        var inventory = new StaticInventory(
            new Dictionary<string, DateTimeOffset>
            {
                ["2026/01/old.pdf"] = DateTimeOffset.UtcNow.AddMinutes(-60)
            },
            "2026/01/old.pdf");
        var service = new AttachmentReconciliationService(
            _dbContext,
            storage,
            inventory,
            new AttachmentConcurrencyGate(),
            Microsoft.Extensions.Options.Options.Create(
                new AttachmentReconciliationOptions { OrphanGracePeriodMinutes = 30 }),
            NullLogger<AttachmentReconciliationService>.Instance);

        var result = await service.ReconcileAsync();

        Assert.Equal(1, result.DeletedOrphanCount);
        Assert.Equal(new[] { "2026/01/old.pdf" }, storage.DeletedKeys);
    }

    [Fact]
    public async Task ReconcileAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var service = new AttachmentReconciliationService(
            _dbContext,
            new TrackingFileStorage(),
            new StaticInventory(),
            new AttachmentConcurrencyGate(),
            Microsoft.Extensions.Options.Options.Create(
                new AttachmentReconciliationOptions()),
            NullLogger<AttachmentReconciliationService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ReconcileAsync(cancellationTokenSource.Token));
        Assert.True(true);
    }

    private static Attachment CreateAttachment(string storageKey)
    {
        return new Attachment
        {
            Kind = AttachmentKind.CustomerDocument,
            StorageProvider = LocalFileStorage.ProviderName,
            StorageKey = storageKey,
            OriginalFileName = Path.GetFileName(storageKey),
            ContentType = "application/pdf",
            FileExtension = ".pdf",
            SizeBytes = 10,
            Sha256 = new string('a', 64)
        };
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private sealed class StaticInventory : IFileStorageInventory
    {
        private readonly IReadOnlyList<string> _keys;
        private readonly Dictionary<string, DateTimeOffset> _lastWriteTimes;

        public StaticInventory(params string[] keys)
        {
            _keys = keys;
            _lastWriteTimes = keys.ToDictionary(
                key => key,
                _ => DateTimeOffset.UtcNow);
        }

        public StaticInventory(
            Dictionary<string, DateTimeOffset> lastWriteTimes,
            params string[] keys)
        {
            _keys = keys;
            _lastWriteTimes = lastWriteTimes;
        }

        public Task<IReadOnlyList<string>> ListStorageKeysAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_keys);
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
            return Task.FromResult<DateTimeOffset?>(
                _lastWriteTimes.TryGetValue(storageKey, out var lastWriteTimeUtc)
                    ? lastWriteTimeUtc
                    : null);
        }
    }

    private sealed class TrackingFileStorage : IFileStorage
    {
        public List<string> DeletedKeys { get; } = new();

        public Task<StoredFile> SaveAsync(
            Stream content,
            string fileExtension,
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(storageKey);
            return Task.CompletedTask;
        }
    }
}
