using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.AutoDetect;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.Notifications;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MareSynchronos.MareConfiguration.Models;

namespace MareSynchronos.UI.Components.Popup;

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
        : base(logger, mediator, "Syncshell Admin Panel (" + groupFullInfo.GroupAliasOrGID + ")", performanceCollectorService)
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
            ImGui.TextUnformatted(GroupFullInfo.GroupAliasOrGID + " Administrative Panel");

        ImGui.Separator();
        var perm = GroupFullInfo.GroupPermissions;

        using var tabColor = ImRaii.PushColor(ImGuiCol.Tab, UiSharedService.AccentColor);
        using var tabHoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, UiSharedService.AccentHoverColor);
        using var tabActiveColor = ImRaii.PushColor(ImGuiCol.TabActive, UiSharedService.AccentActiveColor);
        using var tabbar = ImRaii.TabBar("syncshell_tab_" + GroupFullInfo.GID);

        if (tabbar)
        {
            var inviteTab = ImRaii.TabItem("Invites");
            if (inviteTab)
            {
                bool isInvitesDisabled = perm.IsDisableInvites();

                if (_uiSharedService.IconTextButton(isInvitesDisabled ? FontAwesomeIcon.Unlock : FontAwesomeIcon.Lock,
                    isInvitesDisabled ? "Unlock Syncshell" : "Lock Syncshell"))
                {
                    perm.SetDisableInvites(!isInvitesDisabled);
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGuiHelpers.ScaledDummy(2f);

                UiSharedService.TextWrapped("One-time invites work as single-use passwords. Use those if you do not want to distribute your Syncshell password.");
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Single one-time invite"))
                {
                    ImGui.SetClipboardText(_apiController.GroupCreateTempInvite(new(GroupFullInfo.Group), 1).Result.FirstOrDefault() ?? string.Empty);
                }
                UiSharedService.AttachToolTip("Creates a single-use password for joining the syncshell which is valid for 24h and copies it to the clipboard.");
                ImGui.InputInt("##amountofinvites", ref _multiInvites);
                ImGui.SameLine();
                using (ImRaii.Disabled(_multiInvites <= 1 || _multiInvites > 100))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Envelope, "Generate " + _multiInvites + " one-time invites"))
                    {
                        _oneTimeInvites.AddRange(_apiController.GroupCreateTempInvite(new(GroupFullInfo.Group), _multiInvites).Result);
                    }
                }

                if (_oneTimeInvites.Any())
                {
                    var invites = string.Join(Environment.NewLine, _oneTimeInvites);
                    ImGui.InputTextMultiline("Generated Multi Invites", ref invites, 5000, new(0, 0), ImGuiInputTextFlags.ReadOnly);
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Copy, "Copy Invites to clipboard"))
                    {
                        ImGui.SetClipboardText(invites);
                    }
                }
            }
            inviteTab.Dispose();

            var mgmtTab = ImRaii.TabItem("User Management");
            if (mgmtTab)
            {
                var userNode = ImRaii.TreeNode("User List & Administration");
                if (userNode)
                {
                    if (!_pairManager.GroupPairs.TryGetValue(GroupFullInfo, out var pairs))
                    {
                        UiSharedService.ColorTextWrapped("No users found in this Syncshell", ImGuiColors.DalamudYellow);
                    }
                    else
                    {
                        using var table = ImRaii.Table("userList#" + GroupFullInfo.Group.GID, 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY);
                        if (table)
                        {
                            ImGui.TableSetupColumn("Alias/UID/Note", ImGuiTableColumnFlags.None, 3);
                            ImGui.TableSetupColumn("Online/Name", ImGuiTableColumnFlags.None, 2);
                            ImGui.TableSetupColumn("Flags", ImGuiTableColumnFlags.None, 1);
                            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 2);
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
                                string onlineText = pair.Key.IsOnline ? "Online" : "Offline";
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
                                        UiSharedService.AttachToolTip("Moderator");
                                    }
                                    if (pair.Value.Value.IsPinned())
                                    {
                                        _uiSharedService.IconText(FontAwesomeIcon.Thumbtack);
                                        UiSharedService.AttachToolTip("Pinned");
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
                                    UiSharedService.AttachToolTip(pair.Value != null && pair.Value.Value.IsModerator() ? "Demod user" : "Mod user");
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
                                    UiSharedService.AttachToolTip(pair.Value != null && pair.Value.Value.IsPinned() ? "Unpin user" : "Pin user");
                                    ImGui.SameLine();

                                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                                    {
                                        if (_uiSharedService.IconButton(FontAwesomeIcon.Trash))
                                        {
                                            _ = _apiController.GroupRemoveUser(new GroupPairDto(GroupFullInfo.Group, pair.Key.UserData));
                                        }
                                    }
                                    UiSharedService.AttachToolTip("Remove user from Syncshell"
                                        + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");

                                    ImGui.SameLine();
                                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                                    {
                                        if (_uiSharedService.IconButton(FontAwesomeIcon.Ban))
                                        {
                                            Mediator.Publish(new OpenBanUserPopupMessage(pair.Key, GroupFullInfo));
                                        }
                                    }
                                    UiSharedService.AttachToolTip("Ban user from Syncshell"
                                        + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");
                                }
                            }
                        }
                    }
                }
                userNode.Dispose();
                var clearNode = ImRaii.TreeNode("Mass Cleanup");
                if (clearNode)
                {
                    using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Broom, "Clear Syncshell"))
                        {
                            _ = _apiController.GroupClear(new(GroupFullInfo.Group));
                        }
                    }
                    UiSharedService.AttachToolTip("This will remove all non-pinned, non-moderator users from the Syncshell."
                        + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");

                    ImGuiHelpers.ScaledDummy(2f);
                    ImGui.Separator();
                    ImGuiHelpers.ScaledDummy(2f);

                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Unlink, "Check for Inactive Users"))
                    {
                        _pruneTestTask = _apiController.GroupPrune(new(GroupFullInfo.Group), _pruneDays, execute: false);
                        _pruneTask = null;
                    }
                    UiSharedService.AttachToolTip($"This will start the prune process for this Syncshell of inactive users that have not logged in the past {_pruneDays} days."
                        + Environment.NewLine + "You will be able to review the amount of inactive users before executing the prune."
                        + UiSharedService.TooltipSeparator + "Note: pruning excludes pinned users and moderators of this Syncshell.");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(150);
                    _uiSharedService.DrawCombo("Days of inactivity", [7, 14, 30, 90], (count) =>
                    {
                        return count + " days";
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
                            UiSharedService.ColorTextWrapped("Calculating inactive users...", ImGuiColors.DalamudYellow);
                        }
                        else
                        {
                            ImGui.AlignTextToFramePadding();
                            UiSharedService.TextWrapped($"Found {_pruneTestTask.Result} user(s) that have not logged in the past {_pruneDays} days.");
                            if (_pruneTestTask.Result > 0)
                            {
                                using (ImRaii.Disabled(!UiSharedService.CtrlPressed()))
                                {
                                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Broom, "Prune Inactive Users"))
                                    {
                                        _pruneTask = _apiController.GroupPrune(new(GroupFullInfo.Group), _pruneDays, execute: true);
                                        _pruneTestTask = null;
                                    }
                                }
                                UiSharedService.AttachToolTip($"Pruning will remove {_pruneTestTask?.Result ?? 0} inactive user(s)."
                                    + UiSharedService.TooltipSeparator + "Hold CTRL to enable this button");
                            }
                        }
                    }
                    if (_pruneTask != null)
                    {
                        if (!_pruneTask.IsCompleted)
                        {
                            UiSharedService.ColorTextWrapped("Pruning Syncshell...", ImGuiColors.DalamudYellow);
                        }
                        else
                        {
                            UiSharedService.TextWrapped($"Syncshell was pruned and {_pruneTask.Result} inactive user(s) have been removed.");
                        }
                    }
                }
                clearNode.Dispose();

                var banNode = ImRaii.TreeNode("User Bans");
                if (banNode)
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Retweet, "Refresh Banlist from Server"))
                    {
                        _bannedUsers = _apiController.GroupGetBannedUsers(new GroupDto(GroupFullInfo.Group)).Result;
                    }

                    if (ImGui.BeginTable("bannedusertable" + GroupFullInfo.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
                    {
                        ImGui.TableSetupColumn("UID", ImGuiTableColumnFlags.None, 1);
                        ImGui.TableSetupColumn("Alias", ImGuiTableColumnFlags.None, 1);
                        ImGui.TableSetupColumn("By", ImGuiTableColumnFlags.None, 1);
                        ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.None, 2);
                        ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 3);
                        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.None, 1);

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
                            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Check, "Unban"))
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

            var discoveryTab = ImRaii.TabItem("AutoDetect");
            if (discoveryTab)
            {
                DrawAutoDetectTab();
            }
            discoveryTab.Dispose();

            var permissionTab = ImRaii.TabItem("Permissions");
            if (permissionTab)
            {
                bool isDisableAnimations = perm.IsDisableAnimations();
                bool isDisableSounds = perm.IsDisableSounds();
                bool isDisableVfx = perm.IsDisableVFX();

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Sound Sync");
                _uiSharedService.BooleanToColoredIcon(!isDisableSounds);
                ImGui.SameLine(230);
                if (_uiSharedService.IconTextButton(isDisableSounds ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp,
                    isDisableSounds ? "Enable sound sync" : "Disable sound sync"))
                {
                    perm.SetDisableSounds(!perm.IsDisableSounds());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Animation Sync");
                _uiSharedService.BooleanToColoredIcon(!isDisableAnimations);
                ImGui.SameLine(230);
                if (_uiSharedService.IconTextButton(isDisableAnimations ? FontAwesomeIcon.WindowClose : FontAwesomeIcon.Running,
                    isDisableAnimations ? "Enable animation sync" : "Disable animation sync"))
                {
                    perm.SetDisableAnimations(!perm.IsDisableAnimations());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("VFX Sync");
                _uiSharedService.BooleanToColoredIcon(!isDisableVfx);
                ImGui.SameLine(230);
                if (_uiSharedService.IconTextButton(isDisableVfx ? FontAwesomeIcon.TimesCircle : FontAwesomeIcon.Sun,
                    isDisableVfx ? "Enable VFX sync" : "Disable VFX sync"))
                {
                    perm.SetDisableVFX(!perm.IsDisableVFX());
                    _ = _apiController.GroupChangeGroupPermissionState(new(GroupFullInfo.Group, perm));
                }
            }
            permissionTab.Dispose();

            if (_isOwner)
            {
                var ownerTab = ImRaii.TabItem("Owner Settings");
                if (ownerTab)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted("New Password");
                    var availableWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
                    var buttonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.Passport, "Change Password");
                    var textSize = ImGui.CalcTextSize("New Password").X;
                    var spacing = ImGui.GetStyle().ItemSpacing.X;

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(availableWidth - buttonSize - textSize - spacing * 2);
                    ImGui.InputTextWithHint("##changepw", "Min 10 characters", ref _newPassword, 50);
                    ImGui.SameLine();
                    using (ImRaii.Disabled(_newPassword.Length < 10))
                    {
                        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Passport, "Change Password"))
                        {
                            _pwChangeSuccess = _apiController.GroupChangePassword(new GroupPasswordDto(GroupFullInfo.Group, _newPassword)).Result;
                            _newPassword = string.Empty;
                        }
                    }
                    UiSharedService.AttachToolTip("Password requires to be at least 10 characters long. This action is irreversible.");

                    if (!_pwChangeSuccess)
                    {
                        UiSharedService.ColorTextWrapped("Failed to change the password. Password requires to be at least 10 characters long.", ImGuiColors.DalamudYellow);
                    }

                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete Syncshell") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                    {
                        IsOpen = false;
                        _ = _apiController.GroupDelete(new(GroupFullInfo.Group));
                    }
                    UiSharedService.AttachToolTip("Hold CTRL and Shift and click to delete this Syncshell." + Environment.NewLine + "WARNING: this action is irreversible.");
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

        UiSharedService.TextWrapped("Activer l'affichage AutoDetect rend la Syncshell visible dans l'onglet AutoDetect et désactive temporairement le mot de passe.");
        ImGuiHelpers.ScaledDummy(4);

        if (_autoDetectStateLoading)
        {
            ImGui.TextDisabled("Chargement de l'état en cours...");
        }

        if (!string.IsNullOrEmpty(_autoDetectMessage))
        {
            UiSharedService.ColorTextWrapped(_autoDetectMessage!, ImGuiColors.DalamudYellow);
        }

        using (ImRaii.Disabled(_autoDetectToggleInFlight || _autoDetectStateLoading))
        {
            if (ImGui.Checkbox("Afficher cette Syncshell dans l'AutoDetect", ref _autoDetectDesiredVisibility))
            {
                // Only change local desired state; sending is done via the validate button
            }
        }
        _uiSharedService.DrawHelpText("Quand cette option est activée, le mot de passe devient inactif tant que la visibilité est maintenue.");

        if (_autoDetectDesiredVisibility)
        {
            ImGuiHelpers.ScaledDummy(4);
            ImGui.TextUnformatted("Options d'affichage AutoDetect");
            ImGui.Separator();

            // Recurring toggle first
            ImGui.Checkbox("Affichage récurrent", ref _adRecurring);
            _uiSharedService.DrawHelpText("Si activé, vous pouvez choisir les jours et une plage horaire récurrents. Si désactivé, seule la durée sera prise en compte.");

            // Duration in hours (only when NOT recurring)
            if (!_adRecurring)
            {
                ImGuiHelpers.ScaledDummy(4);
                int duration = _adDurationHours;
                ImGui.PushItemWidth(120 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt("Durée (heures)", ref duration))
                {
                    _adDurationHours = Math.Clamp(duration, 1, 240);
                }
                ImGui.PopItemWidth();
                _uiSharedService.DrawHelpText("Combien de temps la Syncshell doit rester visible, en heures.");
            }

            ImGuiHelpers.ScaledDummy(4);
            if (_adRecurring)
            {
                ImGuiHelpers.ScaledDummy(4);
                ImGui.TextUnformatted("Jours de la semaine actifs :");
                string[] daysFr = new[] { "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam", "Dim" };
                for (int i = 0; i < 7; i++)
                {
                    ImGui.SameLine(i == 0 ? 0 : 0);
                    bool v = _adWeekdays[i];
                    if (ImGui.Checkbox($"##adwd{i}", ref v)) _adWeekdays[i] = v;
                    ImGui.SameLine();
                    ImGui.TextUnformatted(daysFr[i]);
                    if (i < 6) ImGui.SameLine();
                }
                ImGui.NewLine();
                _uiSharedService.DrawHelpText("Sélectionnez les jours où l'affichage est autorisé (ex: jeudi et dimanche).");

                ImGuiHelpers.ScaledDummy(4);
                ImGui.TextUnformatted("Plage horaire (heure locale Europe/Paris) :");
                ImGui.PushItemWidth(60 * ImGuiHelpers.GlobalScale);
                ImGui.InputInt("Début heure", ref _adStartHour); ImGui.SameLine();
                ImGui.InputInt("min", ref _adStartMinute);
                _adStartHour = Math.Clamp(_adStartHour, 0, 23);
                _adStartMinute = Math.Clamp(_adStartMinute, 0, 59);
                ImGui.SameLine();
                ImGui.TextUnformatted("→"); ImGui.SameLine();
                ImGui.InputInt("Fin heure", ref _adEndHour); ImGui.SameLine();
                ImGui.InputInt("min ", ref _adEndMinute);
                _adEndHour = Math.Clamp(_adEndHour, 0, 23);
                _adEndMinute = Math.Clamp(_adEndMinute, 0, 59);
                ImGui.PopItemWidth();
                _uiSharedService.DrawHelpText("Exemple : de 21h00 à 23h00. Le fuseau utilisé est Europe/Paris (avec changements été/hiver).");
            }
        }

        if (_autoDetectPasswordDisabled && _autoDetectVisible)
        {
            UiSharedService.ColorTextWrapped("Le mot de passe est actuellement désactivé pendant la visibilité AutoDetect.", ImGuiColors.DalamudYellow);
        }

        ImGuiHelpers.ScaledDummy(6);
        using (ImRaii.Disabled(_autoDetectToggleInFlight || _autoDetectStateLoading))
        {
            if (ImGui.Button("Valider et envoyer"))
            {
                _ = SubmitAutoDetectAsync();
            }
            ImGui.SameLine();
            if (ImGui.Button("Recharger l'état"))
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
                _autoDetectMessage = "Impossible de récupérer l'état AutoDetect.";
            }
        }
        catch (Exception ex)
        {
            _autoDetectMessage = force ? $"Erreur lors du rafraîchissement : {ex.Message}" : _autoDetectMessage;
        }
        finally
        {
            _autoDetectStateLoading = false;
        }
    }

    private async Task ToggleAutoDetectAsync(bool desiredVisibility)
    {
        if (_autoDetectToggleInFlight)
        {
            return;
        }

        _autoDetectToggleInFlight = true;
        _autoDetectMessage = null;

        try
        {
            var success = await _syncshellDiscoveryService.SetVisibilityAsync(GroupFullInfo.GID, desiredVisibility, CancellationToken.None).ConfigureAwait(false);
            if (!success)
            {
                _autoDetectMessage = "Impossible de mettre à jour la visibilité AutoDetect.";
                return;
            }

            await EnsureAutoDetectStateAsync(true).ConfigureAwait(false);
            _autoDetectMessage = desiredVisibility
                ? "La Syncshell est désormais visible dans AutoDetect."
                : "La Syncshell n'est plus visible dans AutoDetect.";

            if (desiredVisibility)
            {
                PublishSyncshellPublicNotification();
            }
        }
        catch (Exception ex)
        {
            _autoDetectMessage = $"Erreur lors de la mise à jour AutoDetect : {ex.Message}";
        }
        finally
        {
            _autoDetectToggleInFlight = false;
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
                _autoDetectMessage = "Impossible d'envoyer les paramètres AutoDetect.";
                return;
            }

            await EnsureAutoDetectStateAsync(true).ConfigureAwait(false);
            _autoDetectMessage = _autoDetectDesiredVisibility
                ? "Paramètres AutoDetect envoyés. La Syncshell sera visible selon le planning défini."
                : "La Syncshell n'est plus visible dans AutoDetect.";

            if (_autoDetectDesiredVisibility)
            {
                PublishSyncshellPublicNotification();
            }
        }
        catch (Exception ex)
        {
            _autoDetectMessage = $"Erreur lors de l'envoi des paramètres AutoDetect : {ex.Message}";
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
            var title = $"Syncshell publique: {GroupFullInfo.GroupAliasOrGID}";
            var message = "La Syncshell est désormais visible via AutoDetect.";
            Mediator.Publish(new DualNotificationMessage(title, message, NotificationType.Info, TimeSpan.FromSeconds(4)));
            _notificationTracker.Upsert(NotificationEntry.SyncshellPublic(GroupFullInfo.GID, GroupFullInfo.GroupAliasOrGID));
        }
        catch
        {
            // swallow any notification errors to not break UI flow
        }
    }
}
