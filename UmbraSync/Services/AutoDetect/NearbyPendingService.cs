using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using UmbraSync.MareConfiguration.Models;
using UmbraSync.Localization;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.Notification;
using UmbraSync.WebAPI;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace UmbraSync.Services.AutoDetect;

public sealed class NearbyPendingService : IMediatorSubscriber
{
    private readonly ILogger<NearbyPendingService> _logger;
    private readonly MareMediator _mediator;
    private readonly ApiController _api;
    private readonly AutoDetectRequestService _requestService;
    private readonly NotificationTracker _notificationTracker;
    private readonly ConcurrentDictionary<string, string> _pending = new(StringComparer.Ordinal);
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex ReqRegex = new(@"^Nearby Request: .+ \[(?<uid>[A-Z0-9]+)\]$", RegexOptions.Compiled | RegexOptions.ExplicitCapture, RegexTimeout);
    private static readonly Regex AcceptRegex = new(@"^Nearby Accept: .+ \[(?<uid>[A-Z0-9]+)\]$", RegexOptions.Compiled | RegexOptions.ExplicitCapture, RegexTimeout);

    public NearbyPendingService(ILogger<NearbyPendingService> logger, MareMediator mediator, ApiController api, AutoDetectRequestService requestService, NotificationTracker notificationTracker)
    {
        _logger = logger;
        _mediator = mediator;
        _api = api;
        _requestService = requestService;
        _notificationTracker = notificationTracker;
        _mediator.Subscribe<NotificationMessage>(this, OnNotification);
        _mediator.Subscribe<ManualPairInviteMessage>(this, OnManualPairInvite);
    }

    public MareMediator Mediator => _mediator;

    public IReadOnlyDictionary<string, string> Pending => _pending;

    private void OnNotification(NotificationMessage msg)
    {
        // Watch info messages for Nearby request pattern
        if (msg.Type != UmbraSync.MareConfiguration.Models.NotificationType.Info) return;
        var ma = AcceptRegex.Match(msg.Message);
        if (ma.Success)
        {
            var uidA = ma.Groups["uid"].Value;
            if (!string.IsNullOrEmpty(uidA))
            {
                _ = _api.UserAddPair(new UmbraSync.API.Dto.User.UserDto(new UmbraSync.API.Data.UserData(uidA)));
                _pending.TryRemove(uidA, out _);
                _requestService.RemovePendingRequestByUid(uidA);
                _notificationTracker.Remove(NotificationCategory.AutoDetect, uidA);
                _logger.LogInformation("NearbyPending: auto-accepted pairing with {uid}", uidA);
                Mediator.Publish(new NotificationMessage(
                    Loc.Get("AutoDetect.Notification.AcceptedTitle"),
                    string.Format(CultureInfo.CurrentCulture, Loc.Get("AutoDetect.Notification.AcceptedBody"), uidA),
                    NotificationType.Info, TimeSpan.FromSeconds(5)));
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse nearby pending name, using UID");
            name = uid;
        }
        _pending[uid] = name;
        _logger.LogInformation("NearbyPending: received request from {uid} ({name})", uid, name);
        _notificationTracker.Upsert(NotificationEntry.AutoDetect(uid, name));
        Mediator.Publish(new NotificationMessage(
            Loc.Get("AutoDetect.Notification.IncomingTitle"),
            string.Format(CultureInfo.CurrentCulture, Loc.Get("AutoDetect.Notification.IncomingBody"), name, uid),
            NotificationType.Info, TimeSpan.FromSeconds(5)));
    }

    private void OnManualPairInvite(ManualPairInviteMessage msg)
    {
        if (!string.Equals(msg.TargetUid, _api.UID, StringComparison.Ordinal))
            return;

        var display = !string.IsNullOrWhiteSpace(msg.DisplayName)
            ? msg.DisplayName!
            : (!string.IsNullOrWhiteSpace(msg.SourceAlias) ? msg.SourceAlias : msg.SourceUid);

        _pending[msg.SourceUid] = display;
        _logger.LogInformation("NearbyPending: received manual invite from {uid} ({name})", msg.SourceUid, display);
        _mediator.Publish(new NotificationMessage(
            Loc.Get("AutoDetect.Notification.IncomingTitle"),
            string.Format(CultureInfo.CurrentCulture, Loc.Get("AutoDetect.Notification.IncomingBody"), display, msg.SourceUid),
            NotificationType.Info, TimeSpan.FromSeconds(5)));
        _notificationTracker.Upsert(NotificationEntry.AutoDetect(msg.SourceUid, display));
    }

    public void Remove(string uid)
    {
        _pending.TryRemove(uid, out _);
        _requestService.RemovePendingRequestByUid(uid);
        _notificationTracker.Remove(NotificationCategory.AutoDetect, uid);
    }

    public async Task<bool> AcceptAsync(string uid)
    {
        try
        {
            await _api.UserAddPair(new UmbraSync.API.Dto.User.UserDto(new UmbraSync.API.Data.UserData(uid))).ConfigureAwait(false);
            _pending.TryRemove(uid, out _);
            _requestService.RemovePendingRequestByUid(uid);
            _ = _requestService.SendAcceptNotifyAsync(uid);
            _notificationTracker.Remove(NotificationCategory.AutoDetect, uid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NearbyPending: accept failed for {uid}", uid);
            return false;
        }
    }
}
