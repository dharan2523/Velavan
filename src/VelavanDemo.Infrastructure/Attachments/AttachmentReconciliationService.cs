using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VelavanDemo.Application.Attachments;
using VelavanDemo.Infrastructure.Persistence;

namespace VelavanDemo.Infrastructure.Attachments;

public sealed class AttachmentReconciliationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorage _fileStorage;
    private readonly IFileStorageInventory _inventory;
    private readonly IAttachmentConcurrencyGate _mutationGate;
    private readonly AttachmentReconciliationOptions _options;
    private readonly ILogger<AttachmentReconciliationService> _logger;

    public AttachmentReconciliationService(
        ApplicationDbContext dbContext,
        IFileStorage fileStorage,
        IFileStorageInventory inventory,
        IAttachmentConcurrencyGate mutationGate,
        IOptions<AttachmentReconciliationOptions> options,
        ILogger<AttachmentReconciliationService> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _inventory = inventory;
        _mutationGate = mutationGate;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AttachmentReconciliationResult> ReconcileAsync(
        CancellationToken cancellationToken = default)
    {
        await using var lease = await _mutationGate.AcquireAsync(cancellationToken);

        var databaseKeys = await _dbContext.Attachments
            .AsNoTracking()
            .Select(attachment => attachment.StorageKey)
            .ToListAsync(cancellationToken);

        var physicalKeys = await _inventory.ListStorageKeysAsync(cancellationToken);
        var databaseSet = new HashSet<string>(databaseKeys, StringComparer.Ordinal);
        var physicalSet = new HashSet<string>(physicalKeys, StringComparer.Ordinal);
        var gracePeriod = TimeSpan.FromMinutes(Math.Max(0, _options.OrphanGracePeriodMinutes));
        var deletedOrphans = 0;

        foreach (var orphanKey in physicalSet.Except(databaseSet, StringComparer.Ordinal))
        {
            var lastWriteUtc = await _inventory.GetLastWriteTimeUtcAsync(orphanKey, cancellationToken);

            if (lastWriteUtc.HasValue &&
                DateTimeOffset.UtcNow - lastWriteUtc.Value < gracePeriod)
            {
                continue;
            }

            try
            {
                await _fileStorage.DeleteAsync(orphanKey, cancellationToken);
                deletedOrphans++;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Orphaned attachment file {StorageKey} could not be deleted during reconciliation.",
                    orphanKey);
            }
        }

        var missingKeys = databaseSet.Except(physicalSet, StringComparer.Ordinal).ToArray();

        foreach (var missingKey in missingKeys)
        {
            _logger.LogWarning(
                "Attachment metadata references missing stored file {StorageKey}.",
                missingKey);
        }

        if (deletedOrphans > 0 || missingKeys.Length > 0)
        {
            _logger.LogWarning(
                "Attachment reconciliation removed {DeletedOrphanCount} orphaned files and found {MissingFileCount} missing files.",
                deletedOrphans,
                missingKeys.Length);
        }

        return new AttachmentReconciliationResult(deletedOrphans, missingKeys.Length);
    }
}
