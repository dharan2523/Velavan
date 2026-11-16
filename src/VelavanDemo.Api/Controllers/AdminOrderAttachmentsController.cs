using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VelavanDemo.Api.Authorization;
using VelavanDemo.Application.Attachments;

namespace VelavanDemo.Api.Controllers;

[ApiController]
[Route("api/admin/orders/{orderId:int}/attachments")]
[Authorize(Policy = AuthorizationPolicies.AdminOrSuperAdmin)]
public sealed class AdminOrderAttachmentsController : AttachmentControllerBase
{
    private readonly IAttachmentService _attachmentService;

    public AdminOrderAttachmentsController(IAttachmentService attachmentService)
    {
        _attachmentService = attachmentService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AttachmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachments(
        int orderId,
        CancellationToken cancellationToken)
    {
        var result = await _attachmentService.GetOrderAttachmentsAsync(
            orderId,
            cancellationToken);

        return result.Succeeded ? Ok(result.Value) : ToAttachmentErrorResult(result);
    }

    [HttpPost]
    [EnableRateLimiting("attachment-uploads")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType(typeof(AttachmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadAttachment(
        int orderId,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation failed",
                detail: "An attachment file is required.");
        }

        await using var content = file.OpenReadStream();

        var result = await _attachmentService.UploadOrderAttachmentAsync(
            orderId,
            new AttachmentUpload(content, file.FileName, file.ContentType, file.Length),
            CurrentUserId,
            cancellationToken);

        return result.Succeeded
            ? Created($"/api/attachments/{result.Value!.Id}", result.Value)
            : ToAttachmentErrorResult(result);
    }

    [HttpDelete("{attachmentId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAttachment(
        int orderId,
        int attachmentId,
        CancellationToken cancellationToken)
    {
        var result = await _attachmentService.DeleteOrderAttachmentAsync(
            orderId,
            attachmentId,
            cancellationToken);

        return result.Succeeded ? NoContent() : ToAttachmentErrorResult(result);
    }
}
