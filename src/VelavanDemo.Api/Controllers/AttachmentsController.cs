using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VelavanDemo.Api.Authorization;
using VelavanDemo.Application.Attachments;

namespace VelavanDemo.Api.Controllers;

[ApiController]
[Route("api/attachments")]
[Authorize(Policy = AuthorizationPolicies.AdminOrSuperAdmin)]
public sealed class AttachmentsController : AttachmentControllerBase
{
    private readonly IAttachmentService _attachmentService;

    public AttachmentsController(IAttachmentService attachmentService)
    {
        _attachmentService = attachmentService;
    }

    [HttpGet("{attachmentId:int}")]
    [ProducesResponseType(typeof(AttachmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttachment(
        int attachmentId,
        CancellationToken cancellationToken)
    {
        var result = await _attachmentService.GetAttachmentAsync(
            attachmentId,
            cancellationToken);

        return result.Succeeded ? Ok(result.Value) : ToAttachmentErrorResult(result);
    }

    [HttpGet("{attachmentId:int}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetContent(
        int attachmentId,
        [FromQuery] bool download = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _attachmentService.GetAttachmentContentAsync(
            attachmentId,
            download,
            cancellationToken);

        if (!result.Succeeded)
        {
            return ToAttachmentErrorResult(result);
        }

        var content = result.Value!;

        if (Request.Headers.IfNoneMatch.Any(value =>
                string.Equals(value, content.ETag, StringComparison.Ordinal)))
        {
            content.Content.Dispose();
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers["Content-Disposition"] = BuildContentDisposition(
            content.OriginalFileName,
            content.Inline);
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["ETag"] = content.ETag;

        return new FileStreamResult(content.Content, content.ContentType)
        {
            EnableRangeProcessing = true
        };
    }

    private static string BuildContentDisposition(string fileName, bool inline)
    {
        var dispositionType = inline ? "inline" : "attachment";

        var fallbackFileName = new string(
            fileName
                .Select(character =>
                    character is >= (char)0x20 and <= (char)0x7E &&
                    character is not '"' and not '\\'
                        ? character
                        : '_')
                .ToArray());

        var encodedFileName = Uri.EscapeDataString(fileName).Replace("'", "%27", StringComparison.Ordinal);

        return $"{dispositionType}; filename=\"{fallbackFileName}\"; filename*=UTF-8''{encodedFileName}";
    }
}
