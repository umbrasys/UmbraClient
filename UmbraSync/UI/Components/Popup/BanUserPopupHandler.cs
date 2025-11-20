using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using UmbraSync.API.Dto.Group;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Localization;
using UmbraSync.Services.Mediator;
using UmbraSync.WebAPI;
using System.Globalization;
using System.Numerics;

namespace UmbraSync.UI.Components.Popup;

public class BanUserPopupHandler : IPopupHandler
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private string _banReason = string.Empty;
    private GroupFullInfoDto _group = null!;
    private Pair _reportedPair = null!;

    public BanUserPopupHandler(ApiController apiController, UiSharedService uiSharedService)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
    }

    public Vector2 PopupSize => new(500, 250);

    public bool ShowClose => true;

    public void DrawContent()
    {
        UiSharedService.TextWrapped(string.Format(CultureInfo.CurrentCulture, Loc.Get("BanUserPopup.BanNotice"), _reportedPair.UserData.AliasOrUID));
        ImGui.InputTextWithHint("##banreason", Loc.Get("BanUserPopup.ReasonPlaceholder"), ref _banReason, 255);

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.UserSlash, Loc.Get("BanUserPopup.SubmitButton")))
        {
            ImGui.CloseCurrentPopup();
            var reason = _banReason;
            _ = _apiController.GroupBanUser(new GroupPairDto(_group.Group, _reportedPair.UserData), reason);
            _banReason = string.Empty;
        }
        UiSharedService.TextWrapped(Loc.Get("BanUserPopup.ReasonInfo"));
    }

    public void Open(OpenBanUserPopupMessage message)
    {
        _reportedPair = message.PairToBan;
        _group = message.GroupFullInfoDto;
        _banReason = string.Empty;
    }
}
