using Dalamud.Game.ClientState.Objects.Types;
using System;
using UmbraSync.PlayerData.Handlers;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace UmbraSync.Interop.Ipc;

public class RedrawManager : IDisposable
{
    private readonly MareMediator _mareMediator;
    private readonly DalamudUtilService _dalamudUtil;
    private CancellationTokenSource? _disposalCts = new();
    private bool _disposed;

    public SemaphoreSlim RedrawSemaphore { get; init; } = new(2, 2);

    public RedrawManager(MareMediator mareMediator, DalamudUtilService dalamudUtil)
    {
        _mareMediator = mareMediator;
        _dalamudUtil = dalamudUtil;
    }

    public async Task PenumbraRedrawInternalAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, Action<ICharacter> action, CancellationToken token)
    {
        _mareMediator.Publish(new PenumbraStartRedrawMessage(handler.Address));

        try
        {
            using CancellationTokenSource cancelToken = new CancellationTokenSource();
            using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken.Token, token, EnsureActiveCts(ref _disposalCts).Token);
            var combinedToken = combinedCts.Token;
            cancelToken.CancelAfter(TimeSpan.FromSeconds(15));
            await handler.ActOnFrameworkAfterEnsureNoDrawAsync(action, combinedToken).ConfigureAwait(false);

            if (!_disposalCts!.Token.IsCancellationRequested)
                await _dalamudUtil.WaitWhileCharacterIsDrawing(logger, handler, applicationId, 30000, combinedToken).ConfigureAwait(false);
        }
        finally
        {
            _mareMediator.Publish(new PenumbraEndRedrawMessage(handler.Address));
        }
    }

    internal void Cancel()
    {
        EnsureFreshCts(ref _disposalCts);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            CancelAndDispose(ref _disposalCts);
        }

        _disposed = true;
    }

    private static CancellationTokenSource EnsureActiveCts(ref CancellationTokenSource? cts)
    {
        if (cts == null)
        {
            cts = new CancellationTokenSource();
            return cts;
        }

        if (cts.IsCancellationRequested)
        {
            cts.Dispose();
            cts = new CancellationTokenSource();
        }

        return cts;
    }

    private static void EnsureFreshCts(ref CancellationTokenSource? cts)
    {
        CancelAndDispose(ref cts);
        cts = new CancellationTokenSource();
    }

    private static void CancelAndDispose(ref CancellationTokenSource? cts)
    {
        if (cts == null) return;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // cancellation source already disposed; safe to ignore
        }

        cts.Dispose();
        cts = null;
    }
}
