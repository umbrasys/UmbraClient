using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using UmbraSync.API.Data;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services.Mediator;

namespace UmbraSync.Services;

public class ChatNameReplacementService : DisposableMediatorSubscriberBase
{
    private readonly IChatGui _chatGui;
    private readonly PairManager _pairManager;
    private readonly UmbraProfileManager _umbraProfileManager;
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly ApiController _apiController;

    private static readonly HashSet<XivChatType> RpChatTypes =
    [
        XivChatType.Say,
        XivChatType.Yell,
        XivChatType.Shout,
        XivChatType.Party,
        XivChatType.CrossParty,
        XivChatType.Alliance,
        XivChatType.FreeCompany,
        XivChatType.CustomEmote,
        XivChatType.StandardEmote,
        XivChatType.TellIncoming,
        XivChatType.TellOutgoing,
        XivChatType.Ls1,
        XivChatType.Ls2,
        XivChatType.Ls3,
        XivChatType.Ls4,
        XivChatType.Ls5,
        XivChatType.Ls6,
        XivChatType.Ls7,
        XivChatType.Ls8,
        XivChatType.CrossLinkShell1,
        XivChatType.CrossLinkShell2,
        XivChatType.CrossLinkShell3,
        XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5,
        XivChatType.CrossLinkShell6,
        XivChatType.CrossLinkShell7,
        XivChatType.CrossLinkShell8,
    ];

    public ChatNameReplacementService(ILogger<ChatNameReplacementService> logger, MareMediator mediator,
        IChatGui chatGui, PairManager pairManager,
        UmbraProfileManager umbraProfileManager,
        MareConfigService configService, DalamudUtilService dalamudUtil, ApiController apiController)
        : base(logger, mediator)
    {
        _chatGui = chatGui;
        _pairManager = pairManager;
        _umbraProfileManager = umbraProfileManager;
        _configService = configService;
        _dalamudUtil = dalamudUtil;
        _apiController = apiController;

        _chatGui.ChatMessage += OnChatMessage;

        // Pré-charger le profil du joueur local dès la connexion
        Mediator.Subscribe<ConnectedMessage>(this, (_) => PreloadLocalProfile());
        PreloadLocalProfile();
    }

    private void PreloadLocalProfile()
    {
        if (!_apiController.IsConnected || string.IsNullOrEmpty(_apiController.UID))
            return;

        _ = _dalamudUtil.RunOnFrameworkThread(() =>
            _umbraProfileManager.GetUmbraProfile(new UserData(_apiController.UID)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _chatGui.ChatMessage -= OnChatMessage;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (isHandled || !_configService.Current.UseRpNamesInChat)
            return;

        if (!RpChatTypes.Contains(type))
            return;

        var senderText = ExtractSenderName(sender);
        if (string.IsNullOrEmpty(senderText))
            return;

        var rpName = ResolveRpName(senderText);
        if (rpName == null)
            return;

        ReplaceSenderName(ref sender, senderText, rpName);
    }

    private static string ExtractSenderName(SeString sender)
    {
        foreach (var payload in sender.Payloads)
        {
            if (payload is TextPayload textPayload && !string.IsNullOrWhiteSpace(textPayload.Text))
                return textPayload.Text.Trim();
        }
        return string.Empty;
    }

    private string? ResolveRpName(string senderName)
    {
        var localPlayerName = _dalamudUtil.GetPlayerName();
        if (!string.IsNullOrEmpty(localPlayerName) && NameMatches(senderName, localPlayerName))
        {
            if (_apiController.IsConnected && !string.IsNullOrEmpty(_apiController.UID))
            {
                var profile = _umbraProfileManager.GetUmbraProfile(new UserData(_apiController.UID));
                if (!string.IsNullOrEmpty(profile.RpFirstName) && !string.IsNullOrEmpty(profile.RpLastName)
                    && IsRpFirstNameValid(localPlayerName, profile.RpFirstName))
                    return BuildRpDisplayName(profile);
            }
        }

        foreach (var pair in _pairManager.GetOnlineUserPairs())
        {
            var playerName = pair.PlayerName;
            if (string.IsNullOrEmpty(playerName))
                continue;

            if (NameMatches(senderName, playerName))
            {
                var profile = _umbraProfileManager.GetUmbraProfile(pair.UserData);
                if (!string.IsNullOrEmpty(profile.RpFirstName) && !string.IsNullOrEmpty(profile.RpLastName)
                    && IsRpFirstNameValid(playerName, profile.RpFirstName))
                    return BuildRpDisplayName(profile);
            }
        }

        return null;
    }

    private static bool IsRpFirstNameValid(string vanillaFullName, string rpFirstName)
    {
        var spaceIndex = vanillaFullName.IndexOf(' ');
        var vanillaFirstName = spaceIndex >= 0 ? vanillaFullName[..spaceIndex] : vanillaFullName;
        foreach (var part in rpFirstName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part, vanillaFirstName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string BuildRpDisplayName(UmbraProfileData profile)
    {
        var name = $"{profile.RpFirstName} {profile.RpLastName}";
        return !string.IsNullOrEmpty(profile.RpTitle) ? $"{profile.RpTitle} {name}" : name;
    }

    private static bool NameMatches(string senderName, string fullName)
    {
        if (string.Equals(senderName, fullName, StringComparison.OrdinalIgnoreCase))
            return true;

        var atIndex = senderName.IndexOf('@');
        var nameWithoutWorld = atIndex >= 0 ? senderName[..atIndex] : senderName;

        if (string.Equals(nameWithoutWorld, fullName, StringComparison.OrdinalIgnoreCase))
            return true;

        var senderParts = nameWithoutWorld.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var fullParts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (senderParts.Length != 2 || fullParts.Length != 2)
            return false;

        return PartMatches(senderParts[0], fullParts[0]) && PartMatches(senderParts[1], fullParts[1]);
    }

    private static bool PartMatches(string part, string fullPart)
    {
        if (string.Equals(part, fullPart, StringComparison.OrdinalIgnoreCase))
            return true;

        if (part.Length == 2 && part[1] == '.' &&
            char.ToUpperInvariant(part[0]) == char.ToUpperInvariant(fullPart[0]))
            return true;

        return false;
    }

    private static void ReplaceSenderName(ref SeString sender, string originalName, string rpName)
    {
        var newPayloads = new List<Payload>(sender.Payloads.Count);
        var replaced = false;

        foreach (var payload in sender.Payloads)
        {
            if (!replaced && payload is TextPayload textPayload)
            {
                var text = textPayload.Text ?? string.Empty;
                var index = text.IndexOf(originalName, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    var newText = string.Concat(text.AsSpan(0, index), rpName, text.AsSpan(index + originalName.Length));
                    newPayloads.Add(new TextPayload(newText));
                    replaced = true;
                    continue;
                }
            }
            newPayloads.Add(payload);
        }

        if (replaced)
            sender = new SeString(newPayloads);
    }
}
