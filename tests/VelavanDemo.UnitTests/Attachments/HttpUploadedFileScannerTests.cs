using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VelavanDemo.Application.Attachments;
using VelavanDemo.Application.Common;
using VelavanDemo.Infrastructure.Attachments;

namespace VelavanDemo.UnitTests.Attachments;

public sealed class HttpUploadedFileScannerTests
{
    [Fact]
    public async Task ScanAsync_WhenScannerAcceptsFile_ReturnsSuccessAndSendsApiKey()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var scanner = CreateScanner(handler, apiKey: "scanner-key");

        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await scanner.ScanAsync(
            content,
            "image.png",
            "image/png");

        Assert.True(result.Succeeded);
        Assert.NotNull(capturedRequest);
        Assert.Equal("Bearer", capturedRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("scanner-key", capturedRequest.Headers.Authorization?.Parameter);
        Assert.IsType<MultipartFormDataContent>(capturedRequest.Content);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task ScanAsync_WhenScannerRejectsFile_ReturnsMalwareDetected(
        HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(statusCode));
        var scanner = CreateScanner(handler);

        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await scanner.ScanAsync(
            content,
            "image.png",
            "image/png");

        Assert.False(result.Succeeded);
        Assert.Equal(OperationError.MalwareDetected, result.Error);
    }

    [Fact]
    public async Task ScanAsync_WhenScannerFails_ReturnsScanUnavailable()
    {
        var handler = new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var scanner = CreateScanner(handler);

        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await scanner.ScanAsync(
            content,
            "image.png",
            "image/png");

        Assert.False(result.Succeeded);
        Assert.Equal(OperationError.ScanUnavailable, result.Error);
    }

    [Fact]
    public async Task ScanAsync_WhenScannerTimesOut_ReturnsScanUnavailable()
    {
        var handler = new StubHttpMessageHandler(
            _ => throw new TaskCanceledException("Scanner timed out."));
        var scanner = CreateScanner(handler);

        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await scanner.ScanAsync(
            content,
            "image.png",
            "image/png");

        Assert.False(result.Succeeded);
        Assert.Equal(OperationError.ScanUnavailable, result.Error);
    }

    private static HttpUploadedFileScanner CreateScanner(
        HttpMessageHandler handler,
        string? apiKey = null)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new AttachmentScannerOptions
        {
            Provider = AttachmentScannerOptions.HttpProvider,
            Endpoint = "https://scanner.example.test/scan",
            ApiKey = apiKey,
            TimeoutSeconds = 5
        });

        return new HttpUploadedFileScanner(
            httpClient,
            options,
            NullLogger<HttpUploadedFileScanner>.Instance);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
