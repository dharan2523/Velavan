using VelavanDemo.Application.Attachments;
using VelavanDemo.Application.Common;

namespace VelavanDemo.Infrastructure.Attachments;

/// <summary>
/// Placeholder scanner that accepts every file without inspection.
/// It intentionally performs no malware scanning and is not safe for
/// production. Replace this registration with an approved anti-malware
/// scanner before accepting uploads.
/// </summary>
public sealed class NoOpUploadedFileScanner : IUploadedFileScanner
{
    public Task<OperationResult> ScanAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult.Success());
    }
}
