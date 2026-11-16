using VelavanDemo.Application.Common;

namespace VelavanDemo.Application.Attachments;

/// <summary>
/// Inspects uploaded content for malware before its metadata is persisted.
/// The registered implementation is currently a no-op placeholder. Production
/// deployments must replace it with a real anti-malware scanner (for example,
/// one backed by Windows Defender, ClamAV, or an approved scanning service).
/// </summary>
public interface IUploadedFileScanner
{
    Task<OperationResult> ScanAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
