using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Microsoft.Extensions.Logging;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.WebAPI;
using MareSynchronos.MareConfiguration;
using UmbraSync.API.Data.Enum;

namespace MareSynchronos.Services;

public sealed class ChatTypingDetectionService : IDisposable
{
    private readonly ILogger<ChatTypingDetectionService> _logger;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IGameGui _gameGui;
    private readonly ChatService _chatService;
    private readonly TypingIndicatorStateService _typingStateService;
    private readonly ApiController _apiController;
    private readonly PairManager _pairManager;
    private readonly IPartyList _partyList;
    private readonly MareConfigService _configService;

    private string _lastChatText = string.Empty;
    private bool _isTyping;
    private bool _notifyingRemote;
    private bool _serverSupportWarnLogged;
    private bool _remoteNotificationsEnabled;
    private bool _subscribed;

    public ChatTypingDetectionService(ILogger<ChatTypingDetectionService> logger, IFramework framework,
        IClientState clientState, IGameGui gameGui, ChatService chatService, PairManager pairManager, IPartyList partyList,
        TypingIndicatorStateService typingStateService, ApiController apiController, MareConfigService configService)
    {
        _logger = logger;
        _framework = framework;
        _clientState = clientState;
        _gameGui = gameGui;
        _chatService = chatService;
        _pairManager = pairManager;
        _partyList = partyList;
        _typingStateService = typingStateService;
        _apiController = apiController;
        _configService = configService;

        Subscribe();
        _logger.LogInformation("ChatTypingDetectionService initialized");
    }

    public void Dispose()
    {
        Unsubscribe();
        ResetTypingState();
    }

    public void SoftRestart()
    {
        try
        {
            _logger.LogInformation("TypingDetection: soft restarting");
            Unsubscribe();
            ResetTypingState();
            _chatService.ClearTypingState();
            _typingStateService.ClearAll();
            Subscribe();
            _logger.LogInformation("TypingDetection: soft restart completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TypingDetection: soft restart failed");
        }
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _framework.Update += OnFrameworkUpdate;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _framework.Update -= OnFrameworkUpdate;
        _subscribed = false;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            if (!_clientState.IsLoggedIn)
            {
                ResetTypingState();
                return;
            }

            if (!_configService.Current.TypingIndicatorEnabled)
            {
                ResetTypingState();
                _chatService.ClearTypingState();
                return;
            }

            if (!TryGetChatInput(out var chatText) || string.IsNullOrEmpty(chatText))
            {
                ResetTypingState();
                return;
            }

            if (IsIgnoredCommand(chatText))
            {
                ResetTypingState();
                return;
            }

            var notifyRemote = ShouldNotifyRemote();
            UpdateRemoteNotificationLogState(notifyRemote);
            if (!notifyRemote && _notifyingRemote)
            {
                _chatService.ClearTypingState();
                _notifyingRemote = false;
            }

            if (!_isTyping || !string.Equals(chatText, _lastChatText, StringComparison.Ordinal))
            {
                if (notifyRemote)
                {
                    var scope = GetCurrentTypingScope();
                    _chatService.NotifyTypingKeystroke(scope);
                    _notifyingRemote = true;
                }

                _typingStateService.SetSelfTypingLocal(true);
                _isTyping = true;
            }

            _lastChatText = chatText;
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "ChatTypingDetectionService tick failed");
        }
    }

    private void ResetTypingState()
    {
        if (!_isTyping)
        {
            _lastChatText = string.Empty;
            return;
        }

        _isTyping = false;
        _lastChatText = string.Empty;
        _chatService.ClearTypingState();
        _notifyingRemote = false;
        _typingStateService.SetSelfTypingLocal(false);
    }

    private unsafe TypingScope GetCurrentTypingScope()
    {
        try
        {
            var shellModule = RaptureShellModule.Instance();
            if (shellModule == null)
                return TypingScope.Unknown;

            var chatType = (XivChatType)shellModule->ChatType;
            switch (chatType)
            {
                case XivChatType.Say:
                case XivChatType.Shout:
                case XivChatType.Yell:
                    return TypingScope.Proximity;
                case XivChatType.Party:
                    return TypingScope.Party;
                case XivChatType.CrossParty:
                    return TypingScope.CrossParty;
                default:
                    return TypingScope.Unknown;
            }
        }
        catch
        {
            return TypingScope.Unknown;
        }
    }

    private static bool IsIgnoredCommand(string chatText)
    {
        if (string.IsNullOrWhiteSpace(chatText))
            return false;

        var trimmed = chatText.TrimStart();
        if (!trimmed.StartsWith('/'))
            return false;

        var firstTokenEnd = trimmed.IndexOf(' ');
        var command = firstTokenEnd >= 0 ? trimmed[..firstTokenEnd] : trimmed;
        command = command.TrimEnd();

        var comparison = StringComparison.OrdinalIgnoreCase;
        return command.StartsWith("/tell", comparison)
            || command.StartsWith("/t", comparison)
            || command.StartsWith("/xllog", comparison)
            || command.StartsWith("/umbra", comparison)
            || command.StartsWith("/fc", comparison)
            || command.StartsWith("/freecompany", comparison);
    }

    private unsafe bool ShouldNotifyRemote()
    {
        try
        {
            if (!_configService.Current.TypingIndicatorEnabled)
            {
                return false;
            }

            var supportsTypingState = _apiController.SystemInfoDto.SupportsTypingState;
            var connected = _apiController.IsConnected;
            if (!connected || !supportsTypingState)
            {
                if (!_serverSupportWarnLogged)
                {
                    _logger.LogDebug("TypingDetection: server support unavailable (connected={connected}, supports={supports})", connected, supportsTypingState);
                    _serverSupportWarnLogged = true;
                }
                return false;
            }

            _serverSupportWarnLogged = false;

        var shellModule = RaptureShellModule.Instance();
        if (shellModule == null)
        {
            _logger.LogDebug("TypingDetection: shell module null");
            return true;
        }

            var chatType = (XivChatType)shellModule->ChatType;
            switch (chatType)
            {
                case XivChatType.Say:
                case XivChatType.Shout:
                case XivChatType.Yell:
                    return true;
                case XivChatType.Party:
                case XivChatType.CrossParty:
                    var eligible = PartyContainsPairedMember();
                    return eligible;
                case XivChatType.Debug:
                    return true;
                default:
                    _logger.LogTrace("TypingDetection: channel {type} rejected", chatType);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "ChatTypingDetectionService: failed to evaluate chat channel");
        }

        return true;
    }

    private bool PartyContainsPairedMember()
    {
        try
        {
            var pairedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _pairManager.GetOnlineUserPairs())
            {
                if (!string.IsNullOrEmpty(pair.PlayerName))
                    pairedNames.Add(pair.PlayerName);
            }

            if (pairedNames.Count == 0)
            {
            _logger.LogDebug("TypingDetection: no paired names online");
            return false;
            }

            foreach (var member in _partyList)
            {
                var name = member?.Name?.TextValue;
                if (string.IsNullOrEmpty(name))
                    continue;

                if (pairedNames.Contains(name))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ChatTypingDetectionService: failed to check party composition");
        }

        _logger.LogDebug("TypingDetection: no paired members in party");
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe bool TryGetChatInput(out string chatText)
    {
        chatText = string.Empty;

        var addon = _gameGui.GetAddonByName("ChatLog", 1);
        if (addon.Address == nint.Zero)
            return false;

        var chatLog = (AtkUnitBase*)addon.Address;
        if (chatLog == null || !chatLog->IsVisible)
            return false;

        var textInputNode = chatLog->UldManager.NodeList[16];
        if (textInputNode == null)
            return false;

        var componentNode = textInputNode->GetAsAtkComponentNode();
        if (componentNode == null || componentNode->Component == null)
            return false;

        var cursorNode = componentNode->Component->UldManager.NodeList[14];
        if (cursorNode == null)
            return false;

        var cursorVisible = cursorNode->IsVisible();
        if (!cursorVisible)
        {
            return false;
        }

        var chatInputNode = componentNode->Component->UldManager.NodeList[1];
        if (chatInputNode == null)
            return false;

        var textNode = chatInputNode->GetAsAtkTextNode();
        if (textNode == null)
            return false;

        var rawText = textNode->GetText();
        if (rawText == null)
            return false;

        chatText = rawText.AsDalamudSeString().ToString();
        return true;
    }

    private void UpdateRemoteNotificationLogState(bool notifyRemote)
    {
        if (notifyRemote && !_remoteNotificationsEnabled)
        {
            _remoteNotificationsEnabled = true;
            _logger.LogInformation("TypingDetection: remote notifications enabled");
        }
        else if (!notifyRemote && _remoteNotificationsEnabled)
        {
            _remoteNotificationsEnabled = false;
            _logger.LogInformation("TypingDetection: remote notifications disabled");
        }
    }
}
