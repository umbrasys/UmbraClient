using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services.AutoDetect;

public sealed class NearbyPendingService : IMediatorSubscriber
{
    private readonly ILogger<NearbyPendingService> _logger;
    private readonly MareMediator _mediator;
    private readonly ApiController _api;
    private readonly AutoDetectRequestService _requestService;
    private readonly ConcurrentDictionary<string, string> _pending = new(StringComparer.Ordinal);
    private static readonly Regex ReqRegex = new(@"^Nearby Request: (.+) \[(?<uid>[A-Z0-9]+)\]$", RegexOptions.Compiled);
    private static readonly Regex AcceptRegex = new(@"^Nearby Accept: (.+) \[(?<uid>[A-Z0-9]+)\]$", RegexOptions.Compiled);

    public NearbyPendingService(ILogger<NearbyPendingService> logger, MareMediator mediator, ApiController api, AutoDetectRequestService requestService)
    {
        _logger = logger;
        _mediator = mediator;
        _api = api;
        _requestService = requestService;
        _mediator.Subscribe<NotificationMessage>(this, OnNotification);
    }

    public MareMediator Mediator => _mediator;

    public IReadOnlyDictionary<string, string> Pending => _pending;

    private void OnNotification(NotificationMessage msg)
    {
        // Watch info messages for Nearby request pattern
        if (msg.Type != MareSynchronos.MareConfiguration.Models.NotificationType.Info) return;
        var ma = AcceptRegex.Match(msg.Message);
        if (ma.Success)
        {
            var uidA = ma.Groups["uid"].Value;
            if (!string.IsNullOrEmpty(uidA))
            {
                _ = _api.UserAddPair(new MareSynchronos.API.Dto.User.UserDto(new MareSynchronos.API.Data.UserData(uidA)));
                _pending.TryRemove(uidA, out _);
                _logger.LogInformation("NearbyPending: auto-accepted pairing with {uid}", uidA);
            }
            return;
        }

        var m = ReqRegex.Match(msg.Message);
        if (!m.Success) return;
        var uid = m.Groups["uid"].Value;
        if (string.IsNullOrEmpty(uid)) return;
        // Try to extract name as everything before space and '['
        var name = msg.Message;
        try
        {
            var idx = msg.Message.IndexOf(':');
            if (idx >= 0) name = msg.Message[(idx + 1)..].Trim();
            var br = name.LastIndexOf('[');
            if (br > 0) name = name[..br].Trim();
        }
        catch { name = uid; }
        _pending[uid] = name;
        _logger.LogInformation("NearbyPending: received request from {uid} ({name})", uid, name);
    }

    public void Remove(string uid)
    {
        _pending.TryRemove(uid, out _);
    }

    public async Task<bool> AcceptAsync(string uid)
    {
        try
        {
            await _api.UserAddPair(new MareSynchronos.API.Dto.User.UserDto(new MareSynchronos.API.Data.UserData(uid))).ConfigureAwait(false);
            _pending.TryRemove(uid, out _);
            _ = _requestService.SendAcceptNotifyAsync(uid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NearbyPending: accept failed for {uid}", uid);
            return false;
        }
    }
}
