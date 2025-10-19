using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.API.Dto.User;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using System.Numerics;
using MareSynchronos.Services.ServerConfiguration;

namespace MareSynchronos.UI.Components;

public class DrawUserPair : DrawPairBase
{
    private static readonly Vector4 Violet = new(0.63f, 0.25f, 1f, 1f);
    protected readonly MareMediator _mediator;
    private readonly SelectGroupForPairUi _selectGroupForPairUi;
    private readonly CharaDataManager _charaDataManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public DrawUserPair(string id, Pair entry, UidDisplayHandler displayHandler, ApiController apiController,
        MareMediator mareMediator, SelectGroupForPairUi selectGroupForPairUi,
        UiSharedService uiSharedService, CharaDataManager charaDataManager,
        ServerConfigurationManager serverConfigurationManager)
        : base(id, entry, apiController, displayHandler, uiSharedService)
    {
        if (_pair.UserPair == null) throw new ArgumentException("Pair must be UserPair", nameof(entry));
        _pair = entry;
        _selectGroupForPairUi = selectGroupForPairUi;
        _mediator = mareMediator;
        _charaDataManager = charaDataManager;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public bool IsOnline => _pair.IsOnline;
    public bool IsVisible => _pair.IsVisible;
    public UserPairDto UserPair => _pair.UserPair!;

    protected override float GetRightSideExtraWidth()
    {
        float width = 0f;
        var spacingX = ImGui.GetStyle().ItemSpacing.X;

        var individualSoundsDisabled = (_pair.UserPair?.OwnPermissions.IsDisableSounds() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
        var individualAnimDisabled = (_pair.UserPair?.OwnPermissions.IsDisableAnimations() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
        var individualVFXDisabled = (_pair.UserPair?.OwnPermissions.IsDisableVFX() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);

        if (individualSoundsDisabled || individualAnimDisabled || individualVFXDisabled)
        {
            width += _uiSharedService.GetIconButtonSize(FontAwesomeIcon.ExclamationTriangle).X + spacingX * 0.5f;
        }

        if (_charaDataManager.SharedWithYouData.TryGetValue(_pair.UserData, out var sharedData))
        {
            width += _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Running).X + spacingX * 0.5f;
        }

        return width;
    }

    protected override void DrawLeftSide(float textPosY, float originalY)
    {
        var online = _pair.IsOnline;
        var offlineGrey = ImGuiColors.DalamudGrey3;

        ImGui.SetCursorPosY(textPosY);
        ImGui.PushFont(UiBuilder.IconFont);
        UiSharedService.ColorText(FontAwesomeIcon.Moon.ToIconString(), online ? Violet : offlineGrey);
        ImGui.PopFont();
        UiSharedService.AttachToolTip(online
            ? "User is online"
            : "User is offline");
        if (!(_pair.UserPair!.OwnPermissions.IsPaired() && _pair.UserPair!.OtherPermissions.IsPaired()))
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.ArrowUp.ToIconString(), UiSharedService.AccentColor);
            ImGui.PopFont();
            UiSharedService.AttachToolTip(_pair.UserData.AliasOrUID + " has not added you back");
        }
        else if (_pair.UserPair!.OwnPermissions.IsPaused() || _pair.UserPair!.OtherPermissions.IsPaused())
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.PauseCircle.ToIconString(), ImGuiColors.DalamudYellow);
            ImGui.PopFont();
            UiSharedService.AttachToolTip("Pairing with " + _pair.UserData.AliasOrUID + " is paused");
        }
        if (_pair is { IsOnline: true, IsVisible: true })
        {
            ImGui.SameLine();
            ImGui.SetCursorPosY(textPosY);
            ImGui.PushFont(UiBuilder.IconFont);
            UiSharedService.ColorText(FontAwesomeIcon.Eye.ToIconString(), Violet);
            if (ImGui.IsItemClicked())
            {
                _mediator.Publish(new TargetPairMessage(_pair));
            }
            ImGui.PopFont();
            var visibleTooltip = _pair.UserData.AliasOrUID + " is visible: " + _pair.PlayerName! + Environment.NewLine + "Click to target this player";
            if (_pair.LastAppliedDataBytes >= 0)
            {
                visibleTooltip += UiSharedService.TooltipSeparator;
                visibleTooltip += ((!_pair.IsVisible) ? "(Last) " : string.Empty) + "Mods Info" + Environment.NewLine;
                visibleTooltip += "Files Size: " + UiSharedService.ByteToString(_pair.LastAppliedDataBytes, true);
                if (_pair.LastAppliedApproximateVRAMBytes >= 0)
                {
                    visibleTooltip += Environment.NewLine + "Approx. VRAM Usage: " + UiSharedService.ByteToString(_pair.LastAppliedApproximateVRAMBytes, true);
                }
                if (_pair.LastAppliedDataTris >= 0)
                {
                    visibleTooltip += Environment.NewLine + "Triangle Count (excl. Vanilla): "
                        + (_pair.LastAppliedDataTris > 1000 ? (_pair.LastAppliedDataTris / 1000d).ToString("0.0'k'") : _pair.LastAppliedDataTris);
                }
            }

            UiSharedService.AttachToolTip(visibleTooltip);
        }
    }

    protected override float DrawRightSide(float textPosY, float originalY)
    {
        var pauseIcon = _pair.UserPair!.OwnPermissions.IsPaused() ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var pauseIconSize = _uiSharedService.GetIconButtonSize(pauseIcon);
        var barButtonSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
        var entryUID = _pair.UserData.AliasOrUID;
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var edgePadding = ImGui.GetStyle().WindowPadding.X + 4f * ImGuiHelpers.GlobalScale;
        var windowEndX = ImGui.GetWindowContentRegionMin().X + UiSharedService.GetWindowContentRegionWidth() - edgePadding;
        var rightSidePos = windowEndX - barButtonSize.X;

        // Flyout Menu
        ImGui.SameLine(rightSidePos);
        ImGui.SetCursorPosY(originalY);

        if (_uiSharedService.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("User Flyout Menu");
        }
        if (ImGui.BeginPopup("User Flyout Menu"))
        {
            using (ImRaii.PushId($"buttons-{_pair.UserData.UID}")) DrawPairedClientMenu(_pair);
            ImGui.EndPopup();
        }

        // Pause (mutual pairs only)
        if (_pair.UserPair!.OwnPermissions.IsPaired() && _pair.UserPair!.OtherPermissions.IsPaired())
        {
            rightSidePos -= pauseIconSize.X + spacingX;
            ImGui.SameLine(rightSidePos);
            ImGui.SetCursorPosY(originalY);
            if (_uiSharedService.IconButton(pauseIcon))
            {
                var perm = _pair.UserPair!.OwnPermissions;
                perm.SetPaused(!perm.IsPaused());
                _ = _apiController.UserSetPairPermissions(new(_pair.UserData, perm));
            }
            UiSharedService.AttachToolTip(AppendSeenInfo(!_pair.UserPair!.OwnPermissions.IsPaused()
                ? "Pause pairing with " + entryUID
                : "Resume pairing with " + entryUID));


            var individualSoundsDisabled = (_pair.UserPair?.OwnPermissions.IsDisableSounds() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableSounds() ?? false);
            var individualAnimDisabled = (_pair.UserPair?.OwnPermissions.IsDisableAnimations() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableAnimations() ?? false);
            var individualVFXDisabled = (_pair.UserPair?.OwnPermissions.IsDisableVFX() ?? false) || (_pair.UserPair?.OtherPermissions.IsDisableVFX() ?? false);
            if (individualSoundsDisabled || individualAnimDisabled || individualVFXDisabled)
            {
                var icon = FontAwesomeIcon.ExclamationTriangle;
                var iconwidth = _uiSharedService.GetIconButtonSize(icon);

                rightSidePos -= iconwidth.X + spacingX / 2f;
                ImGui.SameLine(rightSidePos);

                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                _uiSharedService.IconText(icon);
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
        }
        if (_charaDataManager.SharedWithYouData.TryGetValue(_pair.UserData, out var sharedData))
        {
            var icon = FontAwesomeIcon.Running;
            var iconwidth = _uiSharedService.GetIconButtonSize(icon);
            rightSidePos -= iconwidth.X + spacingX / 2f;
            ImGui.SameLine(rightSidePos);
            _uiSharedService.IconText(icon);

            UiSharedService.AttachToolTip($"This user has shared {sharedData.Count} Character Data Sets with you." + UiSharedService.TooltipSeparator
                + "Click to open the Character Data Hub and show the entries.");

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
            {
                _mediator.Publish(new OpenCharaDataHubWithFilterMessage(_pair.UserData));
            }
        }

        return rightSidePos - spacingX;
    }

    private void DrawPairedClientMenu(Pair entry)
    {
        if (entry.IsVisible)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Eye, "Target player"))
            {
                _mediator.Publish(new TargetPairMessage(entry));
                ImGui.CloseCurrentPopup();
            }
        }
        if (!entry.IsPaused)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.User, "Open Profile"))
            {
                _displayHandler.OpenProfile(entry);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("Opens the profile for this user in a new window");
        }
        if (entry.IsVisible)
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
                entry.ApplyLastReceivedData(forced: true);
                ImGui.CloseCurrentPopup();
            }
            UiSharedService.AttachToolTip("This reapplies the last received character data to this character");
        }

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.PlayCircle, "Cycle pause state"))
        {
            _ = _apiController.CyclePause(entry.UserData);
            ImGui.CloseCurrentPopup();
        }
        var entryUID = entry.UserData.AliasOrUID;
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Folder, "Pair Groups"))
        {
            _selectGroupForPairUi.Open(entry);
        }
        UiSharedService.AttachToolTip(AppendSeenInfo("Choose pair groups for " + entryUID));

        var isDisableSounds = entry.UserPair!.OwnPermissions.IsDisableSounds();
        string disableSoundsText = isDisableSounds ? "Enable sound sync" : "Disable sound sync";
        var disableSoundsIcon = isDisableSounds ? FontAwesomeIcon.VolumeMute : FontAwesomeIcon.VolumeUp;
        if (_uiSharedService.IconTextButton(disableSoundsIcon, disableSoundsText))
        {
            var permissions = entry.UserPair.OwnPermissions;
            permissions.SetDisableSounds(!isDisableSounds);
            _mediator.Publish(new PairSyncOverrideChanged(entry.UserData.UID, permissions.IsDisableSounds(), null, null));
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, permissions));
        }

        var isDisableAnims = entry.UserPair!.OwnPermissions.IsDisableAnimations();
        string disableAnimsText = isDisableAnims ? "Enable animation sync" : "Disable animation sync";
        var disableAnimsIcon = isDisableAnims ? FontAwesomeIcon.WindowClose : FontAwesomeIcon.Running;
        if (_uiSharedService.IconTextButton(disableAnimsIcon, disableAnimsText))
        {
            var permissions = entry.UserPair.OwnPermissions;
            permissions.SetDisableAnimations(!isDisableAnims);
            _mediator.Publish(new PairSyncOverrideChanged(entry.UserData.UID, null, permissions.IsDisableAnimations(), null));
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, permissions));
        }

        var isDisableVFX = entry.UserPair!.OwnPermissions.IsDisableVFX();
        string disableVFXText = isDisableVFX ? "Enable VFX sync" : "Disable VFX sync";
        var disableVFXIcon = isDisableVFX ? FontAwesomeIcon.TimesCircle : FontAwesomeIcon.Sun;
        if (_uiSharedService.IconTextButton(disableVFXIcon, disableVFXText))
        {
            var permissions = entry.UserPair.OwnPermissions;
            permissions.SetDisableVFX(!isDisableVFX);
            _mediator.Publish(new PairSyncOverrideChanged(entry.UserData.UID, null, null, permissions.IsDisableVFX()));
            _ = _apiController.UserSetPairPermissions(new UserPermissionsDto(entry.UserData, permissions));
        }

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Unpair Permanently") && UiSharedService.CtrlPressed())
        {
            _ = _apiController.UserRemovePair(new(entry.UserData));
        }
        UiSharedService.AttachToolTip(AppendSeenInfo("Hold CTRL and click to unpair permanently from " + entryUID));
    }

    private string AppendSeenInfo(string tooltip)
    {
        if (_pair.IsVisible) return tooltip;

        var lastSeen = _serverConfigurationManager.GetNameForUid(_pair.UserData.UID);
        if (string.IsNullOrWhiteSpace(lastSeen)) return tooltip;

        return tooltip + " (Vu sous : " + lastSeen + ")";
    }
}
