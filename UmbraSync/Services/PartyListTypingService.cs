using UmbraSync.MareConfiguration;
using System.Collections.Generic;
using UmbraSync.PlayerData.Pairs;
using System;
using System.Linq;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using UmbraSync.API.Dto.User;
using UmbraSync.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace UmbraSync.Services;

public class PartyListTypingService(ILogger<PartyListTypingService> logger,
    MareMediator mediator,
    IPartyList partyList,
    PairManager pairManager,
    MareConfigService configService,
    TypingIndicatorStateService typingStateService)
    : DisposableMediatorSubscriberBase(logger, mediator)
{
    private readonly ILogger<PartyListTypingService> _logger = logger;
    private readonly IPartyList _partyList = partyList;
    private readonly MareConfigService _configService = configService;
    private readonly PairManager _pairManager = pairManager;
    private readonly TypingIndicatorStateService _typingStateService = typingStateService;
    private static readonly TimeSpan TypingDisplayTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TypingDisplayFade = TypingDisplayTime;

    public void Draw()
    {
        if (!_configService.Current.TypingIndicatorEnabled) return;
        if (!_configService.Current.TypingIndicatorShowOnPartyList) return;
        var visibleByAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var visibleUsers = _pairManager.GetVisibleUsers();
            foreach (var u in visibleUsers)
            {
                var alias = string.IsNullOrEmpty(u.AliasOrUID) ? u.UID : u.AliasOrUID;
                if (!visibleByAlias.ContainsKey(alias)) visibleByAlias[alias] = u.UID;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PartyListTypingService: failed to get visible users");
        }

        var activeTypers = _typingStateService.GetActiveTypers(TypingDisplayTime);
        var now = DateTime.UtcNow;

        for (var i = 0; i < _partyList.Length; ++i)
        {
            var member = _partyList[i];
            if (member == null || member.Address == nint.Zero)
                continue;

            var displayName = member.Name.TextValue;
            if (string.IsNullOrEmpty(displayName))
                continue;

            if (visibleByAlias.TryGetValue(displayName, out var uid)
                && activeTypers.TryGetValue(uid, out var entry)
                && (now - entry.LastUpdate) <= TypingDisplayFade)
            {
                _logger.LogDebug("PartyListTypingService: bubble would be shown for {name}", displayName);
            }
        }
    }
}
