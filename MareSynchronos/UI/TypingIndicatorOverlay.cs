using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Data;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace MareSynchronos.UI;

public sealed class TypingIndicatorOverlay : WindowMediatorSubscriberBase
{
    private const int NameplateIconId = 61397;
    private static readonly TimeSpan TypingDisplayTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TypingDisplayDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TypingDisplayFade = TypingDisplayTime;

    private readonly ILogger<TypingIndicatorOverlay> _logger;
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
        _logger = logger;
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

        if (!showParty && !showNameplates)
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

        if (selfActive
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
            if (pair == null)
            {
                var alias = entry.User.AliasOrUID;
                if (string.IsNullOrEmpty(alias))
                    continue;

                var aliasIndex = GetPartyIndexForName(alias);
                if (aliasIndex >= 0)
                {
                    DrawPartyMemberTyping(drawList, partyAddon, aliasIndex);
                }
                continue;
            }

            var index = GetPartyIndexForObjectId(pair.PlayerCharacterId);
            if (index < 0)
                continue;

            DrawPartyMemberTyping(drawList, partyAddon, index);
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
        if (texture == null) return;

        drawList.AddImage(texture.Handle, iconPos, iconPos + iconSize, Vector2.Zero, Vector2.One,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.9f)));
    }

    private unsafe void DrawNameplateIndicators(ImDrawListPtr drawList, IReadOnlyDictionary<string, (UserData User, DateTime FirstSeen, DateTime LastUpdate)> activeTypers,
        bool selfActive, DateTime now, DateTime selfStart, DateTime selfLast)
    {
        var iconWrap = _textureProvider.GetFromGameIcon(NameplateIconId).GetWrapOrEmpty();
        if (iconWrap == null || iconWrap.Handle == IntPtr.Zero)
            return;

        if (selfActive
            && _clientState.LocalPlayer != null
            && (now - selfStart) >= TypingDisplayDelay
            && (now - selfLast) <= TypingDisplayFade)
        {
            var selfId = GetEntityId(_clientState.LocalPlayer.Address);
            if (selfId != 0 && !TryDrawNameplateBubble(drawList, iconWrap, selfId))
            {
                DrawWorldFallbackIcon(drawList, iconWrap, _clientState.LocalPlayer.Position);
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
            if (pair == null)
            {
                _logger.LogInformation("TypingIndicator: no pair found for {uid}, attempting fallback", uid);
            }

            if (objectId != uint.MaxValue && objectId != 0 && TryDrawNameplateBubble(drawList, iconWrap, objectId))
            {
                _logger.LogTrace("TypingIndicator: drew nameplate bubble for {uid} (objectId={objectId})", uid, objectId);
                continue;
            }

            var pairName = pair?.PlayerName ?? entry.User.AliasOrUID ?? string.Empty;
            var pairIdent = pair?.Ident ?? string.Empty;

            _logger.LogInformation("TypingIndicator: fallback draw for {uid} (objectId={objectId}, name={name}, ident={ident})",
                uid, objectId, pairName, pairIdent);

            if (TryResolveWorldPosition(pair, entry.User, objectId, out var worldPos))
            {
                DrawWorldFallbackIcon(drawList, iconWrap, worldPos);
                _logger.LogInformation("TypingIndicator: fallback world draw for {uid} at {pos}", uid, worldPos);
            }
            else
            {
                _logger.LogInformation("TypingIndicator: could not resolve position for {uid}", uid);
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
        if (textureWrap == null || textureWrap.Handle == IntPtr.Zero)
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

        var iconNode = namePlate->RootComponentNode->Component->UldManager.NodeList[0];
        if (iconNode == null)
            return false;

        var scaleX = namePlate->RootComponentNode->AtkResNode.ScaleX;
        var scaleY = namePlate->RootComponentNode->AtkResNode.ScaleY;
        var iconVisible = iconNode->IsVisible();
        var sizeScaleFactor = 1f;
        var scaleVector = new Vector2(scaleX, scaleY);
        var rootPosition = new Vector2(namePlate->RootComponentNode->AtkResNode.X, namePlate->RootComponentNode->AtkResNode.Y);
        var iconLocalPosition = new Vector2(iconNode->X, iconNode->Y) * scaleVector;
        var iconDimensions = new Vector2(iconNode->Width, iconNode->Height) * scaleVector;

        if (!iconVisible)
        {
            sizeScaleFactor = 2.5f;
            var anchor = rootPosition + iconLocalPosition + new Vector2(iconDimensions.X * 0.5f, 0f);

            var distanceOffset = new Vector2(0f, -16f + distance) * scaleVector;
            if (iconNode->Height == 24)
            {
                distanceOffset.Y += 16f * scaleY;
            }
            distanceOffset.Y += 64f * scaleY;

            var referenceSize = GetConfiguredBubbleSize(scaleX * sizeScaleFactor, scaleY * sizeScaleFactor, false, TypingIndicatorBubbleSize.Small);
            var manualOffset = new Vector2(referenceSize.X * 2.00f, referenceSize.Y * 2.00f);

            var iconSizeHidden = GetConfiguredBubbleSize(scaleX * sizeScaleFactor, scaleY * sizeScaleFactor, false);
            var center = anchor + distanceOffset + manualOffset;
            var topLeft = center - (iconSizeHidden / 2f);

            drawList.AddImage(textureWrap.Handle, topLeft, topLeft + iconSizeHidden, Vector2.Zero, Vector2.One,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f)));

            return true;
        }

        var iconPos = rootPosition + iconLocalPosition + new Vector2(iconDimensions.X, 0f);

        var iconOffset = new Vector2(distance / 1.5f, distance / 3.5f) * scaleVector;
        if (iconNode->Height == 24)
        {
            iconOffset.Y -= 8f * scaleY;
        }

        iconPos += iconOffset;

        var iconSize = GetConfiguredBubbleSize(scaleX * sizeScaleFactor, scaleY * sizeScaleFactor, true);

        drawList.AddImage(textureWrap.Handle, iconPos, iconPos + iconSize, Vector2.Zero, Vector2.One,
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
        drawList.AddImage(textureWrap.Handle, iconPos, iconPos + iconSize, Vector2.Zero, Vector2.One,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f)));
    }

    private bool TryGetWorldPosition(uint objectId, out Vector3 position)
    {
        position = Vector3.Zero;
        if (objectId == 0 || objectId == uint.MaxValue)
            return false;

        foreach (var obj in _objectTable)
        {
            if (obj?.EntityId == objectId)
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
            _logger.LogTrace("TypingIndicator: resolved by objectId {objectId}", objectId);
            return true;
        }

        if (pair != null)
        {
            var name = pair.PlayerName;
            if (!string.IsNullOrEmpty(name) && TryGetWorldPositionByName(name!, out position))
            {
                _logger.LogTrace("TypingIndicator: resolved by pair name {name}", name);
                return true;
            }

            var ident = pair.Ident;
            if (!string.IsNullOrEmpty(ident))
            {
                var cached = _dalamudUtil.FindPlayerByNameHash(ident);
                if (!string.IsNullOrEmpty(cached.Name) && TryGetWorldPositionByName(cached.Name, out position))
                {
                    _logger.LogTrace("TypingIndicator: resolved by cached name {name}", cached.Name);
                    return true;
                }

                if (cached.Address != IntPtr.Zero)
                {
                    var objRef = _objectTable.CreateObjectReference(cached.Address);
                    if (objRef != null)
                    {
                        position = objRef.Position;
                        _logger.LogTrace("TypingIndicator: resolved by cached address {addr}", cached.Address);
                        return true;
                    }
                }
            }
        }

        var alias = userData.AliasOrUID;
        if (!string.IsNullOrEmpty(alias) && TryGetWorldPositionByName(alias, out position))
        {
            _logger.LogTrace("TypingIndicator: resolved by user alias {alias}", alias);
            return true;
        }

        return false;
    }

    private bool TryGetWorldPositionByName(string name, out Vector3 position)
    {
        position = Vector3.Zero;
        foreach (var obj in _objectTable)
        {
            if (obj != null && obj.Name.TextValue.Equals(name, StringComparison.OrdinalIgnoreCase))
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

    private static unsafe uint GetEntityId(nint address)
    {
        if (address == nint.Zero) return 0;
        return ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)address)->EntityId;
    }
}
