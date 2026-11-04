using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    private readonly IAttachmentFileInspector _fileInspector;
    private readonly IUploadedFileScanner _fileScanner;
    private readonly ILogger<AttachmentService> _logger;

    public AttachmentService(
        ApplicationDbContext dbContext,
        IFileStorage fileStorage,
        IAttachmentFileInspector fileInspector,
        IUploadedFileScanner fileScanner,
        ILogger<AttachmentService> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _fileInspector = fileInspector;
        _fileScanner = fileScanner;
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

        var policy = AttachmentPolicies.For(AttachmentKind.ProductImage);
        var existingImageCount = await _dbContext.ProductImages
            .CountAsync(image => image.ProductId == productId, cancellationToken);

        if (existingImageCount >= policy.MaxCount)
        {
            return OperationResult<ProductImageResponse>.Failure(
                OperationError.AttachmentLimitReached,
                $"A product can have at most {policy.MaxCount} images.");
        }

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
        var nextSortOrder = existingImageCount == 0
            ? 0
            : await _dbContext.ProductImages
                .Where(image => image.ProductId == productId)
                .MaxAsync(image => image.SortOrder, cancellationToken) + 1;

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
        catch
        {
            await TryDeleteStoredFileAsync(attachment, cancellationToken);
            throw;
        }

        return OperationResult<ProductImageResponse>.Success(Map(productImage));
    }

    public async Task<OperationResult> SetPrimaryProductImageAsync(
        int productId,
        int attachmentId,
        CancellationToken cancellationToken = default)
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
    }

    public async Task<OperationResult> ReorderProductImagesAsync(
        int productId,
        ReorderProductImagesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await ProductExistsAsync(productId, cancellationToken))
        {
            return OperationResult.Failure(OperationError.NotFound, "Product was not found.");
        }

        var images = await _dbContext.ProductImages
            .Where(image => image.ProductId == productId)
            .ToListAsync(cancellationToken);

        var requestedIds = request.AttachmentIds ?? new List<int>();

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
    }

    public async Task<OperationResult> DeleteProductImageAsync(
        int productId,
        int attachmentId,
        CancellationToken cancellationToken = default)
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

        _dbContext.ProductImages.Remove(image);
        _dbContext.Attachments.Remove(attachment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (replacement is not null)
        {
            replacement.IsPrimary = true;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await TryDeleteStoredFileAsync(attachment, cancellationToken);

        return OperationResult.Success();
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

        var policy = AttachmentPolicies.For(AttachmentKind.CustomerDocument);
        var existingCount = await _dbContext.CustomerDocuments
            .CountAsync(document => document.CustomerId == customerId, cancellationToken);

        if (existingCount >= policy.MaxCount)
        {
            return OperationResult<AttachmentResponse>.Failure(
                OperationError.AttachmentLimitReached,
                $"A customer can have at most {policy.MaxCount} documents.");
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
        catch
        {
            await TryDeleteStoredFileAsync(attachment, cancellationToken);
            throw;
        }

        return OperationResult<AttachmentResponse>.Success(Map(attachment));
    }

    public async Task<OperationResult> DeleteCustomerDocumentAsync(
        int customerId,
        int attachmentId,
        CancellationToken cancellationToken = default)
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

        _dbContext.CustomerDocuments.Remove(document);
        _dbContext.Attachments.Remove(attachment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await TryDeleteStoredFileAsync(attachment, cancellationToken);

        return OperationResult.Success();
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

        var policy = AttachmentPolicies.For(AttachmentKind.OrderAttachment);
        var existingCount = await _dbContext.OrderAttachments
            .CountAsync(orderAttachment => orderAttachment.OrderId == orderId, cancellationToken);

        if (existingCount >= policy.MaxCount)
        {
            return OperationResult<AttachmentResponse>.Failure(
                OperationError.AttachmentLimitReached,
                $"An order can have at most {policy.MaxCount} attachments.");
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
        catch
        {
            await TryDeleteStoredFileAsync(attachment, cancellationToken);
            throw;
        }

        return OperationResult<AttachmentResponse>.Success(Map(attachment));
    }

    public async Task<OperationResult> DeleteOrderAttachmentAsync(
        int orderId,
        int attachmentId,
        CancellationToken cancellationToken = default)
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

        _dbContext.OrderAttachments.Remove(orderAttachment);
        _dbContext.Attachments.Remove(attachment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await TryDeleteStoredFileAsync(attachment, cancellationToken);

        return OperationResult.Success();
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

            var inline = !download && attachment.ContentType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase);

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
                        validationError = OperationError.Validation;
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
                await _fileStorage.DeleteAsync(storedFile.StorageKey, cancellationToken);

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
            await TryDeleteStoredFileAsync(storedFile, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Attachment validation failed for stored file key {StorageKey}.",
                storedFile.StorageKey);

            await TryDeleteStoredFileAsync(storedFile, CancellationToken.None);

            return OperationResult<Attachment>.Failure(
                OperationError.Validation,
                "The uploaded file could not be validated.");
        }
    }

    private async Task TryDeleteStoredFileAsync(
        Attachment attachment,
        CancellationToken cancellationToken)
    {
        try
        {
            await _fileStorage.DeleteAsync(attachment.StorageKey, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Stored attachment file {StorageKey} could not be deleted.",
                attachment.StorageKey);
        }
    }

    private async Task TryDeleteStoredFileAsync(
        StoredFile storedFile,
        CancellationToken cancellationToken)
    {
        try
        {
            await _fileStorage.DeleteAsync(storedFile.StorageKey, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Stored attachment file {StorageKey} could not be deleted.",
                storedFile.StorageKey);
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
