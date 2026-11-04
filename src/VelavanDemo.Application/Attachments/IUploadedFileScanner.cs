using VelavanDemo.Application.Common;

namespace VelavanDemo.Application.Attachments;

public interface IUploadedFileScanner
{
    Task<OperationResult> ScanAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
