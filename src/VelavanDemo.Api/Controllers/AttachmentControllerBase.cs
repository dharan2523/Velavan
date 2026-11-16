using Microsoft.AspNetCore.Mvc;
using VelavanDemo.Application.Common;

namespace VelavanDemo.Api.Controllers;

public abstract class AttachmentControllerBase : ApiControllerBase
{
    protected const long MaxUploadRequestBytes = 26L * 1024 * 1024;

    protected IActionResult ToAttachmentErrorResult(OperationResult result)
    {
        return result.Error switch
        {
            OperationError.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Resource not found",
                detail: result.Message),
            OperationError.FileTooLarge => Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "File too large",
                detail: result.Message),
            OperationError.UnsupportedFileType => Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Unsupported file type",
                detail: result.Message),
            OperationError.AttachmentLimitReached => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Attachment limit reached",
                detail: result.Message),
            OperationError.AttachmentConflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Attachment conflict",
                detail: result.Message),
            OperationError.MalwareDetected => Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Unsafe file",
                detail: result.Message),
            OperationError.ScanUnavailable => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Security scan unavailable",
                detail: result.Message),
            OperationError.StorageQuotaExceeded => Problem(
                statusCode: StatusCodes.Status507InsufficientStorage,
                title: "Storage quota exceeded",
                detail: result.Message),
            OperationError.Forbidden => Forbid(),
            OperationError.StorageFailure => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Attachment storage failure",
                detail: result.Message),
            _ => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Request failed",
                detail: result.Message)
        };
    }

    protected IActionResult ToAttachmentErrorResult<T>(OperationResult<T> result)
    {
        return result.Error switch
        {
            OperationError.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Resource not found",
                detail: result.Message),
            OperationError.FileTooLarge => Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "File too large",
                detail: result.Message),
            OperationError.UnsupportedFileType => Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Unsupported file type",
                detail: result.Message),
            OperationError.AttachmentLimitReached => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Attachment limit reached",
                detail: result.Message),
            OperationError.AttachmentConflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Attachment conflict",
                detail: result.Message),
            OperationError.MalwareDetected => Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Unsafe file",
                detail: result.Message),
            OperationError.ScanUnavailable => Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Security scan unavailable",
                detail: result.Message),
            OperationError.StorageQuotaExceeded => Problem(
                statusCode: StatusCodes.Status507InsufficientStorage,
                title: "Storage quota exceeded",
                detail: result.Message),
            OperationError.Forbidden => Forbid(),
            OperationError.StorageFailure => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Attachment storage failure",
                detail: result.Message),
            _ => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Request failed",
                detail: result.Message)
        };
    }
}
