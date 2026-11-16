using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VelavanDemo.Application.Attachments;
using VelavanDemo.Application.Common;
using VelavanDemo.Domain.Entities;
using VelavanDemo.Domain.Enums;
using VelavanDemo.Infrastructure.Persistence;

namespace VelavanDemo.Infrastructure.Attachments;

public sealed class AttachmentService : IAttachmentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorage _fileStorage;
    private readonly IFileStorageInventory _fileStorageInventory;
    private readonly IAttachmentFileInspector _fileInspector;
    private readonly IUploadedFileScanner _fileScanner;
    private readonly IAttachmentConcurrencyGate _mutationGate;
    private readonly AttachmentStorageOptions _storageOptions;
    private readonly ILogger<AttachmentService> _logger;

    public AttachmentService(
        ApplicationDbContext dbContext,
        IFileStorage fileStorage,
        IFileStorageInventory fileStorageInventory,
        IAttachmentFileInspector fileInspector,
        IUploadedFileScanner fileScanner,
        IAttachmentConcurrencyGate mutationGate,
        IOptions<AttachmentStorageOptions> storageOptions,
        ILogger<AttachmentService> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _fileStorageInventory = fileStorageInventory;
        _fileInspector = fileInspector;
        _fileScanner = fileScanner;
        _mutationGate = mutationGate;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    public async Task<OperationResult<IReadOnlyList<ProductImageResponse>>> GetProductImagesAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        if (!await ProductExistsAsync(productId, cancellationToken))
        {
            return OperationResult<IReadOnlyList<ProductImageResponse>>.Failure(
                OperationError.NotFound,
                "Product was not found.");
        }

        var images = await _dbContext.ProductImages
            .AsNoTracking()
            .Include(image => image.Attachment)
            .Where(image => image.ProductId == productId)
            .OrderByDescending(image => image.IsPrimary)
            .ThenBy(image => image.SortOrder)
            .ThenBy(image => image.Id)
            .ToListAsync(cancellationToken);

        return OperationResult<IReadOnlyList<ProductImageResponse>>.Success(
            images.Select(Map).ToArray());
    }

    public async Task<OperationResult<ProductImageResponse>> UploadProductImageAsync(
        int productId,
        AttachmentUpload upload,
        int? uploadedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await ProductExistsAsync(productId, cancellationToken))
        {
            return OperationResult<ProductImageResponse>.Failure(
                OperationError.NotFound,
                "Product was not found.");
        }

        await using var lease = await _mutationGate.AcquireAsync(cancellationToken);

        return await ExecuteInTransactionAsync(async () =>
        {
            var policy = AttachmentPolicies.For(AttachmentKind.ProductImage);
            var existingImageCount = await _dbContext.ProductImages
                .CountAsync(image => image.ProductId == productId, cancellationToken);

            if (existingImageCount >= policy.MaxCount)
            {
                return OperationResult<ProductImageResponse>.Failure(
                    OperationError.AttachmentLimitReached,
                    $"A product can have at most {policy.MaxCount} images.");
            }

            var quotaFailure = await CheckStorageQuotaAsync(upload.Length, cancellationToken);

            if (quotaFailure is not null)
            {
                return OperationResult<ProductImageResponse>.Failure(
                    quotaFailure.Error,
                    quotaFailure.Message!);
            }

            var nextSortOrder = existingImageCount == 0
                ? 0
                : (await _dbContext.ProductImages
                    .Where(image => image.ProductId == productId)
                    .MaxAsync(image => (int?)image.SortOrder, cancellationToken) ?? -1) + 1;

            var storedAttachment = await CreateAttachmentAsync(
                AttachmentKind.ProductImage,
                upload,
                uploadedByUserId,
                cancellationToken);

            if (!storedAttachment.Succeeded)
            {
                return OperationResult<ProductImageResponse>.Failure(
                    storedAttachment.Error,
                    storedAttachment.Message!);
            }

            var attachment = storedAttachment.Value!;
            var productImage = new ProductImage
            {
                ProductId = productId,
                Attachment = attachment,
                IsPrimary = existingImageCount == 0,
                SortOrder = nextSortOrder
            };

            _dbContext.Attachments.Add(attachment);
            _dbContext.ProductImages.Add(productImage);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                await TryDeleteStoredFileAsync(attachment.StorageKey);
                _dbContext.ChangeTracker.Clear();

                return OperationResult<ProductImageResponse>.Failure(
                    OperationError.AttachmentConflict,
                    "The product image changed while it was being saved. Refresh and try again.");
            }
            catch
            {
                await TryDeleteStoredFileAsync(attachment.StorageKey);
                throw;
            }

            return OperationResult<ProductImageResponse>.Success(Map(productImage));
        }, cancellationToken);
    }

    public async Task<OperationResult> SetPrimaryProductImageAsync(
        int productId,
        int attachmentId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await _mutationGate.AcquireAsync(cancellationToken);

        try
        {
            return await ExecuteInTransactionAsync(async () =>
            {
                var images = await _dbContext.ProductImages
                    .Include(image => image.Attachment)
                    .Where(image => image.ProductId == productId)
                    .ToListAsync(cancellationToken);

                var target = images.SingleOrDefault(image => image.AttachmentId == attachmentId);

                if (target is null)
                {
                    return OperationResult.Failure(
                        OperationError.NotFound,
                        "Product image was not found.");
                }

                if (target.IsPrimary)
                {
                    return OperationResult.Success();
                }

                foreach (var image in images.Where(image => image.IsPrimary))
                {
                    image.IsPrimary = false;
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                target.IsPrimary = true;
                await _dbContext.SaveChangesAsync(cancellationToken);

                return OperationResult.Success();
            }, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();

            return OperationResult.Failure(
                OperationError.AttachmentConflict,
                "The primary image changed while it was being saved. Refresh and try again.");
        }
    }

    public async Task<OperationResult> ReorderProductImagesAsync(
        int productId,
        ReorderProductImagesRequest? request,
        CancellationToken cancellationToken = default)
    {
        var requestedIds = request?.AttachmentIds;

        if (requestedIds is null || requestedIds.Count == 0)
        {
            return OperationResult.Failure(
                OperationError.Validation,
                "Provide every product image exactly once when reordering.");
        }

        await using var lease = await _mutationGate.AcquireAsync(cancellationToken);

        return await ExecuteInTransactionAsync(async () =>
        {
            if (!await ProductExistsAsync(productId, cancellationToken))
            {
                return OperationResult.Failure(OperationError.NotFound, "Product was not found.");
            }

            var images = await _dbContext.ProductImages
                .Where(image => image.ProductId == productId)
                .ToListAsync(cancellationToken);

            if (requestedIds.Count != images.Count ||
                requestedIds.Distinct().Count() != requestedIds.Count ||
                !requestedIds.ToHashSet().SetEquals(images.Select(image => image.AttachmentId)))
            {
                return OperationResult.Failure(
                    OperationError.Validation,
                    "Provide every product image exactly once when reordering.");
            }

            var imagesById = images.ToDictionary(image => image.AttachmentId);
            var sortOrder = 0;

            foreach (var imageId in requestedIds)
            {
                imagesById[imageId].SortOrder = sortOrder++;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            return OperationResult.Success();
        }, cancellationToken);
    }

    public async Task<OperationResult> DeleteProductImageAsync(
        int productId,
        int attachmentId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await _mutationGate.AcquireAsync(cancellationToken);
        string? storageKey = null;

        var result = await ExecuteInTransactionAsync(async () =>
        {
            var image = await _dbContext.ProductImages
                .Include(candidate => candidate.Attachment)
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.ProductId == productId &&
                        candidate.AttachmentId == attachmentId,
                    cancellationToken);

            if (image is null)
            {
                return OperationResult.Failure(
                    OperationError.NotFound,
                    "Product image was not found.");
            }

            var replacement = image.IsPrimary
                ? await _dbContext.ProductImages
                    .Where(candidate =>
                        candidate.ProductId == productId &&
                        candidate.AttachmentId != attachmentId)
                    .OrderBy(candidate => candidate.SortOrder)
                    .ThenBy(candidate => candidate.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;

            var attachment = image.Attachment;
            storageKey = attachment.StorageKey;

            _dbContext.ProductImages.Remove(image);
            _dbContext.Attachments.Remove(attachment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (replacement is not null)
            {
                replacement.IsPrimary = true;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return OperationResult.Success();
        }, cancellationToken);

        if (result.Succeeded && storageKey is not null)
        {
            await TryDeleteStoredFileAsync(storageKey);
        }

        return result;
    }

    public async Task<OperationResult<IReadOnlyList<AttachmentResponse>>> GetCustomerDocumentsAsync(
        int customerId,
        CancellationToken cancellationToken = default)
    {
        if (!await CustomerExistsAsync(customerId, cancellationToken))
        {
            return OperationResult<IReadOnlyList<AttachmentResponse>>.Failure(
                OperationError.NotFound,
                "Customer was not found.");
        }

        var documents = await _dbContext.CustomerDocuments
            .AsNoTracking()
            .Include(document => document.Attachment)
            .Where(document => document.CustomerId == customerId)
            .OrderByDescending(document => document.Attachment.CreatedAtUtc)
            .ThenByDescending(document => document.AttachmentId)
            .ToListAsync(cancellationToken);

        return OperationResult<IReadOnlyList<AttachmentResponse>>.Success(
            documents.Select(document => Map(document.Attachment)).ToArray());
    }

    public async Task<OperationResult<AttachmentResponse>> UploadCustomerDocumentAsync(
        int customerId,
        AttachmentUpload upload,
        int? uploadedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await CustomerExistsAsync(customerId, cancellationToken))
        {
            return OperationResult<AttachmentResponse>.Failure(
                OperationError.NotFound,
                "Customer was not found.");
        }

        await using var lease = await _mutationGate.AcquireAsync(cancellationToken);

        return await ExecuteInTransactionAsync(async () =>
        {
            var policy = AttachmentPolicies.For(AttachmentKind.CustomerDocument);
            var existingCount = await _dbContext.CustomerDocuments
                .CountAsync(document => document.CustomerId == customerId, cancellationToken);

            if (existingCount >= policy.MaxCount)
            {
                return OperationResult<AttachmentResponse>.Failure(
                    OperationError.AttachmentLimitReached,
                    $"A customer can have at most {policy.MaxCount} documents.");
            }

            var quotaFailure = await CheckStorageQuotaAsync(upload.Length, cancellationToken);

            if (quotaFailure is not null)
            {
                return OperationResult<AttachmentResponse>.Failure(
                    quotaFailure.Error,
                    quotaFailure.Message!);
            }

            var storedAttachment = await CreateAttachmentAsync(
                AttachmentKind.CustomerDocument,
                upload,
                uploadedByUserId,
                cancellationToken);

            if (!storedAttachment.Succeeded)
            {
                return OperationResult<AttachmentResponse>.Failure(
                    storedAttachment.Error,
                    storedAttachment.Message!);
            }

            var attachment = storedAttachment.Value!;

            _dbContext.Attachments.Add(attachment);
            _dbContext.CustomerDocuments.Add(new CustomerDocument
            {
                CustomerId = customerId,
                Attachment = attachment
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                await TryDeleteStoredFileAsync(attachment.StorageKey);
                _dbContext.ChangeTracker.Clear();

                return OperationResult<AttachmentResponse>.Failure(
                    OperationError.AttachmentConflict,
                    "The customer document changed while it was being saved. Refresh and try again.");
            }
            catch
            {
                await TryDeleteStoredFileAsync(attachment.StorageKey);
                throw;
            }

            return OperationResult<AttachmentResponse>.Success(Map(attachment));
        }, cancellationToken);
    }

    public async Task<OperationResult> DeleteCustomerDocumentAsync(
        int customerId,
        int attachmentId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await _mutationGate.AcquireAsync(cancellationToken);
        string? storageKey = null;

        var result = await ExecuteInTransactionAsync(async () =>
        {
            var document = await _dbContext.CustomerDocuments
                .Include(candidate => candidate.Attachment)
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.CustomerId == customerId &&
                        candidate.AttachmentId == attachmentId,
                    cancellationToken);

            if (document is null)
            {
                return OperationResult.Failure(
                    OperationError.NotFound,
                    "Customer document was not found.");
            }

            var attachment = document.Attachment;
            storageKey = attachment.StorageKey;

            _dbContext.CustomerDocuments.Remove(document);
            _dbContext.Attachments.Remove(attachment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return OperationResult.Success();
        }, cancellationToken);

        if (result.Succeeded && storageKey is not null)
        {
            await TryDeleteStoredFileAsync(storageKey);
        }

        return result;
    }

    public async Task<OperationResult<IReadOnlyList<AttachmentResponse>>> GetOrderAttachmentsAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        if (!await OrderExistsAsync(orderId, cancellationToken))
        {
            return OperationResult<IReadOnlyList<AttachmentResponse>>.Failure(
                OperationError.NotFound,
                "Order was not found.");
        }

        var attachments = await _dbContext.OrderAttachments
            .AsNoTracking()
            .Include(orderAttachment => orderAttachment.Attachment)
            .Where(orderAttachment => orderAttachment.OrderId == orderId)
            .OrderByDescending(orderAttachment => orderAttachment.Attachment.CreatedAtUtc)
            .ThenByDescending(orderAttachment => orderAttachment.AttachmentId)
            .ToListAsync(cancellationToken);

        return OperationResult<IReadOnlyList<AttachmentResponse>>.Success(
            attachments.Select(orderAttachment => Map(orderAttachment.Attachment)).ToArray());
    }

    public async Task<OperationResult<AttachmentResponse>> UploadOrderAttachmentAsync(
        int orderId,
        AttachmentUpload upload,
        int? uploadedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await OrderExistsAsync(orderId, cancellationToken))
        {
            return OperationResult<AttachmentResponse>.Failure(
                OperationError.NotFound,
                "Order was not found.");
        }

        await using var lease = await _mutationGate.AcquireAsync(cancellationToken);

        return await ExecuteInTransactionAsync(async () =>
        {
            var policy = AttachmentPolicies.For(AttachmentKind.OrderAttachment);
            var existingCount = await _dbContext.OrderAttachments
                .CountAsync(orderAttachment => orderAttachment.OrderId == orderId, cancellationToken);

            if (existingCount >= policy.MaxCount)
            {
                return OperationResult<AttachmentResponse>.Failure(
                    OperationError.AttachmentLimitReached,
                    $"An order can have at most {policy.MaxCount} attachments.");
            }

            var quotaFailure = await CheckStorageQuotaAsync(upload.Length, cancellationToken);

            if (quotaFailure is not null)
            {
                return OperationResult<AttachmentResponse>.Failure(
                    quotaFailure.Error,
                    quotaFailure.Message!);
            }

            var storedAttachment = await CreateAttachmentAsync(
                AttachmentKind.OrderAttachment,
                upload,
                uploadedByUserId,
                cancellationToken);

            if (!storedAttachment.Succeeded)
            {
                return OperationResult<AttachmentResponse>.Failure(
                    storedAttachment.Error,
                    storedAttachment.Message!);
            }

            var attachment = storedAttachment.Value!;

            _dbContext.Attachments.Add(attachment);
            _dbContext.OrderAttachments.Add(new OrderAttachment
            {
                OrderId = orderId,
                Attachment = attachment
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                await TryDeleteStoredFileAsync(attachment.StorageKey);
                _dbContext.ChangeTracker.Clear();

                return OperationResult<AttachmentResponse>.Failure(
                    OperationError.AttachmentConflict,
                    "The order attachment changed while it was being saved. Refresh and try again.");
            }
            catch
            {
                await TryDeleteStoredFileAsync(attachment.StorageKey);
                throw;
            }

            return OperationResult<AttachmentResponse>.Success(Map(attachment));
        }, cancellationToken);
    }

    public async Task<OperationResult> DeleteOrderAttachmentAsync(
        int orderId,
        int attachmentId,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await _mutationGate.AcquireAsync(cancellationToken);
        string? storageKey = null;

        var result = await ExecuteInTransactionAsync(async () =>
        {
            var orderAttachment = await _dbContext.OrderAttachments
                .Include(candidate => candidate.Attachment)
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.OrderId == orderId &&
                        candidate.AttachmentId == attachmentId,
                    cancellationToken);

            if (orderAttachment is null)
            {
                return OperationResult.Failure(
                    OperationError.NotFound,
                    "Order attachment was not found.");
            }

            var attachment = orderAttachment.Attachment;
            storageKey = attachment.StorageKey;

            _dbContext.OrderAttachments.Remove(orderAttachment);
            _dbContext.Attachments.Remove(attachment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return OperationResult.Success();
        }, cancellationToken);

        if (result.Succeeded && storageKey is not null)
        {
            await TryDeleteStoredFileAsync(storageKey);
        }

        return result;
    }

    public async Task<OperationResult<AttachmentResponse>> GetAttachmentAsync(
        int attachmentId,
        CancellationToken cancellationToken = default)
    {
        var attachment = await _dbContext.Attachments
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == attachmentId, cancellationToken);

        return attachment is null
            ? OperationResult<AttachmentResponse>.Failure(
                OperationError.NotFound,
                "Attachment was not found.")
            : OperationResult<AttachmentResponse>.Success(Map(attachment));
    }

    public async Task<OperationResult<AttachmentContent>> GetAttachmentContentAsync(
        int attachmentId,
        bool download,
        CancellationToken cancellationToken = default)
    {
        var attachment = await _dbContext.Attachments
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == attachmentId, cancellationToken);

        if (attachment is null)
        {
            return OperationResult<AttachmentContent>.Failure(
                OperationError.NotFound,
                "Attachment was not found.");
        }

        try
        {
            var content = await _fileStorage.OpenReadAsync(
                attachment.StorageKey,
                cancellationToken);

            var inline = !download && AttachmentPolicies.CanPreviewInline(attachment.ContentType);

            return OperationResult<AttachmentContent>.Success(
                new AttachmentContent(
                    content,
                    attachment.ContentType,
                    attachment.OriginalFileName,
                    inline,
                    $"\"{attachment.Sha256}\""));
        }
        catch (FileNotFoundException exception)
        {
            _logger.LogWarning(
                exception,
                "Attachment {AttachmentId} metadata exists but its content is missing.",
                attachmentId);

            return OperationResult<AttachmentContent>.Failure(
                OperationError.StorageFailure,
                "The attachment content is unavailable.");
        }
        catch (IOException exception)
        {
            _logger.LogError(
                exception,
                "Attachment {AttachmentId} content could not be opened.",
                attachmentId);

            return OperationResult<AttachmentContent>.Failure(
                OperationError.StorageFailure,
                "The attachment content is unavailable.");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogError(
                exception,
                "Attachment {AttachmentId} content access was denied.",
                attachmentId);

            return OperationResult<AttachmentContent>.Failure(
                OperationError.StorageFailure,
                "The attachment content is unavailable.");
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(
                exception,
                "Attachment {AttachmentId} has an invalid storage key.",
                attachmentId);

            return OperationResult<AttachmentContent>.Failure(
                OperationError.StorageFailure,
                "The attachment content is unavailable.");
        }
    }

    private async Task<OperationResult<Attachment>> CreateAttachmentAsync(
        AttachmentKind kind,
        AttachmentUpload upload,
        int? uploadedByUserId,
        CancellationToken cancellationToken)
    {
        var policy = AttachmentPolicies.For(kind);
        var sanitizedFileName = AttachmentFileName.Sanitize(upload.FileName);
        var extension = Path.GetExtension(sanitizedFileName).ToLowerInvariant();

        if (extension.Length == 0 || !policy.AllowedExtensions.Contains(extension))
        {
            return OperationResult<Attachment>.Failure(
                OperationError.UnsupportedFileType,
                "The file type is not allowed for this attachment.");
        }

        if (upload.Length <= 0)
        {
            return OperationResult<Attachment>.Failure(
                OperationError.Validation,
                "The uploaded file is empty.");
        }

        if (upload.Length > policy.MaxBytes)
        {
            return OperationResult<Attachment>.Failure(
                OperationError.FileTooLarge,
                $"The file exceeds the {policy.MaxBytes / (1024 * 1024)} MB limit.");
        }

        StoredFile storedFile;

        try
        {
            if (upload.Content.CanSeek)
            {
                upload.Content.Position = 0;
            }

            storedFile = await _fileStorage.SaveAsync(
                upload.Content,
                extension,
                policy.MaxBytes,
                cancellationToken);
        }
        catch (AttachmentFileTooLargeException)
        {
            return OperationResult<Attachment>.Failure(
                OperationError.FileTooLarge,
                $"The file exceeds the {policy.MaxBytes / (1024 * 1024)} MB limit.");
        }
        catch (IOException exception)
        {
            _logger.LogError(exception, "Attachment storage is unavailable while saving a file.");

            return OperationResult<Attachment>.Failure(
                OperationError.StorageFailure,
                "The attachment storage is unavailable.");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogError(exception, "Attachment storage denied access while saving a file.");

            return OperationResult<Attachment>.Failure(
                OperationError.StorageFailure,
                "The attachment storage is unavailable.");
        }

        try
        {
            AttachmentFileInfo? fileInfo = null;
            var validationError = OperationError.None;
            string? validationMessage = null;

            await using (var content = await _fileStorage.OpenReadAsync(
                storedFile.StorageKey,
                cancellationToken))
            {
                var inspection = _fileInspector.Inspect(
                    content,
                    kind,
                    sanitizedFileName,
                    upload.ContentType,
                    storedFile.SizeBytes);

                if (!inspection.Succeeded)
                {
                    validationError = inspection.Error;
                    validationMessage = inspection.Message;
                }
                else
                {
                    content.Position = 0;

                    var scanResult = await _fileScanner.ScanAsync(
                        content,
                        sanitizedFileName,
                        inspection.Value!.ContentType,
                        cancellationToken);

                    if (!scanResult.Succeeded)
                    {
                        validationError = scanResult.Error;
                        validationMessage = scanResult.Message
                            ?? "The uploaded file failed security scanning.";
                    }
                    else
                    {
                        fileInfo = inspection.Value;
                    }
                }
            }

            if (validationError != OperationError.None)
            {
                await TryDeleteStoredFileAsync(storedFile.StorageKey);

                return OperationResult<Attachment>.Failure(
                    validationError,
                    validationMessage!);
            }

            return OperationResult<Attachment>.Success(new Attachment
            {
                Kind = kind,
                StorageProvider = storedFile.Provider,
                StorageKey = storedFile.StorageKey,
                OriginalFileName = sanitizedFileName,
                ContentType = fileInfo!.ContentType,
                FileExtension = fileInfo.FileExtension,
                SizeBytes = storedFile.SizeBytes,
                Sha256 = storedFile.Sha256,
                Width = fileInfo.Width,
                Height = fileInfo.Height,
                UploadedByUserId = uploadedByUserId
            });
        }
        catch (OperationCanceledException)
        {
            await TryDeleteStoredFileAsync(storedFile.StorageKey);
            throw;
        }
        catch (IOException exception)
        {
            _logger.LogError(
                exception,
                "Stored attachment file key {StorageKey} could not be read for validation.",
                storedFile.StorageKey);

            await TryDeleteStoredFileAsync(storedFile.StorageKey);

            return OperationResult<Attachment>.Failure(
                OperationError.StorageFailure,
                "The attachment storage is unavailable.");
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogError(
                exception,
                "Stored attachment file key {StorageKey} could not be accessed for validation.",
                storedFile.StorageKey);

            await TryDeleteStoredFileAsync(storedFile.StorageKey);

            return OperationResult<Attachment>.Failure(
                OperationError.StorageFailure,
                "The attachment storage is unavailable.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Attachment validation failed for stored file key {StorageKey}.",
                storedFile.StorageKey);

            await TryDeleteStoredFileAsync(storedFile.StorageKey);

            return OperationResult<Attachment>.Failure(
                OperationError.Validation,
                "The uploaded file could not be validated.");
        }
    }

    private async Task<OperationResult?> CheckStorageQuotaAsync(
        long additionalBytes,
        CancellationToken cancellationToken)
    {
        if (_storageOptions.MaxTotalBytes <= 0)
        {
            return null;
        }

        try
        {
            var totalBytes = await _fileStorageInventory.GetTotalSizeBytesAsync(cancellationToken);

            if (totalBytes + additionalBytes > _storageOptions.MaxTotalBytes)
            {
                return OperationResult.Failure(
                    OperationError.StorageQuotaExceeded,
                    "The attachment storage quota has been reached.");
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Attachment storage quota could not be checked.");

            return OperationResult.Failure(
                OperationError.StorageFailure,
                "The attachment storage is unavailable.");
        }
    }

    private async Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await action();
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await action();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task TryDeleteStoredFileAsync(string storageKey)
    {
        try
        {
            await _fileStorage.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Stored attachment file {StorageKey} could not be deleted.",
                storageKey);
        }
    }

    private Task<bool> ProductExistsAsync(int productId, CancellationToken cancellationToken)
    {
        return _dbContext.Products
            .AsNoTracking()
            .AnyAsync(product => product.Id == productId, cancellationToken);
    }

    private Task<bool> CustomerExistsAsync(int customerId, CancellationToken cancellationToken)
    {
        return _dbContext.Customers
            .AsNoTracking()
            .AnyAsync(customer => customer.Id == customerId, cancellationToken);
    }

    private Task<bool> OrderExistsAsync(int orderId, CancellationToken cancellationToken)
    {
        return _dbContext.Orders
            .AsNoTracking()
            .AnyAsync(order => order.Id == orderId, cancellationToken);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        if (exception.InnerException is SqlException sqlException &&
            sqlException.Number is 2601 or 2627)
        {
            return true;
        }

        var message = exception.InnerException?.Message ?? exception.Message;

        return message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase);
    }

    private static AttachmentResponse Map(Attachment attachment)
    {
        return new AttachmentResponse(
            attachment.Id,
            attachment.Kind,
            attachment.OriginalFileName,
            attachment.ContentType,
            attachment.FileExtension,
            attachment.SizeBytes,
            attachment.Width,
            attachment.Height,
            attachment.CreatedAtUtc);
    }

    private static ProductImageResponse Map(ProductImage image)
    {
        return new ProductImageResponse(
            image.AttachmentId,
            image.Attachment.OriginalFileName,
            image.Attachment.ContentType,
            image.Attachment.FileExtension,
            image.Attachment.SizeBytes,
            image.Attachment.Width,
            image.Attachment.Height,
            image.IsPrimary,
            image.SortOrder,
            image.Attachment.CreatedAtUtc);
    }
}
