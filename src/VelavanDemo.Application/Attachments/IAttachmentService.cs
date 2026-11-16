using VelavanDemo.Application.Common;

namespace VelavanDemo.Application.Attachments;

public interface IAttachmentService
{
    Task<OperationResult<IReadOnlyList<ProductImageResponse>>> GetProductImagesAsync(
        int productId,
        CancellationToken cancellationToken = default);

    Task<OperationResult<ProductImageResponse>> UploadProductImageAsync(
        int productId,
        AttachmentUpload upload,
        int? uploadedByUserId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> SetPrimaryProductImageAsync(
        int productId,
        int attachmentId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ReorderProductImagesAsync(
        int productId,
        ReorderProductImagesRequest? request,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteProductImageAsync(
        int productId,
        int attachmentId,
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<AttachmentResponse>>> GetCustomerDocumentsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    Task<OperationResult<AttachmentResponse>> UploadCustomerDocumentAsync(
        int customerId,
        AttachmentUpload upload,
        int? uploadedByUserId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteCustomerDocumentAsync(
        int customerId,
        int attachmentId,
        CancellationToken cancellationToken = default);

    Task<OperationResult<IReadOnlyList<AttachmentResponse>>> GetOrderAttachmentsAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<OperationResult<AttachmentResponse>> UploadOrderAttachmentAsync(
        int orderId,
        AttachmentUpload upload,
        int? uploadedByUserId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteOrderAttachmentAsync(
        int orderId,
        int attachmentId,
        CancellationToken cancellationToken = default);

    Task<OperationResult<AttachmentResponse>> GetAttachmentAsync(
        int attachmentId,
        CancellationToken cancellationToken = default);

    Task<OperationResult<AttachmentContent>> GetAttachmentContentAsync(
        int attachmentId,
        bool download,
        CancellationToken cancellationToken = default);
}
