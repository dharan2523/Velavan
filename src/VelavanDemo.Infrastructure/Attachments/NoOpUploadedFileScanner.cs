using VelavanDemo.Application.Attachments;
using VelavanDemo.Application.Common;

namespace VelavanDemo.Infrastructure.Attachments;

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
