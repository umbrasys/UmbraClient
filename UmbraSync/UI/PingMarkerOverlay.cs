using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Microsoft.Extensions.Logging;
using System.Numerics;
using UmbraSync.API.Data;
using UmbraSync.API.Data.Enum;
using UmbraSync.API.Dto.Group;
using UmbraSync.API.Dto.Ping;
using UmbraSync.MareConfiguration;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.Ping;
using UmbraSync.WebAPI;

namespace UmbraSync.UI;

public sealed class PingMarkerOverlay : WindowMediatorSubscriberBase
{
    // Animation
    private const float Fps = 30f;
    private const float PinDropDuration = 0.35f;
    private const float PinDropHeight = 80f;
    private const float PinCircleCenterYRatio = 0.30f;

    // Ping wheel (matching SmartPings)
    private const float WheelHoldDuration = 0.4f;
    private const float WheelSize = 310f;
    private const float WheelCenterMultiplier = 0.141f;
    private const int WheelRows = 2;
    private const int WheelCols = 3;

    // Per-type sprite sheet configs (matching SmartPings values)
    private readonly record struct PingTypeConfig(
        string PingSheet, int PingRows, int PingCols, int PingTotalFrames, int PingHoldFrames,
        string RingSheet, int RingRows, int RingCols, int RingTotalFrames,
        float Scale, int MinRing, int MaxRing, float PingToRingRatio,
        Vector2 RingPivotOffset, Vector2 PingPivotOffset,
        int AuthorTagLastFrame);

    private static readonly Dictionary<PingMarkerType, PingTypeConfig> PingConfigs = new()
    {
        [PingMarkerType.Basic] = new(
            "ping_sheet.png", 4, 4, 60, 46,
            "ping_ring_sheet.png", 5, 7, 35,
            1.0f, 70, 800, 0.5f,
            Vector2.Zero, Vector2.Zero, 46),
        [PingMarkerType.Question] = new(
            "question_ping_sheet.png", 8, 8, 58, 1,
            "question_ring_sheet.png", 5, 8, 39,
            0.7f, 50, 600, 1.1f,
            Vector2.Zero, new(0, 0.05f), 41),
        [PingMarkerType.Danger] = new(
            "danger_ping_sheet.png", 4, 4, 51, 37,
            "danger_ring_sheet.png", 4, 8, 31,
            1.2f, 85, 1050, 0.45f,
            new(0.05f, -0.02f), new(0, 0.07f), 37),
        [PingMarkerType.Assist] = new(
            "assist_ping_sheet.png", 8, 8, 58, 1,
            "assist_ring_sheet.png", 6, 8, 45,
            0.8f, 53, 636, 1.05f,
            Vector2.Zero, new(0, 0.01f), 41),
        [PingMarkerType.OnMyWay] = new(
            "onmyway_ping_sheet.png", 9, 8, 68, 1,
            "onmyway_ring_sheet.png", 9, 8, 65,
            0.8f, 53, 636, 0.9f,
            Vector2.Zero, Vector2.Zero, 55),
    };

    // Type-specific tint colors (for non-Basic pings)
    private static readonly Dictionary<PingMarkerType, Vector4> TypeColors = new()
    {
        [PingMarkerType.Basic] = new(0f, 0.80f, 1f, 1f),
        [PingMarkerType.Question] = new(1f, 0.87f, 0f, 1f),
        [PingMarkerType.Danger] = new(1f, 0.30f, 0.30f, 1f),
        [PingMarkerType.Assist] = new(0f, 0.80f, 1f, 1f),
        [PingMarkerType.OnMyWay] = new(0.60f, 0.80f, 1f, 1f),
    };

    // Letter colors for Basic pings
    private static readonly Vector4[] LetterColors =
    [
        new(0.00f, 0.80f, 1.00f, 1f), // A — Cyan
        new(0.26f, 0.80f, 0.00f, 1f), // B — Green
        new(1.00f, 0.31f, 0.31f, 1f), // C — Red
        new(1.00f, 0.87f, 0.00f, 1f), // D — Gold
        new(0.00f, 0.53f, 1.00f, 1f), // E — Blue
        new(0.87f, 0.33f, 0.87f, 1f), // F — Purple
        new(1.00f, 0.47f, 0.66f, 1f), // G — Pink
        new(1.00f, 1.00f, 1.00f, 1f), // H — White
    ];

    // Wheel sections
    private enum WheelSection { Center, Left, Up, Right, Down }

    private static readonly Dictionary<WheelSection, PingMarkerType> WheelTypeMap = new()
    {
        [WheelSection.Left] = PingMarkerType.Question,
        [WheelSection.Up] = PingMarkerType.Danger,
        [WheelSection.Right] = PingMarkerType.OnMyWay,
        [WheelSection.Down] = PingMarkerType.Assist,
    };

    private readonly ILogger<PingMarkerOverlay> _typedLogger;
    private readonly MareConfigService _configService;
    private readonly IGameGui _gameGui;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IKeyState _keyState;
    private readonly PingMarkerStateService _pingStateService;
    private readonly PingPermissionService _permissionService;
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly ITextureProvider _textureProvider;
    private readonly IDalamudPluginInterface _pluginInterface;

    private readonly Dictionary<string, ISharedImmediateTexture> _textureCache = new(StringComparer.Ordinal);
    private string? _resourceDir;

    // Input state
    private bool _cursorIsPing;
    private bool _wasKeyDown;
    private DateTime? _keyHoldStartTime;
    private const float KeyHoldDurationSeconds = 1.0f;

    // Ping wheel state
    private Vector2? _pingClickPos;
    private float _pingHoldDuration;
    private bool _pingWheelActive;
    private WheelSection _activeWheelSection;

    public PingMarkerOverlay(
        ILogger<PingMarkerOverlay> logger,
        MareMediator mediator,
        PerformanceCollectorService performanceCollectorService,
        MareConfigService configService,
        IGameGui gameGui,
        IClientState clientState,
        IObjectTable objectTable,
        IKeyState keyState,
        PingMarkerStateService pingStateService,
        PingPermissionService permissionService,
        ApiController apiController,
        DalamudUtilService dalamudUtil,
        ITextureProvider textureProvider,
        IDalamudPluginInterface pluginInterface)
        : base(logger, mediator, nameof(PingMarkerOverlay), performanceCollectorService)
    {
        _typedLogger = logger;
        _configService = configService;
        _gameGui = gameGui;
        _clientState = clientState;
        _objectTable = objectTable;
        _keyState = keyState;
        _pingStateService = pingStateService;
        _permissionService = permissionService;
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _textureProvider = textureProvider;
        _pluginInterface = pluginInterface;

        RespectCloseHotkey = false;
        IsOpen = true;
        Flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoInputs;
    }

    protected override void DrawInternal()
    {
        if (_cursorIsPing)
            Flags &= ~ImGuiWindowFlags.NoInputs;
        else
            Flags |= ImGuiWindowFlags.NoInputs;

        var viewport = ImGui.GetMainViewport();
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetWindowPos(viewport.Pos);
        ImGui.SetWindowSize(viewport.Size);

        if (!_clientState.IsLoggedIn || !_configService.Current.PingEnabled)
            return;

        if (_dalamudUtil.IsInGpose)
            return;

        HandleKeyInput();

        // Keep capturing mouse while a click is pending (wheel hold)
        if (_pingClickPos.HasValue)
            ImGui.SetNextFrameWantCaptureMouse(true);

        var rightClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Right);
        var escapePressed = ImGui.IsKeyPressed(ImGuiKey.Escape);

        if (rightClicked || escapePressed)
        {
            _cursorIsPing = false;
            CancelPingClick();
        }

        var drawList = ImGui.GetForegroundDrawList();

        DrawMarkers(drawList);
        DrawKeyHoldProgress(drawList);

        if (_cursorIsPing)
            HandlePingInteraction(drawList);

        DrawPingCursor(drawList);
    }

    private unsafe void HandleKeyInput()
    {
        var keybind = (VirtualKey)_configService.Current.PingKeybind;
        var isKeyDown = _keyState[keybind];

        if (_cursorIsPing)
        {
            // Already in ping mode — single press to deactivate
            if (isKeyDown && !_wasKeyDown)
            {
                var vanillaTextInputActive = RaptureAtkModule.Instance()->AtkModule.IsTextInputActive();
                if (!vanillaTextInputActive)
                {
                    _cursorIsPing = false;
                    CancelPingClick();
                    _keyState[keybind] = false;
                }
            }
        }
        else
        {
            if (isKeyDown)
            {
                var vanillaTextInputActive = RaptureAtkModule.Instance()->AtkModule.IsTextInputActive();
                if (!vanillaTextInputActive && !_permissionService.IsInInstance())
                {
                    if (!_wasKeyDown)
                    {
                        _keyHoldStartTime = DateTime.UtcNow;
                    }
                    else if (_keyHoldStartTime.HasValue)
                    {
                        var elapsed = (float)(DateTime.UtcNow - _keyHoldStartTime.Value).TotalSeconds;
                        if (elapsed >= KeyHoldDurationSeconds)
                        {
                            _cursorIsPing = true;
                            _keyHoldStartTime = null;
                            _keyState[keybind] = false;
                        }
                    }
                }
                else
                {
                    _keyHoldStartTime = null;
                }
            }
            else
            {
                _keyHoldStartTime = null;
            }
        }
        _wasKeyDown = isKeyDown;
    }

    private void DrawKeyHoldProgress(ImDrawListPtr drawList)
    {
        if (!_keyHoldStartTime.HasValue || _cursorIsPing) return;

        var elapsed = (float)(DateTime.UtcNow - _keyHoldStartTime.Value).TotalSeconds;
        var progress = Math.Clamp(elapsed / KeyHoldDurationSeconds, 0f, 1f);

        var viewport = ImGui.GetMainViewport();
        var center = new Vector2(viewport.Size.X * 0.5f, viewport.Size.Y * 0.85f);
        var barWidth = 200f;
        var barHeight = 6f;
        var barPos = new Vector2(center.X - barWidth * 0.5f, center.Y);
        
        drawList.AddRectFilled(barPos, barPos + new Vector2(barWidth, barHeight),
            ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)), 3f);
        drawList.AddRectFilled(barPos, barPos + new Vector2(barWidth * progress, barHeight),
            ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.2f, 0.9f)), 3f);
    }

    private void HandlePingInteraction(ImDrawListPtr drawList)
    {
        var uiScale = _configService.Current.PingUiScale;

        // Start new click
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !_pingClickPos.HasValue)
        {
            var clickPos = ImGui.GetMousePos();

            if (TryRemoveNearbyOwnPing(clickPos))
                return;

            _pingClickPos = clickPos;
            _pingHoldDuration = 0;
            _pingWheelActive = false;
        }

        // Update hold state
        if (_pingClickPos.HasValue && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _pingHoldDuration += ImGui.GetIO().DeltaTime;
            var moveDistance = Vector2.Distance(ImGui.GetMousePos(), _pingClickPos.Value);

            if (!_pingWheelActive
                && (_pingHoldDuration > WheelHoldDuration ||
                    moveDistance > WheelCenterMultiplier * WheelSize * uiScale))
            {
                _pingWheelActive = true;
            }

            if (_pingWheelActive)
            {
                _activeWheelSection = DrawPingWheel(drawList, _pingClickPos.Value, ImGui.GetMousePos(), uiScale);
            }
        }

        // Left mouse released
        if (_pingClickPos.HasValue && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            var pingType = PingMarkerType.Basic; // default: quick click

            if (_pingWheelActive)
            {
                if (WheelTypeMap.TryGetValue(_activeWheelSection, out var wheelType))
                    pingType = wheelType;
                else
                    pingType = default; // Center = cancel
            }

            if (pingType != default)
            {
                PlacePing(_pingClickPos.Value, pingType);
            }

            _cursorIsPing = false;
            CancelPingClick();
        }
    }

    private void CancelPingClick()
    {
        _pingClickPos = null;
        _pingHoldDuration = 0;
        _pingWheelActive = false;
    }

    private WheelSection DrawPingWheel(ImDrawListPtr drawList, Vector2 wheelPos, Vector2 mousePos, float uiScale)
    {
        var size = WheelSize * uiScale;

        int frame;
        WheelSection section;

        if (Vector2.Distance(wheelPos, mousePos) < WheelCenterMultiplier * size)
        {
            frame = 0;
            section = WheelSection.Center;
        }
        else
        {
            var delta = mousePos - wheelPos;
            var angle = MathF.Atan2(delta.Y, delta.X);

            if (angle >= -MathF.PI / 4 && angle <= MathF.PI / 4)
            {
                frame = 3; section = WheelSection.Right;
            }
            else if (angle >= MathF.PI / 4 && angle <= MathF.PI * 3 / 4)
            {
                frame = 1; section = WheelSection.Down;
            }
            else if (angle >= -MathF.PI * 3 / 4 && angle <= -MathF.PI / 4)
            {
                frame = 4; section = WheelSection.Up;
            }
            else
            {
                frame = 2; section = WheelSection.Left;
            }
        }

        var wheelTex = GetTexture("ping_wheel_sheet.png");
        if (wheelTex != null)
        {
            GetFrameUVs(WheelRows, WheelCols, frame, out var uv0, out var uv1);
            var half = size / 2f;
            drawList.AddImage(wheelTex.Handle,
                wheelPos - new Vector2(half, half),
                wheelPos + new Vector2(half, half),
                uv0, uv1);
        }

        // Line from center to mouse when a direction is selected
        if (section != WheelSection.Center)
        {
            var direction = Vector2.Normalize(mousePos - wheelPos);
            var lineStart = wheelPos + WheelCenterMultiplier * size * direction;
            var neonBlue = ImGui.ColorConvertFloat4ToU32(new Vector4(0.06f, 0.85f, 1f, 1f));
            drawList.AddLine(lineStart, mousePos, neonBlue, 2f);
        }

        return section;
    }

    private bool TryRemoveNearbyOwnPing(Vector2 screenPos)
    {
        const float hitRadius = 30f;
        var myUID = _apiController.UID ?? string.Empty;
        if (string.IsNullOrEmpty(myUID)) return false;

        var markers = _pingStateService.GetMarkersForTerritory(_clientState.TerritoryType);
        PingMarkerEntry? closest = null;
        var closestDist = float.MaxValue;

        foreach (var marker in markers)
        {
            if (!string.Equals(marker.SenderUID, myUID, StringComparison.Ordinal))
                continue;

            if (!_gameGui.WorldToScreen(marker.WorldPosition, out var markerScreenPos))
                continue;

            var dist = Vector2.Distance(screenPos, markerScreenPos);
            if (dist < hitRadius * _configService.Current.PingUiScale && dist < closestDist)
            {
                closest = marker;
                closestDist = dist;
            }
        }

        if (closest == null) return false;

        _pingStateService.TryRemoveMarker(closest.Ping.Id);

        if (!string.IsNullOrEmpty(closest.GroupGID))
        {
            var removeDto = new PingMarkerRemoveDto { PingId = closest.Ping.Id };
            _ = _apiController.GroupRemovePing(new GroupDto(new GroupData(closest.GroupGID)), removeDto);
        }

        _typedLogger.LogDebug("PingMarkerOverlay: removed own ping {id} by click", closest.Ping.Id);
        return true;
    }

    private void PlacePing(Vector2 screenPos, PingMarkerType type)
    {
        if (!_gameGui.ScreenToWorld(screenPos, out var worldPos))
        {
            _typedLogger.LogDebug("PingMarkerOverlay: ScreenToWorld failed at {pos}", screenPos);
            return;
        }

        var territoryId = _clientState.TerritoryType;
        var mapId = _clientState.MapId;

        var pingDto = new PingMarkerDto
        {
            Id = Guid.NewGuid(),
            Type = type,
            PositionX = worldPos.X,
            PositionY = worldPos.Y,
            PositionZ = worldPos.Z,
            TerritoryId = territoryId,
            MapId = mapId,
            Timestamp = DateTime.UtcNow.Ticks,
        };

        var senderName = _objectTable.LocalPlayer?.Name.TextValue ?? "???";
        var senderUID = _apiController.UID ?? string.Empty;

        var groups = _permissionService.GetPingableGroups();
        if (groups.Count > 0)
        {
            foreach (var group in groups)
            {
                var entry = new PingMarkerEntry
                {
                    Ping = pingDto,
                    SenderName = senderName,
                    SenderUID = senderUID,
                    GroupGID = group.Group.GID,
                };

                if (_pingStateService.TryAddMarker(entry))
                {
                    _ = _apiController.GroupSendPing(new GroupDto(group.Group), pingDto);
                    _typedLogger.LogDebug("PingMarkerOverlay: placed {type} ping at {pos} in {group}", type, worldPos, group.GroupAliasOrGID);
                }
            }
        }
        else
        {
            var entry = new PingMarkerEntry
            {
                Ping = pingDto,
                SenderName = senderName,
                SenderUID = senderUID,
                GroupGID = string.Empty,
            };
            _pingStateService.TryAddMarker(entry);
            _typedLogger.LogDebug("PingMarkerOverlay: placed local {type} ping at {pos}", type, worldPos);
        }
    }

    #region Drawing

    private unsafe void DrawMarkers(ImDrawListPtr drawList)
    {
        var territoryId = _clientState.TerritoryType;
        var markers = _pingStateService.GetMarkersForTerritory(territoryId);

        if (markers.Count == 0) return;

        var controlPtr = Control.Instance();
        if (controlPtr == null) return;

        var viewProj = controlPtr->ViewProjectionMatrix;
        var controlCamera = controlPtr->CameraManager.GetActiveCamera();
        if (controlCamera == null) return;
        var renderCamera = controlCamera->SceneCamera.RenderCamera;
        if (renderCamera == null) return;

        var proj = renderCamera->ProjectionMatrix;
        if (!Matrix4x4.Invert(proj, out var invProj)) return;

        var view = Matrix4x4.Multiply(viewProj, invProj);
        var nearPlane = new Vector4(view.M13, view.M23, view.M33, view.M43 + renderCamera->NearPlane);

        var uiScale = _configService.Current.PingUiScale;
        var opacity = _configService.Current.PingOpacity;
        var showAuthor = _configService.Current.PingShowAuthorName;

        foreach (var marker in markers)
        {
            marker.DrawDuration += ImGui.GetIO().DeltaTime;
            DrawSingleMarker(drawList, marker, nearPlane, uiScale, opacity, showAuthor);
        }
    }

    private void DrawSingleMarker(ImDrawListPtr drawList, PingMarkerEntry marker, Vector4 nearPlane, float uiScale, float opacity, bool showAuthor)
    {
        if (!PingConfigs.TryGetValue(marker.Ping.Type, out var config))
            return;

        var worldPos = marker.WorldPosition;

        // Hide pings beyond 20m from the local player
        var localPlayer = _objectTable.LocalPlayer;
        if (localPlayer != null)
        {
            var dist = Vector3.Distance(localPlayer.Position, worldPos);
            if (dist > 250f)
                return;
        }

        // Occlusion: skip if behind a wall/pillar
        if (!HasLineOfSightFromCamera(worldPos, targetHeightOffset: 0.5f))
            return;

        var onScreen = _gameGui.WorldToScreen(worldPos, out var screenPos);

        // Occlusion: skip if behind a game UI window
        if (onScreen && IsScreenPointBehindGameUI(screenPos))
            return;

        // Perspective sizing (using type-specific scale)
        var hForward = Vector3.Normalize(new Vector3(nearPlane.X, 0, nearPlane.Z));
        var hRight = Vector3.Transform(hForward, Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2));

        _gameGui.WorldToScreen(worldPos + config.Scale * hRight, out var spRight);
        _gameGui.WorldToScreen(worldPos - config.Scale * hRight, out var spLeft);
        _gameGui.WorldToScreen(worldPos + config.Scale * hForward, out var spForward);
        _gameGui.WorldToScreen(worldPos - config.Scale * hForward, out var spBack);

        var ringSize = new Vector2(MathF.Abs(spRight.X - spLeft.X), MathF.Abs(spForward.Y - spBack.Y));

        if (ringSize.X < config.MinRing)
        {
            ringSize.Y *= config.MinRing / ringSize.X;
            ringSize.X = config.MinRing;
        }
        if (ringSize.X > config.MaxRing)
        {
            ringSize.Y *= config.MaxRing / ringSize.X;
            ringSize.X = config.MaxRing;
        }
        ringSize *= uiScale;

        var t = marker.DrawDuration;

        // Tint color: letter-based for Basic, type-based for others
        Vector4 tintColor;
        if (marker.Ping.Type == PingMarkerType.Basic)
        {
            var colorIndex = Math.Clamp(marker.Label - 'A', 0, LetterColors.Length - 1);
            tintColor = LetterColors[colorIndex];
        }
        else
        {
            tintColor = TypeColors.GetValueOrDefault(marker.Ping.Type, new Vector4(0, 0.8f, 1, 1));
        }

        // === Ring sprite animation ===
        var ringFrame = (int)(t * Fps);
        if (ringFrame < config.RingTotalFrames && onScreen)
        {
            var ringTex = GetTexture(config.RingSheet);
            if (ringTex != null)
            {
                GetFrameUVs(config.RingRows, config.RingCols, ringFrame, out var uv0, out var uv1);
                var p0 = screenPos - ringSize / 2f + config.RingPivotOffset * ringSize;
                var p1 = screenPos + ringSize / 2f + config.RingPivotOffset * ringSize;
                var ringTint = new Vector4(tintColor.X, tintColor.Y, tintColor.Z, opacity);
                drawList.AddImage(ringTex.Handle, p0, p1, uv0, uv1, ImGui.ColorConvertFloat4ToU32(ringTint));
            }
        }

        if (!onScreen)
            return;

        // === Ping sprite (drop-in + fade) ===
        var pingSize = ringSize.X * config.PingToRingRatio;
        var dropProgress = Math.Clamp(t / PinDropDuration, 0f, 1f);
        var dropEase = EaseOutCubic(dropProgress);
        var dropOffset = PinDropHeight * uiScale * (1f - dropEase);
        var pinAlpha = dropEase;

        var pingTex = GetTexture(config.PingSheet);
        if (pingTex != null && pinAlpha > 0.01f)
        {
            GetFrameUVs(config.PingRows, config.PingCols, 0, out var uv0, out var uv1);
            var p0 = new Vector2(screenPos.X - pingSize / 2f, screenPos.Y - pingSize - dropOffset)
                     + config.PingPivotOffset * pingSize;
            var p1 = new Vector2(screenPos.X + pingSize / 2f, screenPos.Y - dropOffset)
                     + config.PingPivotOffset * pingSize;
            var dropTint = new Vector4(tintColor.X, tintColor.Y, tintColor.Z, opacity * pinAlpha);
            drawList.AddImage(pingTex.Handle, p0, p1, uv0, uv1, ImGui.ColorConvertFloat4ToU32(dropTint));
        }

        // === Letter overlay (Basic type only) ===
        if (marker.Ping.Type == PingMarkerType.Basic && pingSize > 10f && pinAlpha > 0.01f)
        {
            var pinTop = screenPos.Y - pingSize - dropOffset + config.PingPivotOffset.Y * pingSize;
            var circleCenter = new Vector2(
                screenPos.X + config.PingPivotOffset.X * pingSize,
                pinTop + pingSize * PinCircleCenterYRatio);
            var letterText = marker.Label.ToString();
            var letterFontSize = Math.Clamp(pingSize * 0.35f, 10f, 28f);
            var letterTextSize = ImGui.CalcTextSize(letterText) * (letterFontSize / ImGui.GetFontSize());
            var letterPos = circleCenter - letterTextSize / 2f;

            drawList.AddText(ImGui.GetFont(), letterFontSize, letterPos + Vector2.One,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, opacity * 0.7f * pinAlpha)), letterText);
            drawList.AddText(ImGui.GetFont(), letterFontSize, letterPos,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, opacity * pinAlpha)), letterText);
        }

        // === Author tag ===
        var authorDuration = config.AuthorTagLastFrame / Fps;
        if (showAuthor && !string.IsNullOrEmpty(marker.SenderName) && t <= authorDuration)
        {
            var fontSize = Math.Clamp(0.125f * ringSize.X / config.Scale, 14f, 26f);
            var authorText = marker.SenderName;
            var textSize = ImGui.CalcTextSize(authorText) * (fontSize / ImGui.GetFontSize());

            drawList.AddRectFilled(
                screenPos + new Vector2(-5f, 0f),
                screenPos + textSize,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.6f)));

            drawList.AddText(ImGui.GetFont(), fontSize, screenPos,
                ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1f)),
                authorText);
        }
    }

    private unsafe void DrawPingCursor(ImDrawListPtr drawList)
    {
        if (!_cursorIsPing || !_permissionService.CanPlacePingsAnywhere())
            return;

        // Don't draw cursor while wheel is active
        if (_pingWheelActive)
            return;

        ImGui.SetMouseCursor(ImGuiMouseCursor.None);
        var stage = AtkStage.Instance();
        if (stage != null)
        {
            stage->AtkCursor.Hide();
        }

        var mousePos = ImGui.GetMousePos();
        var size = 50f * _configService.Current.PingUiScale;
        var half = size / 2f;

        var cursorTex = GetTexture("ping_cursor.png");
        if (cursorTex != null)
        {
            drawList.AddImage(cursorTex.Handle, mousePos - new Vector2(half, half), mousePos + new Vector2(half, half));
        }
    }

    #endregion

    #region Helpers

    private static void GetFrameUVs(int rowCount, int colCount, int frame, out Vector2 uv0, out Vector2 uv1)
    {
        var row = frame / colCount;
        var col = frame % colCount;
        uv0 = new Vector2((float)col / colCount, (float)row / rowCount);
        uv1 = uv0 + new Vector2(1f / colCount, 1f / rowCount);
    }

    private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

    private static unsafe bool HasLineOfSightFromCamera(Vector3 target, float targetHeightOffset = 0.5f)
    {
        var cameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance();
        if (cameraManager == null || cameraManager->CurrentCamera == null)
            return true;

        var cameraPos = cameraManager->CurrentCamera->Position;
        return HasLineOfSight(cameraPos, target, fromHeightOffset: 0f, toHeightOffset: targetHeightOffset);
    }

    private static unsafe bool HasLineOfSight(Vector3 from, Vector3 to, float fromHeightOffset = 0f, float toHeightOffset = 0.5f)
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

    private IDalamudTextureWrap? GetTexture(string filename)
    {
        if (!_textureCache.TryGetValue(filename, out var tex))
        {
            _resourceDir ??= Path.Combine(
                Path.GetDirectoryName(_pluginInterface.AssemblyLocation.FullName)!,
                "Resources", "Ping");
            tex = _textureProvider.GetFromFile(Path.Combine(_resourceDir, filename));
            _textureCache[filename] = tex;
        }

        var wrap = tex.GetWrapOrEmpty();
        return wrap.Handle == IntPtr.Zero ? null : wrap;
    }

    #endregion
}
