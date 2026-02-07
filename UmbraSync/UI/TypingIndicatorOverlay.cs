using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.Logging;
using System.Numerics;
using UmbraSync.API.Data;
using UmbraSync.API.Data.Extensions;
using UmbraSync.MareConfiguration;
using UmbraSync.MareConfiguration.Models;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;

namespace UmbraSync.UI;

public sealed class TypingIndicatorOverlay : WindowMediatorSubscriberBase
{
    private const int NameplateIconId = 61397;
    private static readonly TimeSpan TypingDisplayTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TypingDisplayDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TypingDisplayFade = TypingDisplayTime;

    private readonly ILogger<TypingIndicatorOverlay> _typedLogger;
    private readonly MareConfigService _configService;
    private readonly IGameGui _gameGui;
    private readonly ITextureProvider _textureProvider;
    private readonly IClientState _clientState;
    private readonly PairManager _pairManager;
    private readonly IPartyList _partyList;
    private readonly IObjectTable _objectTable;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly TypingIndicatorStateService _typingStateService;
    private readonly ApiController _apiController;

    public TypingIndicatorOverlay(ILogger<TypingIndicatorOverlay> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
        MareConfigService configService, IGameGui gameGui, ITextureProvider textureProvider, IClientState clientState,
        IPartyList partyList, IObjectTable objectTable, DalamudUtilService dalamudUtil, PairManager pairManager,
        TypingIndicatorStateService typingStateService, ApiController apiController)
        : base(logger, mediator, nameof(TypingIndicatorOverlay), performanceCollectorService)
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

        var showParty = _configService.Current.TypingIndicatorShowOnPartyList;
        var showNameplates = _configService.Current.TypingIndicatorShowOnNameplates;

        if ((!showParty && !showNameplates) || _dalamudUtil.IsInGpose)
            return;

        var overlayDrawList = ImGui.GetWindowDrawList();
        var activeTypers = _typingStateService.GetActiveTypers(TypingDisplayTime);
        var hasSelf = _typingStateService.TryGetSelfTyping(TypingDisplayTime, out var selfStart, out var selfLast);
        var now = DateTime.UtcNow;

        if (showParty)
        {
            DrawPartyIndicators(overlayDrawList, activeTypers, hasSelf, now, selfStart, selfLast);
        }

        if (showNameplates)
        {
            DrawNameplateIndicators(ImGui.GetWindowDrawList(), activeTypers, hasSelf, now, selfStart, selfLast);
        }
    }

    private unsafe void DrawPartyIndicators(ImDrawListPtr drawList, IReadOnlyDictionary<string, (UserData User, DateTime FirstSeen, DateTime LastUpdate)> activeTypers,
        bool selfActive, DateTime now, DateTime selfStart, DateTime selfLast)
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
            
            if (objectId != 0 && objectId != uint.MaxValue)
            {
                targetIndex = GetPartyIndexFromAgentHUD(objectId);
            }
            if (targetIndex < 0 && objectId != 0 && objectId != uint.MaxValue)
            {
                targetIndex = GetPartyIndexForObjectId(objectId);
            }
            
            if (targetIndex < 0 && !string.IsNullOrEmpty(playerName))
            {
                targetIndex = GetPartyIndexForName(playerName);
            }

            if (targetIndex < 0)
                continue;

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

    private unsafe void DrawNameplateIndicators(ImDrawListPtr drawList, IReadOnlyDictionary<string, (UserData User, DateTime FirstSeen, DateTime LastUpdate)> activeTypers,
        bool selfActive, DateTime now, DateTime selfStart, DateTime selfLast)
    {
        var iconWrap = _textureProvider.GetFromGameIcon(NameplateIconId).GetWrapOrEmpty();
        if (iconWrap.Handle == IntPtr.Zero)
            return;

        var showSelf = _configService.Current.TypingIndicatorShowSelf;
        if (selfActive
            && showSelf
            && _objectTable.LocalPlayer != null
            && (now - selfStart) >= TypingDisplayDelay
            && (now - selfLast) <= TypingDisplayFade)
        {
            var selfId = GetEntityId(_objectTable.LocalPlayer.Address);
            if (selfId != 0)
            {
                TryDrawNameplateBubble(drawList, iconWrap, selfId);
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
            var pairName = pair?.PlayerName ?? entry.User.AliasOrUID;
            var pairIdent = pair?.Ident ?? string.Empty;
            var isPartyMember = IsPartyMember(objectId, pairName);
            var isRelevantMember = IsPlayerRelevant(pair, isPartyMember);

            if (objectId != uint.MaxValue && objectId != 0 && TryDrawNameplateBubble(drawList, iconWrap, objectId))
            {
                _typedLogger.LogTrace("TypingIndicator: drew nameplate bubble for {uid} (objectId={objectId})", uid, objectId);
                continue;
            }

            var hasWorldPosition = TryResolveWorldPosition(pair, entry.User, objectId, out var worldPos);
            var isNearby = hasWorldPosition && IsWithinRelevantDistance(worldPos);

            if (!isRelevantMember && !isNearby)
                continue;

            if (pair == null)
            {
                _typedLogger.LogTrace("TypingIndicator: no pair found for {uid}, attempting fallback", uid);
            }

            _typedLogger.LogTrace("TypingIndicator: fallback draw for {uid} (objectId={objectId}, name={name}, ident={ident})",
                uid, objectId, pairName, pairIdent);

            if (hasWorldPosition && HasLineOfSightFromCamera(worldPos))
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
        var targetWorldPos = Vector3.Zero;

        for (var i = 0; i < ui3D->NamePlateObjectInfoCount; i++)
        {
            var objectInfo = ui3D->NamePlateObjectInfoPointers[i];
            if (objectInfo.Value == null || objectInfo.Value->GameObject == null)
                continue;

            if (objectInfo.Value->GameObject->EntityId != objectId)
                continue;

            if ((byte)objectInfo.Value->GameObject->ObjectKind != 1)
                continue;

            if (objectInfo.Value->GameObject->YalmDistanceFromPlayerX > 15f)
                return false;

            namePlate = &addonNamePlate->NamePlateObjectArray[objectInfo.Value->NamePlateIndex];
            distance = objectInfo.Value->GameObject->YalmDistanceFromPlayerX;
            targetWorldPos = objectInfo.Value->GameObject->Position;
            break;
        }

        if (namePlate == null || namePlate->RootComponentNode == null)
            return false;

        if (!HasLineOfSightFromCamera(targetWorldPos))
            return false;

        var iconNode = namePlate->RootComponentNode->Component->UldManager.NodeList[0];
        if (iconNode == null)
            return false;

        var nameplateScaleX = namePlate->RootComponentNode->AtkResNode.ScaleX;
        var nameplateScaleY = namePlate->RootComponentNode->AtkResNode.ScaleY;
        var iconVisible = iconNode->IsVisible();
        var scaleVector = new Vector2(nameplateScaleX, nameplateScaleY);
        float bubbleScaleFactor = distance <= 10f ? 1.0f : 0.5f;
        var rootPosition = new Vector2(namePlate->RootComponentNode->AtkResNode.X, namePlate->RootComponentNode->AtkResNode.Y);
        var iconLocalPosition = new Vector2(iconNode->X, iconNode->Y) * scaleVector;
        var iconDimensions = new Vector2(iconNode->Width, iconNode->Height) * scaleVector;

        if (!iconVisible)
        {
            return false;
        }

        var iconPos = rootPosition + iconLocalPosition + new Vector2(iconDimensions.X, 0f);

        var iconOffset = new Vector2(distance / 1.5f, distance / 3.5f) * scaleVector;
        if (iconNode->Height == 24)
        {
            iconOffset.Y -= 8f * nameplateScaleY;
        }

        iconPos += iconOffset;

        var bubbleSize = GetConfiguredBubbleSize(bubbleScaleFactor, bubbleScaleFactor, true);

        if (IsScreenPointBehindGameUI(iconPos + bubbleSize / 2f))
            return false;

        drawList.AddImage(textureWrap.Handle, iconPos, iconPos + bubbleSize, Vector2.Zero, Vector2.One,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f)));

        return true;
    }

    private void DrawWorldFallbackIcon(ImDrawListPtr drawList, IDalamudTextureWrap textureWrap, Vector3 worldPosition)
    {
        var offsetPosition = worldPosition + new Vector3(0f, 1.8f, 0f);
        if (!_gameGui.WorldToScreen(offsetPosition, out var screenPos))
            return;

        var iconSize = GetConfiguredBubbleSize(ImGuiHelpers.GlobalScale, ImGuiHelpers.GlobalScale, false);
        var iconPos = screenPos - (iconSize / 2f) - new Vector2(0f, iconSize.Y * 0.6f);

        if (IsScreenPointBehindGameUI(iconPos + iconSize / 2f))
            return;

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

            if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
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

            if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                continue;

            if (obj.Name.TextValue.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                position = obj.Position;
                return true;
            }
        }

        return false;
    }

    private unsafe int GetPartyIndexFromAgentHUD(uint objectId)
    {
        if (objectId == 0 || objectId == uint.MaxValue)
            return -1;

        try
        {
            var framework = Framework.Instance();
            if (framework == null) return -1;

            var uiModule = framework->GetUIModule();
            if (uiModule == null) return -1;

            var agentModule = uiModule->GetAgentModule();
            if (agentModule == null) return -1;

            var agentHud = agentModule->GetAgentHUD();
            if (agentHud == null) return -1;

            var partyMembers = agentHud->PartyMembers;

            for (var i = 0; i < agentHud->PartyMemberCount; i++)
            {
                if (partyMembers[i].EntityId == objectId)
                    return i;
            }
        }
        catch (Exception ex)
        {
            _typedLogger.LogDebug(ex, "Failed to get party index from AgentHUD for objectId {ObjectId}", objectId);
        }

        return -1;
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
        if (objectId != 0 && objectId != uint.MaxValue && GetPartyIndexFromAgentHUD(objectId) >= 0)
            return true;

        if (objectId != 0 && objectId != uint.MaxValue && GetPartyIndexForObjectId(objectId) >= 0)
            return true;

        if (!string.IsNullOrEmpty(playerName) && GetPartyIndexForName(playerName) >= 0)
            return true;

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
        if (_objectTable.LocalPlayer == null)
            return false;

        var distance = Vector3.Distance(_objectTable.LocalPlayer.Position, position);
        return distance <= 15f;
    }
    
    private static unsafe bool IsScreenPointBehindGameUI(Vector2 screenPoint)
    {
        var stage = AtkStage.Instance();
        if (stage == null) return false;

        var unitManager = stage->RaptureAtkUnitManager;
        if (unitManager == null) return false;

        for (var i = 0; i < unitManager->AtkUnitManager.AllLoadedUnitsList.Count; i++)
        {
            var addon = unitManager->AtkUnitManager.AllLoadedUnitsList.Entries[i].Value;
            if (addon == null || !addon->IsVisible || addon->RootNode == null)
                continue;

            var name = addon->NameString;
            if (string.IsNullOrEmpty(name) || name[0] == '_')
                continue;

            if (name is "NamePlate" or "FadeMiddle" or "FadeBlack" or "NowLoading"
                or "ScreenFrameSystem" or "ChatLog" or "ChatLogPanel_0" or "ChatLogPanel_1"
                or "ChatLogPanel_2" or "ChatLogPanel_3")
                continue;

            var addonPos = new Vector2(addon->X, addon->Y);
            var addonW = addon->RootNode->Width * addon->Scale;
            var addonH = addon->RootNode->Height * addon->Scale;

            if (screenPoint.X >= addonPos.X && screenPoint.X <= addonPos.X + addonW &&
                screenPoint.Y >= addonPos.Y && screenPoint.Y <= addonPos.Y + addonH)
                return true;
        }

        return false;
    }

    private static unsafe bool HasLineOfSightFromCamera(Vector3 target, float targetHeightOffset = 1.0f)
    {
        var cameraManager = CameraManager.Instance();
        if (cameraManager == null || cameraManager->CurrentCamera == null)
            return true;

        var cameraPos = cameraManager->CurrentCamera->Position;
        return HasLineOfSight(cameraPos, target, fromHeightOffset: 0f, toHeightOffset: targetHeightOffset);
    }

    private static unsafe bool HasLineOfSight(Vector3 from, Vector3 to, float fromHeightOffset = 0f, float toHeightOffset = 1.0f)
    {
        var origin = new Vector3(from.X, from.Y + fromHeightOffset, from.Z);
        var target = new Vector3(to.X, to.Y + toHeightOffset, to.Z);

        var direction = target - origin;
        var distance = direction.Length();

        if (distance < 0.001f)
            return true;

        direction /= distance;

        var module = Framework.Instance()->BGCollisionModule;
        if (module != null && module->SceneManager != null && module->SceneManager->FirstScene != null)
        {
            var originV4 = new Vector4(origin, 0f);
            RaycastHit hit;
            var raycastParams = new RaycastParams
            {
                Algorithm = 0,
                Origin = &originV4,
                Direction = &direction,
                MaxDistance = &distance,
            };
            if (module->SceneManager->FirstScene->Raycast(&hit, ulong.MaxValue, &raycastParams))
                return false;
        }

        return !BGCollisionModule.RaycastMaterialFilter(origin, direction, out _, distance);
    }

    private static unsafe uint GetEntityId(nint address)
    {
        if (address == nint.Zero) return 0;
        return ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address)->EntityId;
    }
}