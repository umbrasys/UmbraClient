using System;
using System.Text;
using System.Threading;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using UmbraSync.API.Data;
using UmbraSync.Interop;
using UmbraSync.MareConfiguration;
using UmbraSync.API.Data.Enum;
using UmbraSync.MareConfiguration.Models;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;
using UmbraSync.Utils;
using UmbraSync.WebAPI;
using Microsoft.Extensions.Logging;

namespace UmbraSync.Services;

public class ChatService : DisposableMediatorSubscriberBase
{
    public const int DefaultColor = 710;
    private const string ManualPairInvitePrefix = "[UmbraPairInvite|";
    public const int CommandMaxNumber = 50;

    private readonly ILogger<ChatService> _logger;
    private readonly IChatGui _chatGui;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareConfigService _mareConfig;
    private readonly ApiController _apiController;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    private readonly Lazy<GameChatHooks> _gameChatHooks;

    private readonly object _typingLock = new();
    private CancellationTokenSource? _typingCts;
    private bool _isTypingAnnounced;
    private DateTime _lastTypingSent = DateTime.MinValue;
    private TypingScope _lastScope = TypingScope.Unknown;
    private static readonly TimeSpan TypingIdle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TypingResendInterval = TimeSpan.FromMilliseconds(750);

    public ChatService(ILogger<ChatService> logger, DalamudUtilService dalamudUtil, MareMediator mediator, ApiController apiController,
        PairManager pairManager, ILoggerFactory loggerFactory, IGameInteropProvider gameInteropProvider, IChatGui chatGui,
        MareConfigService mareConfig, ServerConfigurationManager serverConfigurationManager) : base(logger, mediator)
    {
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _chatGui = chatGui;
        _mareConfig = mareConfig;
        _apiController = apiController;
        _pairManager = pairManager;
        _serverConfigurationManager = serverConfigurationManager;

        Mediator.Subscribe<UserChatMsgMessage>(this, HandleUserChat);
        Mediator.Subscribe<GroupChatMsgMessage>(this, HandleGroupChat);

        _gameChatHooks = new(() => new GameChatHooks(loggerFactory.CreateLogger<GameChatHooks>(), gameInteropProvider, SendChatShell));
        _ = Task.Run(() =>
        {
            try
            {
                _ = _gameChatHooks.Value;
                _isTypingAnnounced = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize chat hooks");
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _typingCts?.Cancel();
        _typingCts?.Dispose();
        if (_gameChatHooks.IsValueCreated)
            _gameChatHooks.Value!.Dispose();
    }
    public void NotifyTypingKeystroke(TypingScope scope)
    {
        lock (_typingLock)
        {
            var now = DateTime.UtcNow;
            if (!_isTypingAnnounced || (now - _lastTypingSent) >= TypingResendInterval)
            {
                _ = Task.Run(async () =>
                {
                    try { await _apiController.UserSetTypingState(true, scope).ConfigureAwait(false); }
                    catch (Exception ex) { _logger.LogDebug(ex, "NotifyTypingKeystroke: failed to send typing=true"); }
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
                    await _apiController.UserSetTypingState(false, _lastScope).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    // reset timer
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "NotifyTypingKeystroke: failed to send typing=false");
                }
                finally
                {
                    lock (_typingLock)
                    {
                        if (!token.IsCancellationRequested)
                        {
                            _isTypingAnnounced = false;
                            _lastTypingSent = DateTime.MinValue;
                        }
                    }
                }
            });
        }
    }
    public void ClearTypingState()
    {
        lock (_typingLock)
        {
            _typingCts?.Cancel();
            _typingCts?.Dispose();
            _typingCts = null;
            if (_isTypingAnnounced)
            {
                _ = Task.Run(async () =>
                {
                    try { await _apiController.UserSetTypingState(false, _lastScope).ConfigureAwait(false); }
                    catch (Exception ex) { _logger.LogDebug(ex, "ClearTypingState: failed to send typing=false"); }
                });
                _isTypingAnnounced = false;
                _lastTypingSent = DateTime.MinValue;
            }
        }
    }

    private void HandleUserChat(UserChatMsgMessage message)
    {
        var chatMsg = message.ChatMsg;
        var prefix = new SeStringBuilder();
        prefix.AddText("[UmbraChat] ");
        _chatGui.Print(new XivChatEntry{
            MessageBytes = [..prefix.Build().Encode(), ..message.ChatMsg.PayloadContent],
            Name = chatMsg.SenderName,
            Type = XivChatType.TellIncoming
        });
    }

    private ushort ResolveShellColor(int shellColor)
    {
        if (shellColor != 0)
            return (ushort)shellColor;
        var globalColor = _mareConfig.Current.ChatColor;
        if (globalColor != 0)
            return (ushort)globalColor;
        return (ushort)DefaultColor;
    }

    private XivChatType ResolveShellLogKind(int shellLogKind)
    {
        if (shellLogKind != 0)
            return (XivChatType)shellLogKind;
        return (XivChatType)_mareConfig.Current.ChatLogKind;
    }

    private void HandleGroupChat(GroupChatMsgMessage message)
    {
        if (_mareConfig.Current.DisableSyncshellChat)
            return;

        var chatMsg = message.ChatMsg;
        var shellConfig = _serverConfigurationManager.GetShellConfigForGid(message.GroupInfo.GID);
        var shellNumber = shellConfig.ShellNumber;

        if (!shellConfig.Enabled)
            return;

        ushort color = ResolveShellColor(shellConfig.Color);
        var extraChatTags = _mareConfig.Current.ExtraChatTags;
        var logKind = ResolveShellLogKind(shellConfig.LogKind);

        var payload = SeString.Parse(message.ChatMsg.PayloadContent);
        if (TryHandleManualPairInvite(message, payload))
            return;

        var msg = new SeStringBuilder();
        if (extraChatTags)
        {
            msg.Add(ChatUtils.CreateExtraChatTagPayload(message.GroupInfo.GID));
            msg.Add(RawPayload.LinkTerminator);
        }
        if (color != 0)
            msg.AddUiForeground((ushort)color);
        msg.AddText($"[SS{shellNumber}]<");
        if (message.ChatMsg.Sender.UID.Equals(_apiController.UID, StringComparison.Ordinal))
        {
            msg.AddText(chatMsg.SenderName);
        }
        else
        {
            msg.Add(new PlayerPayload(chatMsg.SenderName, chatMsg.SenderHomeWorldId));
        }
        msg.AddText("> ");
        msg.Append(payload);
        if (color != 0)
            msg.AddUiForegroundOff();

        _chatGui.Print(new XivChatEntry{
            Message = msg.Build(),
            Name = chatMsg.SenderName,
            Type = logKind
        });
    }

    private bool TryHandleManualPairInvite(GroupChatMsgMessage message, SeString payload)
    {
        var textValue = payload.TextValue;
        if (string.IsNullOrEmpty(textValue) || !textValue.StartsWith(ManualPairInvitePrefix, StringComparison.Ordinal))
            return false;

        var content = textValue[ManualPairInvitePrefix.Length..];
        if (content.EndsWith("]", StringComparison.Ordinal))
        {
            content = content[..^1];
        }

        var parts = content.Split('|');
        if (parts.Length < 4)
            return true;

        var sourceUid = parts[0];
        var sourceAlias = DecodeInviteField(parts[1]);
        var targetUid = parts[2];
        var displayName = DecodeInviteField(parts[3]);
        var inviteId = parts.Length > 4 ? parts[4] : Guid.NewGuid().ToString("N");

        if (!string.Equals(targetUid, _apiController.UID, StringComparison.Ordinal))
            return true;

        Mediator.Publish(new ManualPairInviteMessage(sourceUid, sourceAlias, targetUid, string.IsNullOrEmpty(displayName) ? null : displayName, inviteId));
        _logger.LogDebug("Received manual pair invite from {source} via syncshell", sourceUid);

        return true;
    }

    private static string DecodeInviteField(string encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return string.Empty;

        try
        {
            var bytes = Convert.FromBase64String(encoded);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return encoded;
        }
    }
    public void PrintChannelExample(string message, string gid = "")
    {
        int chatType = _mareConfig.Current.ChatLogKind;

        foreach (var group in _pairManager.Groups)
        {
            if (group.Key.GID.Equals(gid, StringComparison.Ordinal))
            {
                int shellChatType = _serverConfigurationManager.GetShellConfigForGid(gid).LogKind;
                if (shellChatType != 0)
                    chatType = shellChatType;
            }
        }

        _chatGui.Print(new XivChatEntry{
            Message = message,
            Name = "",
            Type = (XivChatType)chatType
        });
    }
    public void MaybeUpdateShellName(int shellNumber)
    {
        if (_mareConfig.Current.DisableSyncshellChat)
            return;

        foreach (var group in _pairManager.Groups)
        {
            var shellConfig = _serverConfigurationManager.GetShellConfigForGid(group.Key.GID);
            if (shellConfig.Enabled && shellConfig.ShellNumber == shellNumber)
            {
                if (_gameChatHooks.IsValueCreated && _gameChatHooks.Value.ChatChannelOverride != null)
                {
                    if (_gameChatHooks.Value.ChatChannelOverride.ChannelName.StartsWith($"SS [{shellNumber}]", StringComparison.Ordinal))
                        SwitchChatShell(shellNumber);
                }
            }
        }
    }

    public void SwitchChatShell(int shellNumber)
    {
        if (_mareConfig.Current.DisableSyncshellChat)
            return;

        foreach (var group in _pairManager.Groups)
        {
            var shellConfig = _serverConfigurationManager.GetShellConfigForGid(group.Key.GID);
            if (shellConfig.Enabled && shellConfig.ShellNumber == shellNumber)
            {
                var name = _serverConfigurationManager.GetNoteForGid(group.Key.GID) ?? group.Key.AliasOrGID;
                _gameChatHooks.Value.ChatChannelOverride = new()
                {
                    ChannelName = $"SS [{shellNumber}]: {name}",
                    ChatMessageHandler = chatBytes => SendChatShell(shellNumber, chatBytes)
                };
                return;
            }
        }

        _chatGui.PrintError($"[UmbraSync] Syncshell number #{shellNumber} not found");
    }

    public void SendChatShell(int shellNumber, byte[] chatBytes)
    {
        if (_mareConfig.Current.DisableSyncshellChat)
            return;

        foreach (var group in _pairManager.Groups)
        {
            var shellConfig = _serverConfigurationManager.GetShellConfigForGid(group.Key.GID);
            if (shellConfig.Enabled && shellConfig.ShellNumber == shellNumber)
            {
                _ = Task.Run(async () => {
                    var chatMsg = await _dalamudUtil.RunOnFrameworkThread(() => {
                        return new ChatMessage()
                        {
                            SenderName = _dalamudUtil.GetPlayerName(),
                            SenderHomeWorldId = _dalamudUtil.GetHomeWorldId(),
                            PayloadContent = chatBytes
                        };
                    }).ConfigureAwait(false);
                    ClearTypingState();
                    await _apiController.GroupChatSendMsg(new(group.Key), chatMsg).ConfigureAwait(false);
                }).ConfigureAwait(false);
                return;
            }
        }

        _chatGui.PrintError($"[UmbraSync] Syncshell number #{shellNumber} not found");
    }
}
