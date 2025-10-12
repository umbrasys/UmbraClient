using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.User;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Services.AutoDetect;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.Files.Models;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Linq;

namespace MareSynchronos.UI;

public class CompactUi : WindowMediatorSubscriberBase
{
    public float TransferPartHeight;
    public float WindowContentWidth;
    private readonly ApiController _apiController;
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly FileUploadManager _fileTransferManager;
    private readonly GroupPanel _groupPanel;
    private readonly PairGroupsUi _pairGroupsUi;
    private readonly PairManager _pairManager;
    private readonly SelectGroupForPairUi _selectGroupForPairUi;
    private readonly SelectPairForGroupUi _selectPairsForGroupUi;
    private readonly ServerConfigurationManager _serverManager;
    private readonly Stopwatch _timeout = new();
    private readonly CharaDataManager _charaDataManager;
    private readonly NearbyPendingService _nearbyPending;
    private readonly AutoDetectRequestService _autoDetectRequestService;
    private readonly UidDisplayHandler _uidDisplayHandler;
    private readonly UiSharedService _uiSharedService;
    private bool _buttonState;
    private string _characterOrCommentFilter = string.Empty;
    private Pair? _lastAddedUser;
    private string _lastAddedUserComment = string.Empty;
    private Vector2 _lastPosition = Vector2.One;
    private Vector2 _lastSize = Vector2.One;
    private string _pairToAdd = string.Empty;
    private int _secretKeyIdx = -1;
    private bool _showModalForUserAddition;
    private bool _showSyncShells;
    private bool _wasOpen;
    private bool _nearbyOpen = true;
    private List<Services.Mediator.NearbyEntry> _nearbyEntries = new();

    public CompactUi(ILogger<CompactUi> logger, UiSharedService uiShared, MareConfigService configService, ApiController apiController, PairManager pairManager, ChatService chatService,
        ServerConfigurationManager serverManager, MareMediator mediator, FileUploadManager fileTransferManager, UidDisplayHandler uidDisplayHandler, CharaDataManager charaDataManager,
        NearbyPendingService nearbyPendingService,
        AutoDetectRequestService autoDetectRequestService,
        PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "###UmbraSyncMainUI", performanceCollectorService)
    {
        _uiSharedService = uiShared;
        _configService = configService;
        _apiController = apiController;
        _pairManager = pairManager;
        _serverManager = serverManager;
        _fileTransferManager = fileTransferManager;
        _uidDisplayHandler = uidDisplayHandler;
        _charaDataManager = charaDataManager;
        _nearbyPending = nearbyPendingService;
        _autoDetectRequestService = autoDetectRequestService;
        var tagHandler = new TagHandler(_serverManager);

        _groupPanel = new(this, uiShared, _pairManager, chatService, uidDisplayHandler, _configService, _serverManager, _charaDataManager);
        _selectGroupForPairUi = new(tagHandler, uidDisplayHandler, _uiSharedService);
        _selectPairsForGroupUi = new(tagHandler, uidDisplayHandler);
        _pairGroupsUi = new(configService, tagHandler, uidDisplayHandler, apiController, _selectPairsForGroupUi, _uiSharedService);

#if DEBUG
        string dev = "Dev Build";
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        WindowName = $"UmbraSync {dev} ({ver.Major}.{ver.Minor}.{ver.Build})###UmbraSyncMainUIDev";
        Toggle();
#else
        var ver = Assembly.GetExecutingAssembly().GetName().Version!;
        WindowName = "UmbraSync " + ver.Major + "." + ver.Minor + "." + ver.Build + "###UmbracSyncMainUI";
#endif
        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = true);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<CutsceneStartMessage>(this, (_) => UiSharedService_GposeStart());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => UiSharedService_GposeEnd());
        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => _currentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));
        Mediator.Subscribe<DiscoveryListUpdated>(this, (msg) =>
        {
            _nearbyEntries = msg.Entries;
            // Update last-seen character names for matched entries
            foreach (var e in _nearbyEntries.Where(x => x.IsMatch))
            {
                var uid = e.Uid;
                var lastSeen = e.DisplayName ?? e.Name;
                if (!string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(lastSeen))
                {
                    _serverManager.SetNameForUid(uid, lastSeen);
                }
            }
        });

        Flags |= ImGuiWindowFlags.NoDocking;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(350, 400),
            MaximumSize = new Vector2(350, 2000),
        };
    }

    protected override void DrawInternal()
    {
        UiSharedService.AccentColor = new Vector4(0x8D / 255f, 0x37 / 255f, 0xC0 / 255f, 1f);
        UiSharedService.AccentHoverColor = new Vector4(0x3A / 255f, 0x15 / 255f, 0x50 / 255f, 1f);
        UiSharedService.AccentActiveColor = UiSharedService.AccentHoverColor;
        var accent = UiSharedService.AccentColor;
        using var titleBg = ImRaii.PushColor(ImGuiCol.TitleBg, accent);
        using var titleBgActive = ImRaii.PushColor(ImGuiCol.TitleBgActive, accent);
        using var titleBgCollapsed = ImRaii.PushColor(ImGuiCol.TitleBgCollapsed, accent);
        using var buttonHover = ImRaii.PushColor(ImGuiCol.ButtonHovered, UiSharedService.AccentHoverColor);
        using var buttonActive = ImRaii.PushColor(ImGuiCol.ButtonActive, UiSharedService.AccentActiveColor);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().WindowPadding.Y - 1f * ImGuiHelpers.GlobalScale + ImGui.GetStyle().ItemSpacing.Y);
        WindowContentWidth = UiSharedService.GetWindowContentRegionWidth();
        if (!_apiController.IsCurrentVersion)
        {
            var ver = _apiController.CurrentClientVersion;
            var unsupported = "UNSUPPORTED VERSION";
            using (_uiSharedService.UidFont.Push())
            {
                var uidTextSize = ImGui.CalcTextSize(unsupported);
                ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 - uidTextSize.X / 2);
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(UiSharedService.AccentColor, unsupported);
            }
            UiSharedService.ColorTextWrapped($"Your UmbraSync installation is out of date, the current version is {ver.Major}.{ver.Minor}.{ver.Build}. " +
                $"It is highly recommended to keep UmbraSync up to date. Open /xlplugins and update the plugin.", UiSharedService.AccentColor);
        }

        using (ImRaii.PushId("header")) DrawUIDHeader();
        ImGui.Separator();
        using (ImRaii.PushId("serverstatus")) DrawServerStatus();

        if (_apiController.ServerState is ServerState.Connected)
        {
            var hasShownSyncShells = _showSyncShells;

        using (var hoverColor = ImRaii.PushColor(ImGuiCol.ButtonHovered, UiSharedService.AccentHoverColor))
        using (var activeColor = ImRaii.PushColor(ImGuiCol.ButtonActive, UiSharedService.AccentActiveColor))
        {
            if (!hasShownSyncShells)
            {
                using var selectedColor = ImRaii.PushColor(ImGuiCol.Button, accent);
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    if (ImGui.Button(FontAwesomeIcon.User.ToIconString(), new Vector2((UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
                    {
                        _showSyncShells = false;
                    }
                }
            }
            else
            {
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    if (ImGui.Button(FontAwesomeIcon.User.ToIconString(), new Vector2((UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
                    {
                        _showSyncShells = false;
                    }
                }
            }

            UiSharedService.AttachToolTip("Individual pairs");

            ImGui.SameLine();

            if (hasShownSyncShells)
            {
                using var selectedColor = ImRaii.PushColor(ImGuiCol.Button, accent);
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    if (ImGui.Button(FontAwesomeIcon.UserFriends.ToIconString(), new Vector2((UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
                    {
                        _showSyncShells = true;
                    }
                }
            }
            else
            {
                using (ImRaii.PushFont(UiBuilder.IconFont))
                {
                    if (ImGui.Button(FontAwesomeIcon.UserFriends.ToIconString(), new Vector2((UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X) / 2, 30 * ImGuiHelpers.GlobalScale)))
                    {
                        _showSyncShells = true;
                    }
                }
            }

            UiSharedService.AttachToolTip("Syncshells");
        }

            DrawDefaultSyncSettings();
            if (!hasShownSyncShells)
            {
                using (ImRaii.PushId("pairlist")) DrawPairList();
            }
            else
            {
                using (ImRaii.PushId("syncshells")) _groupPanel.DrawSyncshells();
            }
            ImGui.Separator();
            using (ImRaii.PushId("transfers")) DrawTransfers();
            TransferPartHeight = ImGui.GetCursorPosY() - TransferPartHeight;
            using (ImRaii.PushId("group-user-popup")) _selectPairsForGroupUi.Draw(_pairManager.DirectPairs);
                using (ImRaii.PushId("grouping-popup")) _selectGroupForPairUi.Draw();
        }

        if (_configService.Current.OpenPopupOnAdd && _pairManager.LastAddedUser != null)
        {
            _lastAddedUser = _pairManager.LastAddedUser;
            _pairManager.LastAddedUser = null;
            ImGui.OpenPopup("Set Notes for New User");
            _showModalForUserAddition = true;
            _lastAddedUserComment = string.Empty;
        }

        if (ImGui.BeginPopupModal("Set Notes for New User", ref _showModalForUserAddition, UiSharedService.PopupWindowFlags))
        {
            if (_lastAddedUser == null)
            {
                _showModalForUserAddition = false;
            }
            else
            {
                UiSharedService.TextWrapped($"You have successfully added {_lastAddedUser.UserData.AliasOrUID}. Set a local note for the user in the field below:");
                ImGui.InputTextWithHint("##noteforuser", $"Note for {_lastAddedUser.UserData.AliasOrUID}", ref _lastAddedUserComment, 100);
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save Note"))
                {
                    _serverManager.SetNoteForUid(_lastAddedUser.UserData.UID, _lastAddedUserComment);
                    _lastAddedUser = null;
                    _lastAddedUserComment = string.Empty;
                    _showModalForUserAddition = false;
                }
            }
            UiSharedService.SetScaledWindowSize(275);
            ImGui.EndPopup();
        }

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (_lastSize != size || _lastPosition != pos)
        {
            _lastSize = size;
            _lastPosition = pos;
            Mediator.Publish(new CompactUiChange(_lastSize, _lastPosition));
        }
    }

    public override void OnClose()
    {
        _uidDisplayHandler.Clear();
        base.OnClose();
    }

    private void DrawDefaultSyncSettings()
    {
        ImGuiHelpers.ScaledDummy(4f);
        using (ImRaii.PushId("sync-defaults"))
        {
            const string soundLabel = "Audio";
            const string animLabel = "Anim";
            const string vfxLabel = "VFX";
            const string soundSubject = "de l'audio";
            const string animSubject = "des animations";
            const string vfxSubject = "des effets visuels";

            bool soundsDisabled = _configService.Current.DefaultDisableSounds;
            bool animsDisabled = _configService.Current.DefaultDisableAnimations;
            bool vfxDisabled = _configService.Current.DefaultDisableVfx;
            bool showNearby = _configService.Current.EnableAutoDetectDiscovery;
            int pendingInvites = _nearbyPending.Pending.Count;

            const string nearbyLabel = "AutoDetect";


            var soundIcon = soundsDisabled ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp;
            var animIcon = animsDisabled ? FontAwesomeIcon.WindowClose : FontAwesomeIcon.Running;
            var vfxIcon = vfxDisabled ? FontAwesomeIcon.TimesCircle : FontAwesomeIcon.Sun;

            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float audioWidth = _uiSharedService.GetIconTextButtonSize(soundIcon, soundLabel);
            float animWidth = _uiSharedService.GetIconTextButtonSize(animIcon, animLabel);
            float vfxWidth = _uiSharedService.GetIconTextButtonSize(vfxIcon, vfxLabel);
            float nearbyWidth = showNearby ? _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.UserPlus, pendingInvites > 0 ? $"{nearbyLabel} ({pendingInvites})" : nearbyLabel) : 0f;
            int buttonCount = 3 + (showNearby ? 1 : 0);
            float totalWidth = audioWidth + animWidth + vfxWidth + nearbyWidth + spacing * (buttonCount - 1);
            float available = ImGui.GetContentRegionAvail().X;
            float startCursorX = ImGui.GetCursorPosX();
            if (totalWidth < available)
            {
                ImGui.SetCursorPosX(startCursorX + (available - totalWidth) / 2f);
            }

            DrawDefaultSyncButton(soundIcon, soundLabel, audioWidth, soundsDisabled,
                state =>
                {
                    _configService.Current.DefaultDisableSounds = state;
                    _configService.Save();
                    Mediator.Publish(new ApplyDefaultsToAllSyncsMessage(soundSubject, state));
                },
                () => DisableStateTooltip(soundSubject, _configService.Current.DefaultDisableSounds));

            DrawDefaultSyncButton(animIcon, animLabel, animWidth, animsDisabled,
                state =>
                {
                    _configService.Current.DefaultDisableAnimations = state;
                    _configService.Save();
                    Mediator.Publish(new ApplyDefaultsToAllSyncsMessage(animSubject, state));
                },
                () => DisableStateTooltip(animSubject, _configService.Current.DefaultDisableAnimations), spacing);

            DrawDefaultSyncButton(vfxIcon, vfxLabel, vfxWidth, vfxDisabled,
                state =>
                {
                    _configService.Current.DefaultDisableVfx = state;
                    _configService.Save();
                    Mediator.Publish(new ApplyDefaultsToAllSyncsMessage(vfxSubject, state));
                },
                () => DisableStateTooltip(vfxSubject, _configService.Current.DefaultDisableVfx), spacing);

            if (showNearby)
            {
                ImGui.SameLine(0, spacing);
                var autodetectLabel = pendingInvites > 0 ? $"{nearbyLabel} ({pendingInvites})" : nearbyLabel;
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserPlus, autodetectLabel, nearbyWidth))
                {
                    Mediator.Publish(new UiToggleMessage(typeof(AutoDetectUi)));
                }
                string tooltip = pendingInvites > 0
                    ? string.Format("Vous avez {0} invitation{1} reçue. Ouvrez l\'interface AutoDetect pour y répondre.", pendingInvites, pendingInvites > 1 ? "s" : string.Empty)
                    : "Ouvrir les outils AutoDetect (invitations et proximité).\n\nLes demandes reçues sont listées dans l\'onglet 'Invitations'.";
                UiSharedService.AttachToolTip(tooltip);
            }
        }
        ImGui.Separator();
    }

    private void DrawDefaultSyncButton(FontAwesomeIcon icon, string label, float width, bool currentState,
        Action<bool> onToggle, Func<string> tooltipProvider, float spacingOverride = -1f)
    {
        if (spacingOverride >= 0f)
        {
            ImGui.SameLine(0, spacingOverride);
        }

        var colorsPushed = 0;
        if (currentState)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.35f, 0.35f, 1f));
            colorsPushed++;
        }

        if (_uiSharedService.IconTextButton(icon, label, width))
        {
            var newState = !currentState;
            onToggle(newState);
        }

        if (colorsPushed > 0)
        {
            ImGui.PopStyleColor(colorsPushed);
        }

        UiSharedService.AttachToolTip(tooltipProvider());
    }

    private static string DisableStateTooltip(string context, bool disabled)
    {
        var state = disabled ? "désactivée" : "activée";
        return $"Synchronisation {context} par défaut : {state}.\nCliquez pour modifier.";
    }

    private void DrawAddCharacter()
    {
        ImGui.Dummy(new(10));
        var keys = _serverManager.CurrentServer!.SecretKeys;
        if (keys.Any())
        {
            if (_secretKeyIdx == -1) _secretKeyIdx = keys.First().Key;
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Add current character with secret key"))
            {
                _serverManager.CurrentServer!.Authentications.Add(new MareConfiguration.Models.Authentication()
                {
                    CharacterName = _uiSharedService.PlayerName,
                    WorldId = _uiSharedService.WorldId,
                    SecretKeyIdx = _secretKeyIdx
                });

                _serverManager.Save();

                _ = _apiController.CreateConnections();
            }

            _uiSharedService.DrawCombo("Secret Key##addCharacterSecretKey", keys, (f) => f.Value.FriendlyName, (f) => _secretKeyIdx = f.Key);
        }
        else
        {
            UiSharedService.ColorTextWrapped("No secret keys are configured for the current server.", ImGuiColors.DalamudYellow);
        }
    }

    private void DrawAddPair()
    {
        var buttonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus);
        ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonSize.X);
        ImGui.InputTextWithHint("##otheruid", "Other players UID/Alias", ref _pairToAdd, 20);
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        var canAdd = !_pairManager.DirectPairs.Any(p => string.Equals(p.UserData.UID, _pairToAdd, StringComparison.Ordinal) || string.Equals(p.UserData.Alias, _pairToAdd, StringComparison.Ordinal));
        using (ImRaii.Disabled(!canAdd))
        {
            if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
            {
                _ = _apiController.UserAddPair(new(new(_pairToAdd)));
                _pairToAdd = string.Empty;
            }
            UiSharedService.AttachToolTip("Pair with " + (_pairToAdd.IsNullOrEmpty() ? "other user" : _pairToAdd));
        }

        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawFilter()
    {
        var playButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Play);

        var users = GetFilteredUsers();
        var userCount = users.Count;

        var spacing = userCount > 0
            ? playButtonSize.X + ImGui.GetStyle().ItemSpacing.X
            : 0;

        ImGui.SetNextItemWidth(WindowContentWidth - spacing);
        ImGui.InputTextWithHint("##filter", "Filter for UID/notes", ref _characterOrCommentFilter, 255);

        if (userCount == 0) return;

        var pausedUsers = users.Where(u => u.UserPair!.OwnPermissions.IsPaused() && u.UserPair.OtherPermissions.IsPaired()).ToList();
        var resumedUsers = users.Where(u => !u.UserPair!.OwnPermissions.IsPaused() && u.UserPair.OtherPermissions.IsPaired()).ToList();

        if (!pausedUsers.Any() && !resumedUsers.Any()) return;
        ImGui.SameLine();

        switch (_buttonState)
        {
            case true when !pausedUsers.Any():
                _buttonState = false;
                break;

            case false when !resumedUsers.Any():
                _buttonState = true;
                break;

            case true:
                users = pausedUsers;
                break;

            case false:
                users = resumedUsers;
                break;
        }

        if (_timeout.ElapsedMilliseconds > 5000)
            _timeout.Reset();

        var button = _buttonState ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;

        using (ImRaii.Disabled(_timeout.IsRunning))
        {
            if (_uiSharedService.IconButton(button) && UiSharedService.CtrlPressed())
            {
                foreach (var entry in users)
                {
                    var perm = entry.UserPair!.OwnPermissions;
                    perm.SetPaused(!perm.IsPaused());
                    _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, perm));
                }

                _timeout.Start();
                _buttonState = !_buttonState;
            }
            if (!_timeout.IsRunning)
                UiSharedService.AttachToolTip($"Hold Control to {(button == FontAwesomeIcon.Play ? "resume" : "pause")} pairing with {users.Count} out of {userCount} displayed users.");
            else
                UiSharedService.AttachToolTip($"Next execution is available at {(5000 - _timeout.ElapsedMilliseconds) / 1000} seconds");
        }
    }

    private void DrawPairList()
    {
        using (ImRaii.PushId("addpair")) DrawAddPair();
        using (ImRaii.PushId("pairs")) DrawPairs();
        TransferPartHeight = ImGui.GetCursorPosY();
        using (ImRaii.PushId("filter")) DrawFilter();
    }

    private void DrawPairs()
    {
        var ySize = TransferPartHeight == 0
            ? 1
            : (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - TransferPartHeight - ImGui.GetCursorPosY();
        var users = GetFilteredUsers().OrderBy(u => u.GetPairSortKey(), StringComparer.Ordinal);

        ImGui.BeginChild("list", new Vector2(WindowContentWidth, ySize), border: false);

        var pendingCount = _nearbyPending?.Pending.Count ?? 0;
        if (pendingCount > 0)
        {
            UiSharedService.ColorTextWrapped("Invitation AutoDetect en attente. Ouvrez l\'interface AutoDetect pour gérer vos demandes.", ImGuiColors.DalamudYellow);
            ImGuiHelpers.ScaledDummy(4);
        }

        var onlineUsers = users.Where(u => u.UserPair!.OtherPermissions.IsPaired() && (u.IsOnline || u.UserPair!.OwnPermissions.IsPaused())).Select(c => new DrawUserPair("Online" + c.UserData.UID, c, _uidDisplayHandler, _apiController, Mediator, _selectGroupForPairUi, _uiSharedService, _charaDataManager)).ToList();
        var visibleUsers = users.Where(u => u.IsVisible).Select(c => new DrawUserPair("Visible" + c.UserData.UID, c, _uidDisplayHandler, _apiController, Mediator, _selectGroupForPairUi, _uiSharedService, _charaDataManager)).ToList();
        var offlineUsers = users.Where(u => !u.UserPair!.OtherPermissions.IsPaired() || (!u.IsOnline && !u.UserPair!.OwnPermissions.IsPaused())).Select(c => new DrawUserPair("Offline" + c.UserData.UID, c, _uidDisplayHandler, _apiController, Mediator, _selectGroupForPairUi, _uiSharedService, _charaDataManager)).ToList();

        _pairGroupsUi.Draw(visibleUsers, onlineUsers, offlineUsers);

        if (_configService.Current.EnableAutoDetectDiscovery)
        {
            using (ImRaii.PushId("group-Nearby"))
            {
                var icon = _nearbyOpen ? FontAwesomeIcon.CaretSquareDown : FontAwesomeIcon.CaretSquareRight;
                _uiSharedService.IconText(icon);
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) _nearbyOpen = !_nearbyOpen;
                ImGui.SameLine();
                var onUmbra = _nearbyEntries?.Count(e => e.IsMatch && e.AcceptPairRequests && !string.IsNullOrEmpty(e.Token) && !IsAlreadyPairedQuickMenu(e)) ?? 0;
                ImGui.TextUnformatted($"Nearby ({onUmbra})");
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) _nearbyOpen = !_nearbyOpen;
                if (_nearbyOpen)
                {
                    ImGui.Indent();
                    var nearby = _nearbyEntries == null
                        ? new List<Services.Mediator.NearbyEntry>()
                        : _nearbyEntries.Where(e => e.IsMatch && e.AcceptPairRequests && !string.IsNullOrEmpty(e.Token) && !IsAlreadyPairedQuickMenu(e))
                            .OrderBy(e => e.Distance)
                            .ToList();
                    if (nearby.Count == 0)
                    {
                        UiSharedService.ColorTextWrapped("Aucun nouveau joueur detecté.", ImGuiColors.DalamudGrey3);
                    }
                    else
                    {
                        foreach (var e in nearby)
                        {
                            if (!e.AcceptPairRequests || string.IsNullOrEmpty(e.Token))
                                continue;

                            var name = e.DisplayName ?? e.Name;
                            ImGui.AlignTextToFramePadding();
                            ImGui.TextUnformatted(name);
                            var right = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth();
                            ImGui.SameLine();

                            var statusButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.UserPlus);
                            ImGui.SetCursorPosX(right - statusButtonSize.X);
                            if (!e.AcceptPairRequests)
                            {
                                _uiSharedService.IconText(FontAwesomeIcon.Ban, ImGuiColors.DalamudGrey3);
                                UiSharedService.AttachToolTip("Les demandes sont désactivées pour ce joueur");
                            }
                            else if (!string.IsNullOrEmpty(e.Token))
                            {
                                using (ImRaii.PushId(e.Token ?? e.Uid ?? e.Name ?? string.Empty))
                                {
                                    if (_uiSharedService.IconButton(FontAwesomeIcon.UserPlus))
                                    {
                                        _ = _autoDetectRequestService.SendRequestAsync(e.Token!, e.Uid, e.DisplayName);
                                    }
                                }
                                UiSharedService.AttachToolTip("Envoyer une invitation d'apparaige");
                            }
                            else
                            {
                                _uiSharedService.IconText(FontAwesomeIcon.QuestionCircle, ImGuiColors.DalamudGrey3);
                                UiSharedService.AttachToolTip("Impossible d'inviter ce joueur");
                            }
                        }
                    }
                    ImGui.Unindent();
                    ImGui.Separator();
                }
            }
        }

        ImGui.EndChild();
    }

    private bool IsAlreadyPairedQuickMenu(Services.Mediator.NearbyEntry entry)
    {
        try
        {
            if (!string.IsNullOrEmpty(entry.Uid))
            {
                if (_pairManager.DirectPairs.Any(p => string.Equals(p.UserData.UID, entry.Uid, StringComparison.Ordinal)))
                    return true;
            }

            var key = (entry.DisplayName ?? entry.Name) ?? string.Empty;
            if (string.IsNullOrEmpty(key)) return false;

            return _pairManager.DirectPairs.Any(p => string.Equals(p.UserData.AliasOrUID, key, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private void DrawServerStatus()
    {
        var buttonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Link);
        var userCount = _apiController.OnlineUsers.ToString(CultureInfo.InvariantCulture);
        var userSize = ImGui.CalcTextSize(userCount);
        var textSize = ImGui.CalcTextSize("Users Online");
        string shardConnection = string.Equals(_apiController.ServerInfo.ShardName, "Main", StringComparison.OrdinalIgnoreCase) ? string.Empty : $"Shard: {_apiController.ServerInfo.ShardName}";
        var shardTextSize = ImGui.CalcTextSize(shardConnection);
        var printShard = !string.IsNullOrEmpty(_apiController.ServerInfo.ShardName) && shardConnection != string.Empty;

        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - (userSize.X + textSize.X) / 2 - ImGui.GetStyle().ItemSpacing.X / 2);
            if (!printShard) ImGui.AlignTextToFramePadding();
            ImGui.TextColored(UiSharedService.AccentColor, userCount);
            ImGui.SameLine();
            if (!printShard) ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Users Online");
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(UiSharedService.AccentColor, "Not connected to any server");
        }

        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y);
            ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth()) / 2 - shardTextSize.X / 2);
            ImGui.TextUnformatted(shardConnection);
        }

        ImGui.SameLine();
        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((userSize.Y + textSize.Y) / 2 + shardTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }
        var isLinked = !_serverManager.CurrentServer!.FullPause;
        var color = isLinked ? new Vector4(0.63f, 0.25f, 1f, 1f) : UiSharedService.GetBoolColor(isLinked);
        var connectedIcon = isLinked ? FontAwesomeIcon.Link : FontAwesomeIcon.Unlink;

        if (_apiController.ServerState is ServerState.Connected)
        {
            ImGui.SetCursorPosX(0 + ImGui.GetStyle().ItemSpacing.X);
            if (_uiSharedService.IconButton(FontAwesomeIcon.UserCircle))
            {
                Mediator.Publish(new UiToggleMessage(typeof(EditProfileUi)));
            }
            UiSharedService.AttachToolTip("Edit your Profile");
        }

        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        if (printShard)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ((userSize.Y + textSize.Y) / 2 + shardTextSize.Y) / 2 - ImGui.GetStyle().ItemSpacing.Y + buttonSize.Y / 2);
        }

        if (_apiController.ServerState is not (ServerState.Reconnecting or ServerState.Disconnecting))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            if (_uiSharedService.IconButton(connectedIcon))
            {
                _serverManager.CurrentServer.FullPause = !_serverManager.CurrentServer.FullPause;
                _serverManager.Save();
                _ = _apiController.CreateConnections();
            }
            ImGui.PopStyleColor();
            UiSharedService.AttachToolTip(!_serverManager.CurrentServer.FullPause ? "Disconnect from " + _serverManager.CurrentServer.ServerName : "Connect to " + _serverManager.CurrentServer.ServerName);
        }
    }

    private void DrawTransfers()
    {
        var currentUploads = _fileTransferManager.CurrentUploads.ToList();

        if (currentUploads.Any())
        {
            ImGui.AlignTextToFramePadding();
            _uiSharedService.IconText(FontAwesomeIcon.Upload);
            ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

            var totalUploads = currentUploads.Count;

            var doneUploads = currentUploads.Count(c => c.IsTransferred);
            var totalUploaded = currentUploads.Sum(c => c.Transferred);
            var totalToUpload = currentUploads.Sum(c => c.Total);

            ImGui.TextUnformatted($"{doneUploads}/{totalUploads}");
            var uploadText = $"({UiSharedService.ByteToString(totalUploaded)}/{UiSharedService.ByteToString(totalToUpload)})";
            var textSize = ImGui.CalcTextSize(uploadText);
            ImGui.SameLine(WindowContentWidth - textSize.X);
            ImGui.TextUnformatted(uploadText);
        }

        var currentDownloads = _currentDownloads.SelectMany(d => d.Value.Values).ToList();

        if (currentDownloads.Any())
        {
            ImGui.AlignTextToFramePadding();
            _uiSharedService.IconText(FontAwesomeIcon.Download);
            ImGui.SameLine(35 * ImGuiHelpers.GlobalScale);

            var totalDownloads = currentDownloads.Sum(c => c.TotalFiles);
            var doneDownloads = currentDownloads.Sum(c => c.TransferredFiles);
            var totalDownloaded = currentDownloads.Sum(c => c.TransferredBytes);
            var totalToDownload = currentDownloads.Sum(c => c.TotalBytes);

            ImGui.TextUnformatted($"{doneDownloads}/{totalDownloads}");
            var downloadText =
                $"({UiSharedService.ByteToString(totalDownloaded)}/{UiSharedService.ByteToString(totalToDownload)})";
            var textSize = ImGui.CalcTextSize(downloadText);
            ImGui.SameLine(WindowContentWidth - textSize.X);
            ImGui.TextUnformatted(downloadText);
        }
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var bottomButtonWidth = (WindowContentWidth - spacing) / 2f;
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.PersonCircleQuestion, "Character Analysis", bottomButtonWidth))
        {
            Mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
        }

        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Running, "Character Data Hub", bottomButtonWidth))
        {
            Mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        }
        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawUIDHeader()
    {
        var uidText = GetUidText();
        var buttonSizeX = 0f;
        Vector2 uidTextSize;

        using (_uiSharedService.UidFont.Push())
        {
            uidTextSize = ImGui.CalcTextSize(uidText);
        }

        var originalPos = ImGui.GetCursorPos();
        ImGui.SetWindowFontScale(1.5f);
        var buttonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Cog);
        buttonSizeX -= buttonSize.X - ImGui.GetStyle().ItemSpacing.X * 2;
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
        ImGui.SetCursorPosY(originalPos.Y + uidTextSize.Y / 2 - buttonSize.Y / 2);
        if (_uiSharedService.IconButton(FontAwesomeIcon.Cog))
        {
            Mediator.Publish(new OpenSettingsUiMessage());
        }
        UiSharedService.AttachToolTip("Open the UmbraSync Settings");

        ImGui.SameLine();
        ImGui.SetCursorPos(originalPos);

        if (_apiController.ServerState is ServerState.Connected)
        {
            buttonSizeX += _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Copy).X - ImGui.GetStyle().ItemSpacing.X * 2;
            ImGui.SetCursorPosY(originalPos.Y + uidTextSize.Y / 2 - buttonSize.Y / 2);
            if (_uiSharedService.IconButton(FontAwesomeIcon.Copy))
            {
                ImGui.SetClipboardText(_apiController.DisplayName);
            }
            UiSharedService.AttachToolTip("Copy your UID to clipboard");
            ImGui.SameLine();
        }
        ImGui.SetWindowFontScale(1f);

        ImGui.SetCursorPosY(originalPos.Y + buttonSize.Y / 2 - uidTextSize.Y / 2 - ImGui.GetStyle().ItemSpacing.Y / 2);
        ImGui.SetCursorPosX((ImGui.GetWindowContentRegionMax().X + ImGui.GetWindowContentRegionMin().X) / 2 + buttonSizeX - uidTextSize.X / 2);
        using (_uiSharedService.UidFont.Push())
            ImGui.TextColored(GetUidColor(), uidText);

        if (_apiController.ServerState is not ServerState.Connected)
            UiSharedService.ColorTextWrapped(GetServerError(), GetUidColor());
        {
            if (_apiController.ServerState is ServerState.NoSecretKey)
            {
                DrawAddCharacter();
            }
        }
    }

    private List<Pair> GetFilteredUsers()
    {
        return _pairManager.DirectPairs.Where(p =>
        {
            if (_characterOrCommentFilter.IsNullOrEmpty()) return true;
            return p.UserData.AliasOrUID.Contains(_characterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ||
                   (p.GetNote()?.Contains(_characterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (p.PlayerName?.Contains(_characterOrCommentFilter, StringComparison.OrdinalIgnoreCase) ?? false);
        }).ToList();
    }

    private string GetServerError()
    {
        return _apiController.ServerState switch
        {
            ServerState.Connecting => "Attempting to connect to the server.",
            ServerState.Reconnecting => "Connection to server interrupted, attempting to reconnect to the server.",
            ServerState.Disconnected => "You are currently disconnected from the sync server.",
            ServerState.Disconnecting => "Disconnecting from the server",
            ServerState.Unauthorized => "Server Response: " + _apiController.AuthFailureMessage,
            ServerState.Offline => "Your selected sync server is currently offline.",
            ServerState.VersionMisMatch =>
                "Your plugin or the server you are connecting to is out of date. Please update your plugin now. If you already did so, contact the server provider to update their server to the latest version.",
            ServerState.RateLimited => "You are rate limited for (re)connecting too often. Disconnect, wait 10 minutes and try again.",
            ServerState.Connected => string.Empty,
            ServerState.NoSecretKey => "You have no secret key set for this current character. Use the button below or open the settings and set a secret key for the current character. You can reuse the same secret key for multiple characters.",
            ServerState.MultiChara => "Your Character Configuration has multiple characters configured with same name and world. You will not be able to connect until you fix this issue. Remove the duplicates from the configuration in Settings -> Service Settings -> Character Management and reconnect manually after.",
            _ => string.Empty
        };
    }

     private Vector4 GetUidColor()
    {
        return _apiController.ServerState switch
        {
            ServerState.Connecting => ImGuiColors.DalamudYellow,
            ServerState.Reconnecting => UiSharedService.AccentColor,
            ServerState.Connected => UiSharedService.AccentColor,
            ServerState.Disconnected => ImGuiColors.DalamudYellow,
            ServerState.Disconnecting => ImGuiColors.DalamudYellow,
            ServerState.Unauthorized => UiSharedService.AccentColor,
            ServerState.VersionMisMatch => UiSharedService.AccentColor,
            ServerState.Offline => UiSharedService.AccentColor,
            ServerState.RateLimited => ImGuiColors.DalamudYellow,
            ServerState.NoSecretKey => ImGuiColors.DalamudYellow,
            ServerState.MultiChara => ImGuiColors.DalamudYellow,
            _ => UiSharedService.AccentColor
        };
    }

    private string GetUidText()
    {
        return _apiController.ServerState switch
        {
            ServerState.Reconnecting => "Reconnecting",
            ServerState.Connecting => "Connecting",
            ServerState.Disconnected => "Disconnected",
            ServerState.Disconnecting => "Disconnecting",
            ServerState.Unauthorized => "Unauthorized",
            ServerState.VersionMisMatch => "Version mismatch",
            ServerState.Offline => "Unavailable",
            ServerState.RateLimited => "Rate Limited",
            ServerState.NoSecretKey => "No Secret Key",
            ServerState.MultiChara => "Duplicate Characters",
            ServerState.Connected => _apiController.DisplayName,
            _ => string.Empty
        };
    }

    private void UiSharedService_GposeEnd()
    {
        IsOpen = _wasOpen;
    }

    private void UiSharedService_GposeStart()
    {
        _wasOpen = IsOpen;
        IsOpen = false;
    }
}
