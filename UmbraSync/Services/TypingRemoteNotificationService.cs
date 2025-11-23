using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UmbraSync.API.Data.Enum;
using UmbraSync.WebAPI;

namespace UmbraSync.Services;

public sealed class TypingRemoteNotificationService
{
    private readonly ILogger<TypingRemoteNotificationService> _logger;
    private readonly ApiController _apiController;
    private readonly System.Threading.Lock _typingLock = new();
    private CancellationTokenSource? _typingCts;
    private bool _isTypingAnnounced;
    private DateTime _lastTypingSent = DateTime.MinValue;
    private TypingScope _lastScope = TypingScope.Unknown;
    private static readonly TimeSpan TypingIdle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TypingResendInterval = TimeSpan.FromMilliseconds(750);

    public TypingRemoteNotificationService(ILogger<TypingRemoteNotificationService> logger, ApiController apiController)
    {
        _logger = logger;
        _apiController = apiController;
    }

    public void NotifyTypingKeystroke(TypingScope scope)
    {
        using var __lock = _typingLock.EnterScope();
            var now = DateTime.UtcNow;
            if (!_isTypingAnnounced || (now - _lastTypingSent) >= TypingResendInterval)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogDebug("TypingRemote: send typing=true scope={scope}", scope);
                        await _apiController.UserSetTypingState(true).ConfigureAwait(false);
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "TypingRemote: failed to send typing=true"); }
                });
                _isTypingAnnounced = true;
                _lastTypingSent = now;
                _lastScope = scope;
            }

            _typingCts?.Cancel();
            _typingCts?.Dispose();
            _typingCts = new CancellationTokenSource();
            var token = _typingCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TypingIdle, token).ConfigureAwait(false);
                    _logger.LogDebug("TypingRemote: send typing=false scope={scope}", _lastScope);
                    await _apiController.UserSetTypingState(false).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    // reset
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "TypingRemote: failed to send typing=false");
                }
                finally
                {
                    using var ___lock = _typingLock.EnterScope();
                        if (!token.IsCancellationRequested)
                        {
                            _isTypingAnnounced = false;
                            _lastTypingSent = DateTime.MinValue;
                        }
                }
            });
    }

    public void ClearTypingState()
    {
        // Do not name this variable '_' because we also use discard assignments below.
        // Using '_' would shadow the discard and cause CS1656 when assigning to it.
        using var ____lock = _typingLock.EnterScope();
            _typingCts?.Cancel();
            _typingCts?.Dispose();
            _typingCts = null;
            if (_isTypingAnnounced)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogDebug("TypingRemote: clear typing state scope={scope}", _lastScope);
                        await _apiController.UserSetTypingState(false).ConfigureAwait(false);
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "TypingRemote: failed to clear typing state"); }
                });
                _isTypingAnnounced = false;
                _lastTypingSent = DateTime.MinValue;
            }
    }
}
