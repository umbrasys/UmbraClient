using UmbraSync.Services.Mediator;
using Microsoft.Extensions.Logging;
namespace UmbraSync.Services;

public sealed class ApplicationSemaphoreService : DisposableMediatorSubscriberBase
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public ApplicationSemaphoreService(ILogger<ApplicationSemaphoreService> logger, MareMediator mediator)
        : base(logger, mediator)
    {
    }
    
    public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(this);
        }
        catch (ObjectDisposedException)
        {
            Logger.LogDebug("Semaphore already disposed during acquire, returning noop releaser");
            return NoopReleaser.Instance;
        }
        catch (OperationCanceledException)
        {
            Logger.LogTrace("Semaphore acquire was cancelled");
            throw;
        }
    }

    private void ReleaseOne()
    {
        try
        {
            _semaphore.Release();
        }
        catch (ObjectDisposedException)
        {
            Logger.LogTrace("Semaphore already disposed during release");
        }
        catch (SemaphoreFullException ex)
        {
            Logger.LogWarning(ex, "Attempted to release semaphore but it was already full");
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        Logger.LogDebug("Disposing ApplicationSemaphoreService");
        _semaphore.Dispose();
    }
    
    private sealed class Releaser : IAsyncDisposable
    {
        private ApplicationSemaphoreService? _owner;

        public Releaser(ApplicationSemaphoreService owner)
        {
            _owner = owner;
        }

        public ValueTask DisposeAsync()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseOne();
            return ValueTask.CompletedTask;
        }
    }
    private sealed class NoopReleaser : IAsyncDisposable
    {
        public static readonly NoopReleaser Instance = new();
        private NoopReleaser() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
