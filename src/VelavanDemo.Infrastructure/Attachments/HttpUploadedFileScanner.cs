using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VelavanDemo.Application.Attachments;
using VelavanDemo.Application.Common;

namespace VelavanDemo.Infrastructure.Attachments;

public sealed class HttpUploadedFileScanner : IUploadedFileScanner
{
    private readonly HttpClient _httpClient;
    private readonly AttachmentScannerOptions _options;
    private readonly ILogger<HttpUploadedFileScanner> _logger;

    public HttpUploadedFileScanner(
        HttpClient httpClient,
        IOptions<AttachmentScannerOptions> options,
        ILogger<HttpUploadedFileScanner> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OperationResult> ScanAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            return OperationResult.Failure(
                OperationError.ScanUnavailable,
                "Malware scanning is not configured.");
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(content);

        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(fileContent, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = form
        };

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return OperationResult.Success();
            }

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            {
                _logger.LogWarning(
                    "Malware scanner rejected uploaded file {FileName} with status {StatusCode}.",
                    fileName,
                    (int)response.StatusCode);

                return OperationResult.Failure(
                    OperationError.MalwareDetected,
                    "The uploaded file failed security scanning.");
            }

            _logger.LogError(
                "Malware scanner returned unexpected status {StatusCode} for uploaded file {FileName}.",
                (int)response.StatusCode,
                fileName);

            return OperationResult.Failure(
                OperationError.ScanUnavailable,
                "The uploaded file could not be security scanned.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Malware scanner timed out for uploaded file {FileName}.", fileName);

            return OperationResult.Failure(
                OperationError.ScanUnavailable,
                "The uploaded file could not be security scanned.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(
                exception,
                "Malware scanner could not be reached for uploaded file {FileName}.",
                fileName);

            return OperationResult.Failure(
                OperationError.ScanUnavailable,
                "The uploaded file could not be security scanned.");
        }
    }
}
