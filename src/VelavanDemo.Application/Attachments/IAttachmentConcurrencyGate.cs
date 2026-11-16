namespace VelavanDemo.Application.Attachments;

public interface IAttachmentConcurrencyGate
{
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default);
}
