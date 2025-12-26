using Dalamud.Interface.Colors;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using UmbraSync.API.Dto.Slot;
using UmbraSync.Localization;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.WebAPI.SignalR;
using UmbraSync.API.Dto.Group;
using UmbraSync.API.Data;

namespace UmbraSync.UI.Components.Popup;

public class SlotPopupHandler : IPopupHandler
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private readonly SlotService _slotService;
    private SlotInfoResponseDto? _slotInfo;

    public SlotPopupHandler(ApiController apiController, UiSharedService uiSharedService, SlotService slotService)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _slotService = slotService;
    }

    public Vector2 PopupSize => new(400, 280);
    public bool ShowClose => true;

    public void Open(OpenSlotPromptMessage msg)
    {
        _slotInfo = msg.SlotInfo;
    }

    public void DrawContent()
    {
        if (_slotInfo == null) return;

        ImGui.TextColored(new Vector4(1, 1, 0, 1), Loc.Get("SlotPopup.Title"));
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextWrapped(string.Format(Loc.Get("SlotPopup.WelcomeTo"), _slotInfo.SlotName));
        if (!string.IsNullOrEmpty(_slotInfo.SlotDescription))
        {
            ImGui.TextWrapped(_slotInfo.SlotDescription);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (_slotInfo.AssociatedSyncshell != null)
        {
            ImGui.TextWrapped(string.Format(Loc.Get("SlotPopup.SyncshellFound"), _slotInfo.AssociatedSyncshell.Name));
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
            ImGui.TextWrapped(Loc.Get("SlotPopup.AutoLeaveNotice"));
            ImGui.PopStyleColor();
            ImGui.Spacing();

            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, Loc.Get("SlotPopup.JoinButton")))
            {
                _slotService.MarkJoinedViaSlot(_slotInfo.AssociatedSyncshell);
                _ = _apiController.SyncshellDiscoveryJoin(new GroupDto(new GroupData(_slotInfo.AssociatedSyncshell.Gid)));
                ImGui.CloseCurrentPopup();
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.Get("SlotPopup.JoinToolTip"));
            }

            ImGui.SameLine();
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Times, Loc.Get("SlotPopup.DeclineButton")))
            {
                _slotService.DeclineSlot(_slotInfo);
                ImGui.CloseCurrentPopup();
            }
        }
        else
        {
            ImGui.TextWrapped(Loc.Get("SlotPopup.NoSyncshell"));
        }
    }
}
