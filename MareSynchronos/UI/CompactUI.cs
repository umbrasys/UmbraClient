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
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Linq;

namespace MareSynchronos.UI;

public class CompactUi : WindowMediatorSubscriberBase
{
    public float TransferPartHeight { get; internal set; }
    public float WindowContentWidth { get; private set; }
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
    private readonly CharacterAnalyzer _characterAnalyzer;
    private readonly UidDisplayHandler _uidDisplayHandler;
    private readonly UiSharedService _uiSharedService;
    private readonly EditProfileUi _editProfileUi;
    private readonly SettingsUi _settingsUi;
    private readonly AutoDetectUi _autoDetectUi;
    private readonly DataAnalysisUi _dataAnalysisUi;
    private bool _buttonState;
    private string _characterOrCommentFilter = string.Empty;
    private Pair? _lastAddedUser;
    private string _lastAddedUserComment = string.Empty;
    private Vector2 _lastPosition = Vector2.One;
    private Vector2 _lastSize = Vector2.One;
    private string _pairToAdd = string.Empty;
    private int _secretKeyIdx = -1;
    private bool _showModalForUserAddition;
    private bool _wasOpen;
    private bool _nearbyOpen = true;
    private bool _visibleOpen = true;
    private bool _selfAnalysisOpen = false;
    private List<Services.Mediator.NearbyEntry> _nearbyEntries = new();
    private const long SelfAnalysisSizeWarningThreshold = 300L * 1024 * 1024;
    private const long SelfAnalysisTriangleWarningThreshold = 150_000;
    private CompactUiSection _activeSection = CompactUiSection.VisiblePairs;
    private const float SidebarWidth = 42f;
    private const float SidebarIconSize = 22f;
    private const float ContentFontScale = UiSharedService.ContentFontScale;
    private static readonly Vector4 SidebarButtonColor = new(0.08f, 0.08f, 0.10f, 0.92f);
    private static readonly Vector4 SidebarButtonHoverColor = new(0.12f, 0.12f, 0.16f, 0.95f);
    private static readonly Vector4 SidebarButtonActiveColor = new(0.16f, 0.16f, 0.22f, 0.95f);

    private enum CompactUiSection
    {
        VisiblePairs,
        IndividualPairs,
        Syncshells,
        AutoDetect,
        CharacterAnalysis,
        CharacterDataHub,
        EditProfile,
        Settings
    }

    private enum PairContentMode
    {
        All,
        VisibleOnly
    }

    public CompactUi(ILogger<CompactUi> logger, UiSharedService uiShared, MareConfigService configService, ApiController apiController, PairManager pairManager, ChatService chatService,
        ServerConfigurationManager serverManager, MareMediator mediator, FileUploadManager fileTransferManager, UidDisplayHandler uidDisplayHandler, CharaDataManager charaDataManager,
        NearbyPendingService nearbyPendingService,
        AutoDetectRequestService autoDetectRequestService,
        CharacterAnalyzer characterAnalyzer,
        PerformanceCollectorService performanceCollectorService,
        EditProfileUi editProfileUi,
        SettingsUi settingsUi,
        AutoDetectUi autoDetectUi,
        DataAnalysisUi dataAnalysisUi)
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
        _characterAnalyzer = characterAnalyzer;
        _editProfileUi = editProfileUi;
        _settingsUi = settingsUi;
        _autoDetectUi = autoDetectUi;
        _dataAnalysisUi = dataAnalysisUi;
        var tagHandler = new TagHandler(_serverManager);

        _groupPanel = new(this, uiShared, _pairManager, chatService, uidDisplayHandler, _serverManager, _charaDataManager, _autoDetectRequestService);
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
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(1400, 2000),
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
        var sidebarWidth = ImGuiHelpers.ScaledVector2(SidebarWidth, 0).X;

        using var fontScale = UiSharedService.PushFontScale(ContentFontScale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ImGui.GetStyle().FramePadding * ContentFontScale);

        ImGui.BeginChild("compact-sidebar", new Vector2(sidebarWidth, 0), false, ImGuiWindowFlags.NoScrollbar);
        DrawSidebar();
        ImGui.EndChild();

        ImGui.SameLine();

        float separatorHeight = ImGui.GetWindowHeight() - ImGui.GetStyle().WindowPadding.Y * 2f;
        float separatorX = ImGui.GetCursorPosX();
        float separatorY = ImGui.GetCursorPosY();
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var end = new Vector2(start.X, start.Y + separatorHeight);
        drawList.AddLine(start, end, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.08f)), 1f * ImGuiHelpers.GlobalScale);
        ImGui.SetCursorPos(new Vector2(separatorX + 6f * ImGuiHelpers.GlobalScale, separatorY));

        ImGui.BeginChild("compact-content", Vector2.Zero, false);
        WindowContentWidth = UiSharedService.GetWindowContentRegionWidth();

        if (!_apiController.IsCurrentVersion)
        {
            DrawUnsupportedVersionBanner();
            ImGui.Separator();
        }

        using (ImRaii.PushId("header")) DrawUIDHeader();
        ImGui.Separator();
        using (ImRaii.PushId("serverstatus")) DrawServerStatus();
        ImGui.Separator();

        DrawMainContent();

        ImGui.EndChild();

        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        if (_lastSize != size || _lastPosition != pos)
        {
            _lastSize = size;
            _lastPosition = pos;
            Mediator.Publish(new CompactUiChange(_lastSize, _lastPosition));
        }

        ImGui.PopStyleVar();
    }

    public override void OnClose()
    {
        _uidDisplayHandler.Clear();
        base.OnClose();
    }

    private void DrawDefaultSyncSettings()
    {
        ImGuiHelpers.ScaledDummy(3f);
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

            var soundIcon = soundsDisabled ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp;
            var animIcon = animsDisabled ? FontAwesomeIcon.WindowClose : FontAwesomeIcon.Running;
            var vfxIcon = vfxDisabled ? FontAwesomeIcon.TimesCircle : FontAwesomeIcon.Sun;

            float spacing = ImGui.GetStyle().ItemSpacing.X;
            float audioWidth = _uiSharedService.GetIconTextButtonSize(soundIcon, soundLabel);
            float animWidth = _uiSharedService.GetIconTextButtonSize(animIcon, animLabel);
            float vfxWidth = _uiSharedService.GetIconTextButtonSize(vfxIcon, vfxLabel);
            float totalWidth = audioWidth + animWidth + vfxWidth + spacing * 2f;
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

if (showNearby && pendingInvites > 0)
            {
                ImGuiHelpers.ScaledDummy(3f);
                UiSharedService.ColorTextWrapped($"AutoDetect : {pendingInvites} invitation(s) en attente. Utilisez l'icône AutoDetect dans la barre latérale pour y répondre.", ImGuiColors.DalamudYellow);
            }

            DrawSelfAnalysisPreview();
        }
        ImGui.Separator();
    }

    private void DrawSelfAnalysisPreview()
    {
        using (ImRaii.PushId("self-analysis"))
        {
            UiSharedService.DrawCard("self-analysis-card", () =>
            {
                bool arrowState = _selfAnalysisOpen;
                UiSharedService.DrawArrowToggle(ref arrowState, "##self-analysis-toggle");
                if (arrowState != _selfAnalysisOpen)
                {
                    _selfAnalysisOpen = arrowState;
                }

                ImGui.SameLine(0f, 6f * ImGuiHelpers.GlobalScale);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Self Analysis");
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _selfAnalysisOpen = !_selfAnalysisOpen;
                }

                if (!_selfAnalysisOpen)
                {
                    return;
                }

                ImGuiHelpers.ScaledDummy(4f);

                var summary = _characterAnalyzer.CurrentSummary;
                bool isAnalyzing = _characterAnalyzer.IsAnalysisRunning;

                if (isAnalyzing)
                {
                    UiSharedService.ColorTextWrapped(
                        $"Analyse en cours ({_characterAnalyzer.CurrentFile}/{System.Math.Max(_characterAnalyzer.TotalFiles, 1)})...",
                        ImGuiColors.DalamudYellow);
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.StopCircle, "Annuler l'analyse"))
                    {
                        _characterAnalyzer.CancelAnalyze();
                    }
                    UiSharedService.AttachToolTip("Stopper l'analyse en cours.");
                }
                else
                {
                    bool recalculate = !summary.HasUncomputedEntries && !summary.IsEmpty;
                    var label = recalculate ? "Recalculer l'analyse" : "Lancer l'analyse";
                    var icon = recalculate ? FontAwesomeIcon.Sync : FontAwesomeIcon.PlayCircle;
                    if (_uiSharedService.IconTextButton(icon, label))
                    {
                        _ = _characterAnalyzer.ComputeAnalysis(print: false, recalculate: recalculate);
                    }
                    UiSharedService.AttachToolTip(recalculate
                        ? "Recalcule toutes les entrées pour mettre à jour les tailles partagées."
                        : "Analyse vos fichiers actuels pour estimer le poids partagé.");
                }

                if (summary.IsEmpty && !isAnalyzing)
                {
                    UiSharedService.ColorTextWrapped("Aucune donnée analysée pour l'instant. Lancez une analyse pour générer cet aperçu.",
                        ImGuiColors.DalamudGrey2);
                    return;
                }

                if (summary.HasUncomputedEntries && !isAnalyzing)
                {
                    UiSharedService.ColorTextWrapped("Certaines entrées n'ont pas encore de taille calculée. Lancez l'analyse pour compléter les données.",
                        ImGuiColors.DalamudYellow);
                }

                ImGuiHelpers.ScaledDummy(3f);

                UiSharedService.DrawGrouped(() =>
                {
                    if (ImGui.BeginTable("self-analysis-stats", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
                    {
                        ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthStretch, 0.55f);
                        ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthStretch, 0.45f);

                        DrawSelfAnalysisStatRow("Fichiers moddés", summary.TotalFiles.ToString("N0", CultureInfo.CurrentCulture));

                        var compressedValue = UiSharedService.ByteToString(summary.TotalCompressedSize);
                        Vector4? compressedColor = null;
                        FontAwesomeIcon? compressedIcon = null;
                        Vector4? compressedIconColor = null;
                        string? compressedTooltip = null;
                        if (summary.HasUncomputedEntries)
                        {
                            compressedColor = ImGuiColors.DalamudYellow;
                            compressedTooltip = "Lancez l'analyse pour calculer la taille de téléchargement exacte.";
                        }
                        else if (summary.TotalCompressedSize >= SelfAnalysisSizeWarningThreshold)
                        {
                            compressedColor = ImGuiColors.DalamudYellow;
                            compressedTooltip = "Au-delà de 300 MiB, certains joueurs peuvent ne pas voir toutes vos modifications.";
                            compressedIcon = FontAwesomeIcon.ExclamationTriangle;
                            compressedIconColor = ImGuiColors.DalamudYellow;
                        }

                        DrawSelfAnalysisStatRow("Taille compressée", compressedValue, compressedColor, compressedTooltip, compressedIcon, compressedIconColor);
                        DrawSelfAnalysisStatRow("Taille extraite", UiSharedService.ByteToString(summary.TotalOriginalSize));

                        Vector4? trianglesColor = null;
                        FontAwesomeIcon? trianglesIcon = null;
                        Vector4? trianglesIconColor = null;
                        string? trianglesTooltip = null;
                        if (summary.TotalTriangles >= SelfAnalysisTriangleWarningThreshold)
                        {
                            trianglesColor = ImGuiColors.DalamudYellow;
                            trianglesTooltip = "Plus de 150k triangles peuvent entraîner un auto-pause et impacter les performances.";
                            trianglesIcon = FontAwesomeIcon.ExclamationTriangle;
                            trianglesIconColor = ImGuiColors.DalamudYellow;
                        }
                        DrawSelfAnalysisStatRow("Triangles moddés", UiSharedService.TrisToString(summary.TotalTriangles), trianglesColor, trianglesTooltip, trianglesIcon, trianglesIconColor);

                        ImGui.EndTable();
                    }
                }, rounding: 4f, expectedWidth: ImGui.GetContentRegionAvail().X, drawBorder: false);

                string lastAnalysisText;
                Vector4 lastAnalysisColor = ImGuiColors.DalamudGrey2;
                if (isAnalyzing)
                {
                    lastAnalysisText = "Dernière analyse : en cours...";
                    lastAnalysisColor = ImGuiColors.DalamudYellow;
                }
                else if (_characterAnalyzer.LastCompletedAnalysis.HasValue)
                {
                    var localTime = _characterAnalyzer.LastCompletedAnalysis.Value.ToLocalTime();
                    lastAnalysisText = $"Dernière analyse : {localTime.ToString("g", CultureInfo.CurrentCulture)}";
                }
                else
                {
                    lastAnalysisText = "Dernière analyse : jamais";
                }

                ImGuiHelpers.ScaledDummy(2f);
                UiSharedService.ColorTextWrapped(lastAnalysisText, lastAnalysisColor);

                ImGuiHelpers.ScaledDummy(3f);

                if (_uiSharedService.IconTextButton(FontAwesomeIcon.PersonCircleQuestion, "Ouvrir l'analyse détaillée"))
                {
                    Mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
                }
            }, stretchWidth: true);
        }
    }

    private static void DrawSelfAnalysisStatRow(string label, string value, Vector4? valueColor = null, string? tooltip = null, FontAwesomeIcon? icon = null, Vector4? iconColor = null)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(label);
        ImGui.TableNextColumn();
        if (icon.HasValue)
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (iconColor.HasValue)
                {
                    using var iconColorPush = ImRaii.PushColor(ImGuiCol.Text, iconColor.Value);
                    ImGui.TextUnformatted(icon.Value.ToIconString());
                }
                else
                {
                    ImGui.TextUnformatted(icon.Value.ToIconString());
                }
            }
            ImGui.SameLine(0f, 4f);
        }

        if (valueColor.HasValue)
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, valueColor.Value);
            ImGui.TextUnformatted(value);
        }
        else
        {
            ImGui.TextUnformatted(value);
        }

        if (!string.IsNullOrEmpty(tooltip))
        {
            UiSharedService.AttachToolTip(tooltip);
        }
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

    private void DrawPairList(PairContentMode mode)
    {
        if (mode == PairContentMode.All)
        {
            using (ImRaii.PushId("addpair")) DrawAddPair();
        }

        using (ImRaii.PushId("pairs")) DrawPairs(mode);
        TransferPartHeight = ImGui.GetCursorPosY();
        using (ImRaii.PushId("filter")) DrawFilter();
    }

    private void DrawPairs(PairContentMode mode)
    {
        float availableHeight = ImGui.GetContentRegionAvail().Y;
        float ySize;
        if (TransferPartHeight <= 0)
        {
            float reserve = ImGui.GetFrameHeightWithSpacing() * 2f;
            ySize = availableHeight - reserve;
            if (ySize <= 0)
            {
                ySize = System.Math.Max(availableHeight, 1f);
            }
        }
        else
        {
            ySize = (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - TransferPartHeight - ImGui.GetCursorPosY();
        }
        var allUsers = GetFilteredUsers().OrderBy(u => u.GetPairSortKey(), StringComparer.Ordinal).ToList();
        var visibleUsersSource = allUsers.Where(u => u.IsVisible).ToList();
        var nonVisibleUsers = allUsers.Where(u => !u.IsVisible).ToList();
        var nearbyEntriesForDisplay = _configService.Current.EnableAutoDetectDiscovery
            ? GetNearbyEntriesForDisplay()
            : new List<Services.Mediator.NearbyEntry>();

        ImGui.BeginChild("list", new Vector2(WindowContentWidth, ySize), border: false);

        if (mode == PairContentMode.All)
        {
            var pendingCount = _nearbyPending?.Pending.Count ?? 0;
            if (pendingCount > 0)
            {
                UiSharedService.ColorTextWrapped("Invitation AutoDetect en attente. Ouvrez l\'interface AutoDetect pour gérer vos demandes.", ImGuiColors.DalamudYellow);
                ImGuiHelpers.ScaledDummy(4);
            }
        }

        if (mode == PairContentMode.VisibleOnly)
        {
            var visibleUsers = visibleUsersSource.Select(c => new DrawUserPair("Visible" + c.UserData.UID, c, _uidDisplayHandler, _apiController, Mediator, _selectGroupForPairUi, _uiSharedService, _charaDataManager, _serverManager)).ToList();
            bool showVisibleCard = visibleUsers.Count > 0;
            bool showNearbyCard = nearbyEntriesForDisplay.Count > 0;

            if (!showVisibleCard && !showNearbyCard)
            {
                const string calmMessage = "C'est bien trop calme ici... Il n'y a rien pour le moment.";
                using (_uiSharedService.UidFont.Push())
                {
                    var regionMin = ImGui.GetWindowContentRegionMin();
                    var availableWidth = UiSharedService.GetWindowContentRegionWidth();
                    var regionHeight = UiSharedService.GetWindowContentRegionHeight();
                    var textSize = ImGui.CalcTextSize(calmMessage, hideTextAfterDoubleHash: false, availableWidth);
                    var xOffset = MathF.Max(0f, (availableWidth - textSize.X) / 2f);
                    var yOffset = MathF.Max(0f, (regionHeight - textSize.Y) / 2f);
                    ImGui.SetCursorPos(new Vector2(regionMin.X + xOffset, regionMin.Y + yOffset));
                    UiSharedService.ColorTextWrapped(calmMessage, ImGuiColors.DalamudGrey3, regionMin.X + availableWidth);
                }
            }
            else
            {
                if (showVisibleCard)
                {
                    DrawVisibleCard(visibleUsers);
                }

                if (showNearbyCard)
                {
                    DrawNearbyCard(nearbyEntriesForDisplay);
                }
            }
        }
        else
        {
            var onlineUsers = nonVisibleUsers.Where(u => u.UserPair!.OtherPermissions.IsPaired() && (u.IsOnline || u.UserPair!.OwnPermissions.IsPaused()))
                .Select(c => new DrawUserPair("Online" + c.UserData.UID, c, _uidDisplayHandler, _apiController, Mediator, _selectGroupForPairUi, _uiSharedService, _charaDataManager, _serverManager))
                .ToList();
            var offlineUsers = nonVisibleUsers.Where(u => !u.UserPair!.OtherPermissions.IsPaired() || (!u.IsOnline && !u.UserPair!.OwnPermissions.IsPaused()))
                .Select(c => new DrawUserPair("Offline" + c.UserData.UID, c, _uidDisplayHandler, _apiController, Mediator, _selectGroupForPairUi, _uiSharedService, _charaDataManager, _serverManager))
                .ToList();

            Action? drawVisibleExtras = null;
            if (nearbyEntriesForDisplay.Count > 0)
            {
                var entriesForExtras = nearbyEntriesForDisplay;
                drawVisibleExtras = () => DrawNearbyCard(entriesForExtras);
            }

            _pairGroupsUi.Draw(Array.Empty<DrawUserPair>().ToList(), onlineUsers, offlineUsers, drawVisibleExtras);
        }

        ImGui.EndChild();
    }

    private List<Services.Mediator.NearbyEntry> GetNearbyEntriesForDisplay()
    {
        if (_nearbyEntries == null || _nearbyEntries.Count == 0)
        {
            return new List<Services.Mediator.NearbyEntry>();
        }

        return _nearbyEntries
            .Where(e => e.IsMatch && e.AcceptPairRequests && !string.IsNullOrEmpty(e.Token) && !IsAlreadyPairedQuickMenu(e))
            .OrderBy(e => e.Distance)
            .ToList();
    }

    private void DrawVisibleCard(List<DrawUserPair> visibleUsers)
    {
        if (visibleUsers.Count == 0)
        {
            return;
        }

        ImGuiHelpers.ScaledDummy(4f);
        using (ImRaii.PushId("group-Visible"))
        {
            UiSharedService.DrawCard("visible-card", () =>
            {
                bool visibleState = _visibleOpen;
                UiSharedService.DrawArrowToggle(ref visibleState, "##visible-toggle");
                if (visibleState != _visibleOpen)
                {
                    _visibleOpen = visibleState;
                }

                ImGui.SameLine(0f, 6f * ImGuiHelpers.GlobalScale);
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted($"Visible ({visibleUsers.Count})");
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _visibleOpen = !_visibleOpen;
                }

                if (!_visibleOpen)
                {
                    return;
                }

                ImGuiHelpers.ScaledDummy(4f);
                var indent = 18f * ImGuiHelpers.GlobalScale;
                ImGui.Indent(indent);
                foreach (var visibleUser in visibleUsers)
                {
                    visibleUser.DrawPairedClient();
                }
                ImGui.Unindent(indent);
            }, stretchWidth: true);
        }
        ImGuiHelpers.ScaledDummy(4f);
    }

    private void DrawNearbyCard(IReadOnlyList<Services.Mediator.NearbyEntry> nearbyEntries)
    {
        if (nearbyEntries.Count == 0)
        {
            return;
        }

        ImGuiHelpers.ScaledDummy(4f);
        using (ImRaii.PushId("group-Nearby"))
        {
            UiSharedService.DrawCard("nearby-card", () =>
            {
                bool nearbyState = _nearbyOpen;
                UiSharedService.DrawArrowToggle(ref nearbyState, "##nearby-toggle");
                if (nearbyState != _nearbyOpen)
                {
                    _nearbyOpen = nearbyState;
                }

                ImGui.SameLine(0f, 6f * ImGuiHelpers.GlobalScale);
                var onUmbra = nearbyEntries.Count;
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted($"Nearby ({onUmbra})");
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _nearbyOpen = !_nearbyOpen;
                }

                if (!_nearbyOpen)
                {
                    return;
                }

                ImGuiHelpers.ScaledDummy(4f);
                var indent = 18f * ImGuiHelpers.GlobalScale;
                ImGui.Indent(indent);
                foreach (var e in nearbyEntries)
                {
                    if (!e.AcceptPairRequests || string.IsNullOrEmpty(e.Token))
                    {
                        continue;
                    }

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
                ImGui.Unindent(indent);
            }, stretchWidth: true);
        }
        ImGuiHelpers.ScaledDummy(4f);
    }

    private void DrawSidebar()
    {
        bool isConnected = _apiController.ServerState is ServerState.Connected;

        ImGuiHelpers.ScaledDummy(6f);
        DrawConnectionIcon();
        ImGuiHelpers.ScaledDummy(12f);

        DrawSidebarButton(FontAwesomeIcon.Eye, "Visible pairs", CompactUiSection.VisiblePairs, isConnected);
        ImGuiHelpers.ScaledDummy(3f);
        DrawSidebarButton(FontAwesomeIcon.User, "Individual pairs", CompactUiSection.IndividualPairs, isConnected);
        ImGuiHelpers.ScaledDummy(3f);
        DrawSidebarButton(FontAwesomeIcon.UserFriends, "Syncshells", CompactUiSection.Syncshells, isConnected);
        ImGuiHelpers.ScaledDummy(3f);
        int pendingInvites = _nearbyPending?.Pending.Count ?? 0;
        bool highlightAutoDetect = pendingInvites > 0;
        string autoDetectTooltip = highlightAutoDetect
            ? $"AutoDetect — {pendingInvites} invitation(s) en attente"
            : "AutoDetect";
        DrawSidebarButton(FontAwesomeIcon.BroadcastTower, autoDetectTooltip, CompactUiSection.AutoDetect, isConnected, highlightAutoDetect, pendingInvites);
        ImGuiHelpers.ScaledDummy(3f);
        DrawSidebarButton(FontAwesomeIcon.PersonCircleQuestion, "Character Analysis", CompactUiSection.CharacterAnalysis, isConnected, _dataAnalysisUi.IsOpen, 0, () =>
        {
            Mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
        });
        ImGuiHelpers.ScaledDummy(3f);
        DrawSidebarButton(FontAwesomeIcon.Running, "Character Data Hub", CompactUiSection.CharacterDataHub, isConnected, false, 0, () =>
        {
            Mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        });
        ImGuiHelpers.ScaledDummy(12f);
        DrawSidebarButton(FontAwesomeIcon.UserCircle, "Edit Profile", CompactUiSection.EditProfile, isConnected);
        ImGuiHelpers.ScaledDummy(3f);
        DrawSidebarButton(FontAwesomeIcon.Cog, "Settings", CompactUiSection.Settings, true, _settingsUi.IsOpen, 0, () =>
        {
            Mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        });
    }

    private void DrawSidebarButton(FontAwesomeIcon icon, string tooltip, CompactUiSection section, bool enabled = true, bool highlight = false, int badgeCount = 0, Action? onClick = null)
    {
        using var id = ImRaii.PushId((int)section);
        float regionWidth = ImGui.GetContentRegionAvail().X;
        float buttonWidth = SidebarIconSize * ImGuiHelpers.GlobalScale;
        float offset = System.Math.Max(0f, (regionWidth - buttonWidth) / 2f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);

        bool isActive = _activeSection == section;

        if (DrawSidebarSquareButton(icon, isActive, highlight, enabled, badgeCount))
        {
            if (onClick != null)
            {
                onClick.Invoke();
            }
            else
            {
                _activeSection = section;
            }
        }

        UiSharedService.AttachToolTip(tooltip);
    }

    private void DrawConnectionIcon()
    {
        var state = _apiController.ServerState;
        bool hasServer = _serverManager.CurrentServer != null;
        bool isLinked = hasServer && !_serverManager.CurrentServer!.FullPause;
        var icon = isLinked ? FontAwesomeIcon.Unlink : FontAwesomeIcon.Link;

        using var id = ImRaii.PushId("connection-icon");
        float regionWidth = ImGui.GetContentRegionAvail().X;
        float buttonWidth = SidebarIconSize * ImGuiHelpers.GlobalScale;
        float offset = System.Math.Max(0f, (regionWidth - buttonWidth) / 2f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);

        bool isTogglingDisabled = !hasServer || state is ServerState.Reconnecting or ServerState.Disconnecting;

        if (DrawSidebarSquareButton(icon, isLinked, false, !isTogglingDisabled, 0) && !isTogglingDisabled)
        {
            ToggleConnection();
        }

        if (hasServer)
        {
            var tooltip = isLinked
                ? $"Disconnect from {_serverManager.CurrentServer!.ServerName}"
                : $"Connect to {_serverManager.CurrentServer!.ServerName}";
            UiSharedService.AttachToolTip(tooltip);
        }
        else
        {
            UiSharedService.AttachToolTip("No server configured");
        }
    }

    private bool DrawSidebarSquareButton(FontAwesomeIcon icon, bool isActive, bool highlight, bool enabled, int badgeCount)
    {
        float size = SidebarIconSize * ImGuiHelpers.GlobalScale;

        bool useAccent = (isActive || highlight) && enabled;
        var buttonColor = useAccent ? UiSharedService.AccentColor : SidebarButtonColor;
        var hoverColor = useAccent ? UiSharedService.AccentHoverColor : SidebarButtonHoverColor;
        var activeColor = useAccent ? UiSharedService.AccentActiveColor : SidebarButtonActiveColor;

        string iconText = icon.ToIconString();
        Vector2 iconSize;
        using (_uiSharedService.IconFont.Push())
        {
            iconSize = ImGui.CalcTextSize(iconText);
        }

        var start = ImGui.GetCursorScreenPos();
        bool clicked;

        using var disabled = ImRaii.Disabled(!enabled);
        using var buttonColorPush = ImRaii.PushColor(ImGuiCol.Button, buttonColor);
        using var hoverColorPush = ImRaii.PushColor(ImGuiCol.ButtonHovered, hoverColor);
        using var activeColorPush = ImRaii.PushColor(ImGuiCol.ButtonActive, activeColor);

        clicked = ImGui.Button("##sidebar-icon", new Vector2(size, size));

        using (_uiSharedService.IconFont.Push())
        {
            var textPos = new Vector2(
                start.X + (size - iconSize.X) / 2f,
                start.Y + (size - iconSize.Y) / 2f);
            uint iconColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.85f, 0.9f, 1f));
            if (highlight)
                iconColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.45f, 0.85f, 0.45f, 1f));
            else if (isActive)
                iconColor = ImGui.GetColorU32(ImGuiCol.Text);
            ImGui.GetWindowDrawList().AddText(textPos, iconColor, iconText);
        }

        if (badgeCount > 0)
        {
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            float radius = 6f * ImGuiHelpers.GlobalScale;
            var center = new Vector2(max.X - radius * 0.8f, min.Y + radius * 0.8f);
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddCircleFilled(center, radius, ImGui.ColorConvertFloat4ToU32(UiSharedService.AccentColor));
            string badgeText = badgeCount > 9 ? "9+" : badgeCount.ToString();
            var textSize = ImGui.CalcTextSize(badgeText);
            drawList.AddText(center - textSize / 2f, ImGui.GetColorU32(ImGuiCol.Text), badgeText);
        }

        return clicked && enabled;
    }


    private void ToggleConnection()
    {
        if (_serverManager.CurrentServer == null) return;

        _serverManager.CurrentServer.FullPause = !_serverManager.CurrentServer.FullPause;
        _serverManager.Save();
        _ = _apiController.CreateConnections();
    }

    private void DrawUnsupportedVersionBanner()
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

        UiSharedService.ColorTextWrapped(
            $"Your UmbraSync installation is out of date, the current version is {ver.Major}.{ver.Minor}.{ver.Build}. " +
            "It is highly recommended to keep UmbraSync up to date. Open /xlplugins and update the plugin.",
            UiSharedService.AccentColor);
    }

    private void DrawMainContent()
    {
        if (_activeSection is CompactUiSection.EditProfile)
        {
            _editProfileUi.DrawInline();
            DrawNewUserNoteModal();
            return;
        }

        bool requiresConnection = RequiresServerConnection(_activeSection);
        if (requiresConnection && _apiController.ServerState is not ServerState.Connected)
        {
            UiSharedService.ColorTextWrapped("Connectez-vous au serveur pour accéder à cette section.", ImGuiColors.DalamudGrey3);
            DrawNewUserNoteModal();
            return;
        }

        switch (_activeSection)
        {
            case CompactUiSection.VisiblePairs:
                DrawPairSection(PairContentMode.VisibleOnly);
                break;
            case CompactUiSection.IndividualPairs:
                DrawPairSection(PairContentMode.All);
                break;
            case CompactUiSection.Syncshells:
                DrawSyncshellSection();
                break;
            case CompactUiSection.AutoDetect:
                DrawAutoDetectSection();
                break;
        }

        DrawNewUserNoteModal();
    }

    private void DrawPairSection(PairContentMode mode)
    {
        DrawDefaultSyncSettings();
        using (ImRaii.PushId("pairlist")) DrawPairList(mode);
        ImGui.Separator();
        using (ImRaii.PushId("transfers")) DrawTransfers();
        TransferPartHeight = ImGui.GetCursorPosY() - TransferPartHeight;
        using (ImRaii.PushId("group-user-popup")) _selectPairsForGroupUi.Draw(_pairManager.DirectPairs);
        using (ImRaii.PushId("grouping-popup")) _selectGroupForPairUi.Draw();
    }

    private void DrawSyncshellSection()
    {
        using (ImRaii.PushId("syncshells")) _groupPanel.DrawSyncshells();
        ImGui.Separator();
        using (ImRaii.PushId("transfers")) DrawTransfers();
        TransferPartHeight = ImGui.GetCursorPosY() - TransferPartHeight;
        using (ImRaii.PushId("group-user-popup")) _selectPairsForGroupUi.Draw(_pairManager.DirectPairs);
        using (ImRaii.PushId("grouping-popup")) _selectGroupForPairUi.Draw();
    }

    private void DrawAutoDetectSection()
    {
        using (ImRaii.PushId("autodetect-inline")) _autoDetectUi.DrawInline();
    }

    private void DrawNewUserNoteModal()
    {
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
    }

    private static bool RequiresServerConnection(CompactUiSection section)
    {
        return section is CompactUiSection.VisiblePairs
            or CompactUiSection.IndividualPairs
            or CompactUiSection.Syncshells
            or CompactUiSection.AutoDetect;
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
        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawUIDHeader()
    {
        var uidText = GetUidText();
        Vector2 uidTextSize;

        using (_uiSharedService.UidFont.Push())
        {
            uidTextSize = ImGui.CalcTextSize(uidText);
        }

        var originalPos = ImGui.GetCursorPos();
        UiSharedService.SetFontScale(1.5f);
        Vector2 buttonSize = Vector2.Zero;
        float spacingX = ImGui.GetStyle().ItemSpacing.X;

        if (_apiController.ServerState is ServerState.Connected)
        {
            buttonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Copy);
            ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonSize.X);
            ImGui.SetCursorPosY(originalPos.Y + uidTextSize.Y / 2f - buttonSize.Y / 2f);
            if (_uiSharedService.IconButton(FontAwesomeIcon.Copy))
            {
                ImGui.SetClipboardText(_apiController.DisplayName);
            }
            UiSharedService.AttachToolTip("Copy your UID to clipboard");
            ImGui.SameLine();
        }

        ImGui.SetCursorPos(originalPos);
        UiSharedService.SetFontScale(1f);

        float referenceHeight = buttonSize.Y > 0f ? buttonSize.Y : ImGui.GetFrameHeight();
        ImGui.SetCursorPosY(originalPos.Y + referenceHeight / 2f - uidTextSize.Y / 2f - spacingX / 2f);
        float contentMin = ImGui.GetWindowContentRegionMin().X;
        float contentMax = ImGui.GetWindowContentRegionMax().X;
        float availableWidth = contentMax - contentMin;
        float center = contentMin + availableWidth / 2f;
        ImGui.SetCursorPosX(center - uidTextSize.X / 2f);

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
