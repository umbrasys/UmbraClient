using System;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using UmbraSync.Localization;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using UmbraSync.API.Data;
using UmbraSync.API.Data.Enum;
using UmbraSync.API.Data.Extensions;
using UmbraSync.API.Dto.Group;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.AutoDetect;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;
using UmbraSync.UI.Handlers;
using UmbraSync.WebAPI;

namespace UmbraSync.UI.Components;

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
        bool showShared = _charaDataManager.SharedWithYouData.TryGetValue(_pair.UserData, out _);
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

        width += spacing * 1.2f;

        return width;
    }

    protected override float GetLeftSideReservedWidth()
    {
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float iconWidth = UiSharedService.GetIconSize(FontAwesomeIcon.Moon).X; // representative FA icon width

        int iconCount = 1; // presence icon (eye/moon) is always shown

        bool hasPrefixIcon = _pair.IsPaused || (_pair.UserPair != null && (_pair.IsOnline || _pair.IsVisible));
        if (hasPrefixIcon) iconCount++;

        bool entryIsOwner = string.Equals(_pair.UserData.UID, _group.OwnerUID, StringComparison.Ordinal);
        bool entryIsMod = _fullInfoDto.GroupPairStatusInfo.IsModerator();
        bool entryIsPinned = _fullInfoDto.GroupPairStatusInfo.IsPinned();
        bool hasRoleIcon = entryIsOwner || entryIsMod || entryIsPinned;
        if (hasRoleIcon) iconCount++;

        // total icon widths + spacing between them + a small extra gap before the text
        float total = iconWidth * iconCount + spacing * (iconCount + 0.5f);
        return total;
    }

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        var entryUID = _pair.UserData.AliasOrUID;
        var entryIsMod = _fullInfoDto.GroupPairStatusInfo.IsModerator();
        var entryIsOwner = string.Equals(_pair.UserData.UID, _group.OwnerUID, StringComparison.Ordinal);
        var entryIsPinned = _fullInfoDto.GroupPairStatusInfo.IsPinned();
        var presenceIcon = _pair.IsVisible ? FontAwesomeIcon.Eye : FontAwesomeIcon.CloudMoon;
        var presenceColor = (_pair.IsOnline || _pair.IsVisible) ? new Vector4(0.63f, 0.25f, 1f, 1f) : ImGuiColors.DalamudGrey;
        var presenceText = string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.Offline"), entryUID);

        ImGui.SetCursorPosY(textPosY);
        bool drewPrefixIcon = false;

        if (_pair.IsPaused)
        {
            presenceText = string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.Unknown"), entryUID);

            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.PauseCircle.ToIconString(), ImGuiColors.DalamudYellow);
            ImGui.PopFont();

            UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.Paused"), entryUID));
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
                UiSharedService.AttachToolTip(string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.IndividuallyPaired"), entryUID));
                drewPrefixIcon = true;
            }
        }
        if (drewPrefixIcon)
            ImGui.SameLine(0f, ImGui.GetStyle().ItemSpacing.X * 1.2f);

        ImGui.SetCursorPosY(textPosY);
        ImGui.PushFont(UiBuilder.IconFont);
        UiSharedService.ColorText(presenceIcon.ToIconString(), presenceColor);
        ImGui.PopFont();

        if (_pair.IsOnline && !_pair.IsVisible) presenceText = string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.Online"), entryUID);
        else if (_pair.IsOnline && _pair.IsVisible) presenceText = string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.VisibleHeader"), entryUID, _pair.PlayerName) + Environment.NewLine + Loc.Get("GroupPair.VisibleTarget");

        if (_pair.IsVisible)
        {
            if (ImGui.IsItemClicked())
            {
                _mediator.Publish(new TargetPairMessage(_pair));
            }
            if (_pair.LastAppliedDataBytes >= 0)
            {
                presenceText += UiSharedService.TooltipSeparator;
                presenceText += ((!_pair.IsVisible) ? Loc.Get("GroupPair.VisibleLastPrefix") : string.Empty) + Loc.Get("GroupPair.VisibleMods") + Environment.NewLine;
                presenceText += string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.VisibleFiles"), UiSharedService.ByteToString(_pair.LastAppliedDataBytes, true));
                if (_pair.LastAppliedApproximateVRAMBytes >= 0)
                {
                    presenceText += Environment.NewLine + string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.VisibleVram"), UiSharedService.ByteToString(_pair.LastAppliedApproximateVRAMBytes, true));
                }
                if (_pair.LastAppliedDataTris >= 0)
                {
                    presenceText += Environment.NewLine + string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.VisibleTris"),
                        _pair.LastAppliedDataTris > 1000 ? (_pair.LastAppliedDataTris / 1000d).ToString("0.0'k'", CultureInfo.CurrentCulture) : _pair.LastAppliedDataTris.ToString(CultureInfo.CurrentCulture));
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
            UiSharedService.AttachToolTip(Loc.Get("GroupPair.Owner"));
        }
        else if (entryIsMod)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.UserShield.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip(Loc.Get("GroupPair.Moderator"));
        }
        else if (entryIsPinned)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextUnformatted(FontAwesomeIcon.Thumbtack.ToIconString());
            ImGui.PopFont();
            UiSharedService.AttachToolTip(Loc.Get("GroupPair.Pinned"));
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
        bool showInfo = (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled || animDisabled || soundsDisabled || vfxDisabled);
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
        var rightEdgeGap = spacing * 1.2f;

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
            ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - rightMargin - rightEdgeGap - totalWidth);
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
            bool centerWarning = permIcon == FontAwesomeIcon.ExclamationTriangle && showPause && showBars && !showShared && !showPlus;
            if (centerWarning)
            {
                float barsClusterWidth = showBars ? (barButtonWidth + spacing * 0.5f) : 0f;
                float leftAreaWidth = MathF.Max(totalWidth - pauseButtonWidth - barsClusterWidth, 0f);
                float warningX = baseX + MathF.Max((leftAreaWidth - infoIconWidth) / 2f, 0f);
                currentX = warningX;
                ImGui.SetCursorPosX(currentX);
            }

            ImGui.SetCursorPosY(textPosY);
            if (individualAnimDisabled || individualSoundsDisabled || individualVFXDisabled)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                _uiSharedService.IconText(permIcon);
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();

                    ImGui.TextUnformatted(Loc.Get("GroupPair.Permissions.Header"));

                    if (individualSoundsDisabled)
                    {
                        var userSoundsText = string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.Permissions.SoundDisabled"), _pair.UserData.AliasOrUID);
                        _uiSharedService.IconText(FontAwesomeIcon.VolumeMute);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userSoundsText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.Permissions.StatusLine"),
                            _pair.UserPair!.OwnPermissions.IsDisableSounds() ? Loc.Get("Common.Disabled") : Loc.Get("Common.Enabled"),
                            _pair.UserPair!.OtherPermissions.IsDisableSounds() ? Loc.Get("Common.Disabled") : Loc.Get("Common.Enabled")));
                    }

                    if (individualAnimDisabled)
                    {
                        var userAnimText = string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.Permissions.AnimDisabled"), _pair.UserData.AliasOrUID);
                        _uiSharedService.IconText(FontAwesomeIcon.WindowClose);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userAnimText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.Permissions.StatusLine"),
                            _pair.UserPair!.OwnPermissions.IsDisableAnimations() ? Loc.Get("Common.Disabled") : Loc.Get("Common.Enabled"),
                            _pair.UserPair!.OtherPermissions.IsDisableAnimations() ? Loc.Get("Common.Disabled") : Loc.Get("Common.Enabled")));
                    }

                    if (individualVFXDisabled)
                    {
                        var userVFXText = string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.Permissions.VfxDisabled"), _pair.UserData.AliasOrUID);
                        _uiSharedService.IconText(FontAwesomeIcon.TimesCircle);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userVFXText);
                        ImGui.NewLine();
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.Permissions.StatusLine"),
                            _pair.UserPair!.OwnPermissions.IsDisableVFX() ? Loc.Get("Common.Disabled") : Loc.Get("Common.Enabled"),
                            _pair.UserPair!.OtherPermissions.IsDisableVFX() ? Loc.Get("Common.Disabled") : Loc.Get("Common.Enabled")));
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

                    ImGui.TextUnformatted(Loc.Get("GroupPair.SyncshellPermissions.Header"));

                    if (soundsDisabled)
                    {
                        var userSoundsText = string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.SyncshellPermissions.SoundDisabled"), _pair.UserData.AliasOrUID);
                        _uiSharedService.IconText(FontAwesomeIcon.VolumeMute);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userSoundsText);
                    }

                    if (animDisabled)
                    {
                        var userAnimText = string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.SyncshellPermissions.AnimDisabled"), _pair.UserData.AliasOrUID);
                        _uiSharedService.IconText(FontAwesomeIcon.WindowClose);
                        ImGui.SameLine(40 * ImGuiHelpers.GlobalScale);
                        ImGui.TextUnformatted(userAnimText);
                    }

                    if (vfxDisabled)
                    {
                        var userVFXText = string.Format(CultureInfo.CurrentCulture, Loc.Get("GroupPair.SyncshellPermissions.VfxDisabled"), _pair.UserData.AliasOrUID);
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

            if (_uiSharedService.IconPlusButtonCentered())
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
            if (pauseIcon == FontAwesomeIcon.Pause ? _uiSharedService.IconPauseButtonCentered() : _uiSharedService.IconButtonCentered(pauseIcon))
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
            if (_uiSharedService.IconButtonCentered(FontAwesomeIcon.Bars))
            {
                // Use a consistent popup ID between OpenPopup and BeginPopup
                ImGui.OpenPopup("Syncshell Flyout Menu");
            }
            currentX += barButtonWidth;
            ImGui.SetCursorPosX(currentX);
        }
        // Must match the ID used in OpenPopup above
        if (ImGui.BeginPopup("Syncshell Flyout Menu"))
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

            if (_pair.IsVisible && _uiSharedService.IconTextButton(FontAwesomeIcon.Eye, "Target player"))
            {
                _mediator.Publish(new TargetPairMessage(_pair));
                ImGui.CloseCurrentPopup();
            }
            if (!_pair.IsPaused && _uiSharedService.IconTextButton(FontAwesomeIcon.User, "Open Profile"))
            {
                _displayHandler.OpenProfile(_pair);
                ImGui.CloseCurrentPopup();
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
