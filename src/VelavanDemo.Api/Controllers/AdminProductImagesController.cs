using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VelavanDemo.Api.Authorization;
using VelavanDemo.Application.Attachments;

namespace VelavanDemo.Api.Controllers;

[ApiController]
[Route("api/admin/products/{productId:int}/images")]
[Authorize(Policy = AuthorizationPolicies.AdminOrSuperAdmin)]
public sealed class AdminProductImagesController : AttachmentControllerBase
{
    private readonly IAttachmentService _attachmentService;

    public AdminProductImagesController(IAttachmentService attachmentService)
    {
        _attachmentService = attachmentService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProductImageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetImages(
        int productId,
        CancellationToken cancellationToken)
    {
        var result = await _attachmentService.GetProductImagesAsync(
            productId,
            cancellationToken);

        return result.Succeeded ? Ok(result.Value) : ToAttachmentErrorResult(result);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    [ProducesResponseType(typeof(ProductImageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> UploadImage(
        int productId,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation failed",
                detail: "An image file is required.");
        }

        await using var content = file.OpenReadStream();

        var result = await _attachmentService.UploadProductImageAsync(
            productId,
            new AttachmentUpload(content, file.FileName, file.ContentType, file.Length),
            CurrentUserId,
            cancellationToken);

        return result.Succeeded
            ? Created($"/api/attachments/{result.Value!.Id}", result.Value)
            : ToAttachmentErrorResult(result);
    }

    [HttpPatch("{attachmentId:int}/primary")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPrimaryImage(
        int productId,
        int attachmentId,
        CancellationToken cancellationToken)
    {
        var result = await _attachmentService.SetPrimaryProductImageAsync(
            productId,
            attachmentId,
            cancellationToken);

        return result.Succeeded ? NoContent() : ToAttachmentErrorResult(result);
    }

    [HttpPut("order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReorderImages(
        int productId,
        [FromBody] ReorderProductImagesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _attachmentService.ReorderProductImagesAsync(
            productId,
            request,
            cancellationToken);

        return result.Succeeded ? NoContent() : ToAttachmentErrorResult(result);
    }

    [HttpDelete("{attachmentId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(
        int productId,
        int attachmentId,
        CancellationToken cancellationToken)
    {
        var result = await _attachmentService.DeleteProductImageAsync(
            productId,
            attachmentId,
            cancellationToken);

        return result.Succeeded ? NoContent() : ToAttachmentErrorResult(result);
    }
}
