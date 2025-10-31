using System;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Enum;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.AutoDetect;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;

namespace MareSynchronos.UI.Components;

public class DrawGroupPair : DrawPairBase
{
    protected readonly MareMediator _mediator;
    private readonly GroupPairFullInfoDto _fullInfoDto;
    private readonly GroupFullInfoDto _group;
    private readonly CharaDataManager _charaDataManager;
    private readonly AutoDetectRequestService _autoDetectRequestService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private const string ManualPairInvitePrefix = "[UmbraPairInvite|";

    public DrawGroupPair(string id, Pair entry, ApiController apiController,
        MareMediator mareMediator, GroupFullInfoDto group, GroupPairFullInfoDto fullInfoDto,
        UidDisplayHandler handler, UiSharedService uiSharedService, CharaDataManager charaDataManager,
        AutoDetectRequestService autoDetectRequestService, ServerConfigurationManager serverConfigurationManager)
        : base(id, entry, apiController, handler, uiSharedService)
    {
        _group = group;
        _fullInfoDto = fullInfoDto;
        _mediator = mareMediator;
        _charaDataManager = charaDataManager;
        _autoDetectRequestService = autoDetectRequestService;
        _serverConfigurationManager = serverConfigurationManager;
    }

    protected override float GetRightSideExtraWidth()
    {
        float width = 0f;
        float spacing = ImGui.GetStyle().ItemSpacing.X;

        var soundsDisabled = _fullInfoDto.GroupUserPermissions.IsDisableSounds();
        var animDisabled = _fullInfoDto.GroupUserPermissions.IsDisableAnimations();
        var vfxDisabled = _fullInfoDto.GroupUserPermissions.IsDisableVFX();
        var individualSoundsDisabled = (_pair.UserPair?.OwnPermissions.IsDisableSounds() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
        var individualAnimDisabled = (_pair.UserPair?.OwnPermissions.IsDisableAnimations() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
        var individualVFXDisabled = (_pair.UserPair?.OwnPermissions.IsDisableVFX() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);

        bool showInfo = individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled || animDisabled || soundsDisabled || vfxDisabled;
        bool showShared = _charaDataManager.SharedWithYouData.TryGetValue(_pair.UserData, out var sharedData);
        bool showPlus = _pair.UserPair == null && _pair.IsOnline;

        if (showShared)
        {
            width += _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Running).X + spacing;
        }

        if (showInfo)
        {
            var icon = (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled)
                ? FontAwesomeIcon.ExclamationTriangle
                : FontAwesomeIcon.InfoCircle;
            width += UiSharedService.GetIconSize(icon).X + spacing;
        }

        if (showPlus)
        {
            width += _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus).X + spacing;
        }

        return width;
    }

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        var entryUID = _pair.UserData.AliasOrUID;
        var entryIsMod = _fullInfoDto.GroupPairStatusInfo.IsModerator();
        var entryIsOwner = string.Equals(_pair.UserData.UID, _group.OwnerUID, StringComparison.Ordinal);
        var entryIsPinned = _fullInfoDto.GroupPairStatusInfo.IsPinned();
        var presenceIcon = _pair.IsVisible ? FontAwesomeIcon.Eye : FontAwesomeIcon.CloudMoon;
        var presenceColor = (_pair.IsOnline || _pair.IsVisible) ? new Vector4(0.63f, 0.25f, 1f, 1f) : ImGuiColors.DalamudGrey;
        var presenceText = entryUID + " is offline";

        ImGui.SetCursorPosY(textPosY);
        bool drewPrefixIcon = false;

        if (_pair.IsPaused)
        {
            presenceText = entryUID + " online status is unknown (paused)";

            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.PauseCircle.ToIconString(), ImGuiColors.DalamudYellow);
            ImGui.PopFont();

            UiSharedService.AttachToolTip("Pairing status with " + entryUID + " is paused");
            drewPrefixIcon = true;
        }
        else
        {
            bool individuallyPaired = _pair.UserPair != null;
            var violet = new Vector4(0.63f, 0.25f, 1f, 1f);
            if (individuallyPaired && (_pair.IsOnline || _pair.IsVisible))
            {
                ImGui.PushFont(UiBuilder.IconFont);
                UiSharedService.ColorText(FontAwesomeIcon.Moon.ToIconString(), violet);
                ImGui.PopFont();
                UiSharedService.AttachToolTip("You are individually paired with " + entryUID);
                drewPrefixIcon = true;
            }
        }
        if (drewPrefixIcon)
            ImGui.SameLine();

        ImGui.SetCursorPosY(textPosY);
        ImGui.PushFont(UiBuilder.IconFont);
        UiSharedService.ColorText(presenceIcon.ToIconString(), presenceColor);
        ImGui.PopFont();

        if (_pair.IsOnline && !_pair.IsVisible) presenceText = entryUID + " is online";
        else if (_pair.IsOnline && _pair.IsVisible) presenceText = entryUID + " is visible: " + _pair.PlayerName + Environment.NewLine + "Click to target this player";

        if (_pair.IsVisible)
        {
            if (ImGui.IsItemClicked())
            {
                _mediator.Publish(new TargetPairMessage(_pair));
            }
            if (_pair.LastAppliedDataBytes >= 0)
            {
                presenceText += UiSharedService.TooltipSeparator;
                presenceText += ((!_pair.IsVisible) ? "(Last) " : string.Empty) + "Mods Info" + Environment.NewLine;
                presenceText += "Files Size: " + UiSharedService.ByteToString(_pair.LastAppliedDataBytes, true);
                if (_pair.LastAppliedApproximateVRAMBytes >= 0)
                {
                    presenceText += Environment.NewLine + "Approx. VRAM Usage: " + UiSharedService.ByteToString(_pair.LastAppliedApproximateVRAMBytes, true);
                }
                if (_pair.LastAppliedDataTris >= 0)
                {
                    presenceText += Environment.NewLine + "Triangle Count (excl. Vanilla): "
                        + (_pair.LastAppliedDataTris > 1000 ? (_pair.LastAppliedDataTris / 1000d).ToString("0.0'k'") : _pair.LastAppliedDataTris);
                }
            }
        }

        UiSharedService.AttachToolTip(presenceText);

        if (entryIsOwner)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Crown.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("User is owner of this Syncshell");
        }
        else if (entryIsMod)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.UserShield.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("User is moderator of this Syncshell");
        }
        else if (entryIsPinned)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Thumbtack.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip("User is pinned in this Syncshell");
        }
    }

    protected override float DrawRightSide(float textPosY, float originalY)
    {
        var entryUID = _fullInfoDto.UserAliasOrUID;
        var entryIsMod = _fullInfoDto.GroupPairStatusInfo.IsModerator();
        var entryIsOwner = string.Equals(_pair.UserData.UID, _group.OwnerUID, StringComparison.Ordinal);
        var entryIsPinned = _fullInfoDto.GroupPairStatusInfo.IsPinned();
        var userIsOwner = string.Equals(_group.OwnerUID, _apiController.UID, StringComparison.OrdinalIgnoreCase);
        var userIsModerator = _group.GroupUserInfo.IsModerator();

        var soundsDisabled = _fullInfoDto.GroupUserPermissions.IsDisableSounds();
        var animDisabled = _fullInfoDto.GroupUserPermissions.IsDisableAnimations();
        var vfxDisabled = _fullInfoDto.GroupUserPermissions.IsDisableVFX();
        var individualSoundsDisabled = (_pair.UserPair?.OwnPermissions.IsDisableSounds() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
        var individualAnimDisabled = (_pair.UserPair?.OwnPermissions.IsDisableAnimations() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
        var individualVFXDisabled = (_pair.UserPair?.OwnPermissions.IsDisableVFX() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);

        bool showShared = _charaDataManager.SharedWithYouData.TryGetValue(_pair.UserData, out var sharedData);
        bool showInfo = (individualAnimDisabled || individualSoundsDisabled || animDisabled || soundsDisabled);
        bool showPlus = _pair.UserPair == null && _pair.IsOnline;
        bool showBars = (userIsOwner || (userIsModerator && !entryIsMod && !entryIsOwner)) || !_pair.IsPaused;
        bool showPause = true;

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var permIcon = (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled) ? FontAwesomeIcon.ExclamationTriangle
            : ((soundsDisabled || animDisabled || vfxDisabled) ? FontAwesomeIcon.InfoCircle : FontAwesomeIcon.None);
        var runningIconWidth = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Running).X;
        var infoIconWidth = showInfo && permIcon != FontAwesomeIcon.None ? UiSharedService.GetIconSize(permIcon).X : 0f;
        var plusButtonWidth = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Plus).X;
        var pauseIcon = _fullInfoDto.GroupUserPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseButtonWidth = _uiSharedService.GetIconButtonSize(pauseIcon).X;
        var barButtonWidth = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars).X;

        float totalWidth = 0f;
        void Accumulate(bool condition, float width)
        {
            if (!condition || width <= 0f) return;
            if (totalWidth > 0f) totalWidth += spacing;
            totalWidth += width;
        }

        Accumulate(showShared, runningIconWidth);
        Accumulate(showInfo && infoIconWidth > 0f, infoIconWidth);
        Accumulate(showPlus, plusButtonWidth);
        Accumulate(showPause, pauseButtonWidth);
        if (showBars)
        {
            if (totalWidth > 0f) totalWidth += spacing;
            totalWidth += barButtonWidth;
        }
        if (showPause && showBars)
        {
            totalWidth -= spacing * 0.5f;
            if (totalWidth < 0f) totalWidth = 0f;
        }

        float cardPaddingX = UiSharedService.GetCardContentPaddingX();
        float rightMargin = cardPaddingX + 6f * ImGuiHelpers.GlobalScale;
        float baseX = MathF.Max(ImGui.GetCursorPosX(),
            ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - rightMargin - totalWidth);
        float currentX = baseX;

        ImGui.SameLine();
        ImGui.SetCursorPosX(baseX);

        if (showShared)
        {
            ImGui.SetCursorPosY(textPosY);
            _uiSharedService.IconText(FontAwesomeIcon.Running);

            UiSharedService.AttachToolTip($"This user has shared {sharedData!.Count} Character Data Sets with you." + UiSharedService.TooltipSeparator
                + "Click to open the Character Data Hub and show the entries.");

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                _mediator.Publish(new OpenCharaDataHubWithFilterMessage(_pair.UserData));
            }
            currentX += runningIconWidth + spacing;
            ImGui.SetCursorPosX(currentX);
        }

        if (showInfo && infoIconWidth > 0f)
        {
            ImGui.SetCursorPosY(textPosY);
            if (individualAnimDisabled || individualSoundsDisabled)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                _uiSharedService.IconText(permIcon);
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();

                    ImGui.TextUnformatted("Individual User permissions");

                    if (individualSoundsDisabled)
                    {
                        var userSoundsText = "Sound sync disabled with " + _pair.UserData.AliasOrUID;
                        _uiSharedService.IconText(FontAwesomeIcon.VolumeMute);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userSoundsText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted("You: " + (_pair.UserPair!.OwnPermissions.IsDisableSounds() ? "Disabled" : "Enabled") + ", They: " + (_pair.UserPair!.OtherPermissions.IsDisableSounds() ? "Disabled" : "Enabled"));
                    }

                    if (individualAnimDisabled)
                    {
                        var userAnimText = "Animation sync disabled with " + _pair.UserData.AliasOrUID;
                        _uiSharedService.IconText(FontAwesomeIcon.WindowClose);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userAnimText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted("You: " + (_pair.UserPair!.OwnPermissions.IsDisableAnimations() ? "Disabled" : "Enabled") + ", They: " + (_pair.UserPair!.OtherPermissions.IsDisableAnimations() ? "Disabled" : "Enabled"));
                    }

                    if (individualVFXDisabled)
                    {
                        var userVFXText = "VFX sync disabled with " + _pair.UserData.AliasOrUID;
                        _uiSharedService.IconText(FontAwesomeIcon.TimesCircle);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userVFXText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted("You: " + (_pair.UserPair!.OwnPermissions.IsDisableVFX() ? "Disabled" : "Enabled") + ", They: " + (_pair.UserPair!.OtherPermissions.IsDisableVFX() ? "Disabled" : "Enabled"));
                    }

                    ImGui.EndTooltip();
                }
            }
            else
            {
                _uiSharedService.IconText(permIcon);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();

                    ImGui.TextUnformatted("Syncshell User permissions");

                    if (soundsDisabled)
                    {
                        var userSoundsText = "Sound sync disabled by " + _pair.UserData.AliasOrUID;
                        _uiSharedService.IconText(FontAwesomeIcon.VolumeMute);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userSoundsText);
                    }

                    if (animDisabled)
                    {
                        var userAnimText = "Animation sync disabled by " + _pair.UserData.AliasOrUID;
                        _uiSharedService.IconText(FontAwesomeIcon.WindowClose);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userAnimText);
                    }

                    if (vfxDisabled)
                    {
                        var userVFXText = "VFX sync disabled by " + _pair.UserData.AliasOrUID;
                        _uiSharedService.IconText(FontAwesomeIcon.TimesCircle);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userVFXText);
                    }

                    ImGui.EndTooltip();
                }
            }

            currentX += infoIconWidth + spacing;
            ImGui.SetCursorPosX(currentX);
        }

        if (showPlus)
        {
            ImGui.SetCursorPosY(originalY);

            if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
            {
                var targetUid = _pair.UserData.UID;
                if (!string.IsNullOrEmpty(targetUid))
                {
                    _ = SendGroupPairInviteAsync(targetUid, entryUID);
                }
            }
            UiSharedService.AttachToolTip(AppendSeenInfo("Send pairing invite to " + entryUID));
            currentX += plusButtonWidth + spacing;
            ImGui.SetCursorPosX(currentX);
        }

        if (showPause)
        {
            float gapToBars = showBars ? spacing * 0.5f : spacing;
            ImGui.SetCursorPosY(originalY);
            if (_uiSharedService.IconButton(pauseIcon))
            {
                var newPermissions = _fullInfoDto.GroupUserPermissions ^ GroupUserPermissions.Paused;
                _fullInfoDto.GroupUserPermissions = newPermissions;
                _ = _apiController.GroupChangeIndividualPermissionState(new GroupPairUserPermissionDto(_group.Group, _pair.UserData, newPermissions));
            }

            UiSharedService.AttachToolTip(AppendSeenInfo((_fullInfoDto.GroupUserPermissions.IsPaused() ? "Resume" : "Pause") + " syncing with " + entryUID));
            currentX += pauseButtonWidth + gapToBars;
            ImGui.SetCursorPosX(currentX);
        }

        if (showBars)
        {
            ImGui.SetCursorPosY(originalY);
            if (_uiSharedService.IconButton(FontAwesomeIcon.Bars))
            {
                ImGui.OpenPopup("Syncshell Flyout Menu");
            }
            currentX += barButtonWidth;
            ImGui.SetCursorPosX(currentX);
        }
        if (ImGui.BeginPopup("Popup"))
        {
            if ((userIsModerator || userIsOwner) && !(entryIsMod || entryIsOwner))
            {
                var pinText = entryIsPinned ? "Unpin user" : "Pin user";
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Thumbtack, pinText))
                {
                    ImGui.CloseCurrentPopup();
                    var userInfo = _fullInfoDto.GroupPairStatusInfo ^ GroupUserInfo.IsPinned;
                    _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(_fullInfoDto.Group, _fullInfoDto.User, userInfo));
                }
                UiSharedService.AttachToolTip("Pin this user to the Syncshell. Pinned users will not be deleted in case of a manually initiated Syncshell clean");

                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Remove user") && UiSharedService.CtrlPressed())
                {
                    ImGui.CloseCurrentPopup();
                    _ = _apiController.GroupRemoveUser(_fullInfoDto);
                }

                UiSharedService.AttachToolTip("Hold CTRL and click to remove user " + (_pair.UserData.AliasOrUID) + " from Syncshell");
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserSlash, "Ban User"))
                {
                    ImGui.CloseCurrentPopup();
                    _mediator.Publish(new OpenBanUserPopupMessage(_pair, _group));
                }
                UiSharedService.AttachToolTip("Ban user from this Syncshell");
            }

            if (userIsOwner)
            {
                string modText = entryIsMod ? "Demod user" : "Mod user";
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserShield, modText) && UiSharedService.CtrlPressed())
                {
                    ImGui.CloseCurrentPopup();
                    var userInfo = _fullInfoDto.GroupPairStatusInfo ^ GroupUserInfo.IsModerator;
                    _ = _apiController.GroupSetUserInfo(new GroupPairUserInfoDto(_fullInfoDto.Group, _fullInfoDto.User, userInfo));
                }
                UiSharedService.AttachToolTip("Hold CTRL to change the moderator status for " + (_fullInfoDto.UserAliasOrUID) + Environment.NewLine +
                    "Moderators can kick, ban/unban, pin/unpin users and clear the Syncshell.");
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Crown, "Transfer Ownership") && UiSharedService.CtrlPressed() && UiSharedService.ShiftPressed())
                {
                    ImGui.CloseCurrentPopup();
                    _ = _apiController.GroupChangeOwnership(_fullInfoDto);
                }
                UiSharedService.AttachToolTip("Hold CTRL and SHIFT and click to transfer ownership of this Syncshell to " + (_fullInfoDto.UserAliasOrUID) + Environment.NewLine + "WARNING: This action is irreversible.");
            }

            if (userIsOwner || (userIsModerator && !(entryIsMod || entryIsOwner)))
                ImGui.Separator();

            if (_pair.IsVisible)
            {
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Eye, "Target player"))
                {
                    _mediator.Publish(new TargetPairMessage(_pair));
                    ImGui.CloseCurrentPopup();
                }
            }
            if (!_pair.IsPaused)
            {
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.User, "Open Profile"))
                {
                    _displayHandler.OpenProfile(_pair);
                    ImGui.CloseCurrentPopup();
                }
            }
            if (_pair.IsVisible)
            {
#if DEBUG
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.PersonCircleQuestion, "Open Analysis"))
                {
                    _displayHandler.OpenAnalysis(_pair);
                    ImGui.CloseCurrentPopup();
                }
#endif
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Sync, "Reload last data"))
                {
                    _pair.ApplyLastReceivedData(forced: true);
                    ImGui.CloseCurrentPopup();
                }
                UiSharedService.AttachToolTip("This reapplies the last received character data to this character");
            }
            ImGui.EndPopup();
        }

        return baseX - spacing;
    }

    private string AppendSeenInfo(string tooltip)
    {
        if (_pair.IsVisible) return tooltip;

        var lastSeen = _serverConfigurationManager.GetNameForUid(_pair.UserData.UID);
        if (string.IsNullOrWhiteSpace(lastSeen)) return tooltip;

        return tooltip + " (Vu sous : " + lastSeen + ")";
    }

    private async Task SendGroupPairInviteAsync(string targetUid, string displayName)
    {
        try
        {
            var ok = await _autoDetectRequestService.SendDirectUidRequestAsync(targetUid, displayName).ConfigureAwait(false);
            if (!ok) return;

            await SendManualInviteSignalAsync(targetUid, displayName).ConfigureAwait(false);
        }
        catch
        {
            // errors are logged within the request service; ignore here
        }
    }

    private async Task SendManualInviteSignalAsync(string targetUid, string displayName)
    {
        if (string.IsNullOrEmpty(_apiController.UID)) return;

        var senderAliasRaw = string.IsNullOrEmpty(_apiController.DisplayName) ? _apiController.UID : _apiController.DisplayName;
        var senderAlias = EncodeInviteField(senderAliasRaw);
        var targetDisplay = EncodeInviteField(displayName);
        var inviteId = Guid.NewGuid().ToString("N");
        var payloadText = new StringBuilder()
            .Append(ManualPairInvitePrefix)
            .Append(_apiController.UID)
            .Append('|')
            .Append(senderAlias)
            .Append('|')
            .Append(targetUid)
            .Append('|')
            .Append(targetDisplay)
            .Append('|')
            .Append(inviteId)
            .Append(']')
            .ToString();

        var payload = new SeStringBuilder().AddText(payloadText).Build().Encode();
        var chatMessage = new ChatMessage
        {
            SenderName = senderAlias,
            PayloadContent = payload
        };

        try
        {
            await _apiController.GroupChatSendMsg(new GroupDto(_group.Group), chatMessage).ConfigureAwait(false);
        }
        catch
        {
            // ignore - invite remains tracked locally even if group chat signal fails
        }
    }

    private static string EncodeInviteField(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        return Convert.ToBase64String(bytes);
    }
}
