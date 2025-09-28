using MareSynchronos.MareConfiguration;
using System.Collections.Generic;
using MareSynchronos.PlayerData.Pairs;
using System;
using System.Collections.Concurrent;
using System.Linq;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public class PartyListTypingService : DisposableMediatorSubscriberBase
{
    private readonly ILogger<PartyListTypingService> _logger;
    private readonly IPartyList _partyList;
    private readonly MareConfigService _configService;
    private readonly PairManager _pairManager;
    private readonly ConcurrentDictionary<string, DateTime> _typingUsers = new();
    private readonly ConcurrentDictionary<string, DateTime> _typingNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan TypingDisplayTime = TimeSpan.FromSeconds(2);

    public PartyListTypingService(ILogger<PartyListTypingService> logger,
        MareMediator mediator,
        IPartyList partyList,
        PairManager pairManager,
        MareConfigService configService)
        : base(logger, mediator)
    {
        _logger = logger;
        _partyList = partyList;
        _pairManager = pairManager;
        _configService = configService;

        Mediator.Subscribe<UserTypingStateMessage>(this, OnUserTyping);
    }

    private void OnUserTyping(UserTypingStateMessage msg)
    {
        var now = DateTime.UtcNow;
        var uid = msg.Typing.User.UID;
        var aliasOrUid = msg.Typing.User.AliasOrUID ?? uid;

        if (msg.Typing.IsTyping)
        {
            _typingUsers[uid] = now;
            _typingNames[aliasOrUid] = now;
        }
        else
        {
            _typingUsers.TryRemove(uid, out _);
            _typingNames.TryRemove(aliasOrUid, out _);
        }
    }

    private static bool HasTypingBubble(SeString name)
    {
        return name.Payloads.Any(p => p is IconPayload ip && ip.Icon == BitmapFontIcon.AutoTranslateBegin);
    }

    private static SeString WithTypingBubble(SeString baseName)
    {
        var ssb = new SeStringBuilder();
        ssb.Append(baseName);
        ssb.Add(new IconPayload(BitmapFontIcon.AutoTranslateBegin));
        ssb.AddText("...");
        ssb.Add(new IconPayload(BitmapFontIcon.AutoTranslateEnd));
        return ssb.Build();
    }

    public void Draw()
    {
        if (!_configService.Current.TypingIndicatorShowOnPartyList) return;
        // Build map of visible users by AliasOrUID -> UID (case-insensitive)
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

        foreach (var member in _partyList)
        {
            if (string.IsNullOrEmpty(member.Name?.TextValue)) continue;

            var now = DateTime.UtcNow;
            var displayName = member.Name.TextValue;
            if (visibleByAlias.TryGetValue(displayName, out var uid)
                && _typingUsers.TryGetValue(uid, out var last)
                && (now - last) < TypingDisplayTime)
            {
                if (!HasTypingBubble(member.Name))
                {
                    // IPartyMember.Name is read-only; rendering bubble here requires Addon-level modification. Keeping compile-safe for now.
                    _logger.LogDebug("PartyListTypingService: bubble would be shown for {name}", displayName);
                }
            }
        }
    }
}