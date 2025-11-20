using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using UmbraSync.API.Data;
using UmbraSync.API.Data.Comparer;
using UmbraSync.API.Data.Enum;
using UmbraSync.API.Data.Extensions;
using UmbraSync.API.Dto.Group;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.AutoDetect;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;
using UmbraSync.UI.Components;
using UmbraSync.UI.Handlers;
using UmbraSync.WebAPI;
using System;
using System.Globalization;
using System.Numerics;
using UmbraSync.Localization;

namespace UmbraSync.UI.Components;

internal sealed class GroupPanel
{
    private readonly Dictionary<string, bool> _expandedGroupState = new(StringComparer.Ordinal);
    private readonly CompactUi _mainUi;
    private readonly PairManager _pairManager;
    private readonly ChatService _chatService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly CharaDataManager _charaDataManager;
    private readonly AutoDetectRequestService _autoDetectRequestService;
    private readonly Dictionary<string, bool> _showGidForEntry = new(StringComparer.Ordinal);
    private readonly UidDisplayHandler _uidDisplayHandler;
    private readonly UiSharedService _uiShared;
    private List<BannedGroupUserDto> _bannedUsers = new();
    private int _bulkInviteCount = 10;
    private List<string> _bulkOneTimeInvites = new();
    private string _editGroupComment = string.Empty;
    private string _editGroupEntry = string.Empty;
    private bool _errorGroupCreate = false;
    private string _errorGroupCreateMessage = string.Empty;
    private bool _errorGroupJoin;
    private bool _isPasswordValid;
    private GroupPasswordDto? _lastCreatedGroup = null;
    private bool _modalBanListOpened;
    private bool _modalBulkOneTimeInvitesOpened;
    private bool _modalChangePwOpened;
    private string _newSyncShellPassword = string.Empty;
    private bool _showModalBanList = false;
    private bool _showModalBulkOneTimeInvites = false;
    private bool _showModalChangePassword;
    private bool _showModalCreateGroup;
    private bool _showModalEnterPassword;
    private string _newSyncShellAlias = string.Empty;
    private bool _createIsTemporary = false;
    private int _tempSyncshellDurationHours = 24;
    private readonly int[] _temporaryDurationOptions = new[]
    {
        1,
        12,
        24,
        48,
        72,
        96,
        120,
        144,
        168
    };
    private string _syncShellPassword = string.Empty;
    private string _syncShellToJoin = string.Empty;

    public GroupPanel(CompactUi mainUi, UiSharedService uiShared, PairManager pairManager, ChatService chatServivce,
        UidDisplayHandler uidDisplayHandler, ServerConfigurationManager serverConfigurationManager,
        CharaDataManager charaDataManager, AutoDetectRequestService autoDetectRequestService)
    {
        _mainUi = mainUi;
        _uiShared = uiShared;
        _pairManager = pairManager;
        _chatService = chatServivce;
        _uidDisplayHandler = uidDisplayHandler;
        _serverConfigurationManager = serverConfigurationManager;
        _charaDataManager = charaDataManager;
        _autoDetectRequestService = autoDetectRequestService;
    }

    private ApiController ApiController => _uiShared.ApiController;

    public void DrawSyncshells()
    {
        using var fontScale = UiSharedService.PushFontScale(UiSharedService.ContentFontScale);
        using (ImRaii.PushId("addsyncshell")) DrawAddSyncshell();
        using (ImRaii.PushId("syncshelllist")) DrawSyncshellList();
        _mainUi.TransferPartHeight = ImGui.GetCursorPosY();
    }

    private void DrawAddSyncshell()
    {
        var style = ImGui.GetStyle();
        float buttonHeight = ImGui.GetFrameHeight() + style.FramePadding.Y * 0.5f;
        float glyphWidth;
        using (_uiShared.IconFont.Push())
            glyphWidth = ImGui.CalcTextSize(FontAwesomeIcon.Plus.ToIconString()).X;
        var buttonWidth = glyphWidth + style.FramePadding.X * 2f;

        ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonWidth - style.ItemSpacing.X);
        ImGui.InputTextWithHint("##syncshellid", Loc.Get("Syncshell.Input.Placeholder"), ref _syncShellToJoin, 50);
        ImGui.SameLine(ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - buttonWidth);

        var joinModalTitle = Loc.Get("Syncshell.Join.ModalTitle");
        var createModalTitle = Loc.Get("Syncshell.Create.ModalTitle");
        bool userCanJoinMoreGroups = _pairManager.GroupPairs.Count < ApiController.ServerInfo.MaxGroupsJoinedByUser;
        bool userCanCreateMoreGroups = _pairManager.GroupPairs.Count(u => string.Equals(u.Key.Owner.UID, ApiController.UID, StringComparison.Ordinal)) < ApiController.ServerInfo.MaxGroupsCreatedByUser;
        var trimmedInput = _syncShellToJoin.Trim();
        bool alreadyInGroup = _pairManager.GroupPairs.Select(p => p.Key).Any(p => string.Equals(p.Group.Alias, trimmedInput, StringComparison.Ordinal)
            || string.Equals(p.Group.GID, trimmedInput, StringComparison.Ordinal));

        if (alreadyInGroup) ImGui.BeginDisabled();
        if (_uiShared.IconPlusButtonCentered(height: buttonHeight))
        {
            if (!string.IsNullOrWhiteSpace(trimmedInput))
            {
                if (userCanJoinMoreGroups)
                {
                    _errorGroupJoin = false;
                    _showModalEnterPassword = true;
                    ImGui.OpenPopup(joinModalTitle);
                }
            }
            else
            {
                if (userCanCreateMoreGroups)
                {
                    _lastCreatedGroup = null;
                    _errorGroupCreate = false;
                    _newSyncShellAlias = string.Empty;
                    _createIsTemporary = false;
                    _tempSyncshellDurationHours = 24;
                    _errorGroupCreateMessage = string.Empty;
                    _showModalCreateGroup = true;
                    ImGui.OpenPopup(createModalTitle);
                }
            }
        }
        if (trimmedInput.IsNullOrEmpty())
        {
            var tooltip = userCanCreateMoreGroups
                ? Loc.Get("Syncshell.Tooltip.CreateAllowed")
                : string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Tooltip.CreateDenied"), ApiController.ServerInfo.MaxGroupsCreatedByUser);
            UiSharedService.AttachToolTip(tooltip);
        }
        else
        {
            var tooltip = userCanJoinMoreGroups
                ? string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Tooltip.JoinAllowed"), trimmedInput)
                : string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Tooltip.JoinDenied"), ApiController.ServerInfo.MaxGroupsJoinedByUser);
            UiSharedService.AttachToolTip(tooltip);
        }

        if (alreadyInGroup) ImGui.EndDisabled();

        if (ImGui.BeginPopupModal(joinModalTitle, ref _showModalEnterPassword, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped(Loc.Get("Syncshell.Join.Warning"));
            ImGui.Separator();
            UiSharedService.TextWrapped(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Join.PasswordPrompt"), trimmedInput));
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##password", string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Join.PasswordPlaceholder"), trimmedInput), ref _syncShellPassword, 255, ImGuiInputTextFlags.Password);
            if (_errorGroupJoin)
            {
                UiSharedService.ColorTextWrapped(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Loc.Get("Syncshell.Join.Error"),
                        ApiController.ServerInfo.MaxGroupsJoinedByUser,
                        ApiController.ServerInfo.MaxGroupUserCount),
                    new Vector4(1, 0, 0, 1));
            }
            if (ImGui.Button(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Join.Button"), trimmedInput)))
            {
                var shell = trimmedInput;
                var pw = _syncShellPassword;
                _errorGroupJoin = !ApiController.GroupJoin(new(new GroupData(shell), pw)).Result;
                if (!_errorGroupJoin)
                {
                    _syncShellToJoin = string.Empty;
                    _showModalEnterPassword = false;
                }
                _syncShellPassword = string.Empty;
            }
            UiSharedService.SetScaledWindowSize(290);
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopupModal(createModalTitle, ref _showModalCreateGroup, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped(Loc.Get("Syncshell.Create.TypePrompt"));
            bool showPermanent = !_createIsTemporary;
            if (ImGui.RadioButton(Loc.Get("Syncshell.Create.TypePermanent"), showPermanent))
            {
                _createIsTemporary = false;
            }
            ImGui.SameLine();
            if (ImGui.RadioButton(Loc.Get("Syncshell.Create.TypeTemporary"), _createIsTemporary))
            {
                _createIsTemporary = true;
                _newSyncShellAlias = string.Empty;
        }

        if (!_createIsTemporary)
        {
            UiSharedService.TextWrapped(Loc.Get("Syncshell.Create.AliasPrompt"));
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##syncshellalias", Loc.Get("Syncshell.Create.AliasPlaceholder"), ref _newSyncShellAlias, 50);
        }
        else
        {
            _newSyncShellAlias = string.Empty;
        }

        if (_createIsTemporary)
        {
            UiSharedService.TextWrapped(Loc.Get("Syncshell.Create.TempMaxDuration"));
            if (_tempSyncshellDurationHours > 168) _tempSyncshellDurationHours = 168;
            for (int i = 0; i < _temporaryDurationOptions.Length; i++)
            {
                var option = _temporaryDurationOptions[i];
                var isSelected = _tempSyncshellDurationHours == option;
                string label = option switch
                {
                    >= 24 when option % 24 == 0 => option == 24 ? Loc.Get("Syncshell.Create.Duration24h") : string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Create.DurationDays"), option / 24),
                    _ => string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Create.DurationHours"), option)
                };

                if (ImGui.RadioButton(label, isSelected))
                {
                    _tempSyncshellDurationHours = option;
                }

                // Start a new line after every 3 buttons
                if ((i + 1) % 3 == 0)
                {
                    ImGui.NewLine();
                }
                else
                {
                    ImGui.SameLine();
                }
            }

            var expiresLocal = DateTime.Now.AddHours(_tempSyncshellDurationHours);
            UiSharedService.TextWrapped(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Create.ExpiresAt"), expiresLocal.ToString("g", CultureInfo.CurrentCulture)));
        }

            UiSharedService.TextWrapped(Loc.Get("Syncshell.Create.ButtonPrompt"));
            var createButtonHeight = ImGui.GetFrameHeight() * 1.1f;
            var createLabel = Loc.Get("Syncshell.Create.ButtonLabel");
            var createButtonWidth = ImGui.CalcTextSize(createLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
            var cursorX = ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - createButtonWidth) * 0.5f;
            if (cursorX > ImGui.GetCursorPosX()) ImGui.SetCursorPosX(cursorX);
            if (ImGui.Button(createLabel, new Vector2(createButtonWidth, createButtonHeight)))
            {
                try
                {
                    if (_createIsTemporary)
                    {
                        var expiresAtUtc = DateTime.UtcNow.AddHours(_tempSyncshellDurationHours);
                        _lastCreatedGroup = ApiController.GroupCreateTemporary(expiresAtUtc).Result;
                    }
                    else
                    {
                        var aliasInput = string.IsNullOrWhiteSpace(_newSyncShellAlias) ? null : _newSyncShellAlias.Trim();
                        _lastCreatedGroup = ApiController.GroupCreate(aliasInput).Result;
                        if (_lastCreatedGroup != null)
                        {
                            _newSyncShellAlias = string.Empty;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _lastCreatedGroup = null;
                    _errorGroupCreate = true;
                    if (ex.Message.Contains("name is already in use", StringComparison.OrdinalIgnoreCase))
                    {
                        _errorGroupCreateMessage = Loc.Get("Syncshell.Create.NameInUse");
                    }
                    else
                    {
                        _errorGroupCreateMessage = ex.Message;
                    }
                }
            }

            if (_lastCreatedGroup != null)
            {
                ImGui.Separator();
                _errorGroupCreate = false;
                _errorGroupCreateMessage = string.Empty;
                if (!string.IsNullOrWhiteSpace(_lastCreatedGroup.Group.Alias))
                {
                    ImGui.TextUnformatted(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Create.Result.Name"), _lastCreatedGroup.Group.Alias));
                }
                ImGui.TextUnformatted(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Create.Result.Id"), _lastCreatedGroup.Group.GID));
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Create.Result.Password"), _lastCreatedGroup.Password));
                ImGui.SameLine();
                if (_uiShared.IconButton(FontAwesomeIcon.Copy))
                {
                    ImGui.SetClipboardText(_lastCreatedGroup.Password);
                }
                UiSharedService.TextWrapped(Loc.Get("Syncshell.Create.Result.PasswordNote"));
                if (_lastCreatedGroup.IsTemporary && _lastCreatedGroup.ExpiresAt != null)
                {
                    var expiresLocal = _lastCreatedGroup.ExpiresAt.Value.ToLocalTime();
                    UiSharedService.TextWrapped(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Create.TempExpires"), expiresLocal.ToString("g", CultureInfo.CurrentCulture)));
                }
            }

            if (_errorGroupCreate)
            {
                var msg = string.IsNullOrWhiteSpace(_errorGroupCreateMessage)
                    ? Loc.Get("Syncshell.Create.Error.General")
                    : _errorGroupCreateMessage;
                UiSharedService.ColorTextWrapped(msg, new Vector4(1, 0, 0, 1));
            }

            UiSharedService.SetScaledWindowSize(350);
            ImGui.EndPopup();
        }

        ImGuiHelpers.ScaledDummy(2);
    }

    private void DrawSyncshell(GroupFullInfoDto groupDto, List<Pair> pairsInGroup)
    {
        int shellNumber = _serverConfigurationManager.GetShellNumberForGid(groupDto.GID);

        var name = groupDto.Group.Alias ?? groupDto.GID;
        if (!_expandedGroupState.ContainsKey(groupDto.GID))
        {
            _expandedGroupState[groupDto.GID] = false;
        }

        UiSharedService.DrawCard($"syncshell-card-{groupDto.GID}", () =>
        {
        bool expandedState = _expandedGroupState[groupDto.GID];
        UiSharedService.DrawArrowToggle(ref expandedState, $"##syncshell-toggle-{groupDto.GID}");
        if (expandedState != _expandedGroupState[groupDto.GID])
        {
            _expandedGroupState[groupDto.GID] = expandedState;
        }
        ImGui.SameLine(0f, 6f * ImGuiHelpers.GlobalScale);

        var textIsGid = true;
        string groupName = groupDto.GroupAliasOrGID;

        if (string.Equals(groupDto.OwnerUID, ApiController.UID, StringComparison.Ordinal))
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Crown.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Card.Owner"), groupName));
            ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X * 1.2f);
        }
        else if (groupDto.GroupUserInfo.IsModerator())
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.UserShield.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Card.Moderator"), groupName));
            ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X * 1.2f);
        }

        _showGidForEntry.TryGetValue(groupDto.GID, out var showGidInsteadOfName);
        var groupComment = _serverConfigurationManager.GetNoteForGid(groupDto.GID);
        if (!showGidInsteadOfName && !string.IsNullOrEmpty(groupComment))
        {
            groupName = groupComment;
            textIsGid = false;
        }

        if (!string.Equals(_editGroupEntry, groupDto.GID, StringComparison.Ordinal))
        {
            var totalMembers = pairsInGroup.Count + 1;
            var connectedMembers = pairsInGroup.Count(p => p.IsOnline) + 1;
            var maxCapacity = ApiController.ServerInfo.MaxGroupUserCount;
            ImGui.TextUnformatted($"{connectedMembers}/{totalMembers}");
            UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Card.ConnectedTooltip"),
                connectedMembers, totalMembers, maxCapacity, groupDto.Group.GID));
            if (textIsGid) ImGui.PushFont(UiBuilder.MonoFont);
            ImGui.SameLine();
            ImGui.TextUnformatted(groupName);
            if (textIsGid) ImGui.PopFont();
            UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Card.SwitchTooltip"), groupName, pairsInGroup.Count + 1, groupDto.OwnerAliasOrUID));
            if (groupDto.IsTemporary)
            {
                ImGui.SameLine();
                UiSharedService.ColorText(Loc.Get("Syncshell.Card.TempLabel"), ImGuiColors.DalamudOrange);
                if (groupDto.ExpiresAt != null)
                {
                    var tempExpireLocal = groupDto.ExpiresAt.Value.ToLocalTime();
                    UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Card.TempExpire"), tempExpireLocal.ToString("g", CultureInfo.CurrentCulture)));
                }
                else
                {
                    UiSharedService.AttachToolTip(Loc.Get("Syncshell.Card.TempTooltip"));
                }
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                var prevState = textIsGid;
                if (_showGidForEntry.ContainsKey(groupDto.GID))
                {
                    prevState = _showGidForEntry[groupDto.GID];
                }

                _showGidForEntry[groupDto.GID] = !prevState;
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _serverConfigurationManager.SetNoteForGid(_editGroupEntry, _editGroupComment);
                _editGroupComment = _serverConfigurationManager.GetNoteForGid(groupDto.GID) ?? string.Empty;
                _editGroupEntry = groupDto.GID;
                _chatService.MaybeUpdateShellName(shellNumber);
            }
        }
        else
        {
            var buttonSizes = _uiShared.GetIconButtonSize(FontAwesomeIcon.Bars).X + _uiShared.GetIconButtonSize(FontAwesomeIcon.LockOpen).X;
            ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetCursorPosX() - buttonSizes - ImGui.GetStyle().ItemSpacing.X * 2);
            if (ImGui.InputTextWithHint("", Loc.Get("Syncshell.Card.CommentPlaceholder"), ref _editGroupComment, 255, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                _serverConfigurationManager.SetNoteForGid(groupDto.GID, _editGroupComment);
                _editGroupEntry = string.Empty;
                _chatService.MaybeUpdateShellName(shellNumber);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _editGroupEntry = string.Empty;
            }
            UiSharedService.AttachToolTip(Loc.Get("Syncshell.Card.CommentTooltip"));
        }


        using (ImRaii.PushId(groupDto.GID + "settings")) DrawSyncShellButtons(groupDto, pairsInGroup);

        if (_showModalBanList && !_modalBanListOpened)
        {
            _modalBanListOpened = true;
            ImGui.OpenPopup(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Banlist.ModalTitle"), groupDto.GID));
        }

        if (!_showModalBanList) _modalBanListOpened = false;

        if (ImGui.BeginPopupModal(string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Banlist.ModalTitle"), groupDto.GID), ref _showModalBanList, UiSharedService.PopupWindowFlags))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.Retweet, Loc.Get("Syncshell.Banlist.Refresh")))
            {
                _bannedUsers = ApiController.GroupGetBannedUsers(groupDto).Result;
            }

            if (ImGui.BeginTable("bannedusertable" + groupDto.GID, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY))
            {
                ImGui.TableSetupColumn(Loc.Get("Syncshell.Banlist.Column.Uid"), ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn(Loc.Get("Syncshell.Banlist.Column.Alias"), ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn(Loc.Get("Syncshell.Banlist.Column.By"), ImGuiTableColumnFlags.None, 1);
                ImGui.TableSetupColumn(Loc.Get("Syncshell.Banlist.Column.Date"), ImGuiTableColumnFlags.None, 2);
                ImGui.TableSetupColumn(Loc.Get("Syncshell.Banlist.Column.Reason"), ImGuiTableColumnFlags.None, 3);
                ImGui.TableSetupColumn(Loc.Get("Syncshell.Banlist.Column.Actions"), ImGuiTableColumnFlags.None, 1);

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
                    if (_uiShared.IconTextButton(FontAwesomeIcon.Check, string.Format(CultureInfo.CurrentCulture, Loc.Get("Syncshell.Banlist.Unban"), bannedUser.UID)))
                    {
                        _ = ApiController.GroupUnbanUser(bannedUser);
                        _bannedUsers.RemoveAll(b => string.Equals(b.UID, bannedUser.UID, StringComparison.Ordinal));
                    }
                }

                ImGui.EndTable();
            }
            UiSharedService.SetScaledWindowSize(700, 300);
            ImGui.EndPopup();
        }

        if (_showModalChangePassword && !_modalChangePwOpened)
        {
            _modalChangePwOpened = true;
            ImGui.OpenPopup("Change Syncshell Password");
        }

        if (!_showModalChangePassword) _modalChangePwOpened = false;

        if (ImGui.BeginPopupModal("Change Syncshell Password", ref _showModalChangePassword, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("Enter the new Syncshell password for Syncshell " + name + " here.");
            UiSharedService.TextWrapped("This action is irreversible");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##changepw", "New password for " + name, ref _newSyncShellPassword, 255);
            if (ImGui.Button("Change password"))
            {
                var pw = _newSyncShellPassword;
                _isPasswordValid = ApiController.GroupChangePassword(new(groupDto.Group, pw)).Result;
                _newSyncShellPassword = string.Empty;
                if (_isPasswordValid) _showModalChangePassword = false;
            }

            if (!_isPasswordValid)
            {
                UiSharedService.ColorTextWrapped("The selected password is too short. It must be at least 10 characters.", new Vector4(1, 0, 0, 1));
            }

            UiSharedService.SetScaledWindowSize(290);
            ImGui.EndPopup();
        }

        if (_showModalBulkOneTimeInvites && !_modalBulkOneTimeInvitesOpened)
        {
            _modalBulkOneTimeInvitesOpened = true;
            ImGui.OpenPopup("Create Bulk One-Time Invites");
        }

        if (!_showModalBulkOneTimeInvites) _modalBulkOneTimeInvitesOpened = false;

        if (ImGui.BeginPopupModal("Create Bulk One-Time Invites", ref _showModalBulkOneTimeInvites, UiSharedService.PopupWindowFlags))
        {
            UiSharedService.TextWrapped("This allows you to create up to 100 one-time invites at once for the Syncshell " + name + "." + Environment.NewLine
                + "The invites are valid for 24h after creation and will automatically expire.");
            ImGui.Separator();
            if (_bulkOneTimeInvites.Count == 0)
            {
                ImGui.SetNextItemWidth(-1);
                ImGui.SliderInt("Amount##bulkinvites", ref _bulkInviteCount, 1, 100);
                if (_uiShared.IconTextButton(FontAwesomeIcon.MailBulk, "Create invites"))
                {
                    _bulkOneTimeInvites = ApiController.GroupCreateTempInvite(groupDto, _bulkInviteCount).Result;
                }
            }
            else
            {
                UiSharedService.TextWrapped("A total of " + _bulkOneTimeInvites.Count + " invites have been created.");
                if (_uiShared.IconTextButton(FontAwesomeIcon.Copy, "Copy invites to clipboard"))
                {
                    ImGui.SetClipboardText(string.Join(Environment.NewLine, _bulkOneTimeInvites));
                }
            }

            UiSharedService.SetScaledWindowSize(290);
            ImGui.EndPopup();
        }

        bool hideOfflineUsers = pairsInGroup.Count > 1000;

        ImGui.Indent(20);
        if (expandedState)
        {
            var sortedPairs = pairsInGroup
                .OrderByDescending(u => string.Equals(u.UserData.UID, groupDto.OwnerUID, StringComparison.Ordinal))
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsModerator())
                .ThenByDescending(u => u.GroupPair[groupDto].GroupPairStatusInfo.IsPinned())
                .ThenBy(u => u.GetPairSortKey(), StringComparer.OrdinalIgnoreCase);

            var visibleUsers = new List<DrawGroupPair>();
            var onlineUsers = new List<DrawGroupPair>();
            var offlineUsers = new List<DrawGroupPair>();

            foreach (var pair in sortedPairs)
            {
                var drawPair = new DrawGroupPair(
                    groupDto.GID + pair.UserData.UID, pair,
                    ApiController, _mainUi.Mediator, groupDto,
                    pair.GroupPair.Single(
                        g => GroupDataComparer.Instance.Equals(g.Key.Group, groupDto.Group)
                    ).Value,
                    _uidDisplayHandler,
                    _uiShared,
                    _charaDataManager,
                    _autoDetectRequestService,
                    _serverConfigurationManager);

                if (pair.IsVisible)
                    visibleUsers.Add(drawPair);
                else if (pair.IsOnline)
                    onlineUsers.Add(drawPair);
                else
                    offlineUsers.Add(drawPair);
            }

            if (visibleUsers.Count > 0)
            {
                ImGui.TextUnformatted("Visible");
                ImGui.Separator();
                UidDisplayHandler.RenderPairList(visibleUsers);
            }

            if (onlineUsers.Count > 0)
            {
                ImGui.TextUnformatted("Online");
                ImGui.Separator();
                UidDisplayHandler.RenderPairList(onlineUsers);
            }

            if (offlineUsers.Count > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                ImGui.TextUnformatted("Offline/Unknown");
                ImGui.PopStyleColor();
                ImGui.Separator();
                if (hideOfflineUsers)
                {
                    UiSharedService.ColorText($"    {offlineUsers.Count} offline users omitted from display.", ImGuiColors.DalamudGrey);
                }
                else
                {
                    UidDisplayHandler.RenderPairList(offlineUsers);
                }
            }

            ImGui.Separator();
        }
        ImGui.Unindent(20);
        }, stretchWidth: true);
    }

    private void DrawSyncShellButtons(GroupFullInfoDto groupDto, List<Pair> groupPairs)
    {
        var infoIcon = FontAwesomeIcon.InfoCircle;

        bool invitesEnabled = !groupDto.GroupPermissions.IsDisableInvites();
        var soundsDisabled = groupDto.GroupPermissions.IsDisableSounds();
        var animDisabled = groupDto.GroupPermissions.IsDisableAnimations();
        var vfxDisabled = groupDto.GroupPermissions.IsDisableVFX();

        var userSoundsDisabled = groupDto.GroupUserPermissions.IsDisableSounds();
        var userAnimDisabled = groupDto.GroupUserPermissions.IsDisableAnimations();
        var userVFXDisabled = groupDto.GroupUserPermissions.IsDisableVFX();

        bool showInfoIcon = !invitesEnabled || soundsDisabled || animDisabled || vfxDisabled || userSoundsDisabled || userAnimDisabled || userVFXDisabled;

        var lockedIcon = invitesEnabled ? FontAwesomeIcon.LockOpen : FontAwesomeIcon.Lock;
        var animIcon = animDisabled ? FontAwesomeIcon.WindowClose : FontAwesomeIcon.Running;
        var soundsIcon = soundsDisabled ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp;
        var vfxIcon = vfxDisabled ? FontAwesomeIcon.TimesCircle : FontAwesomeIcon.Sun;
        var userAnimIcon = userAnimDisabled ? FontAwesomeIcon.WindowClose : FontAwesomeIcon.Running;
        var userSoundsIcon = userSoundsDisabled ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp;
        var userVFXIcon = userVFXDisabled ? FontAwesomeIcon.TimesCircle : FontAwesomeIcon.Sun;

        var iconSize = UiSharedService.GetIconSize(infoIcon);
        var barbuttonSize = _uiShared.GetIconButtonSize(FontAwesomeIcon.Bars);
        var isOwner = string.Equals(groupDto.OwnerUID, ApiController.UID, StringComparison.Ordinal);

        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var cardPaddingX = UiSharedService.GetCardContentPaddingX();
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - cardPaddingX - 6f * ImGuiHelpers.GlobalScale;
        var pauseIcon = groupDto.GroupUserPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseIconSize = _uiShared.GetIconButtonSize(pauseIcon);

        ImGui.SameLine(windowEndX - barbuttonSize.X - (showInfoIcon ? iconSize.X : 0) - (showInfoIcon ? spacingX : 0) - pauseIconSize.X - spacingX);

        if (showInfoIcon)
        {
            _uiShared.IconText(infoIcon);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                if (!invitesEnabled || soundsDisabled || animDisabled || vfxDisabled)
                {
                    ImGui.TextUnformatted("Syncshell permissions");

                    if (!invitesEnabled)
                    {
                        var lockedText = "Syncshell is closed for joining";
                        _uiShared.IconText(lockedIcon);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(lockedText);
                    }

                    if (soundsDisabled)
                    {
                        var soundsText = "Sound sync disabled through owner";
                        _uiShared.IconText(soundsIcon);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(soundsText);
                    }

                    if (animDisabled)
                    {
                        var animText = "Animation sync disabled through owner";
                        _uiShared.IconText(animIcon);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(animText);
                    }

                    if (vfxDisabled)
                    {
                        var vfxText = "VFX sync disabled through owner";
                        _uiShared.IconText(vfxIcon);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(vfxText);
                    }
                }

                if (userSoundsDisabled || userAnimDisabled || userVFXDisabled)
                {
                    if (!invitesEnabled || soundsDisabled || animDisabled || vfxDisabled)
                        ImGui.Separator();

                    ImGui.TextUnformatted("Your permissions");

                    if (userSoundsDisabled)
                    {
                        var userSoundsText = "Sound sync disabled through you";
                        _uiShared.IconText(userSoundsIcon);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userSoundsText);
                    }

                    if (userAnimDisabled)
                    {
                        var userAnimText = "Animation sync disabled through you";
                        _uiShared.IconText(userAnimIcon);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userAnimText);
                    }

                    if (userVFXDisabled)
                    {
                        var userVFXText = "VFX sync disabled through you";
                        _uiShared.IconText(userVFXIcon);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userVFXText);
                    }

                    if (!invitesEnabled || soundsDisabled || animDisabled || vfxDisabled)
                        UiSharedService.TextWrapped("Note that syncshell permissions for disabling take precedence over your own set permissions");
                }
                ImGui.EndTooltip();
            }
            ImGui.SameLine();
        }

        bool clickedPause = pauseIcon == FontAwesomeIcon.Pause
            ? _uiShared.IconPauseButtonCentered(pauseIconSize.Y)
            : _uiShared.IconButtonCentered(pauseIcon, pauseIconSize.Y);
        if (clickedPause)
        {
            var userPerm = groupDto.GroupUserPermissions ^ GroupUserPermissions.Paused;
            _ = ApiController.GroupChangeIndividualPermissionState(new GroupPairUserPermissionDto(groupDto.Group, new UserData(ApiController.UID), userPerm));
        }
        UiSharedService.AttachToolTip((groupDto.GroupUserPermissions.IsPaused() ? "Resume" : "Pause") + " pairing with all users in this Syncshell");
        ImGui.SameLine();

        if (_uiShared.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("ShellPopup");
        }

        if (ImGui.BeginPopup("ShellPopup"))
        {
            if (_uiShared.IconTextButton(FontAwesomeIcon.ArrowCircleLeft, "Leave Syncshell") && UiSharedService.CtrlPressed())
            {
                _ = ApiController.GroupLeave(groupDto);
            }
            UiSharedService.AttachToolTip("Hold CTRL and click to leave this Syncshell" + (!string.Equals(groupDto.OwnerUID, ApiController.UID, StringComparison.Ordinal) ? string.Empty : Environment.NewLine
                + "WARNING: This action is irreversible" + Environment.NewLine + "Leaving an owned Syncshell will transfer the ownership to a random person in the Syncshell."));

            if (_uiShared.IconTextButton(FontAwesomeIcon.Copy, "Copy ID"))
            {
                ImGui.CloseCurrentPopup();
                ImGui.SetClipboardText(groupDto.GroupAliasOrGID);
            }
            UiSharedService.AttachToolTip("Copy Syncshell ID to Clipboard");

            if (_uiShared.IconTextButton(FontAwesomeIcon.StickyNote, "Copy Notes"))
            {
                ImGui.CloseCurrentPopup();
                ImGui.SetClipboardText(UiSharedService.GetNotes(groupPairs));
            }
            UiSharedService.AttachToolTip("Copies all your notes for all users in this Syncshell to the clipboard." + Environment.NewLine + "They can be imported via Settings -> General -> Notes -> Import notes from clipboard");

            var soundsText = userSoundsDisabled ? "Enable sound sync" : "Disable sound sync";
            if (_uiShared.IconTextButton(userSoundsIcon, soundsText))
            {
                ImGui.CloseCurrentPopup();
                var perm = groupDto.GroupUserPermissions;
                perm.SetDisableSounds(!perm.IsDisableSounds());
                _mainUi.Mediator.Publish(new GroupSyncOverrideChanged(groupDto.Group.GID, perm.IsDisableSounds(), null, null));
                _ = ApiController.GroupChangeIndividualPermissionState(new(groupDto.Group, new UserData(ApiController.UID), perm));
            }
            UiSharedService.AttachToolTip("Sets your allowance for sound synchronization for users of this syncshell."
                + Environment.NewLine + "Disabling the synchronization will stop applying sound modifications for users of this syncshell."
                + Environment.NewLine + "Note: this setting can be forcefully overridden to 'disabled' through the syncshell owner."
                + Environment.NewLine + "Note: this setting does not apply to individual pairs that are also in the syncshell.");

            var animText = userAnimDisabled ? "Enable animations sync" : "Disable animations sync";
            if (_uiShared.IconTextButton(userAnimIcon, animText))
            {
                ImGui.CloseCurrentPopup();
                var perm = groupDto.GroupUserPermissions;
                perm.SetDisableAnimations(!perm.IsDisableAnimations());
                _mainUi.Mediator.Publish(new GroupSyncOverrideChanged(groupDto.Group.GID, null, perm.IsDisableAnimations(), null));
                _ = ApiController.GroupChangeIndividualPermissionState(new(groupDto.Group, new UserData(ApiController.UID), perm));
            }
            UiSharedService.AttachToolTip("Sets your allowance for animations synchronization for users of this syncshell."
                + Environment.NewLine + "Disabling the synchronization will stop applying animations modifications for users of this syncshell."
                + Environment.NewLine + "Note: this setting might also affect sound synchronization"
                + Environment.NewLine + "Note: this setting can be forcefully overridden to 'disabled' through the syncshell owner."
                + Environment.NewLine + "Note: this setting does not apply to individual pairs that are also in the syncshell.");

            var vfxText = userVFXDisabled ? "Enable VFX sync" : "Disable VFX sync";
            if (_uiShared.IconTextButton(userVFXIcon, vfxText))
            {
                ImGui.CloseCurrentPopup();
                var perm = groupDto.GroupUserPermissions;
                perm.SetDisableVFX(!perm.IsDisableVFX());
                _mainUi.Mediator.Publish(new GroupSyncOverrideChanged(groupDto.Group.GID, null, null, perm.IsDisableVFX()));
                _ = ApiController.GroupChangeIndividualPermissionState(new(groupDto.Group, new UserData(ApiController.UID), perm));
            }
            UiSharedService.AttachToolTip("Sets your allowance for VFX synchronization for users of this syncshell."
                                          + Environment.NewLine + "Disabling the synchronization will stop applying VFX modifications for users of this syncshell."
                                          + Environment.NewLine + "Note: this setting might also affect animation synchronization to some degree"
                                          + Environment.NewLine + "Note: this setting can be forcefully overridden to 'disabled' through the syncshell owner."
                                          + Environment.NewLine + "Note: this setting does not apply to individual pairs that are also in the syncshell.");

            if (isOwner || groupDto.GroupUserInfo.IsModerator())
            {
                ImGui.Separator();
                if (_uiShared.IconTextButton(FontAwesomeIcon.Cog, "Open Admin Panel"))
                {
                    ImGui.CloseCurrentPopup();
                    _mainUi.Mediator.Publish(new OpenSyncshellAdminPanel(groupDto));
                }
            }

            ImGui.EndPopup();
        }
    }

    private void DrawSyncshellList()
    {
        float availableHeight = ImGui.GetContentRegionAvail().Y;
        float ySize;
        if (_mainUi.TransferPartHeight <= 0)
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
            ySize = (ImGui.GetWindowContentRegionMax().Y - ImGui.GetWindowContentRegionMin().Y) - _mainUi.TransferPartHeight - ImGui.GetCursorPosY();
        }

        ImGui.BeginChild("list", new Vector2(_mainUi.WindowContentWidth, ySize), border: false);
        foreach (var entry in _pairManager.GroupPairs.OrderBy(g => g.Key.Group.AliasOrGID, StringComparer.OrdinalIgnoreCase).ToList())
        {
            using (ImRaii.PushId(entry.Key.Group.GID)) DrawSyncshell(entry.Key, entry.Value);
        }
        ImGui.EndChild();
    }
}
