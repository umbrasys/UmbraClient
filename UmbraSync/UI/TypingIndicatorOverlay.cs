using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using UmbraSync.API.Data;
using UmbraSync.API.Data.Extensions;
using UmbraSync.API.Data.Enum;
using UmbraSync.MareConfiguration;
using UmbraSync.MareConfiguration.Models;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.WebAPI;
using UmbraSync.Localization;
using Microsoft.Extensions.Logging;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace UmbraSync.UI;

public sealed class TypingIndicatorOverlay : WindowMediatorSubscriberBase
{
    private const int NameplateIconId = 61397;
    private static readonly TimeSpan TypingDisplayTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TypingDisplayDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TypingDisplayFade = TypingDisplayTime;
    private const int AllianceMemberSlots = 24;

    private readonly ILogger<TypingIndicatorOverlay> _typedLogger;
    private readonly MareConfigService _configService;
    private readonly IGameGui _gameGui;
    private readonly ITextureProvider _textureProvider;
    private readonly IClientState _clientState;
    private readonly PairManager _pairManager;
    private readonly IPartyList _partyList;
    private readonly IObjectTable _objectTable;
    private IPlayerCharacter? LocalPlayer => _clientState.LocalPlayer;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly TypingIndicatorStateService _typingStateService;
    private readonly ApiController _apiController;
    private readonly List<(uint EntityId, string Name)> _allianceMembersCache = new(AllianceMemberSlots);

    public TypingIndicatorOverlay(ILogger<TypingIndicatorOverlay> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
        MareConfigService configService, IGameGui gameGui, ITextureProvider textureProvider, IClientState clientState,
        IPartyList partyList, IObjectTable objectTable, DalamudUtilService dalamudUtil, PairManager pairManager,
        TypingIndicatorStateService typingStateService, ApiController apiController)
        : base(logger, mediator, Loc.Get("TypingOverlay.WindowTitle"), performanceCollectorService)
    {
        _typedLogger = logger;
        _configService = configService;
        _gameGui = gameGui;
        _textureProvider = textureProvider;
        _clientState = clientState;
        _partyList = partyList;
        _objectTable = objectTable;
        _dalamudUtil = dalamudUtil;
        _pairManager = pairManager;
        _typingStateService = typingStateService;
        _apiController = apiController;

        RespectCloseHotkey = false;
        IsOpen = true;
        Flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav;
    }

    protected override void DrawInternal()
    {
        var viewport = ImGui.GetMainViewport();
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetWindowPos(viewport.Pos);
        ImGui.SetWindowSize(viewport.Size);

        if (!_clientState.IsLoggedIn)
            return;

        var typingEnabled = _configService.Current.TypingIndicatorEnabled;
        if (!typingEnabled)
            return;

        var showParty = _configService.Current.TypingIndicatorShowOnPartyList;
        var showNameplates = _configService.Current.TypingIndicatorShowOnNameplates;

        if (!showParty && !showNameplates)
            return;

        var overlayDrawList = ImGui.GetWindowDrawList();
        var activeTypers = _typingStateService.GetActiveTypers(TypingDisplayTime);
        var hasSelf = _typingStateService.TryGetSelfTyping(TypingDisplayTime, out var selfStart, out var selfLast);
        var now = DateTime.UtcNow;
        var allianceMembers = GetAllianceMembersSnapshot();

        if (showParty)
        {
            DrawPartyIndicators(overlayDrawList, activeTypers, hasSelf, now, selfStart, selfLast, allianceMembers);
        }

        if (showNameplates)
        {
            DrawNameplateIndicators(ImGui.GetWindowDrawList(), activeTypers, hasSelf, now, selfStart, selfLast, allianceMembers);
        }
    }

    private unsafe void DrawPartyIndicators(ImDrawListPtr drawList, IReadOnlyDictionary<string, (UserData User, DateTime FirstSeen, DateTime LastUpdate, UmbraSync.API.Data.Enum.TypingScope Scope)> activeTypers,
        bool selfActive, DateTime now, DateTime selfStart, DateTime selfLast, IReadOnlyList<(uint EntityId, string Name)> allianceMembers)
    {
        var partyAddon = (AtkUnitBase*)_gameGui.GetAddonByName("_PartyList", 1).Address;
        if (partyAddon == null || !partyAddon->IsVisible)
            return;

        var showSelf = _configService.Current.TypingIndicatorShowSelf;
        if (selfActive
            && showSelf
            && (now - selfStart) >= TypingDisplayDelay
            && (now - selfLast) <= TypingDisplayFade)
        {
            DrawPartyMemberTyping(drawList, partyAddon, 0);
        }

        foreach (var (uid, entry) in activeTypers)
        {
            if ((now - entry.LastUpdate) > TypingDisplayFade)
                continue;

            var pair = _pairManager.GetPairByUID(uid);
            var targetIndex = -1;
            var playerName = pair?.PlayerName;
            var objectId = pair?.PlayerCharacterId ?? uint.MaxValue;
            var resolvedName = !string.IsNullOrEmpty(playerName) ? playerName : entry.User.AliasOrUID;

            if (objectId != 0 && objectId != uint.MaxValue)
            {
                targetIndex = GetPartyIndexForObjectId(objectId);
                if (targetIndex >= 0 && !string.IsNullOrEmpty(playerName))
                {
                    var member = _partyList[targetIndex];
                    if (member == null)
                        continue;

                    var memberName = member.Name.TextValue;
                    if (!string.IsNullOrEmpty(memberName) && !memberName.Equals(playerName, StringComparison.OrdinalIgnoreCase))
                    {
                        var nameIndex = GetPartyIndexForName(playerName);
                        targetIndex = nameIndex;
                    }
                }
            }

            if (targetIndex < 0 && !string.IsNullOrEmpty(playerName))
            {
                targetIndex = GetPartyIndexForName(playerName);
            }

            if (targetIndex < 0)
                continue;

            if (entry.Scope == TypingScope.FreeCompany && !IsFreeCompanyMember(objectId, resolvedName))
            {
                _typedLogger.LogTrace("TypingIndicator: suppressed non-FC party bubble for {uid} due to scope={scope}", uid, entry.Scope);
                continue;
            }

            if (entry.Scope == TypingScope.Alliance && !IsAllianceMember(objectId, resolvedName, allianceMembers))
            {
                _typedLogger.LogTrace("TypingIndicator: suppressed non-alliance party bubble for {uid} due to scope={scope}", uid, entry.Scope);
                continue;
            }

            DrawPartyMemberTyping(drawList, partyAddon, targetIndex);
        }
    }

    private unsafe void DrawPartyMemberTyping(ImDrawListPtr drawList, AtkUnitBase* partyList, int memberIndex)
    {
        if (memberIndex < 0 || memberIndex > 7) return;

        var nodeIndex = 23 - memberIndex;
        if (partyList->UldManager.NodeListCount <= nodeIndex) return;

        var memberNode = (AtkComponentNode*)partyList->UldManager.NodeList[nodeIndex];
        if (memberNode == null || !memberNode->AtkResNode.IsVisible()) return;

        var iconNode = memberNode->Component->UldManager.NodeListCount > 4 ? memberNode->Component->UldManager.NodeList[4] : null;
        if (iconNode == null) return;

        var align = partyList->UldManager.NodeList[3]->Y;
        var partyScale = partyList->Scale;

        var iconOffset = new Vector2(-14, 8) * partyScale;
        var iconSize = new Vector2(iconNode->Width / 2f, iconNode->Height / 2f) * partyScale;

        var iconPos = new Vector2(
            partyList->X + (memberNode->AtkResNode.X * partyScale) + (iconNode->X * partyScale) + (iconNode->Width * partyScale / 2f),
            partyList->Y + align + (memberNode->AtkResNode.Y * partyScale) + (iconNode->Y * partyScale) + (iconNode->Height * partyScale / 2f));

        iconPos += iconOffset;

        var texture = _textureProvider.GetFromGame("ui/uld/charamake_dataimport.tex").GetWrapOrEmpty();
        if (texture.Handle == IntPtr.Zero) return;

        drawList.AddImage(texture.Handle, iconPos, iconPos + iconSize, Vector2.Zero, Vector2.One,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.9f)));
    }

    private unsafe void DrawNameplateIndicators(ImDrawListPtr drawList, IReadOnlyDictionary<string, (UserData User, DateTime FirstSeen, DateTime LastUpdate, UmbraSync.API.Data.Enum.TypingScope Scope)> activeTypers,
        bool selfActive, DateTime now, DateTime selfStart, DateTime selfLast, IReadOnlyList<(uint EntityId, string Name)> allianceMembers)
    {
        var iconWrap = _textureProvider.GetFromGameIcon(NameplateIconId).GetWrapOrEmpty();
        if (iconWrap.Handle == IntPtr.Zero)
            return;

        var showSelf = _configService.Current.TypingIndicatorShowSelf;
        var selfPlayer = LocalPlayer;
        if (selfActive
            && showSelf
            && selfPlayer != null
            && (now - selfStart) >= TypingDisplayDelay
            && (now - selfLast) <= TypingDisplayFade)
        {
            var selfId = GetEntityId(selfPlayer.Address);
            if (selfId != 0 && !TryDrawNameplateBubble(drawList, iconWrap, selfId))
            {
                DrawWorldFallbackIcon(drawList, iconWrap, selfPlayer.Position);
            }
        }

        foreach (var (uid, entry) in activeTypers)
        {
            if ((now - entry.LastUpdate) > TypingDisplayFade)
                continue;

            if (string.Equals(uid, _apiController.UID, StringComparison.Ordinal))
                continue;

            var pair = _pairManager.GetPairByUID(uid);
            var objectId = pair?.PlayerCharacterId ?? 0;
            var pairName = pair?.PlayerName ?? entry.User.AliasOrUID ?? string.Empty;
            var pairIdent = pair?.Ident ?? string.Empty;
            var isPartyMember = IsPartyMember(objectId, pairName);
            var isAllianceMember = IsAllianceMember(objectId, pairName, allianceMembers);
            var isFreeCompanyMember = IsFreeCompanyMember(objectId, pairName);

            // Enforce party-only visibility when the scope is Party/CrossParty
            if (entry.Scope is TypingScope.Party or TypingScope.CrossParty && !isPartyMember)
            {
                _typedLogger.LogTrace("TypingIndicator: suppressed non-party bubble for {uid} due to scope={scope}", uid, entry.Scope);
                continue;
            }
            if (entry.Scope == TypingScope.FreeCompany && !isFreeCompanyMember)
            {
                _typedLogger.LogTrace("TypingIndicator: suppressed non-FC bubble for {uid} due to scope={scope}", uid, entry.Scope);
                continue;
            }
            if (entry.Scope == TypingScope.Alliance && !isAllianceMember)
            {
                _typedLogger.LogTrace("TypingIndicator: suppressed non-alliance bubble for {uid} due to scope={scope}", uid, entry.Scope);
                continue;
            }

            var isRelevantMember = IsPlayerRelevant(pair, isPartyMember);

            if (objectId != uint.MaxValue && objectId != 0 && TryDrawNameplateBubble(drawList, iconWrap, objectId))
            {
                _typedLogger.LogTrace("TypingIndicator: drew nameplate bubble for {uid} (objectId={objectId}, scope={scope})", uid, objectId, entry.Scope);
                continue;
            }

            var hasWorldPosition = TryResolveWorldPosition(pair, entry.User, objectId, out var worldPos);
            var isNearby = hasWorldPosition && IsWithinRelevantDistance(worldPos);

            if (!isRelevantMember && !isNearby)
                continue;

            // For Party/CrossParty scope, do not draw fallback world icon for non-party even if nearby
            if (entry.Scope is TypingScope.Party or TypingScope.CrossParty && !isPartyMember)
                continue;
            if (entry.Scope == TypingScope.FreeCompany && !isFreeCompanyMember)
                continue;
            if (entry.Scope == TypingScope.Alliance && !isAllianceMember)
                continue;

            if (pair == null)
            {
                _typedLogger.LogTrace("TypingIndicator: no pair found for {uid}, attempting fallback", uid);
            }

            _typedLogger.LogTrace("TypingIndicator: fallback draw for {uid} (objectId={objectId}, name={name}, ident={ident}, scope={scope})",
                uid, objectId, pairName, pairIdent, entry.Scope);

            if (hasWorldPosition)
            {
                DrawWorldFallbackIcon(drawList, iconWrap, worldPos);
                _typedLogger.LogTrace("TypingIndicator: fallback world draw for {uid} at {pos}", uid, worldPos);
            }
            else
            {
                _typedLogger.LogTrace("TypingIndicator: could not resolve position for {uid}", uid);
            }
        }
    }

    private Vector2 GetConfiguredBubbleSize(float scaleX, float scaleY, bool isNameplateVisible, TypingIndicatorBubbleSize? overrideSize = null)
    {
        var sizeSetting = overrideSize ?? _configService.Current.TypingIndicatorBubbleSize;
        var baseSize = sizeSetting switch
        {
            TypingIndicatorBubbleSize.Small when isNameplateVisible => 32f,
            TypingIndicatorBubbleSize.Medium when isNameplateVisible => 44f,
            TypingIndicatorBubbleSize.Large when isNameplateVisible => 56f,
            TypingIndicatorBubbleSize.Small => 15f,
            TypingIndicatorBubbleSize.Medium => 25f,
            TypingIndicatorBubbleSize.Large => 35f,
            _ => 35f,
        };

        return new Vector2(baseSize * scaleX, baseSize * scaleY);
    }

    private unsafe bool TryDrawNameplateBubble(ImDrawListPtr drawList, IDalamudTextureWrap textureWrap, uint objectId)
    {
        if (textureWrap.Handle == IntPtr.Zero)
            return false;

        var framework = Framework.Instance();
        if (framework == null)
            return false;

        var ui3D = framework->GetUIModule()->GetUI3DModule();
        if (ui3D == null)
            return false;

        var addonNamePlate = (AddonNamePlate*)_gameGui.GetAddonByName("NamePlate", 1).Address;
        if (addonNamePlate == null)
            return false;

        AddonNamePlate.NamePlateObject* namePlate = null;
        float distance = 0f;

        for (var i = 0; i < ui3D->NamePlateObjectInfoCount; i++)
        {
            var objectInfo = ui3D->NamePlateObjectInfoPointers[i];
            if (objectInfo.Value == null || objectInfo.Value->GameObject == null)
                continue;

            if (objectInfo.Value->GameObject->EntityId != objectId)
                continue;

            if (objectInfo.Value->GameObject->YalmDistanceFromPlayerX > 35f)
                return false;

            namePlate = &addonNamePlate->NamePlateObjectArray[objectInfo.Value->NamePlateIndex];
            distance = objectInfo.Value->GameObject->YalmDistanceFromPlayerX;
            break;
        }

        if (namePlate == null || namePlate->RootComponentNode == null)
            return false;

        var rootComponent = namePlate->RootComponentNode;
        var component = rootComponent->Component;
        if (component == null || component->UldManager.NodeListCount == 0)
            return false;

        var iconNode = component->UldManager.NodeList[0];
        if (iconNode == null)
            return false;

        var rootVisible = rootComponent->AtkResNode.IsVisible();
        var iconVisible = iconNode->IsVisible();
        var isNameplateVisible = rootVisible && iconVisible;

        var scaleX = rootComponent->AtkResNode.ScaleX;
        var scaleY = rootComponent->AtkResNode.ScaleY;
        var scaleVector = new Vector2(scaleX, scaleY);
        var rootPosition = new Vector2(rootComponent->AtkResNode.X, rootComponent->AtkResNode.Y);
        var iconLocalPosition = new Vector2(iconNode->X, iconNode->Y) * scaleVector;
        var iconDimensions = new Vector2(iconNode->Width, iconNode->Height) * scaleVector;

        // Decide style: if plate is hidden/masked, force Top; otherwise use configured style
        var style = _configService.Current.TypingIndicatorNameplateStyle;
        var useTop = !isNameplateVisible || style == TypingIndicatorNameplateStyle.Top;

        Vector2 iconPos;
        Vector2 iconOffset;
        Vector2 iconSize;

        if (useTop)
        {
            // Top style placement above the nameplate area (centered horizontally)
            // Compute bubble size first to properly center it.
            iconSize = GetConfiguredBubbleSize(scaleX, scaleY, true, TypingIndicatorBubbleSize.Large);
            var basePos = rootPosition + iconLocalPosition;
            // Center X on the nameplate area, then apply vertical offset logic.
            iconPos = new Vector2(basePos.X + (iconDimensions.X * 0.5f) - (iconSize.X * 0.5f), basePos.Y);
            iconOffset = new Vector2(0f, (-24f + (distance / 1f)) * scaleY);
            if (iconNode->Height == 24)
            {
                iconOffset.Y += 16f * scaleY;
            }
            // When nameplate UI is hidden, push the bubble a bit further down to keep it readable
            if (!isNameplateVisible)
            {
                iconOffset.Y += 48f * scaleY;
            }

            iconPos += iconOffset;
        }
        else
        {
            // Side style placement next to the plate's name area (RTyping-like when plate is visible)
            iconPos = rootPosition + iconLocalPosition + new Vector2(iconDimensions.X, 0f);
            iconOffset = new Vector2(distance / 1.5f, distance / 3f) * scaleVector;
            if (iconNode->Height == 24)
            {
                iconOffset.Y -= 8f * scaleY;
            }

            iconPos += iconOffset;
            iconSize = GetConfiguredBubbleSize(scaleX, scaleY, true);
        }

        var nameplateOpacity = Math.Clamp(_configService.Current.TypingIndicatorNameplateOpacity, 0f, 1f);
        drawList.AddImage(textureWrap.Handle, iconPos, iconPos + iconSize, Vector2.Zero, Vector2.One,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, nameplateOpacity)));

        return true;
    }

    private void DrawWorldFallbackIcon(ImDrawListPtr drawList, IDalamudTextureWrap textureWrap, Vector3 worldPosition)
    {
        var offsetPosition = worldPosition + new Vector3(0f, 1.8f, 0f);
        if (!_gameGui.WorldToScreen(offsetPosition, out var screenPos))
            return;

        var iconSize = GetConfiguredBubbleSize(ImGuiHelpers.GlobalScale, ImGuiHelpers.GlobalScale, false);
        var iconPos = screenPos - (iconSize / 2f) - new Vector2(0f, iconSize.Y * 0.6f);
        drawList.AddImage(textureWrap.Handle, iconPos, iconPos + iconSize, Vector2.Zero, Vector2.One,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f)));
    }

    private bool TryGetWorldPosition(uint objectId, out Vector3 position)
    {
        position = Vector3.Zero;
        if (objectId == 0 || objectId == uint.MaxValue)
            return false;

        for (var i = 0; i < _objectTable.Length; ++i)
        {
            var obj = _objectTable[i];
            if (obj == null)
                continue;

            if (obj.EntityId == objectId)
            {
                position = obj.Position;
                return true;
            }
        }

        return false;
    }

    private bool TryResolveWorldPosition(Pair? pair, UserData userData, uint objectId, out Vector3 position)
    {
        if (TryGetWorldPosition(objectId, out position))
        {
            _typedLogger.LogTrace("TypingIndicator: resolved by objectId {objectId}", objectId);
            return true;
        }

        if (pair != null)
        {
            var name = pair.PlayerName;
            if (!string.IsNullOrEmpty(name) && TryGetWorldPositionByName(name!, out position))
            {
                _typedLogger.LogTrace("TypingIndicator: resolved by pair name {name}", name);
                return true;
            }

            var ident = pair.Ident;
            if (!string.IsNullOrEmpty(ident))
            {
                var cached = _dalamudUtil.FindPlayerByNameHash(ident);
                if (!string.IsNullOrEmpty(cached.Name) && TryGetWorldPositionByName(cached.Name, out position))
                {
                    _typedLogger.LogTrace("TypingIndicator: resolved by cached name {name}", cached.Name);
                    return true;
                }

                if (cached.Address != IntPtr.Zero)
                {
                    var objRef = _objectTable.CreateObjectReference(cached.Address);
                    if (objRef != null)
                    {
                        position = objRef.Position;
                        _typedLogger.LogTrace("TypingIndicator: resolved by cached address {addr}", cached.Address);
                        return true;
                    }
                }
            }
        }

        var alias = userData.AliasOrUID;
        if (!string.IsNullOrEmpty(alias) && TryGetWorldPositionByName(alias, out position))
        {
            _typedLogger.LogTrace("TypingIndicator: resolved by user alias {alias}", alias);
            return true;
        }

        return false;
    }

    private bool TryGetWorldPositionByName(string name, out Vector3 position)
    {
        position = Vector3.Zero;
        for (var i = 0; i < _objectTable.Length; ++i)
        {
            var obj = _objectTable[i];
            if (obj == null)
                continue;

            if (obj.Name.TextValue.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                position = obj.Position;
                return true;
            }
        }

        return false;
    }

    private int GetPartyIndexForObjectId(uint objectId)
    {
        for (var i = 0; i < _partyList.Count; ++i)
        {
            var member = _partyList[i];
            if (member == null) continue;

            var gameObject = member.GameObject;
            if (gameObject != null && GetEntityId(gameObject.Address) == objectId)
                return i;
        }

        return -1;
    }

    private int GetPartyIndexForName(string name)
    {
        for (var i = 0; i < _partyList.Count; ++i)
        {
            var member = _partyList[i];
            if (member?.Name == null) continue;

            if (member.Name.TextValue.Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private bool IsPartyMember(uint objectId, string? playerName)
    {
        if (objectId != 0 && objectId != uint.MaxValue && GetPartyIndexForObjectId(objectId) >= 0)
            return true;

        if (!string.IsNullOrEmpty(playerName) && GetPartyIndexForName(playerName) >= 0)
            return true;

        return false;
    }

    private IReadOnlyList<(uint EntityId, string Name)> GetAllianceMembersSnapshot()
    {
        _allianceMembersCache.Clear();
        if (!_partyList.IsAlliance)
            return _allianceMembersCache;

        for (var i = 0; i < AllianceMemberSlots; ++i)
        {
            var memberAddress = _partyList.GetAllianceMemberAddress(i);
            if (memberAddress == nint.Zero)
                continue;

            var member = _partyList.CreateAllianceMemberReference(memberAddress);
            if (member == null)
                continue;

            var name = member.Name?.TextValue ?? string.Empty;
            var entityId = member.EntityId;
            if (entityId == 0 && string.IsNullOrEmpty(name))
                continue;

            _allianceMembersCache.Add((entityId, name));
        }

        return _allianceMembersCache;
    }

    private static bool IsAllianceMember(uint objectId, string? playerName, IReadOnlyList<(uint EntityId, string Name)> allianceMembers)
    {
        if (allianceMembers.Count == 0)
            return false;

        if (objectId != 0 && objectId != uint.MaxValue && allianceMembers.Any(m => m.EntityId == objectId))
            return true;

        if (string.IsNullOrEmpty(playerName))
            return false;

        return allianceMembers.Any(m =>
            !string.IsNullOrEmpty(m.Name) && m.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsFreeCompanyMember(uint objectId, string? playerName)
    {
        var localPlayer = LocalPlayer;
        if (localPlayer == null)
            return false;

        var localTag = localPlayer.CompanyTag?.TextValue;
        if (string.IsNullOrEmpty(localTag))
            return false;

        if (TryGetFreeCompanyTag(objectId, out var remoteTag))
            return string.Equals(remoteTag, localTag, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(playerName)
            && TryGetFreeCompanyTagByName(playerName, out remoteTag))
        {
            return string.Equals(remoteTag, localTag, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private bool TryGetFreeCompanyTag(uint objectId, out string tag)
    {
        tag = string.Empty;
        if (objectId == 0 || objectId == uint.MaxValue)
            return false;

        for (var i = 0; i < _objectTable.Length; ++i)
        {
            var obj = _objectTable[i];
            if (obj == null || obj.EntityId != objectId)
                continue;

            if (obj is IPlayerCharacter player)
            {
                var remoteTag = player.CompanyTag?.TextValue;
                if (!string.IsNullOrEmpty(remoteTag))
                {
                    tag = remoteTag;
                    return true;
                }
            }

            break;
        }

        return false;
    }

    private bool TryGetFreeCompanyTagByName(string name, out string tag)
    {
        tag = string.Empty;
        if (string.IsNullOrEmpty(name))
            return false;

        for (var i = 0; i < _objectTable.Length; ++i)
        {
            if (_objectTable[i] is IPlayerCharacter player
                && player.Name.TextValue.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                var remoteTag = player.CompanyTag?.TextValue;
                if (!string.IsNullOrEmpty(remoteTag))
                {
                    tag = remoteTag;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPlayerRelevant(Pair? pair, bool isPartyMember)
    {
        if (isPartyMember)
            return true;

        if (pair?.UserPair != null)
        {
            var userPair = pair.UserPair;
            if (userPair.OtherPermissions.IsPaired() || userPair.OwnPermissions.IsPaired())
                return true;
        }

        if (pair?.GroupPair != null && pair.GroupPair.Any(g =>
                !g.Value.GroupUserPermissions.IsPaused() &&
                !g.Key.GroupUserPermissions.IsPaused()))
        {
            return true;
        }

        return false;
    }

    private bool IsWithinRelevantDistance(Vector3 position)
    {
        var localPlayer = LocalPlayer;
        if (localPlayer == null)
            return false;

        var distance = Vector3.Distance(localPlayer.Position, position);
        return distance <= 40f;
    }

    private static unsafe uint GetEntityId(nint address)
    {
        if (address == nint.Zero) return 0;
        return ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address)->EntityId;
    }
}
