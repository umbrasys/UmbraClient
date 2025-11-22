using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using UmbraSync.API.Data.Enum;
using UmbraSync.API.Data.Extensions;
using UmbraSync.API.Dto.Group;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.AutoDetect;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.Notification;
using UmbraSync.WebAPI;
using UmbraSync.Localization;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UmbraSync.MareConfiguration.Models;

namespace UmbraSync.UI;

public class SyncshellAdminUI : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly bool _isModerator = false;
    private readonly bool _isOwner = false;
    private readonly List<string> _oneTimeInvites = [];
    private readonly PairManager _pairManager;
    private readonly UiSharedService _uiSharedService;
    private readonly SyncshellDiscoveryService _syncshellDiscoveryService;
    private readonly NotificationTracker _notificationTracker;
    private List<BannedGroupUserDto> _bannedUsers = [];
    private int _multiInvites;
    private string _newPassword;
    private bool _pwChangeSuccess;
    private Task<int>? _pruneTestTask;
    private Task<int>? _pruneTask;
    private int _pruneDays = 14;
    private bool _autoDetectStateInitialized;
    private bool _autoDetectStateLoading;
    private bool _autoDetectToggleInFlight;
    private bool _autoDetectVisible;
    private bool _autoDetectPasswordDisabled;
    private string? _autoDetectMessage;
    
    private bool _autoDetectDesiredVisibility;
    private int _adDurationHours = 2;
    private bool _adRecurring = false;
    private readonly bool[] _adWeekdays = new bool[7];
    private int _adStartHour = 21;
    private int _adStartMinute = 0;
    private int _adEndHour = 23;
    private int _adEndMinute = 0;
    private const string AutoDetectTimeZone = "Europe/Paris";

    public SyncshellAdminUI(ILogger<SyncshellAdminUI> logger, MareMediator mediator, ApiController apiController,
        UiSharedService uiSharedService, PairManager pairManager, SyncshellDiscoveryService syncshellDiscoveryService,
        GroupFullInfoDto groupFullInfo, PerformanceCollectorService performanceCollectorService, NotificationTracker notificationTracker)
        : base(logger, mediator, string.Format(CultureInfo.CurrentCulture, Loc.Get("SyncshellAdmin.WindowTitle"), groupFullInfo.GroupAliasOrGID), performanceCollectorService)
    {
        GroupFullInfo = groupFullInfo;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _syncshellDiscoveryService = syncshellDiscoveryService;
        _notificationTracker = notificationTracker;
        _isOwner = string.Equals(GroupFullInfo.OwnerUID, _apiController.UID, StringComparison.Ordinal);
        _isModerator = GroupFullInfo.GroupUserInfo.IsModerator();
        _newPassword = string.Empty;
        _multiInvites = 30;
        _pwChangeSuccess = true;
        _autoDetectVisible = groupFullInfo.AutoDetectVisible;
        _autoDetectDesiredVisibility = _autoDetectVisible;
        _autoDetectPasswordDisabled = groupFullInfo.PasswordTemporarilyDisabled;
        Mediator.Subscribe<SyncshellAutoDetectStateChanged>(this, OnSyncshellAutoDetectStateChanged);
        IsOpen = true;
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new(700, 500),
            MaximumSize = new(700, 2000),
        };
    }

    public GroupFullInfoDto GroupFullInfo { get; private set; }

    protected override void DrawInternal()
    {
        if (!_isModerator && !_isOwner) return;

        GroupFullInfo = _pairManager.Groups[GroupFullInfo.Group];
        if (!_autoDetectToggleInFlight && !_autoDetectStateLoading)
        {
            _autoDetectVisible = GroupFullInfo.AutoDetectVisible;
            _autoDetectPasswordDisabled = GroupFullInfo.PasswordTemporarilyDisabled;
        }

        using var id = ImRaii.PushId("syncshell_admin_" + GroupFullInfo.GID);

        using (_uiSharedService.UidFont.Push())
            ImGui.TextUnformatted(string.Format(CultureInfo.CurrentCulture, Loc.Get("SyncshellAdmin.AdminPanelTitle"), GroupFullInfo.GroupAliasOrGID));

        ImGui.Separator();
        var perm = GroupFullInfo.GroupPermissions;

        using var tabColor = ImRaii.PushColor(ImGuiCol.Tab, UiSharedService.AccentColor);
        using var tabHoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, UiSharedService.AccentHoverColor);
        using var tabActiveColor = ImRaii.PushColor(ImGuiCol.TabActive, UiSharedService.AccentActiveColor);
        using var tabbar = ImRaii.TabBar("syncshell_tab_" + GroupFullInfo.GID);

        if (tabbar)
        {
            var inviteTab = ImRaii.TabItem(Loc.Get("SyncshellAdmin.Tab.Invites"));
            if (inviteTab)
            {
                bool isInvitesDisabled = perm.IsDisableInvites();

                if (_uiSharedService.IconTextButton(isInvitesDisabled ? FontAwesomeIcon.Unlock : FontAwesomeIcon.Lock,
                    isInvitesDisabled ? Loc.Get("SyncshellAdmin.Invites.Unlock") : Loc.Get("SyncshellAdmin.Invites.Lock")))
                {
                    perm.SetDisableInvites(!isInvitesDisabled);
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGuiHelpers.ScaledDummy(2f);

                UiSharedService.TextWrapped(Loc.Get("SyncshellAdmin.Invites.Description"));
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Envelope, Loc.Get("SyncshellAdmin.Invites.Single")))
                {
                    ImGui.SetClipboardText(_apiController.GroupCreateTempInvite(new(GroupFullInfo.Group), 1).Result.FirstOrDefault() ?? string.Empty);
                }
                UiSharedService.AttachToolTip(Loc.Get("SyncshellAdmin.Invites.SingleTooltip"));
                ImGui.InputInt("##amountofinvites", ref _multiInvites);
                ImGui.SameLine();
                using (ImRaii.Disabled(_multiInvites <= 1 || _multiInvites > 100))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Envelope, string.Format(CultureInfo.CurrentCulture, Loc.Get("SyncshellAdmin.Invites.Multi"), _multiInvites)))
                    {
                        _oneTimeInvites.AddRange(_apiController.GroupCreateTempInvite(new(GroupFullInfo.Group), _multiInvites).Result);
                    }
                }

                if (_oneTimeInvites.Any())
                {
                    var invites = string.Join(Environment.NewLine, _oneTimeInvites);
                    ImGui.InputTextMultiline(Loc.Get("SyncshellAdmin.Invites.GeneratedLabel"), ref invites, 5000, new(0, 0), ImGuiInputTextFlags.ReadOnly);
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Copy, Loc.Get("SyncshellAdmin.Invites.Copy")))
                    {
                        ImGui.SetClipboardText(invites);
                    }
                }
            }
            inviteTab.Dispose();

            var mgmtTab = ImRaii.TabItem(Loc.Get("SyncshellAdmin.Tab.UserManagement"));
            if (mgmtTab)
            {
                var userNode = ImRaii.TreeNode(Loc.Get("SyncshellAdmin.Users.Tree"));
                if (userNode)
                {
                    if (!_pairManager.GroupPairs.TryGetValue(GroupFullInfo, out var pairs))
                    {
                        UiSharedService.ColorTextWrapped(Loc.Get("SyncshellAdmin.Users.Empty"), ImGuiColors.DalamudYellow);
                    }
                    else
                    {
                        using var table = ImRaii.Table("userList#" + GroupFullInfo.Group.GID, 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY);
                        if (table)
                        {
                            ImGui.TableSetupColumn(Loc.Get("SyncshellAdmin.Users.ColAlias"), ImGuiTableColumnFlags.None, 3);
                            ImGui.TableSetupColumn(Loc.Get("SyncshellAdmin.Users.ColOnline"), ImGuiTableColumnFlags.None, 2);
                            ImGui.TableSetupColumn(Loc.Get("SyncshellAdmin.Users.ColFlags"), ImGuiTableColumnFlags.None, 1);
                            ImGui.TableSetupColumn(Loc.Get("SyncshellAdmin.Users.ColActions"), ImGuiTableColumnFlags.None, 2);
                            ImGui.TableHeadersRow();

                            var groupedPairs = new Dictionary<Pair, GroupUserInfo?>(pairs.Select(p => new KeyValuePair<Pair, GroupUserInfo?>(p,
                                p.GroupPair.TryGetValue(GroupFullInfo, out GroupPairFullInfoDto? value) ? value.GroupPairStatusInfo : null)));

                            foreach (var pair in groupedPairs.OrderBy(p =>
                            {
                                if (p.Value == null) return 10;
                                if (p.Value.Value.IsModerator()) return 0;
                                if (p.Value.Value.IsPinned()) return 1;
                                return 10;
                            }).ThenBy(p => p.Key.GetNote() ?? p.Key.UserData.AliasOrUID, StringComparer.OrdinalIgnoreCase))
                            {
                                using var tableId = ImRaii.PushId("userTable_" + pair.Key.UserData.UID);

                                ImGui.TableNextColumn(); // alias/uid/note
                                var note = pair.Key.GetNote();
                                var text = note == null ? pair.Key.UserData.AliasOrUID : note + " (" + pair.Key.UserData.AliasOrUID + ")";
                                ImGui.AlignTextToFramePadding();
                                ImGui.TextUnformatted(text);

                                ImGui.TableNextColumn(); // online/name
                                string onlineText = pair.Key.IsOnline ? Loc.Get("SyncshellAdmin.Users.Online") : Loc.Get("SyncshellAdmin.Users.Offline");
                                string? name = pair.Key.GetNoteOrName();
                                if (!string.IsNullOrEmpty(name))
                                {
                                    onlineText += " (" + name + ")";
                                }
                                var boolcolor = UiSharedService.GetBoolColor(pair.Key.IsOnline);
                                ImGui.AlignTextToFramePadding();
                                UiSharedService.ColorText(onlineText, boolcolor);

                                ImGui.TableNextColumn(); // special flags
                                if (pair.Value != null && (pair.Value.Value.IsModerator() || pair.Value.Value.IsPinned()))
                                {
                                    if (pair.Value.Value.IsModerator())
                                    {
                                        _uiSharedService.IconText(FontAwesomeIcon.UserShield);
                                        UiSharedService.AttachToolTip(Loc.Get("SyncshellAdmin.Users.ModeratorTooltip"));
                                    }
                                    if (pair.Value.Value.IsPinned())
                                    {
                                        _uiSharedService.IconText(FontAwesomeIcon.Thumbtack);
                                        UiSharedService.AttachToolTip(Loc.Get("SyncshellAdmin.Users.PinnedTooltip"));
                                    }
                                }
                                else
                                {
                                    _uiSharedService.IconText(FontAwesomeIcon.None);
                                }

                                ImGui.TableNextColumn(); // actions
                                if (_isOwner)
                                {
                                    if (_uiSharedService.IconButton(FontAwesomeIcon.UserShield))
                                    {
                                        GroupUserInfo userInfo = pair.Value ?? GroupUserInfo.None;

                                        userInfo.SetModerator(!userInfo.IsModerator());

                                        _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(GroupFullInfo.Group, pair.Key.UserData, userInfo));
                                    }
                                    UiSharedService.AttachToolTip(pair.Value != null && pair.Value.Value.IsModerator() ? Loc.Get("SyncshellAdmin.Users.Demod") : Loc.Get("SyncshellAdmin.Users.Mod"));
                                    ImGui.SameLine();
                                }

                                if (_isOwner || (pair.Value == null || (pair.Value != null && !pair.Value.Value.IsModerator())))
                                {
                                    if (_uiSharedService.IconButton(FontAwesomeIcon.Thumbtack))
                                    {
                                        GroupUserInfo userInfo = pair.Value ?? GroupUserInfo.None;

                                        userInfo.SetPinned(!userInfo.IsPinned());

                                        _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(GroupFullInfo.Group, pair.Key.UserData, userInfo));
                                    }
                                    UiSharedService.AttachToolTip(pair.Value != null && pair.Value.Value.IsPinned() ? Loc.Get("SyncshellAdmin.Users.Unpin") : Loc.Get("SyncshellAdmin.Users.Pin"));
                                    ImGui.SameLine();

                                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                                    {
                                        if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                                        {
                                            _ = _apiController.GroupRemoveUser(new GroupPairDto(GroupFullInfo.Group, pair.Key.UserData));
                                        }
                                    }
                                    UiSharedService.AttachToolTip(Loc.Get("SyncshellAdmin.Users.Remove") + UiSharedService.TooltipSeparator + Loc.Get("SyncshellAdmin.Users.RemoveHint"));

                                    ImGui.SameLine();
                                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                                    {
                                        if (_uiSharedService.IconButton(FontAwesomeIcon.Ban))
                                        {
                                            Mediator.Publish(new OpenBanUserPopupMessage(pair.Key, GroupFullInfo));
                                        }
                                    }
                                    UiSharedService.AttachToolTip(Loc.Get("SyncshellAdmin.Users.Ban") + UiSharedService.TooltipSeparator + Loc.Get("SyncshellAdmin.Users.BanHint"));
                                }
                            }
                        }
                    }
                }
                userNode.Dispose();
                var clearNode = ImRaii.TreeNode(Loc.Get("SyncshellAdmin.Cleanup.Tree"));
                if (clearNode)
                {
                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Broom, Loc.Get("SyncshellAdmin.Cleanup.Clear")))
                        {
                            _ = _apiController.GroupClear(new(GroupFullInfo.Group));
                        }
                    }
                    UiSharedService.AttachToolTip(Loc.Get("SyncshellAdmin.Cleanup.ClearTooltip") + UiSharedService.TooltipSeparator + Loc.Get("SyncshellAdmin.Cleanup.CtrlHint"));

                    ImGuiHelpers.ScaledDummy(2f);
                    ImGui.Separator();
                    ImGuiHelpers.ScaledDummy(2f);

                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Unlink, Loc.Get("SyncshellAdmin.Cleanup.CheckInactive")))
                    {
                        _pruneTestTask = _apiController.GroupPrune(new(GroupFullInfo.Group), _pruneDays, execute: false);
                        _pruneTask = null;
                    }
                    UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("SyncshellAdmin.Cleanup.PruneTooltip"), _pruneDays)
                        + Environment.NewLine + Loc.Get("SyncshellAdmin.Cleanup.PruneReview")
                        + UiSharedService.TooltipSeparator + Loc.Get("SyncshellAdmin.Cleanup.PruneNote"));
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(150);
                    _uiSharedService.DrawCombo(Loc.Get("SyncshellAdmin.Cleanup.PruneDaysLabel"), [7, 14, 30, 90], (count) =>
                    {
                        return string.Format(CultureInfo.CurrentCulture, Loc.Get("SyncshellAdmin.Cleanup.PruneDaysEntry"), count);
                    },
                    (selected) =>
                    {
                        _pruneDays = selected;
                        _pruneTestTask = null;
                        _pruneTask = null;
                    },
                    _pruneDays);

                    if (_pruneTestTask != null)
                    {
                        if (!_pruneTestTask.IsCompleted)
                        {
                            UiSharedService.ColorTextWrapped(Loc.Get("SyncshellAdmin.Cleanup.PruneCalculating"), ImGuiColors.DalamudYellow);
                        }
                        else
                        {
                            ImGui.AlignTextToFramePadding();
                            UiSharedService.TextWrapped(string.Format(CultureInfo.CurrentCulture, Loc.Get("SyncshellAdmin.Cleanup.PruneFound"), _pruneTestTask.Result, _pruneDays));
                            if (_pruneTestTask.Result > 0)
                            {
                                using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                                {
                                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Broom, Loc.Get("SyncshellAdmin.Cleanup.PruneExecute")))
                                    {
                                        _pruneTask = _apiController.GroupPrune(new(GroupFullInfo.Group), _pruneDays, execute: true);
                                        _pruneTestTask = null;
                                    }
                                }
                                UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("SyncshellAdmin.Cleanup.PruneExecuteTooltip"), _pruneTestTask?.Result ?? 0)
                                    + UiSharedService.TooltipSeparator + Loc.Get("SyncshellAdmin.Cleanup.CtrlHint"));
                            }
                        }
                    }
                    if (_pruneTask != null)
                    {
                        if (!_pruneTask.IsCompleted)
                        {
                            UiSharedService.ColorTextWrapped(Loc.Get("SyncshellAdmin.Cleanup.Pruning"), ImGuiColors.DalamudYellow);
                        }
                        else
                        {
                            UiSharedService.TextWrapped(string.Format(CultureInfo.CurrentCulture, Loc.Get("SyncshellAdmin.Cleanup.PrunedResult"), _pruneTask.Result));
                        }
                    }
                }
                clearNode.Dispose();

                var banNode = ImRaii.TreeNode(Loc.Get("SyncshellAdmin.Bans.Tree"));
                if (banNode)
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Retweet, Loc.Get("SyncshellAdmin.Bans.Refresh")))
                    {
                        _bannedUsers = _apiController.GroupGetBannedUsers(new GroupDto(GroupFullInfo.Group)).Result;
                    }

                    if (ImGui.BeginTable("bannedusertable" + GroupFullInfo.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
                    {
                        ImGui.TableSetupColumn(Loc.Get("SyncshellAdmin.Bans.ColUid"), ImGuiTableColumnFlags.None, 1);
                        ImGui.TableSetupColumn(Loc.Get("SyncshellAdmin.Bans.ColAlias"), ImGuiTableColumnFlags.None, 1);
                        ImGui.TableSetupColumn(Loc.Get("SyncshellAdmin.Bans.ColBy"), ImGuiTableColumnFlags.None, 1);
                        ImGui.TableSetupColumn(Loc.Get("SyncshellAdmin.Bans.ColDate"), ImGuiTableColumnFlags.None, 2);
                        ImGui.TableSetupColumn(Loc.Get("SyncshellAdmin.Bans.ColReason"), ImGuiTableColumnFlags.None, 3);
                        ImGui.TableSetupColumn(Loc.Get("SyncshellAdmin.Bans.ColActions"), ImGuiTableColumnFlags.None, 1);

                        ImGui.TableHeadersRow();

                        foreach (var bannedUser in _bannedUsers.ToList())
                        {
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(bannedUser.UID);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(bannedUser.UserAlias ?? string.Empty);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(bannedUser.BannedBy);
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(bannedUser.BannedOn.ToLocalTime().ToString(CultureInfo.CurrentCulture));
                            ImGui.TableNextColumn();
                            UiSharedService.TextWrapped(bannedUser.Reason);
                            ImGui.TableNextColumn();
                            using var pushId = ImRaii.PushId(bannedUser.UID);
                            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, Loc.Get("SyncshellAdmin.Bans.Unban")))
                            {
                                _ = Task.Run(async () => await _apiController.GroupUnbanUser(bannedUser).ConfigureAwait(false));
                                _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
                            }
                        }

                        ImGui.EndTable();
                    }
                }
                banNode.Dispose();
            }
            mgmtTab.Dispose();

            var discoveryTab = ImRaii.TabItem(Loc.Get("SyncshellAdmin.Tab.AutoDetect"));
            if (discoveryTab)
            {
                DrawAutoDetectTab();
            }
            discoveryTab.Dispose();

            var permissionTab = ImRaii.TabItem(Loc.Get("SyncshellAdmin.Tab.Permissions"));
            if (permissionTab)
            {
                bool isDisableAnimations = perm.IsDisableAnimations();
                bool isDisableSounds = perm.IsDisableSounds();
                bool isDisableVfx = perm.IsDisableVFX();

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Loc.Get("SyncshellAdmin.Permissions.Sound"));
                _uiSharedService.BooleanToColoredIcon(!isDisableSounds);
                ImGui.SameLine(230);
                if (_uiSharedService.IconTextButton(isDisableSounds ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp,
                    isDisableSounds ? Loc.Get("SyncshellAdmin.Permissions.EnableSound") : Loc.Get("SyncshellAdmin.Permissions.DisableSound")))
                {
                    perm.SetDisableSounds(!perm.IsDisableSounds());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Loc.Get("SyncshellAdmin.Permissions.Animation"));
                _uiSharedService.BooleanToColoredIcon(!isDisableAnimations);
                ImGui.SameLine(230);
                if (_uiSharedService.IconTextButton(isDisableAnimations ? FontAwesomeIcon.WindowClose : FontAwesomeIcon.Running,
                    isDisableAnimations ? Loc.Get("SyncshellAdmin.Permissions.EnableAnimation") : Loc.Get("SyncshellAdmin.Permissions.DisableAnimation")))
                {
                    perm.SetDisableAnimations(!perm.IsDisableAnimations());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(Loc.Get("SyncshellAdmin.Permissions.Vfx"));
                _uiSharedService.BooleanToColoredIcon(!isDisableVfx);
                ImGui.SameLine(230);
                if (_uiSharedService.IconTextButton(isDisableVfx ? FontAwesomeIcon.TimesCircle : FontAwesomeIcon.Sun,
                    isDisableVfx ? Loc.Get("SyncshellAdmin.Permissions.EnableVfx") : Loc.Get("SyncshellAdmin.Permissions.DisableVfx")))
                {
                    perm.SetDisableVFX(!perm.IsDisableVFX());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }
            }
            permissionTab.Dispose();

            if (_isOwner)
            {
                var ownerTab = ImRaii.TabItem(Loc.Get("SyncshellAdmin.Tab.Owner"));
                if (ownerTab)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(Loc.Get("SyncshellAdmin.Owner.NewPassword"));
                    var availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                    var buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Passport, Loc.Get("SyncshellAdmin.Owner.ChangePassword"));
                    var textSize = ImGui.CalcTextSize(Loc.Get("SyncshellAdmin.Owner.NewPassword")).X;
                    var spacing = ImGui.GetStyle().ItemSpacing.X;

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(availableWidth - buttonSize - textSize - spacing * 2);
                    ImGui.InputTextWithHint("##changepw", Loc.Get("SyncshellAdmin.Owner.PasswordPlaceholder"), ref _newPassword, 50);
                    ImGui.SameLine();
                    using (ImRaii.Disabled(_newPassword.Length < 10))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Passport, Loc.Get("SyncshellAdmin.Owner.ChangePassword")))
                        {
                            _pwChangeSuccess = _apiController.GroupChangePassword(new GroupPasswordDto(GroupFullInfo.Group, _newPassword)).Result;
                            _newPassword = string.Empty;
                        }
                    }
                    UiSharedService.AttachToolTip(Loc.Get("SyncshellAdmin.Owner.ChangePasswordTooltip"));

                    if (!_pwChangeSuccess)
                    {
                        UiSharedService.ColorTextWrapped(Loc.Get("SyncshellAdmin.Owner.ChangePasswordFailed"), ImGuiColors.DalamudYellow);
                    }

                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, Loc.Get("SyncshellAdmin.Owner.DeleteSyncshell")) && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                    {
                        IsOpen = false;
                        _ = _apiController.GroupDelete(new(GroupFullInfo.Group));
                    }
                    UiSharedService.AttachToolTip(Loc.Get("SyncshellAdmin.Owner.DeleteTooltip"));
                }
                ownerTab.Dispose();
            }
        }
    }

    private void DrawAutoDetectTab()
    {
        if (!_autoDetectStateInitialized && !_autoDetectStateLoading)
        {
            _autoDetectStateInitialized = true;
            _autoDetectStateLoading = true;
            _ = EnsureAutoDetectStateAsync();
        }

        UiSharedService.TextWrapped(Loc.Get("SyncshellAdmin.AutoDetect.Description"));
        ImGuiHelpers.ScaledDummy(4);

        if (_autoDetectStateLoading)
        {
            ImGui.TextDisabled(Loc.Get("SyncshellAdmin.AutoDetect.Loading"));
        }

        if (!string.IsNullOrEmpty(_autoDetectMessage))
        {
            UiSharedService.ColorTextWrapped(_autoDetectMessage!, ImGuiColors.DalamudYellow);
        }

        using (ImRaii.Disabled(_autoDetectToggleInFlight || _autoDetectStateLoading))
        {
            if (ImGui.Checkbox(Loc.Get("SyncshellAdmin.AutoDetect.CheckboxLabel"), ref _autoDetectDesiredVisibility))
            {
                // Only change local desired state; sending is done via the validate button
            }
        }
        _uiSharedService.DrawHelpText(Loc.Get("SyncshellAdmin.AutoDetect.CheckboxHelp"));

        if (_autoDetectDesiredVisibility)
        {
            ImGuiHelpers.ScaledDummy(4);
            ImGui.TextUnformatted(Loc.Get("SyncshellAdmin.AutoDetect.Options"));
            ImGui.Separator();

            // Recurring toggle first
            ImGui.Checkbox(Loc.Get("SyncshellAdmin.AutoDetect.Recurring"), ref _adRecurring);
            _uiSharedService.DrawHelpText(Loc.Get("SyncshellAdmin.AutoDetect.RecurringHelp"));

            // Duration in hours (only when NOT recurring)
            if (!_adRecurring)
            {
                ImGuiHelpers.ScaledDummy(4);
                int duration = _adDurationHours;
                ImGui.PushItemWidth(120 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt(Loc.Get("SyncshellAdmin.AutoDetect.DurationHours"), ref duration))
                {
                    _adDurationHours = Math.Clamp(duration, 1, 240);
                }
                ImGui.PopItemWidth();
                _uiSharedService.DrawHelpText(Loc.Get("SyncshellAdmin.AutoDetect.DurationHelp"));
            }

            ImGuiHelpers.ScaledDummy(4);
            if (_adRecurring)
            {
                ImGuiHelpers.ScaledDummy(4);
                ImGui.TextUnformatted(Loc.Get("SyncshellAdmin.AutoDetect.Weekdays"));
                string[] daysFr = new[]
                {
                    Loc.Get("SyncshellAdmin.AutoDetect.Weekday.Mon"),
                    Loc.Get("SyncshellAdmin.AutoDetect.Weekday.Tue"),
                    Loc.Get("SyncshellAdmin.AutoDetect.Weekday.Wed"),
                    Loc.Get("SyncshellAdmin.AutoDetect.Weekday.Thu"),
                    Loc.Get("SyncshellAdmin.AutoDetect.Weekday.Fri"),
                    Loc.Get("SyncshellAdmin.AutoDetect.Weekday.Sat"),
                    Loc.Get("SyncshellAdmin.AutoDetect.Weekday.Sun"),
                };
                for (int i = 0; i < 7; i++)
                {
                    ImGui.SameLine(0);
                    bool v = _adWeekdays[i];
                    if (ImGui.Checkbox($"##adwd{i}", ref v)) _adWeekdays[i] = v;
                    ImGui.SameLine();
                    ImGui.TextUnformatted(daysFr[i]);
                    if (i < 6) ImGui.SameLine();
                }
                ImGui.NewLine();
                _uiSharedService.DrawHelpText(Loc.Get("SyncshellAdmin.AutoDetect.WeekdaysHelp"));

                ImGuiHelpers.ScaledDummy(4);
                ImGui.TextUnformatted(Loc.Get("SyncshellAdmin.AutoDetect.TimeRangeLabel"));
                ImGui.PushItemWidth(60 * ImGuiHelpers.GlobalScale);
                ImGui.InputInt(Loc.Get("SyncshellAdmin.AutoDetect.StartHour"), ref _adStartHour); ImGui.SameLine();
                ImGui.InputInt(Loc.Get("SyncshellAdmin.AutoDetect.StartMinute"), ref _adStartMinute);
                _adStartHour = Math.Clamp(_adStartHour, 0, 23);
                _adStartMinute = Math.Clamp(_adStartMinute, 0, 59);
                ImGui.SameLine();
                ImGui.TextUnformatted("→"); ImGui.SameLine();
                ImGui.InputInt(Loc.Get("SyncshellAdmin.AutoDetect.EndHour"), ref _adEndHour); ImGui.SameLine();
                ImGui.InputInt(Loc.Get("SyncshellAdmin.AutoDetect.EndMinute"), ref _adEndMinute);
                _adEndHour = Math.Clamp(_adEndHour, 0, 23);
                _adEndMinute = Math.Clamp(_adEndMinute, 0, 59);
                ImGui.PopItemWidth();
                _uiSharedService.DrawHelpText(Loc.Get("SyncshellAdmin.AutoDetect.TimeRangeHelp"));
            }
        }

        if (_autoDetectPasswordDisabled && _autoDetectVisible)
        {
            UiSharedService.ColorTextWrapped(Loc.Get("SyncshellAdmin.AutoDetect.PasswordDisabled"), ImGuiColors.DalamudYellow);
        }

        ImGuiHelpers.ScaledDummy(6);
        using (ImRaii.Disabled(_autoDetectToggleInFlight || _autoDetectStateLoading))
        {
            if (ImGui.Button(Loc.Get("SyncshellAdmin.AutoDetect.Submit")))
            {
                _ = SubmitAutoDetectAsync();
            }
            ImGui.SameLine();
            if (ImGui.Button(Loc.Get("SyncshellAdmin.AutoDetect.Refresh")))
            {
                _autoDetectStateLoading = true;
                _ = EnsureAutoDetectStateAsync(true);
            }
        }
    }

    private async Task EnsureAutoDetectStateAsync(bool force = false)
    {
        try
        {
            var state = await _syncshellDiscoveryService.GetStateAsync(GroupFullInfo.GID, CancellationToken.None).ConfigureAwait(false);
            if (state != null)
            {
                ApplyAutoDetectState(state.AutoDetectVisible, state.PasswordTemporarilyDisabled, true);
                _autoDetectMessage = null;
            }
            else if (force)
            {
                _autoDetectMessage = Loc.Get("SyncshellAdmin.AutoDetect.StateFetchFailed");
            }
        }
        catch (Exception ex)
        {
            _autoDetectMessage = force ? string.Format(CultureInfo.CurrentCulture, Loc.Get("SyncshellAdmin.AutoDetect.RefreshError"), ex.Message) : _autoDetectMessage;
        }
        finally
        {
            _autoDetectStateLoading = false;
        }
    }

    private async Task SubmitAutoDetectAsync()
    {
        if (_autoDetectToggleInFlight)
        {
            return;
        }

        _autoDetectToggleInFlight = true;
        _autoDetectMessage = null;

        try
        {
            // Duration always used when visible
            int? duration = _autoDetectDesiredVisibility ? _adDurationHours : null;

            // Scheduling fields only if recurring is enabled
            int[]? weekdaysArr = null;
            TimeSpan? start = null;
            TimeSpan? end = null;
            string? tz = null;
            if (_autoDetectDesiredVisibility && _adRecurring)
            {
                List<int> weekdays = new();
                for (int i = 0; i < 7; i++) if (_adWeekdays[i]) weekdays.Add(i);
                weekdaysArr = weekdays.Count > 0 ? weekdays.ToArray() : Array.Empty<int>();
                start = new TimeSpan(_adStartHour, _adStartMinute, 0);
                end = new TimeSpan(_adEndHour, _adEndMinute, 0);
                tz = AutoDetectTimeZone;
            }

            var ok = await _syncshellDiscoveryService.SetVisibilityAsync(
                GroupFullInfo.GID,
                _autoDetectDesiredVisibility,
                duration,
                weekdaysArr,
                start,
                end,
                tz,
                CancellationToken.None).ConfigureAwait(false);

            if (!ok)
            {
                _autoDetectMessage = Loc.Get("SyncshellAdmin.AutoDetect.SubmitFailed");
                return;
            }

            await EnsureAutoDetectStateAsync(true).ConfigureAwait(false);
            _autoDetectMessage = _autoDetectDesiredVisibility
                ? Loc.Get("SyncshellAdmin.AutoDetect.VisibleOn")
                : Loc.Get("SyncshellAdmin.AutoDetect.VisibleOff");

            if (_autoDetectDesiredVisibility)
            {
                PublishSyncshellPublicNotification();
            }
        }
        catch (Exception ex)
        {
            _autoDetectMessage = string.Format(CultureInfo.CurrentCulture, Loc.Get("SyncshellAdmin.AutoDetect.SubmitError"), ex.Message);
        }
        finally
        {
            _autoDetectToggleInFlight = false;
        }
    }

    private void ApplyAutoDetectState(bool visible, bool passwordDisabled, bool fromServer)
    {
        _autoDetectVisible = visible;
        _autoDetectPasswordDisabled = passwordDisabled;
        if (fromServer)
        {
            GroupFullInfo.AutoDetectVisible = visible;
            GroupFullInfo.PasswordTemporarilyDisabled = passwordDisabled;
        }
    }

    private void OnSyncshellAutoDetectStateChanged(SyncshellAutoDetectStateChanged msg)
    {
        if (!string.Equals(msg.Gid, GroupFullInfo.GID, StringComparison.OrdinalIgnoreCase)) return;
        ApplyAutoDetectState(msg.Visible, msg.PasswordTemporarilyDisabled, true);
        _autoDetectMessage = null;
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }

    private void PublishSyncshellPublicNotification()
    {
        try
        {
            var title = string.Format(CultureInfo.CurrentCulture, Loc.Get("SyncshellAdmin.AutoDetect.NotificationTitle"), GroupFullInfo.GroupAliasOrGID);
            var message = Loc.Get("SyncshellAdmin.AutoDetect.NotificationMessage");
            Mediator.Publish(new DualNotificationMessage(title, message, NotificationType.Info, TimeSpan.FromSeconds(4)));
            _notificationTracker.Upsert(NotificationEntry.SyncshellPublic(GroupFullInfo.GID, GroupFullInfo.GroupAliasOrGID));
        }
        catch
        {
            // swallow any notification errors to not break UI flow
        }
    }
}
