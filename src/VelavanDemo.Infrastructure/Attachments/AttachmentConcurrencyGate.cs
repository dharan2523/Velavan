using VelavanDemo.Application.Attachments;

namespace VelavanDemo.Infrastructure.Attachments;

public sealed class AttachmentConcurrencyGate : IAttachmentConcurrencyGate, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IAsyncDisposable> AcquireAsync(
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
