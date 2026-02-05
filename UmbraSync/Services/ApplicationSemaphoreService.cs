using System.Runtime.InteropServices;
using UmbraSync.MareConfiguration;
using UmbraSync.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace UmbraSync.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ApplicationSemaphoreSnapshot(bool IsEnabled, int Limit, int InFlight, int Waiting)
{
    public int Remaining => Math.Max(0, Limit - InFlight);
}

public sealed class ApplicationSemaphoreService : DisposableMediatorSubscriberBase
{
    private const int HardLimit = 10;
    private readonly MareConfigService _configService;
    private readonly SemaphoreSlim _semaphore;
    private readonly Lock _limitLock = new();
    private int _currentLimit;
    private int _pendingReductions;
    private int _pendingIncrements;
    private int _waiting;
    private int _inFlight;

    public ApplicationSemaphoreService(ILogger<ApplicationSemaphoreService> logger, MareMediator mediator,
        MareConfigService configService)
        : base(logger, mediator)
    {
        _configService = configService;
        _currentLimit = CalculateLimit();
        _semaphore = new SemaphoreSlim(_currentLimit, HardLimit);

        Mediator.Subscribe<PairProcessingLimitChangedMessage>(this, _ => UpdateSemaphoreLimit());
    }

    private bool IsEnabled => _configService.Current.EnableParallelPairProcessing;

    public ApplicationSemaphoreSnapshot GetSnapshot()
    {
        lock (_limitLock)
        {
            var enabled = IsEnabled;
            var limit = enabled ? _currentLimit : 1;
            var waiting = Math.Max(0, Volatile.Read(ref _waiting));
            var inFlight = Math.Max(0, Volatile.Read(ref _inFlight));
            return new ApplicationSemaphoreSnapshot(enabled, limit, inFlight, waiting);
        }
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _waiting);
        try
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            Interlocked.Decrement(ref _waiting);
            Logger.LogDebug("Semaphore already disposed during acquire, returning noop releaser");
            return NoopReleaser.Instance;
        }
        catch (OperationCanceledException)
        {
            Interlocked.Decrement(ref _waiting);
            Logger.LogTrace("Semaphore acquire was cancelled");
            throw;
        }
        catch
        {
            Interlocked.Decrement(ref _waiting);
            throw;
        }

        Interlocked.Decrement(ref _waiting);
        Interlocked.Increment(ref _inFlight);
        return new Releaser(this);
    }

    private void UpdateSemaphoreLimit()
    {
        lock (_limitLock)
        {
            var desiredLimit = CalculateLimit();

            if (desiredLimit == _currentLimit)
                return;

            Logger.LogDebug("Updating pair processing limit from {old} to {new}", _currentLimit, desiredLimit);

            if (desiredLimit > _currentLimit)
            {
                var increment = desiredLimit - _currentLimit;
                _pendingIncrements += increment;

                var available = HardLimit - _semaphore.CurrentCount;
                var toRelease = Math.Min(_pendingIncrements, available);
                if (toRelease > 0 && TryReleaseSemaphore(toRelease))
                {
                    _pendingIncrements -= toRelease;
                }
            }
            else
            {
                var decrement = _currentLimit - desiredLimit;
                var removed = 0;
                while (removed < decrement && _semaphore.Wait(0))
                {
                    removed++;
                }

                var remaining = decrement - removed;
                if (remaining > 0)
                {
                    _pendingReductions += remaining;
                }

                if (_pendingIncrements > 0)
                {
                    var offset = Math.Min(_pendingIncrements, _pendingReductions);
                    _pendingIncrements -= offset;
                    _pendingReductions -= offset;
                }
            }

            _currentLimit = desiredLimit;
            Logger.LogInformation("Pair processing concurrency updated to {limit} (pending reductions: {pending})",
                _currentLimit, _pendingReductions);
        }
    }

    private int CalculateLimit()
    {
        if (!_configService.Current.EnableParallelPairProcessing)
            return 1;
        return Math.Clamp(_configService.Current.MaxConcurrentPairApplications, 1, HardLimit);
    }

    private bool TryReleaseSemaphore(int count = 1)
    {
        if (count <= 0)
            return true;

        try
        {
            _semaphore.Release(count);
            return true;
        }
        catch (SemaphoreFullException ex)
        {
            Logger.LogDebug(ex, "Attempted to release {count} slots but semaphore is already at hard limit", count);
            return false;
        }
    }

    private void ReleaseOne()
    {
        var inFlight = Interlocked.Decrement(ref _inFlight);
        if (inFlight < 0)
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }

        lock (_limitLock)
        {
            if (_pendingReductions > 0)
            {
                _pendingReductions--;
                return;
            }

            if (_pendingIncrements > 0)
            {
                if (!TryReleaseSemaphore())
                    return;

                _pendingIncrements--;
                return;
            }
        }

        TryReleaseSemaphore();
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
