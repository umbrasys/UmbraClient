using System;
using System.Collections.Concurrent;
using System.Linq;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using MareSynchronos.API.Data;
using MareSynchronos.MareConfiguration;

namespace MareSynchronos.Services;

public sealed class TypingIndicatorStateService : IMediatorSubscriber, IDisposable
{
    private sealed record TypingEntry(UserData User, DateTime FirstSeen, DateTime LastUpdate, MareSynchronos.API.Data.Enum.TypingScope Scope);

    private readonly ConcurrentDictionary<string, TypingEntry> _typingUsers = new(StringComparer.Ordinal);
    private readonly ApiController _apiController;
    private readonly ILogger<TypingIndicatorStateService> _logger;
    private readonly MareConfigService _configService;
    private DateTime _selfTypingLast = DateTime.MinValue;
    private DateTime _selfTypingStart = DateTime.MinValue;
    private bool _selfTypingActive;

    public TypingIndicatorStateService(ILogger<TypingIndicatorStateService> logger, MareMediator mediator, ApiController apiController, MareConfigService configService)
    {
        _logger = logger;
        _apiController = apiController;
        _configService = configService;
        Mediator = mediator;

        mediator.Subscribe<UserTypingStateMessage>(this, OnTypingState);
    }

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
    }

    public MareMediator Mediator { get; }

    public void SetSelfTypingLocal(bool isTyping)
    {
        if (isTyping)
        {
            if (!_selfTypingActive)
                _selfTypingStart = DateTime.UtcNow;
            _selfTypingLast = DateTime.UtcNow;
        }
        else
        {
            _selfTypingStart = DateTime.MinValue;
        }

        _selfTypingActive = isTyping;
    }

    public void ClearAll()
    {
        _typingUsers.Clear();
        _selfTypingActive = false;
        _selfTypingStart = DateTime.MinValue;
        _selfTypingLast = DateTime.MinValue;
        _logger.LogDebug("TypingIndicatorStateService: cleared all typing state");
    }

    private void OnTypingState(UserTypingStateMessage msg)
    {
        if (!_configService.Current.TypingIndicatorEnabled)
            return;
        var uid = msg.Typing.User.UID;
        var now = DateTime.UtcNow;

        if (string.Equals(uid, _apiController.UID, StringComparison.Ordinal))
        {
            _selfTypingActive = msg.Typing.IsTyping;
            if (_selfTypingActive)
            {
                if (_selfTypingStart == DateTime.MinValue)
                    _selfTypingStart = now;
                _selfTypingLast = now;
            }
            else
            {
                _selfTypingStart = DateTime.MinValue;
            }
            _logger.LogInformation("Typing state self -> {state}", _selfTypingActive);
        }
        else if (msg.Typing.IsTyping)
        {
            _typingUsers.AddOrUpdate(uid,
                _ => new TypingEntry(msg.Typing.User, now, now, msg.Typing.Scope),
                (_, existing) => new TypingEntry(msg.Typing.User, existing.FirstSeen, now, msg.Typing.Scope));
        }
        else
        {
            _typingUsers.TryRemove(uid, out _);
        }
    }

    public bool TryGetSelfTyping(TimeSpan maxAge, out DateTime startTyping, out DateTime lastTyping)
    {
        startTyping = _selfTypingStart;
        lastTyping = _selfTypingLast;
        if (!_selfTypingActive)
            return false;

        var now = DateTime.UtcNow;
        if ((now - _selfTypingLast) >= maxAge)
        {
            _selfTypingActive = false;
            _selfTypingStart = DateTime.MinValue;
            return false;
        }

        return true;
    }

    public IReadOnlyDictionary<string, (UserData User, DateTime FirstSeen, DateTime LastUpdate, MareSynchronos.API.Data.Enum.TypingScope Scope)> GetActiveTypers(TimeSpan maxAge)
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _typingUsers.ToArray())
        {
            if ((now - kvp.Value.LastUpdate) >= maxAge)
            {
                _typingUsers.TryRemove(kvp.Key, out _);
            }
        }

        return _typingUsers.ToDictionary(k => k.Key, v => (v.Value.User, v.Value.FirstSeen, v.Value.LastUpdate, v.Value.Scope), StringComparer.Ordinal);
    }
}
