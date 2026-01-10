using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.Logging;
using System.Globalization;
using UmbraSync.API.Dto.CharaData;
using UmbraSync.Localization;
using UmbraSync.MareConfiguration;
using UmbraSync.MareConfiguration.Models;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.CharaData.Models;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;

namespace UmbraSync.UI;

public sealed partial class CharaDataHubUi : WindowMediatorSubscriberBase
{
    private const int maxPoses = 10;
    private readonly CharaDataManager _charaDataManager;
    private readonly CharaDataNearbyManager _charaDataNearbyManager;
    private readonly CharaDataConfigService _configService;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileDialogManager _fileDialogManager;
    private readonly PairManager _pairManager;
    private readonly CharaDataGposeTogetherManager _charaDataGposeTogetherManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiSharedService;
    private readonly McdfShareManager _mcdfShareManager;
    private CancellationTokenSource? _closalCts = new();
    private bool _disableUI = false;
    private CancellationTokenSource? _disposalCts = new();
    private string _exportDescription = string.Empty;
    private string _filterCodeNote = string.Empty;
    private string _filterDescription = string.Empty;
    private Dictionary<string, List<CharaDataMetaInfoExtendedDto>>? _filteredDict;
    private Dictionary<string, (CharaDataFavorite Favorite, CharaDataMetaInfoExtendedDto? MetaInfo, bool DownloadedMetaInfo)> _filteredFavorites = [];
    private bool _filterPoseOnly = false;
    private bool _filterWorldOnly = false;
    private string _gposeTarget = string.Empty;
    private bool _hasValidGposeTarget;
    private string _importCode = string.Empty;
    private bool _isHandlingSelf = false;
    private DateTime _lastFavoriteUpdateTime = DateTime.UtcNow;
    private PoseEntryExtended? _nearbyHovered;
    private bool _openMcdOnlineOnNextRun = false;
    private bool _readExport;
    private string _selectedDtoId = string.Empty;
    private string SelectedDtoId
    {
        get => _selectedDtoId;
        set
        {
            if (!string.Equals(_selectedDtoId, value, StringComparison.Ordinal))
            {
                _charaDataManager.UploadTask = null;
                _selectedDtoId = value;
            }

        }
    }

    private static string SanitizeFileName(string? candidate, string fallback)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        if (string.IsNullOrWhiteSpace(candidate)) return fallback;

        var sanitized = new string(candidate.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
    private string _selectedSpecificUserIndividual = string.Empty;
    private string _selectedSpecificGroupIndividual = string.Empty;
    private string _sharedWithYouDescriptionFilter = string.Empty;
    private bool _sharedWithYouDownloadableFilter = false;
    private string _sharedWithYouOwnerFilter = string.Empty;
    private string _specificIndividualAdd = string.Empty;
    private string _specificGroupAdd = string.Empty;
    private bool _abbreviateCharaName = false;
    private string? _openComboHybridId = null;
    private (string Id, string? Alias, string AliasOrId, string? Note)[]? _openComboHybridEntries = null;
    private bool _comboHybridUsedLastFrame = false;
    private bool _mcdfShareInitialized;
    private string _mcdfShareDescription = string.Empty;
    private readonly List<string> _mcdfShareAllowedIndividuals = new();
    private readonly List<string> _mcdfShareAllowedSyncshells = new();
    private string _mcdfShareIndividualDropdownSelection = string.Empty;
    private string _mcdfShareIndividualInput = string.Empty;
    private string _mcdfShareSyncshellDropdownSelection = string.Empty;
    private string _mcdfShareSyncshellInput = string.Empty;
    private int _mcdfShareExpireDays;

    public CharaDataHubUi(ILogger<CharaDataHubUi> logger, MareMediator mediator, PerformanceCollectorService performanceCollectorService,
                         CharaDataManager charaDataManager, CharaDataNearbyManager charaDataNearbyManager, CharaDataConfigService configService,
                         UiSharedService uiSharedService, ServerConfigurationManager serverConfigurationManager,
                         DalamudUtilService dalamudUtilService, FileDialogManager fileDialogManager, PairManager pairManager,
                         CharaDataGposeTogetherManager charaDataGposeTogetherManager, McdfShareManager mcdfShareManager)
        : base(logger, mediator, $"{Loc.Get("CharaDataHub.WindowTitle")}###UmbraCharaDataUI", performanceCollectorService)
    {
        SetWindowSizeConstraints();

        _charaDataManager = charaDataManager;
        _charaDataNearbyManager = charaDataNearbyManager;
        _configService = configService;
        _uiSharedService = uiSharedService;
        _serverConfigurationManager = serverConfigurationManager;
        _dalamudUtilService = dalamudUtilService;
        _fileDialogManager = fileDialogManager;
        _pairManager = pairManager;
        _charaDataGposeTogetherManager = charaDataGposeTogetherManager;
        _mcdfShareManager = mcdfShareManager;
        Mediator.Subscribe<GposeStartMessage>(this, (_) => IsOpen |= _configService.Current.OpenMareHubOnGposeStart);
        Mediator.Subscribe<OpenCharaDataHubWithFilterMessage>(this, (msg) =>
        {
            IsOpen = true;
            _openDataApplicationShared = true;
            _sharedWithYouOwnerFilter = msg.UserData.AliasOrUID;
            UpdateFilteredItems();
        });
    }

    private bool _openDataApplicationShared = false;

    public string CharaName(string name)
    {
        if (_abbreviateCharaName)
        {
            var split = name.Split(" ");
            return split[0].First() + ". " + split[1].First() + ".";
        }

        return name;
    }

    public override void OnClose()
    {
        if (_disableUI)
        {
            IsOpen = true;
            return;
        }

        try
        {
            _closalCts?.Cancel();
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogTrace(ex, "Attempted to cancel CharaDataHubUi close token after disposal");
        }
        EnsureFreshCts(ref _closalCts);
        SelectedDtoId = string.Empty;
        _filteredDict = null;
        _sharedWithYouOwnerFilter = string.Empty;
        _importCode = string.Empty;
        _charaDataNearbyManager.ComputeNearbyData = false;
        _openComboHybridId = null;
        _openComboHybridEntries = null;
    }

    public override void OnOpen()
    {
        EnsureFreshCts(ref _closalCts);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelAndDispose(ref _closalCts);
            CancelAndDispose(ref _disposalCts);
        }

        base.Dispose(disposing);
    }

    protected override void DrawInternal()
    {
        DrawHubContent();
    }

    public void DrawInline()
    {
        using (ImRaii.PushId("CharaDataHubInline"))
        {
            DrawHubContent();
        }
    }

    private void DrawHubContent()
    {
        if (!_comboHybridUsedLastFrame)
        {
            _openComboHybridId = null;
            _openComboHybridEntries = null;
        }
        _comboHybridUsedLastFrame = false;

        _disableUI = !(_charaDataManager.UiBlockingComputation?.IsCompleted ?? true);
        if (DateTime.UtcNow.Subtract(_lastFavoriteUpdateTime).TotalSeconds > 2)
        {
            _lastFavoriteUpdateTime = DateTime.UtcNow;
            UpdateFilteredFavorites();
        }

        (_hasValidGposeTarget, _gposeTarget) = _charaDataManager.CanApplyInGpose().GetAwaiter().GetResult();

        if (!_charaDataManager.BrioAvailable)
        {
            ImGuiHelpers.ScaledDummy(3);
            UiSharedService.DrawGroupedCenteredColorText(Loc.Get("CharaDataHub.BrioRequired"), UiSharedService.AccentColor);
            UiSharedService.DistanceSeparator();
        }

        using var disabled = ImRaii.Disabled(_disableUI);

        DisableDisabled(() =>
        {
            if (_charaDataManager.DataApplicationTask != null)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Loc.Get("CharaDataHub.ApplyingData"));
                ImGui.SameLine();
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, Loc.Get("CharaDataHub.CancelApplication")))
                {
                    _charaDataManager.CancelDataApplication();
                }
            }
            if (!string.IsNullOrEmpty(_charaDataManager.DataApplicationProgress))
            {
                UiSharedService.ColorTextWrapped(_charaDataManager.DataApplicationProgress, UiSharedService.AccentColor);
            }
            if (_charaDataManager.DataApplicationTask != null)
            {
                UiSharedService.ColorTextWrapped(Loc.Get("CharaDataHub.ApplicationWarning"), UiSharedService.AccentColor);
                ImGuiHelpers.ScaledDummy(5);
                ImGui.Separator();
            }
        });

        bool smallUi = false;
        var accent = UiSharedService.AccentColor;
        if (accent.W <= 0f) accent = ImGuiColors.ParsedPurple;
        using (var topTabHoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, accent))
        using (var topTabActiveColor = ImRaii.PushColor(ImGuiCol.TabActive, accent))
        {
            using var tabs = ImRaii.TabBar("TabsTopLevel");

            _isHandlingSelf = _charaDataManager.HandledCharaData.Any(c => c.Value.IsSelf);
            if (_isHandlingSelf) _openMcdOnlineOnNextRun = false;

            using (var gposeTogetherTabItem = ImRaii.TabItem(Loc.Get("CharaDataHub.Tab.GposeTogether")))
            {
                if (gposeTogetherTabItem)
                {
                    smallUi = true;

                    DrawGposeTogether();
                }
            }

            using (var applicationTabItem = ImRaii.TabItem(Loc.Get("CharaDataHub.Tab.Application"), _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                if (applicationTabItem)
                {
                    smallUi = true;
                    using (var appTabHoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, accent))
                    using (var appTabActiveColor = ImRaii.PushColor(ImGuiCol.TabActive, accent))
                    {
                        using var appTabs = ImRaii.TabBar("TabsApplicationLevel");

                        using (ImRaii.Disabled(!_uiSharedService.IsInGpose))
                        {
                            using (var gposeTabItem = ImRaii.TabItem(Loc.Get("CharaDataHub.Tab.GposeActors")))
                            {
                                if (gposeTabItem)
                                {
                                    using var id = ImRaii.PushId("gposeControls");
                                    DrawGposeControls();
                                }
                            }
                        }
                        if (!_uiSharedService.IsInGpose)
                            UiSharedService.AttachToolTip(Loc.Get("CharaDataHub.GposeOnlyTooltip"));

                        using (var nearbyPosesTabItem = ImRaii.TabItem(Loc.Get("CharaDataHub.Tab.PosesNearby")))
                        {
                            if (nearbyPosesTabItem)
                            {
                                using var id = ImRaii.PushId("nearbyPoseControls");
                                _charaDataNearbyManager.ComputeNearbyData = true;

                                DrawNearbyPoses();
                            }
                            else
                            {
                                _charaDataNearbyManager.ComputeNearbyData = false;
                            }
                        }

                        using (var gposeTabItem = ImRaii.TabItem(Loc.Get("CharaDataHub.Tab.ApplyData"), _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
                        {
                            if (gposeTabItem)
                            {
                                smallUi |= true;
                                using var id = ImRaii.PushId("applyData");
                                DrawDataApplication();
                            }
                        }
                    }
                }
                else
                {
                    _charaDataNearbyManager.ComputeNearbyData = false;
                }
            }

            using (ImRaii.Disabled(_isHandlingSelf))
            {
                ImGuiTabItemFlags flagsTopLevel = ImGuiTabItemFlags.None;
                if (_openMcdOnlineOnNextRun)
                {
                    flagsTopLevel = ImGuiTabItemFlags.SetSelected;
                    _openMcdOnlineOnNextRun = false;
                }

                using (var creationTabItem = ImRaii.TabItem(Loc.Get("CharaDataHub.Tab.DataCreation"), flagsTopLevel))
                {
                    if (creationTabItem)
                    {
                        using (var creationTabHoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, accent))
                        using (var creationTabActiveColor = ImRaii.PushColor(ImGuiCol.TabActive, accent))
                        {
                            using var creationTabs = ImRaii.TabBar("TabsCreationLevel");

                            ImGuiTabItemFlags flags = ImGuiTabItemFlags.None;
                            if (_openMcdOnlineOnNextRun)
                            {
                                flags = ImGuiTabItemFlags.SetSelected;
                                _openMcdOnlineOnNextRun = false;
                            }
                            using (var mcdOnlineTabItem = ImRaii.TabItem(Loc.Get("CharaDataHub.Tab.OnlineData"), flags))
                            {
                                if (mcdOnlineTabItem)
                                {
                                    using var id = ImRaii.PushId("mcdOnline");
                                    DrawMcdOnline();
                                }
                            }

                            using (var mcdfTabItem = ImRaii.TabItem(Loc.Get("CharaDataHub.Tab.McdfExport")))
                            {
                                if (mcdfTabItem)
                                {
                                    using var id = ImRaii.PushId("mcdfExport");
                                    DrawMcdfExport();
                                }
                            }

                            using (var mcdfShareTabItem = ImRaii.TabItem(Loc.Get("CharaDataHub.Tab.McdfShare")))
                            {
                                if (mcdfShareTabItem)
                                {
                                    using var id = ImRaii.PushId("mcdfShare");
                                    DrawMcdfShare();
                                }
                            }
                        }
                    }
                }
            }
            var settingsTabLabel = Loc.Get("CharaDataHub.Tab.Settings");
            if (string.IsNullOrWhiteSpace(settingsTabLabel))
            {
                settingsTabLabel = "Settings";
            }

            using (var settingsTabItem = ImRaii.TabItem(settingsTabLabel))
            {
                if (settingsTabItem)
                {
                    using var id = ImRaii.PushId("settings");
                    DrawSettings();
                }
            }
        }

        if (_isHandlingSelf)
        {
            UiSharedService.AttachToolTip(Loc.Get("CharaDataHub.CreationDisabledTooltip"));
        }

        SetWindowSizeConstraints(smallUi);
    }

    private void DrawAddOrRemoveFavorite(CharaDataFullDto dto)
    {
        DrawFavorite(dto.Uploader.UID + ":" + dto.Id);
    }

    private void DrawAddOrRemoveFavorite(CharaDataMetaInfoExtendedDto? dto)
    {
        if (dto == null) return;
        DrawFavorite(dto.FullId);
    }

    private void DrawFavorite(string id)
    {
        bool isFavorite = _configService.Current.FavoriteCodes.TryGetValue(id, out var favorite);
        if (_configService.Current.FavoriteCodes.ContainsKey(id))
        {
            _uiSharedService.IconText(FontAwesomeIcon.Star, ImGuiColors.ParsedGold);
            UiSharedService.AttachToolTip($"Custom Description: {favorite?.CustomDescription ?? string.Empty}" + UiSharedService.TooltipSeparator
                + "Click to remove from Favorites");
        }
        else
        {
            _uiSharedService.IconText(FontAwesomeIcon.Star, ImGuiColors.DalamudGrey);
            UiSharedService.AttachToolTip("Click to add to Favorites");
        }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (isFavorite) _configService.Current.FavoriteCodes.Remove(id);
            else _configService.Current.FavoriteCodes[id] = new();
            _configService.Save();
        }
    }

    private void DrawGposeControls()
    {
        _uiSharedService.BigText(Loc.Get("CharaDataHub.Apply.GposeActors.Title"));
        ImGuiHelpers.ScaledDummy(5);
        using var indent = ImRaii.PushIndent(10f);

        foreach (var actor in _dalamudUtilService.GetGposeCharactersFromObjectTable())
        {
            if (actor == null) continue;
            using var actorId = ImRaii.PushId(actor.Name.TextValue);
            UiSharedService.DrawGrouped(() =>
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.Crosshairs))
                {
                    unsafe
                    {
                        _dalamudUtilService.GposeTarget = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)actor.Address;
                    }
                }
                ImGui.SameLine();
                UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("CharaDataHub.Apply.GposeActors.TargetTooltip"), CharaName(actor.Name.TextValue)));
                ImGui.AlignTextToFramePadding();
                var pos = ImGui.GetCursorPosX();
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, actor.Address == (_dalamudUtilService.GetGposeTargetGameObjectAsync().GetAwaiter().GetResult()?.Address ?? nint.Zero)))
                {
                    ImGui.TextUnformatted(CharaName(actor.Name.TextValue));
                }
                ImGui.SameLine(250);
                var handled = _charaDataManager.HandledCharaData.GetValueOrDefault(actor.Name.TextValue);
                using (ImRaii.Disabled(handled == null))
                {
                    _uiSharedService.IconText(FontAwesomeIcon.InfoCircle);
                    var id = string.IsNullOrEmpty(handled?.MetaInfo.Uploader.UID) ? handled?.MetaInfo.Id : handled.MetaInfo.FullId;
                    UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("CharaDataHub.Apply.GposeActors.AppliedDataTooltip"), id ?? Loc.Get("CharaDataHub.Apply.GposeActors.NoData")));

                    ImGui.SameLine();
                    // maybe do this better, check with brio for handled charas or sth
                    using (ImRaii.Disabled(!actor.Name.TextValue.StartsWith("Brio ", StringComparison.Ordinal)))
                    {
                        if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                        {
                            _charaDataManager.RemoveChara(actor.Name.TextValue);
                        }
                        UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("CharaDataHub.Apply.GposeActors.RemoveTooltip"), CharaName(actor.Name.TextValue)));
                    }
                    ImGui.SameLine();
                    if (_uiSharedService.IconButton(FontAwesomeIcon.Undo))
                    {
                        _charaDataManager.RevertChara(handled);
                    }
                    UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("CharaDataHub.Apply.GposeActors.RevertTooltip"), CharaName(actor.Name.TextValue)));
                    ImGui.SetCursorPosX(pos);
                    DrawPoseData(handled?.MetaInfo, actor.Name.TextValue, true);
                }
            });

            ImGuiHelpers.ScaledDummy(2);
        }
    }

    private void DrawDataApplication()
    {
        _uiSharedService.BigText(Loc.Get("CharaDataHub.Apply.Title"));

        ImGuiHelpers.ScaledDummy(5);

        if (_uiSharedService.IsInGpose)
        {
            ImGui.TextUnformatted(Loc.Get("CharaDataHub.Apply.TargetLabel"));
            ImGui.SameLine(200);
            UiSharedService.ColorText(CharaName(_gposeTarget), UiSharedService.GetBoolColor(_hasValidGposeTarget));
        }

        if (!_hasValidGposeTarget)
        {
            ImGuiHelpers.ScaledDummy(3);
            UiSharedService.DrawGroupedCenteredColorText(Loc.Get("CharaDataHub.Apply.TargetWarning"), UiSharedService.AccentColor, 350);
        }

        ImGuiHelpers.ScaledDummy(10);

        var accent = UiSharedService.AccentColor;
        if (accent.W <= 0f) accent = ImGuiColors.ParsedPurple;
        using (var applyTabHoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, accent))
        using (var applyTabActiveColor = ImRaii.PushColor(ImGuiCol.TabActive, accent))
        {
            using var tabs = ImRaii.TabBar("Tabs");

            using (var byFavoriteTabItem = ImRaii.TabItem(Loc.Get("CharaDataHub.Apply.Tabs.Favorites")))
            {
                if (byFavoriteTabItem)
                {
                    using var id = ImRaii.PushId("byFavorite");

                    ImGuiHelpers.ScaledDummy(5);

                    var max = ImGui.GetWindowContentRegionMax();
                    UiSharedService.DrawTree(Loc.Get("CharaDataHub.Apply.Filters.Title"), () =>
                    {
                        var maxIndent = ImGui.GetWindowContentRegionMax();
                        ImGui.SetNextItemWidth(maxIndent.X - ImGui.GetCursorPosX());
                        ImGui.InputTextWithHint("##ownFilter", Loc.Get("CharaDataHub.Apply.Filters.CodeOwner"), ref _filterCodeNote, 100);
                        ImGui.SetNextItemWidth(maxIndent.X - ImGui.GetCursorPosX());
                        ImGui.InputTextWithHint("##descFilter", Loc.Get("CharaDataHub.Apply.Filters.Description"), ref _filterDescription, 100);
                        ImGui.Checkbox(Loc.Get("CharaDataHub.Apply.Filters.OnlyPose"), ref _filterPoseOnly);
                        ImGui.Checkbox(Loc.Get("CharaDataHub.Apply.Filters.OnlyWorld"), ref _filterWorldOnly);
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, Loc.Get("CharaDataHub.Apply.Filters.Reset")))
                        {
                            _filterCodeNote = string.Empty;
                            _filterDescription = string.Empty;
                            _filterPoseOnly = false;
                            _filterWorldOnly = false;
                        }
                    });

                    ImGuiHelpers.ScaledDummy(5);
                    ImGui.Separator();
                    using var scrollableChild = ImRaii.Child("favorite");
                    ImGuiHelpers.ScaledDummy(5);
                    using var totalIndent = ImRaii.PushIndent(5f);
                    var cursorPos = ImGui.GetCursorPos();
                    max = ImGui.GetWindowContentRegionMax();
                    foreach (var favorite in _filteredFavorites.OrderByDescending(k => k.Value.Favorite.LastDownloaded))
                    {
                        UiSharedService.DrawGrouped(() =>
                        {
                            using var tableid = ImRaii.PushId(favorite.Key);
                            ImGui.AlignTextToFramePadding();
                            DrawFavorite(favorite.Key);
                            using var innerIndent = ImRaii.PushIndent(25f);
                            ImGui.SameLine();
                            var xPos = ImGui.GetCursorPosX();
                            var maxPos = (max.X - cursorPos.X);

                            bool metaInfoDownloaded = favorite.Value.DownloadedMetaInfo;
                            var metaInfo = favorite.Value.MetaInfo;

                            ImGui.AlignTextToFramePadding();
                            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey, !metaInfoDownloaded))
                            using (ImRaii.PushColor(ImGuiCol.Text, UiSharedService.GetBoolColor(metaInfo != null), metaInfoDownloaded))
                                ImGui.TextUnformatted(favorite.Key);

                            var iconSize = _uiSharedService.GetIconData(FontAwesomeIcon.Check);
                            var refreshButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowsSpin);
                            var applyButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight);
                            var addButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus);
                            var offsetFromRight = maxPos - (iconSize.X + refreshButtonSize.X + applyButtonSize.X + addButtonSize.X + (ImGui.GetStyle().ItemSpacing.X * 3.5f));

                            ImGui.SameLine();
                            ImGui.SetCursorPosX(offsetFromRight);
                            if (metaInfoDownloaded)
                            {
                                _uiSharedService.BooleanToColoredIcon(metaInfo != null, false);
                                if (metaInfo != null)
                                {
                                    UiSharedService.AttachToolTip(Loc.Get("CharaDataHub.Apply.Favorites.MetaInfoPresent") + UiSharedService.TooltipSeparator
                                        + string.Format(CultureInfo.CurrentCulture, Loc.Get("CharaDataHub.Apply.Favorites.MetaUpdated"), metaInfo.UpdatedDate) + Environment.NewLine
                                        + string.Format(CultureInfo.CurrentCulture, Loc.Get("CharaDataHub.Apply.Favorites.MetaDescription"), metaInfo.Description) + Environment.NewLine
                                        + string.Format(CultureInfo.CurrentCulture, Loc.Get("CharaDataHub.Apply.Favorites.MetaPoses"), metaInfo.PoseData.Count));
                                }
                                else
                                {
                                    UiSharedService.AttachToolTip(Loc.Get("CharaDataHub.Apply.Favorites.MetaNotDownloaded") + UiSharedService.TooltipSeparator
                                        + Loc.Get("CharaDataHub.Apply.Favorites.MetaNotAccessible"));
                                }
                            }
                            else
                            {
                                _uiSharedService.IconText(FontAwesomeIcon.QuestionCircle, ImGuiColors.DalamudGrey);
                                UiSharedService.AttachToolTip(Loc.Get("CharaDataHub.Apply.Favorites.MetaUnknown"));
                            }

                            ImGui.SameLine();
                            bool isInTimeout = _charaDataManager.IsInTimeout(favorite.Key);
                            using (ImRaii.Disabled(isInTimeout))
                            {
                                if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowsSpin))
                                {
                                    _charaDataManager.DownloadMetaInfo(favorite.Key, false);
                                    UpdateFilteredItems();
                                }
                            }
                            UiSharedService.AttachToolTip(isInTimeout ? Loc.Get("CharaDataHub.Apply.Favorites.RefreshTimeout")
                                : Loc.Get("CharaDataHub.Apply.Favorites.RefreshTooltip"));

                            ImGui.SameLine();
                            GposeMetaInfoAction((meta) =>
                            {
                                if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight) && meta != null)
                                {
                                    _ = _charaDataManager.ApplyCharaDataToGposeTarget(meta);
                                }
                            }, "Apply Character Data to GPose Target", metaInfo, _hasValidGposeTarget, false);
                            ImGui.SameLine();
                            GposeMetaInfoAction((meta) =>
                            {
                                if (_uiSharedService.IconButton(FontAwesomeIcon.Plus) && meta != null)
                                {
                                    _ = _charaDataManager.SpawnAndApplyData(meta);
                                }
                            }, "Spawn Actor with Brio and apply Character Data", metaInfo, _hasValidGposeTarget, true);

                            string uidText = string.Empty;
                            var uid = favorite.Key.Split(":")[0];
                            if (metaInfo != null)
                            {
                                uidText = metaInfo.Uploader.AliasOrUID;
                            }
                            else
                            {
                                uidText = uid;
                            }

                            var note = _serverConfigurationManager.GetNoteForUid(uid);
                            if (note != null)
                            {
                                uidText = $"{note} ({uidText})";
                            }
                            ImGui.TextUnformatted(uidText);

                            ImGui.TextUnformatted("Last Use: ");
                            ImGui.SameLine();
                            ImGui.TextUnformatted(favorite.Value.Favorite.LastDownloaded == DateTime.MaxValue
                                ? "Never"
                                : favorite.Value.Favorite.LastDownloaded.ToString(CultureInfo.CurrentCulture));

                            var desc = favorite.Value.Favorite.CustomDescription;
                            ImGui.SetNextItemWidth(maxPos - xPos);
                            if (ImGui.InputTextWithHint("##desc", "Custom Description for Favorite", ref desc, 100))
                            {
                                favorite.Value.Favorite.CustomDescription = desc;
                                _configService.Save();
                            }

                            DrawPoseData(metaInfo, _gposeTarget, _hasValidGposeTarget);
                        });

                        ImGuiHelpers.ScaledDummy(5);
                    }

                    if (_configService.Current.FavoriteCodes.Count == 0)
                    {
                        UiSharedService.ColorTextWrapped("You have no favorites added. Add Favorites through the other tabs before you can use this tab.", UiSharedService.AccentColor);
                    }
                }
            }

            using (var byCodeTabItem = ImRaii.TabItem("Code"))
            {
                using var id = ImRaii.PushId("byCodeTab");
                if (byCodeTabItem)
                {
                    using var child = ImRaii.Child("sharedWithYouByCode", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                    DrawHelpFoldout("You can apply character data you have a code for in this tab. Provide the code in it's given format \"OwnerUID:DataId\" into the field below and click on " +
                                    "\"Get Info from Code\". This will provide you basic information about the data behind the code. Afterwards select an actor in GPose and press on \"Download and apply to <actor>\"." + Environment.NewLine + Environment.NewLine
                                    + "Description: as set by the owner of the code to give you more or additional information of what this code may contain." + Environment.NewLine
                                    + "Last Update: the date and time the owner of the code has last updated the data." + Environment.NewLine
                                    + "Is Downloadable: whether or not the code is downloadable and applicable. If the code is not downloadable, contact the owner so they can attempt to fix it." + Environment.NewLine + Environment.NewLine
                                    + "To download a code the code requires correct access permissions to be set by the owner. If getting info from the code fails, contact the owner to make sure they set their Access Permissions for the code correctly.");

                    ImGuiHelpers.ScaledDummy(5);
                    ImGui.InputTextWithHint("##importCode", "Enter Data Code", ref _importCode, 100);
                    using (ImRaii.Disabled(string.IsNullOrEmpty(_importCode)))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Get Info from Code"))
                        {
                            _charaDataManager.DownloadMetaInfo(_importCode);
                        }
                    }
                    GposeMetaInfoAction((meta) =>
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, $"Download and Apply") && meta != null)
                        {
                            _ = _charaDataManager.ApplyCharaDataToGposeTarget(meta);
                        }
                    }, "Apply this Character Data to the current GPose actor", _charaDataManager.LastDownloadedMetaInfo, _hasValidGposeTarget, false);
                    ImGui.SameLine();
                    GposeMetaInfoAction((meta) =>
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, $"Download and Spawn") && meta != null)
                        {
                            _ = _charaDataManager.SpawnAndApplyData(meta);
                        }
                    }, "Spawn a new Brio actor and apply this Character Data", _charaDataManager.LastDownloadedMetaInfo, _hasValidGposeTarget, true);
                    ImGui.SameLine();
                    ImGui.AlignTextToFramePadding();
                    DrawAddOrRemoveFavorite(_charaDataManager.LastDownloadedMetaInfo);

                    ImGui.NewLine();
                    if (!_charaDataManager.DownloadMetaInfoTask?.IsCompleted ?? false)
                    {
                        UiSharedService.ColorTextWrapped("Downloading meta info. Please wait.", UiSharedService.AccentColor);
                    }
                    if ((_charaDataManager.DownloadMetaInfoTask?.IsCompleted ?? false) && !_charaDataManager.DownloadMetaInfoTask.Result.Success)
                    {
                        UiSharedService.ColorTextWrapped(_charaDataManager.DownloadMetaInfoTask.Result.Result, UiSharedService.AccentColor);
                    }

                    using (ImRaii.Disabled(_charaDataManager.LastDownloadedMetaInfo == null))
                    {
                        ImGuiHelpers.ScaledDummy(5);
                        var metaInfo = _charaDataManager.LastDownloadedMetaInfo;
                        ImGui.TextUnformatted("Description");
                        ImGui.SameLine(150);
                        UiSharedService.TextWrapped(string.IsNullOrEmpty(metaInfo?.Description) ? "-" : metaInfo.Description);
                        ImGui.TextUnformatted("Last Update");
                        ImGui.SameLine(150);
                        ImGui.TextUnformatted(metaInfo?.UpdatedDate.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "-");
                        ImGui.TextUnformatted("Is Downloadable");
                        ImGui.SameLine(150);
                        _uiSharedService.BooleanToColoredIcon(metaInfo?.CanBeDownloaded ?? false, inline: false);
                        ImGui.TextUnformatted("Poses");
                        ImGui.SameLine(150);
                        if (metaInfo?.HasPoses ?? false)
                            DrawPoseData(metaInfo, _gposeTarget, _hasValidGposeTarget);
                        else
                            _uiSharedService.BooleanToColoredIcon(false, false);
                    }
                }
            }

            using (var yourOwnTabItem = ImRaii.TabItem("Your Own"))
            {
                using var id = ImRaii.PushId("yourOwnTab");
                if (yourOwnTabItem)
                {
                    DrawHelpFoldout("You can apply character data you created yourself in this tab. If the list is not populated press on \"Download your Character Data\"." + Environment.NewLine + Environment.NewLine
                                     + "To create new and edit your existing character data use the \"Online Data\" tab.");

                    ImGuiHelpers.ScaledDummy(5);

                    using (ImRaii.Disabled(_charaDataManager.GetAllDataTask != null
                        || (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowCircleDown, "Download your Character Data"))
                        {
                            var cts = EnsureFreshCts(ref _disposalCts);
                            _ = _charaDataManager.GetAllData(cts.Token);
                        }
                    }
                    if (_charaDataManager.DataGetTimeoutTask != null && !_charaDataManager.DataGetTimeoutTask.IsCompleted)
                    {
                        UiSharedService.AttachToolTip("You can only refresh all character data from server every minute. Please wait.");
                    }

                    ImGuiHelpers.ScaledDummy(5);
                    ImGui.Separator();

                    using var child = ImRaii.Child("ownDataChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                    using var indent = ImRaii.PushIndent(10f);
                    foreach (var data in _charaDataManager.OwnCharaData.Values)
                    {
                        var hasMetaInfo = _charaDataManager.TryGetMetaInfo(data.FullId, out var metaInfo);
                        if (!hasMetaInfo || metaInfo == null) continue;
                        DrawMetaInfoData(_gposeTarget, _hasValidGposeTarget, metaInfo, true);
                    }

                    ImGuiHelpers.ScaledDummy(5);
                }
            }

            using (var sharedWithYouTabItem = ImRaii.TabItem("Shared With You", _openDataApplicationShared ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                using var id = ImRaii.PushId("sharedWithYouTab");
                if (sharedWithYouTabItem)
                {
                    DrawHelpFoldout("You can apply character data shared with you implicitly in this tab. Shared Character Data are Character Data entries that have \"Sharing\" set to \"Shared\" and you have access through those by meeting the access restrictions, " +
                                    "i.e. you were specified by your UID to gain access or are paired with the other user according to the Access Restrictions setting." + Environment.NewLine + Environment.NewLine
                                    + "Filter if needed to find a specific entry, then just press on \"Apply to <actor>\" and it will download and apply the Character Data to the currently targeted GPose actor." + Environment.NewLine + Environment.NewLine
                                    + "Note: Shared Data of Pairs you have paused will not be shown here.");

                    ImGuiHelpers.ScaledDummy(5);

                    DrawUpdateSharedDataButton();

                    int activeFilters = 0;
                    if (!string.IsNullOrEmpty(_sharedWithYouOwnerFilter)) activeFilters++;
                    if (!string.IsNullOrEmpty(_sharedWithYouDescriptionFilter)) activeFilters++;
                    if (_sharedWithYouDownloadableFilter) activeFilters++;
                    string filtersText = activeFilters == 0 ? "Filters" : $"Filters ({activeFilters} active)";
                    UiSharedService.DrawTree($"{filtersText}##filters", () =>
                    {
                        var filterWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                        ImGui.SetNextItemWidth(filterWidth);
                        if (ImGui.InputTextWithHint("##filter", "Filter by UID/Note", ref _sharedWithYouOwnerFilter, 30))
                        {
                            UpdateFilteredItems();
                        }
                        ImGui.SetNextItemWidth(filterWidth);
                        if (ImGui.InputTextWithHint("##filterDesc", "Filter by Description", ref _sharedWithYouDescriptionFilter, 50))
                        {
                            UpdateFilteredItems();
                        }
                        if (ImGui.Checkbox("Only show downloadable", ref _sharedWithYouDownloadableFilter))
                        {
                            UpdateFilteredItems();
                        }
                    });

                    if (_filteredDict == null && _charaDataManager.GetSharedWithYouTask == null)
                    {
                        _filteredDict = _charaDataManager.SharedWithYouData
                            .ToDictionary(k =>
                            {
                                var note = _serverConfigurationManager.GetNoteForUid(k.Key.UID);
                                return string.IsNullOrEmpty(note) ? k.Key.AliasOrUID : $"{note} ({k.Key.AliasOrUID})";
                            }, k => k.Value, StringComparer.OrdinalIgnoreCase)
                            .Where(k => string.IsNullOrEmpty(_sharedWithYouOwnerFilter) || k.Key.Contains(_sharedWithYouOwnerFilter, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
                    }

                    ImGuiHelpers.ScaledDummy(5);
                    ImGui.Separator();
                    using var child = ImRaii.Child("sharedWithYouChild", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);

                    ImGuiHelpers.ScaledDummy(5);
                    foreach (var entry in _filteredDict ?? [])
                    {
                        bool isFilteredAndHasToBeOpened = entry.Key.Contains(_sharedWithYouOwnerFilter) && _openDataApplicationShared;
                        if (isFilteredAndHasToBeOpened)
                            ImGui.SetNextItemOpen(isFilteredAndHasToBeOpened);
                        UiSharedService.DrawTree($"{entry.Key} - [{entry.Value.Count} Character Data Sets]##{entry.Key}", () =>
                        {
                            foreach (var data in entry.Value)
                            {
                                DrawMetaInfoData(_gposeTarget, _hasValidGposeTarget, data);
                            }
                            ImGuiHelpers.ScaledDummy(5);
                        });
                        if (isFilteredAndHasToBeOpened)
                            _openDataApplicationShared = false;
                    }
                }
            }

            using (var mcdfTabItem = ImRaii.TabItem("From MCDF"))
            {
                using var id = ImRaii.PushId("applyMcdfTab");
                if (mcdfTabItem)
                {
                    using var child = ImRaii.Child("applyMcdf", new(0, 0), false, ImGuiWindowFlags.AlwaysAutoResize);
                    DrawHelpFoldout("You can apply character data shared with you using a MCDF file in this tab." + Environment.NewLine + Environment.NewLine
                                    + "Load the MCDF first via the \"Load MCDF\" button which will give you the basic description that the owner has set during export." + Environment.NewLine
                                    + "You can then apply it to any handled GPose actor." + Environment.NewLine + Environment.NewLine
                                    + "MCDF to share with others can be generated using the \"MCDF Export\" tab at the top.");

                    ImGuiHelpers.ScaledDummy(5);

                    if (_charaDataManager.LoadedMcdfHeader == null || _charaDataManager.LoadedMcdfHeader.IsCompleted)
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.FolderOpen, "Load MCDF"))
                        {
                            _fileDialogManager.OpenFileDialog("Pick MCDF file", ".mcdf", (success, paths) =>
                            {
                                if (!success) return;
                                if (paths.FirstOrDefault() is not string path) return;

                                _configService.Current.LastSavedCharaDataLocation = Path.GetDirectoryName(path) ?? string.Empty;
                                _configService.Save();

                                _charaDataManager.LoadMcdf(path);
                            }, 1, Directory.Exists(_configService.Current.LastSavedCharaDataLocation) ? _configService.Current.LastSavedCharaDataLocation : null);
                        }
                        UiSharedService.AttachToolTip("Load MCDF Metadata into memory");
                        if ((_charaDataManager.LoadedMcdfHeader?.IsCompleted ?? false))
                        {
                            ImGui.TextUnformatted("Loaded file");
                            ImGui.SameLine(200);
                            UiSharedService.TextWrapped(_charaDataManager.LoadedMcdfHeader.Result.LoadedFile.FilePath);
                            ImGui.Text("Description");
                            ImGui.SameLine(200);
                            UiSharedService.TextWrapped(_charaDataManager.LoadedMcdfHeader.Result.LoadedFile.CharaFileData.Description);

                            ImGuiHelpers.ScaledDummy(5);

                            using (ImRaii.Disabled(!_hasValidGposeTarget))
                            {
                                if (_uiSharedService.IconTextButton(FontAwesomeIcon.ArrowRight, "Apply"))
                                {
                                    _ = _charaDataManager.McdfApplyToGposeTarget();
                                }
                                UiSharedService.AttachToolTip($"Apply to {_gposeTarget}");
                                ImGui.SameLine();
                                using (ImRaii.Disabled(!_charaDataManager.BrioAvailable))
                                {
                                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Spawn Actor and Apply"))
                                    {
                                        _charaDataManager.McdfSpawnApplyToGposeTarget();
                                    }
                                }
                            }
                        }
                        if ((_charaDataManager.LoadedMcdfHeader?.IsFaulted ?? false) || (_charaDataManager.McdfApplicationTask?.IsFaulted ?? false))
                        {
                            UiSharedService.ColorTextWrapped("Failure to read MCDF file. MCDF file is possibly corrupt. Re-export the MCDF file and try again.",
                                UiSharedService.AccentColor);
                            UiSharedService.ColorTextWrapped("Note: if this is your MCDF, try redrawing yourself, wait and re-export the file. " +
                                "If you received it from someone else have them do the same.", UiSharedService.AccentColor);
                        }
                    }
                    else
                    {
                        UiSharedService.ColorTextWrapped("Loading Character...", UiSharedService.AccentColor);
                    }
                }
            }
        }
    }

    private void DrawMcdfExport()
    {
        _uiSharedService.BigText("MCDF File Export");

        DrawHelpFoldout("This feature allows you to pack your character into a MCDF file and manually send it to other people. MCDF files be imported during GPose. " +
            "Be aware that the possibility exists that people write unofficial custom exporters to extract the containing data.");

        ImGuiHelpers.ScaledDummy(5);

        ImGui.Checkbox("##readExport", ref _readExport);
        ImGui.SameLine();
        UiSharedService.TextWrapped("I understand that by exporting my character data into a file and sending it to other people I am giving away my current character appearance irrevocably. People I am sharing my data with have the ability to share it with other people without limitations.");

        if (_readExport)
        {
            ImGui.Indent();

            ImGui.InputTextWithHint("Export Descriptor", "This description will be shown on loading the data", ref _exportDescription, 255);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Export Character as MCDF"))
            {
                string defaultFileName = string.IsNullOrEmpty(_exportDescription)
                    ? "export.mcdf"
                    : SanitizeFileName(_exportDescription, "export") + ".mcdf";
                _uiSharedService.FileDialogManager.SaveFileDialog("Export Character to file", ".mcdf", defaultFileName, ".mcdf", (success, path) =>
                {
                    if (!success) return;

                    _configService.Current.LastSavedCharaDataLocation = Path.GetDirectoryName(path) ?? string.Empty;
                    _configService.Save();

                    _charaDataManager.SaveMareCharaFile(_exportDescription, path);
                    _exportDescription = string.Empty;
                }, Directory.Exists(_configService.Current.LastSavedCharaDataLocation) ? _configService.Current.LastSavedCharaDataLocation : null);
            }
            UiSharedService.ColorTextWrapped("Note: For best results make sure you have everything you want to be shared as well as the correct character appearance" +
                " equipped and redraw your character before exporting.", UiSharedService.AccentColor);

            ImGui.Unindent();
        }
    }

    private void DrawMcdfShare()
    {
        if (!_mcdfShareInitialized && !_mcdfShareManager.IsBusy)
        {
            _mcdfShareInitialized = true;
            _ = _mcdfShareManager.RefreshAsync(CancellationToken.None);
        }

        if (_mcdfShareManager.IsBusy)
        {
            UiSharedService.ColorTextWrapped("Traitement en cours...", ImGuiColors.DalamudYellow);
        }

        if (!string.IsNullOrEmpty(_mcdfShareManager.LastError))
        {
            UiSharedService.ColorTextWrapped(_mcdfShareManager.LastError!, ImGuiColors.DalamudRed);
        }
        else if (!string.IsNullOrEmpty(_mcdfShareManager.LastSuccess))
        {
            UiSharedService.ColorTextWrapped(_mcdfShareManager.LastSuccess!, ImGuiColors.HealerGreen);
        }

        if (ImGui.Button("Actualiser les partages"))
        {
            _ = _mcdfShareManager.RefreshAsync(CancellationToken.None);
        }

        ImGui.Separator();
        _uiSharedService.BigText("Créer un partage MCDF");

        ImGui.InputTextWithHint("##mcdfShareDescription", "Description", ref _mcdfShareDescription, 128);
        ImGui.InputInt("Expiration (jours, 0 = jamais)", ref _mcdfShareExpireDays);

        DrawMcdfShareIndividualDropdown();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        if (ImGui.InputTextWithHint("##mcdfShareUidInput", "UID ou vanity", ref _mcdfShareIndividualInput, 32))
        {
            _mcdfShareIndividualDropdownSelection = string.Empty;
        }
        ImGui.SameLine();
        var normalizedUid = NormalizeUidCandidate(_mcdfShareIndividualInput);
        using (ImRaii.Disabled(string.IsNullOrEmpty(normalizedUid)
            || _mcdfShareAllowedIndividuals.Any(p => string.Equals(p, normalizedUid, StringComparison.OrdinalIgnoreCase))))
        {
            if (ImGui.SmallButton("Ajouter"))
            {
                _mcdfShareAllowedIndividuals.Add(normalizedUid);
                _mcdfShareIndividualInput = string.Empty;
                _mcdfShareIndividualDropdownSelection = string.Empty;
            }
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("UID synchronisé à ajouter");
        _uiSharedService.DrawHelpText("Choisissez un pair synchronisé dans la liste ou saisissez un UID. Les utilisateurs listés pourront récupérer ce partage MCDF.");

        foreach (var uid in _mcdfShareAllowedIndividuals.ToArray())
        {
            using (ImRaii.PushId("mcdfShareUid" + uid))
            {
                ImGui.BulletText(FormatPairLabel(uid));
                ImGui.SameLine();
                if (ImGui.SmallButton("Retirer"))
                {
                    _mcdfShareAllowedIndividuals.Remove(uid);
                }
            }
        }

        DrawMcdfShareSyncshellDropdown();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        if (ImGui.InputTextWithHint("##mcdfShareSyncshellInput", "GID ou alias", ref _mcdfShareSyncshellInput, 32))
        {
            _mcdfShareSyncshellDropdownSelection = string.Empty;
        }
        ImGui.SameLine();
        var normalizedSyncshell = NormalizeSyncshellCandidate(_mcdfShareSyncshellInput);
        using (ImRaii.Disabled(string.IsNullOrEmpty(normalizedSyncshell)
            || _mcdfShareAllowedSyncshells.Any(p => string.Equals(p, normalizedSyncshell, StringComparison.OrdinalIgnoreCase))))
        {
            if (ImGui.SmallButton("Ajouter"))
            {
                _mcdfShareAllowedSyncshells.Add(normalizedSyncshell);
                _mcdfShareSyncshellInput = string.Empty;
                _mcdfShareSyncshellDropdownSelection = string.Empty;
            }
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Syncshell à ajouter");
        _uiSharedService.DrawHelpText("Sélectionnez une syncshell synchronisée ou saisissez un identifiant. Les syncshells listées auront accès au partage.");

        foreach (var shell in _mcdfShareAllowedSyncshells.ToArray())
        {
            using (ImRaii.PushId("mcdfShareShell" + shell))
            {
                ImGui.BulletText(FormatSyncshellLabel(shell));
                ImGui.SameLine();
                if (ImGui.SmallButton("Retirer"))
                {
                    _mcdfShareAllowedSyncshells.Remove(shell);
                }
            }
        }

        using (ImRaii.Disabled(_mcdfShareManager.IsBusy))
        {
            if (ImGui.Button("Créer"))
            {
                DateTime? expiresAt = _mcdfShareExpireDays <= 0 ? null : DateTime.UtcNow.AddDays(_mcdfShareExpireDays);
                _ = _mcdfShareManager.CreateShareAsync(_mcdfShareDescription, _mcdfShareAllowedIndividuals.ToList(), _mcdfShareAllowedSyncshells.ToList(), expiresAt, CancellationToken.None);
                _mcdfShareDescription = string.Empty;
                _mcdfShareAllowedIndividuals.Clear();
                _mcdfShareAllowedSyncshells.Clear();
                _mcdfShareIndividualInput = string.Empty;
                _mcdfShareIndividualDropdownSelection = string.Empty;
                _mcdfShareSyncshellInput = string.Empty;
                _mcdfShareSyncshellDropdownSelection = string.Empty;
                _mcdfShareExpireDays = 0;
            }
        }

        ImGui.Separator();
        _uiSharedService.BigText("Mes partages : ");

        if (_mcdfShareManager.OwnShares.Count == 0)
        {
            ImGui.TextDisabled("Aucun partage MCDF créé.");
        }
        else if (ImGui.BeginTable("mcdf-own-shares", 6, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableSetupColumn("Description");
            ImGui.TableSetupColumn("Créé le");
            ImGui.TableSetupColumn("Expire");
            ImGui.TableSetupColumn("Téléchargements");
            ImGui.TableSetupColumn("Accès");
            var style = ImGui.GetStyle();
            float BtnWidth(string label) => ImGui.CalcTextSize(label).X + style.FramePadding.X * 2f;
            float ownActionsWidth = BtnWidth("Appliquer en GPose") + style.ItemSpacing.X + BtnWidth("Enregistrer") + style.ItemSpacing.X + BtnWidth("Supprimer") + 2f; // small margin
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, ownActionsWidth);
            ImGui.TableHeadersRow();

            foreach (var entry in _mcdfShareManager.OwnShares)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(string.IsNullOrEmpty(entry.Description) ? entry.Id.ToString("D", CultureInfo.InvariantCulture) : entry.Description);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.ExpiresAtUtc.HasValue ? entry.ExpiresAtUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "Jamais");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.DownloadCount.ToString(CultureInfo.CurrentCulture));

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"UID : {entry.AllowedIndividuals.Count}, Syncshells : {entry.AllowedSyncshells.Count}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    if (entry.AllowedIndividuals.Count > 0)
                    {
                        ImGui.TextUnformatted("UID autorisés:");
                        foreach (var uid in entry.AllowedIndividuals)
                            ImGui.BulletText(FormatUidWithName(uid));
                    }
                    else
                    {
                        ImGui.TextDisabled("Aucun UID autorisé");
                    }
                    ImGui.Separator();
                    if (entry.AllowedSyncshells.Count > 0)
                    {
                        ImGui.TextUnformatted("Syncshells autorisées:");
                        foreach (var gid in entry.AllowedSyncshells)
                            ImGui.BulletText(FormatSyncshellLabel(gid));
                    }
                    else
                    {
                        ImGui.TextDisabled("Aucune syncshell autorisée");
                    }
                    ImGui.EndTooltip();
                }

                ImGui.TableNextColumn();
                using (ImRaii.PushId("ownShare" + entry.Id))
                {
                    if (ImGui.SmallButton("Appliquer en GPose"))
                    {
                        _ = _mcdfShareManager.ApplyShareAsync(entry.Id, CancellationToken.None);
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Enregistrer"))
                    {
                        var baseName = SanitizeFileName(entry.Description, entry.Id.ToString("D", CultureInfo.InvariantCulture));
                        var defaultName = baseName + ".mcdf";
                        _fileDialogManager.SaveFileDialog("Enregistrer le partage MCDF", ".mcdf", defaultName, ".mcdf", (success, path) =>
                        {
                            if (!success || string.IsNullOrEmpty(path)) return;
                            _ = _mcdfShareManager.ExportShareAsync(entry.Id, path, CancellationToken.None);
                        });
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Supprimer"))
                    {
                        _ = _mcdfShareManager.DeleteShareAsync(entry.Id);
                    }
                }
            }

            ImGui.EndTable();
        }

        ImGui.Separator();
        _uiSharedService.BigText("Partagés avec moi : ");

        if (_mcdfShareManager.SharedShares.Count == 0)
        {
            ImGui.TextDisabled("Aucun partage MCDF reçu.");
        }
        else if (ImGui.BeginTable("mcdf-shared-shares", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableSetupColumn("Description");
            ImGui.TableSetupColumn("Propriétaire");
            ImGui.TableSetupColumn("Expire");
            ImGui.TableSetupColumn("Téléchargements");
            var style2 = ImGui.GetStyle();
            float BtnWidth2(string label) => ImGui.CalcTextSize(label).X + style2.FramePadding.X * 2f;
            float sharedActionsWidth = BtnWidth2("Appliquer") + style2.ItemSpacing.X + BtnWidth2("Enregistrer") + 2f;
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, sharedActionsWidth);
            ImGui.TableHeadersRow();

            foreach (var entry in _mcdfShareManager.SharedShares)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(string.IsNullOrEmpty(entry.Description) ? entry.Id.ToString("D", CultureInfo.InvariantCulture) : entry.Description);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(string.IsNullOrEmpty(entry.OwnerAlias) ? entry.OwnerUid : entry.OwnerAlias);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted($"UID propriétaire: {entry.OwnerUid}");
                    if (!string.IsNullOrEmpty(entry.OwnerAlias))
                    {
                        ImGui.Separator();
                        ImGui.TextUnformatted($"Alias: {entry.OwnerAlias}");
                    }
                    ImGui.EndTooltip();
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.ExpiresAtUtc.HasValue ? entry.ExpiresAtUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "Jamais");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.DownloadCount.ToString(CultureInfo.CurrentCulture));

                ImGui.TableNextColumn();
                using (ImRaii.PushId("sharedShare" + entry.Id))
                {
                    if (ImGui.SmallButton("Appliquer"))
                    {
                        _ = _mcdfShareManager.ApplyShareAsync(entry.Id, CancellationToken.None);
                    }
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Enregistrer"))
                    {
                        var baseName = SanitizeFileName(entry.Description, entry.Id.ToString("D", CultureInfo.InvariantCulture));
                        var defaultName = baseName + ".mcdf";
                        _fileDialogManager.SaveFileDialog("Enregistrer le partage MCDF", ".mcdf", defaultName, ".mcdf", (success, path) =>
                        {
                            if (!success || string.IsNullOrEmpty(path)) return;
                            _ = _mcdfShareManager.ExportShareAsync(entry.Id, path, CancellationToken.None);
                        });
                    }
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawMcdfShareIndividualDropdown()
    {
        ImGui.SetNextItemWidth(220f);
        var previewSource = string.IsNullOrEmpty(_mcdfShareIndividualDropdownSelection)
            ? _mcdfShareIndividualInput
            : _mcdfShareIndividualDropdownSelection;
        var previewLabel = string.IsNullOrEmpty(previewSource)
            ? "Sélectionner un pair synchronisé..."
            : FormatPairLabel(previewSource);

        using var combo = ImRaii.Combo("##mcdfShareUidDropdown", previewLabel, ImGuiComboFlags.None);
        if (!combo)
        {
            return;
        }

        foreach (var pair in _pairManager.DirectPairs
            .OrderBy(p => p.GetNoteOrName() ?? p.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase))
        {
            var normalized = pair.UserData.UID;
            var display = FormatPairLabel(normalized);
            bool selected = string.Equals(normalized, _mcdfShareIndividualDropdownSelection, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(display, selected))
            {
                _mcdfShareIndividualDropdownSelection = normalized;
                _mcdfShareIndividualInput = normalized;
            }
        }
    }

    private void DrawMcdfShareSyncshellDropdown()
    {
        ImGui.SetNextItemWidth(220f);
        var previewSource = string.IsNullOrEmpty(_mcdfShareSyncshellDropdownSelection)
            ? _mcdfShareSyncshellInput
            : _mcdfShareSyncshellDropdownSelection;
        var previewLabel = string.IsNullOrEmpty(previewSource)
            ? "Sélectionner une syncshell..."
            : FormatSyncshellLabel(previewSource);

        using var combo = ImRaii.Combo("##mcdfShareSyncshellDropdown", previewLabel, ImGuiComboFlags.None);
        if (!combo)
        {
            return;
        }

        foreach (var group in _pairManager.Groups.Values
            .OrderBy(g => _serverConfigurationManager.GetNoteForGid(g.GID) ?? g.GroupAliasOrGID, StringComparer.OrdinalIgnoreCase))
        {
            var gid = group.GID;
            var display = FormatSyncshellLabel(gid);
            bool selected = string.Equals(gid, _mcdfShareSyncshellDropdownSelection, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(display, selected))
            {
                _mcdfShareSyncshellDropdownSelection = gid;
                _mcdfShareSyncshellInput = gid;
            }
        }
    }

    private string NormalizeUidCandidate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var trimmed = candidate.Trim();

        foreach (var pair in _pairManager.DirectPairs)
        {
            var alias = pair.UserData.Alias;
            var aliasOrUid = pair.UserData.AliasOrUID;
            var note = pair.GetNoteOrName();

            if (string.Equals(pair.UserData.UID, trimmed, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(alias) && string.Equals(alias, trimmed, StringComparison.OrdinalIgnoreCase))
                || string.Equals(aliasOrUid, trimmed, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(note) && string.Equals(note, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return pair.UserData.UID;
            }
        }

        return trimmed;
    }

    private string NormalizeSyncshellCandidate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var trimmed = candidate.Trim();

        foreach (var group in _pairManager.Groups.Values)
        {
            var alias = group.GroupAlias;
            var aliasOrGid = group.GroupAliasOrGID;
            var note = _serverConfigurationManager.GetNoteForGid(group.GID);

            if (string.Equals(group.GID, trimmed, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(alias) && string.Equals(alias, trimmed, StringComparison.OrdinalIgnoreCase))
                || string.Equals(aliasOrGid, trimmed, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(note) && string.Equals(note, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return group.GID;
            }
        }

        return trimmed;
    }

    private string FormatUidWithName(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return string.Empty;
        var note = _serverConfigurationManager.GetNoteForUid(uid);
        if (!string.IsNullOrEmpty(note)) return $"{uid} ({note})";
        return uid;
    }

    private string FormatPairLabel(string candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return string.Empty;
        }

        foreach (var pair in _pairManager.DirectPairs)
        {
            var alias = pair.UserData.Alias;
            var aliasOrUid = pair.UserData.AliasOrUID;
            var note = pair.GetNoteOrName();

            if (string.Equals(pair.UserData.UID, candidate, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(alias) && string.Equals(alias, candidate, StringComparison.OrdinalIgnoreCase))
                || string.Equals(aliasOrUid, candidate, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(note) && string.Equals(note, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return string.IsNullOrEmpty(note) ? aliasOrUid : $"{note} ({aliasOrUid})";
            }
        }

        return candidate;
    }

    private string FormatSyncshellLabel(string candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return string.Empty;
        }

        foreach (var group in _pairManager.Groups.Values)
        {
            var alias = group.GroupAlias;
            var aliasOrGid = group.GroupAliasOrGID;
            var note = _serverConfigurationManager.GetNoteForGid(group.GID);

            if (string.Equals(group.GID, candidate, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(alias) && string.Equals(alias, candidate, StringComparison.OrdinalIgnoreCase))
                || string.Equals(aliasOrGid, candidate, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(note) && string.Equals(note, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return string.IsNullOrEmpty(note) ? aliasOrGid : $"{note} ({aliasOrGid})";
            }
        }

        return candidate;
    }

    private void DrawMetaInfoData(string selectedGposeActor, bool hasValidGposeTarget, CharaDataMetaInfoExtendedDto data, bool canOpen = false)
    {
        ImGuiHelpers.ScaledDummy(5);
        using var entryId = ImRaii.PushId(data.FullId);

        var startPos = ImGui.GetCursorPosX();
        var maxPos = ImGui.GetWindowContentRegionMax().X;
        var availableWidth = maxPos - startPos;
        UiSharedService.DrawGrouped(() =>
        {
            ImGui.AlignTextToFramePadding();
            DrawAddOrRemoveFavorite(data);

            ImGui.SameLine();
            var favPos = ImGui.GetCursorPosX();
            ImGui.AlignTextToFramePadding();
            UiSharedService.ColorText(data.FullId, UiSharedService.GetBoolColor(data.CanBeDownloaded));
            if (!data.CanBeDownloaded)
            {
                UiSharedService.AttachToolTip("This data is incomplete on the server and cannot be downloaded. Contact the owner so they can fix it. If you are the owner, review the data in the Online Data tab.");
            }

            var offsetFromRight = availableWidth - _uiSharedService.GetIconData(FontAwesomeIcon.Calendar).X - _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ArrowRight).X
                - _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus).X - ImGui.GetStyle().ItemSpacing.X * 2;

            ImGui.SameLine();
            ImGui.SetCursorPosX(offsetFromRight);
            _uiSharedService.IconText(FontAwesomeIcon.Calendar);
            UiSharedService.AttachToolTip($"Last Update: {data.UpdatedDate}");

            ImGui.SameLine();
            GposeMetaInfoAction((meta) =>
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.ArrowRight) && meta != null)
                {
                    _ = _charaDataManager.ApplyCharaDataToGposeTarget(meta);
                }
            }, $"Apply Character data to {CharaName(selectedGposeActor)}", data, hasValidGposeTarget, false);
            ImGui.SameLine();
            GposeMetaInfoAction((meta) =>
            {
                if (_uiSharedService.IconButton(FontAwesomeIcon.Plus) && meta != null)
                {
                    _ = _charaDataManager.SpawnAndApplyData(meta);
                }
            }, "Spawn and Apply Character data", data, hasValidGposeTarget, true);

            using var indent = ImRaii.PushIndent(favPos - startPos);

            if (canOpen)
            {
                using (ImRaii.Disabled(_isHandlingSelf))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Edit, "Open in Online Data Editor"))
                    {
                        SelectedDtoId = data.Id;
                        _openMcdOnlineOnNextRun = true;
                    }
                }
                if (_isHandlingSelf)
                {
                    UiSharedService.AttachToolTip("Cannot use Online Data while having Character Data applied to self.");
                }
            }

            if (string.IsNullOrEmpty(data.Description))
            {
                UiSharedService.ColorTextWrapped("No description set", ImGuiColors.DalamudGrey, availableWidth);
            }
            else
            {
                UiSharedService.TextWrapped(data.Description, availableWidth);
            }

            DrawPoseData(data, selectedGposeActor, hasValidGposeTarget);
        });
    }


    private void DrawPoseData(CharaDataMetaInfoExtendedDto? metaInfo, string actor, bool hasValidGposeTarget)
    {
        if (metaInfo == null || !metaInfo.HasPoses) return;

        bool isInGpose = _uiSharedService.IsInGpose;
        var start = ImGui.GetCursorPosX();
        foreach (var item in metaInfo.PoseExtended)
        {
            if (!item.HasPoseData) continue;

            float DrawIcon(float s)
            {
                ImGui.SetCursorPosX(s);
                var posX = ImGui.GetCursorPosX();
                _uiSharedService.IconText(item.HasWorldData ? FontAwesomeIcon.Circle : FontAwesomeIcon.Running);
                if (item.HasWorldData)
                {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(posX);
                    using var col = ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.WindowBg));
                    _uiSharedService.IconText(FontAwesomeIcon.Running);
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(posX);
                    _uiSharedService.IconText(FontAwesomeIcon.Running);
                }
                ImGui.SameLine();
                return ImGui.GetCursorPosX();
            }

            string tooltip = string.IsNullOrEmpty(item.Description) ? "No description set" : "Pose Description: " + item.Description;
            if (!isInGpose)
            {
                start = DrawIcon(start);
                UiSharedService.AttachToolTip(tooltip + UiSharedService.TooltipSeparator + (item.HasWorldData ? GetWorldDataTooltipText(item) + UiSharedService.TooltipSeparator + "Click to show on Map" : string.Empty));
                if (item.HasWorldData && ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    _dalamudUtilService.SetMarkerAndOpenMap(item.Position, item.Map);
                }
            }
            else
            {
                tooltip += UiSharedService.TooltipSeparator + $"Left Click: Apply this pose to {CharaName(actor)}";
                if (item.HasWorldData) tooltip += Environment.NewLine + $"CTRL+Right Click: Apply world position to {CharaName(actor)}."
                        + UiSharedService.TooltipSeparator + "!!! CAUTION: Applying world position will likely yeet this actor into nirvana. Use at your own risk !!!";
                start = GposePoseAction(currentStart =>
                {
                    var newStart = DrawIcon(currentStart);
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                    {
                        _ = _charaDataManager.ApplyPoseData(item, actor);
                    }
                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && UiSharedService.CtrlPressed())
                    {
                        _ = _charaDataManager.ApplyWorldDataToTarget(item, actor);
                    }

                    return newStart;
                }, tooltip, hasValidGposeTarget, start);
                ImGui.SameLine();
            }
        }
        if (metaInfo.PoseExtended.Any()) ImGui.NewLine();
    }

    private void DrawSettings()
    {
        ImGuiHelpers.ScaledDummy(5);
        _uiSharedService.BigText("Settings");
        ImGuiHelpers.ScaledDummy(5);
        bool openInGpose = _configService.Current.OpenMareHubOnGposeStart;
        if (ImGui.Checkbox("Open Character Data Hub when GPose loads", ref openInGpose))
        {
            _configService.Current.OpenMareHubOnGposeStart = openInGpose;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("This will automatically open the import menu when loading into Gpose. If unchecked you can open the menu manually with /sync gpose");
        bool downloadDataOnConnection = _configService.Current.DownloadMcdDataOnConnection;
        if (ImGui.Checkbox("Download Online Character Data on connecting", ref downloadDataOnConnection))
        {
            _configService.Current.DownloadMcdDataOnConnection = downloadDataOnConnection;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("This will automatically download Online Character Data data (Your Own and Shared with You) once a connection is established to the server.");

        bool showHelpTexts = _configService.Current.ShowHelpTexts;
        if (ImGui.Checkbox("Show \"What is this? (Explanation / Help)\" foldouts", ref showHelpTexts))
        {
            _configService.Current.ShowHelpTexts = showHelpTexts;
            _configService.Save();
        }

        ImGui.Checkbox("Abbreviate Chara Names", ref _abbreviateCharaName);
        _uiSharedService.DrawHelpText("This setting will abbreviate displayed names. This setting is not persistent and will reset between restarts.");

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Last Export Folder");
        ImGui.SameLine(300);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(string.IsNullOrEmpty(_configService.Current.LastSavedCharaDataLocation) ? "Not set" : _configService.Current.LastSavedCharaDataLocation);
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Ban, "Clear Last Export Folder"))
        {
            _configService.Current.LastSavedCharaDataLocation = string.Empty;
            _configService.Save();
        }
        _uiSharedService.DrawHelpText("Use this if the Load or Save MCDF file dialog does not open");
    }

    private void DrawHelpFoldout(string text)
    {
        if (_configService.Current.ShowHelpTexts)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawTree("What is this? (Explanation / Help)", () =>
            {
                UiSharedService.TextWrapped(text);
            });
        }
    }

    private void DisableDisabled(Action drawAction)
    {
        if (_disableUI) ImGui.EndDisabled();
        drawAction();
        if (_disableUI) ImGui.BeginDisabled();
    }

    private CancellationTokenSource EnsureFreshCts(ref CancellationTokenSource? cts)
    {
        CancelAndDispose(ref cts);
        cts = new CancellationTokenSource();
        return cts;
    }

    private void CancelAndDispose(ref CancellationTokenSource? cts)
    {
        if (cts == null) return;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException ex)
        {
            _logger.LogTrace(ex, "Attempted to cancel CharaDataHubUi token after disposal");
        }

        cts.Dispose();
        cts = null;
    }
}