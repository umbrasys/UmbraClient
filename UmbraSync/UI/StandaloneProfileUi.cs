using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using UmbraSync.API.Data.Extensions;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;
using UmbraSync.Localization;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace UmbraSync.UI;

public class StandaloneProfileUi : WindowMediatorSubscriberBase
{
    private readonly UmbraProfileManager _umbraProfileManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private bool _adjustedForScrollBars = false;
    private byte[] _lastProfilePicture = [];
    private byte[] _lastRpProfilePicture = [];
    private IDalamudTextureWrap? _textureWrap;
    private IDalamudTextureWrap? _rpTextureWrap;
    private bool _isRpTab = false;

    public StandaloneProfileUi(ILogger<StandaloneProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder,
        ServerConfigurationManager serverManager, UmbraProfileManager umbraProfileManager, ApiController apiController, Pair pair,
        PerformanceCollectorService performanceCollector)
        : base(logger, mediator, string.Format(System.Globalization.CultureInfo.CurrentCulture, Loc.Get("StandaloneProfile.WindowTitle"), pair.UserData.AliasOrUID) + "##UmbraSyncStandaloneProfileUI" + pair.UserData.AliasOrUID, performanceCollector)
    {
        _uiSharedService = uiBuilder;
        _serverManager = serverManager;
        _umbraProfileManager = umbraProfileManager;
        _apiController = apiController;
        Pair = pair;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize;

        var spacing = ImGui.GetStyle().ItemSpacing;

        Size = new(512 + spacing.X * 3 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 512);

        IsOpen = true;
    }

    public Pair Pair { get; init; }

    protected override void DrawInternal()
    {
        try
        {
            var spacing = ImGui.GetStyle().ItemSpacing;
            var umbraProfile = _umbraProfileManager.GetUmbraProfile(Pair.UserData);

            var accent = UiSharedService.AccentColor;
            if (accent.W <= 0f) accent = ImGuiColors.ParsedPurple;
            using (var topTabHoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, accent))
            using (var topTabActiveColor = ImRaii.PushColor(ImGuiCol.TabActive, accent))
            {
                using var tabBar = ImRaii.TabBar("StandaloneProfileTabBarV2");
                if (tabBar)
                {
                    using (var tabItem = ImRaii.TabItem("RP"))
                    {
                        if (tabItem) _isRpTab = true;
                    }
                    using (var tabItem = ImRaii.TabItem("HRP"))
                    {
                        if (tabItem) _isRpTab = false;
                    }
                }
            }

            var description = _isRpTab ? (umbraProfile.RpDescription ?? Loc.Get("UserProfile.NoRpDescription")) : umbraProfile.Description;
            var isNsfw = _isRpTab ? umbraProfile.IsRpNSFW : umbraProfile.IsNSFW;
            var pfpData = _isRpTab ? umbraProfile.RpImageData.Value : umbraProfile.ImageData.Value;

            if (_isRpTab)
            {
                if (_rpTextureWrap == null || !pfpData.SequenceEqual(_lastRpProfilePicture))
                {
                    _rpTextureWrap?.Dispose();
                    _lastRpProfilePicture = pfpData;
                    _rpTextureWrap = _uiSharedService.LoadImage(_lastRpProfilePicture);
                }
            }
            else
            {
                if (_textureWrap == null || !pfpData.SequenceEqual(_lastProfilePicture))
                {
                    _textureWrap?.Dispose();
                    _lastProfilePicture = pfpData;
                    _textureWrap = _uiSharedService.LoadImage(_lastProfilePicture);
                }
            }

            var currentTexture = _isRpTab ? _rpTextureWrap : _textureWrap;

            var drawList = ImGui.GetWindowDrawList();
            var rectMin = drawList.GetClipRectMin();
            var rectMax = drawList.GetClipRectMax();
            var headerSize = ImGui.GetCursorPosY() - ImGui.GetStyle().WindowPadding.Y;

            using (_uiSharedService.UidFont.Push())
                UiSharedService.ColorText(Pair.UserData.AliasOrUID + (_isRpTab ? " (RP)" : " (HRP)"), UiSharedService.AccentColor);

            if (!string.Equals(Pair.UserData.UID, _apiController.UID, StringComparison.Ordinal))
            {
                var reportButtonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.ExclamationTriangle, Loc.Get("StandaloneProfile.ReportButton"));
                ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - reportButtonSize);
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, Loc.Get("StandaloneProfile.ReportButton")))
                    Mediator.Publish(new OpenReportPopupMessage(Pair));
            }

            ImGuiHelpers.ScaledDummy(new Vector2(spacing.Y, spacing.Y));
            ImGui.Separator();
            var pos = ImGui.GetCursorPos() with { Y = ImGui.GetCursorPosY() - headerSize };
            ImGuiHelpers.ScaledDummy(new Vector2(256, 256 + spacing.Y));
            var postDummy = ImGui.GetCursorPosY();
            ImGui.SameLine();

            if (isNsfw && !_isRpTab) // HRP NSFW check (legacy)
            {
                // existing logic for NSFW could go here, but let's keep it simple for now as requested
            }

            var descriptionTextSize = ImGui.CalcTextSize(description, hideTextAfterDoubleHash: false, 256f);
            var descriptionChildHeight = rectMax.Y - pos.Y - rectMin.Y - spacing.Y * 2;
            if (descriptionTextSize.Y > descriptionChildHeight && !_adjustedForScrollBars)
            {
                Size = Size!.Value with { X = Size.Value.X + ImGui.GetStyle().ScrollbarSize };
                _adjustedForScrollBars = true;
            }
            else if (descriptionTextSize.Y < descriptionChildHeight && _adjustedForScrollBars)
            {
                Size = Size!.Value with { X = Size.Value.X - ImGui.GetStyle().ScrollbarSize };
                _adjustedForScrollBars = false;
            }
            var childFrame = ImGuiHelpers.ScaledVector2(256 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, descriptionChildHeight);
            childFrame = childFrame with
            {
                X = childFrame.X + (_adjustedForScrollBars ? ImGui.GetStyle().ScrollbarSize : 0),
                Y = childFrame.Y / ImGuiHelpers.GlobalScale
            };
            if (ImGui.BeginChildFrame(1000, childFrame))
            {
                using var _ = _uiSharedService.GameFont.Push();
                if (_isRpTab)
                {
                    if (!string.IsNullOrEmpty(umbraProfile.RpFirstName) || !string.IsNullOrEmpty(umbraProfile.RpLastName))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpFirstName") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextUnformatted($"{umbraProfile.RpFirstName} {umbraProfile.RpLastName}");
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpTitle))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpTitle") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextUnformatted(umbraProfile.RpTitle);
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpAge) || !string.IsNullOrEmpty(umbraProfile.RpHeight) || !string.IsNullOrEmpty(umbraProfile.RpBuild))
                    {
                        var details = new List<string>();
                        if (!string.IsNullOrEmpty(umbraProfile.RpAge)) details.Add($"{Loc.Get("UserProfile.RpAge")} : {umbraProfile.RpAge}");
                        if (!string.IsNullOrEmpty(umbraProfile.RpHeight)) details.Add($"{Loc.Get("UserProfile.RpHeight")} : {umbraProfile.RpHeight}");
                        if (!string.IsNullOrEmpty(umbraProfile.RpBuild)) details.Add($"{Loc.Get("UserProfile.RpBuild")} : {umbraProfile.RpBuild}");
                        ImGui.TextUnformatted(string.Join(" | ", details));
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpOccupation))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpOccupation") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextUnformatted(umbraProfile.RpOccupation);
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpAffiliation))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpAffiliation") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextUnformatted(umbraProfile.RpAffiliation);
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpAlignment))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpAlignment") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextUnformatted(umbraProfile.RpAlignment);
                    }
                    ImGui.Separator();
                }
                ImGui.TextWrapped(description);
                if (_isRpTab && !string.IsNullOrEmpty(umbraProfile.RpAdditionalInfo))
                {
                    ImGui.Separator();
                    UiSharedService.ColorText(Loc.Get("UserProfile.RpAdditionalInfo") + " :", accent);
                    ImGui.TextWrapped(umbraProfile.RpAdditionalInfo);
                }
            }
            ImGui.EndChildFrame();

            ImGui.SetCursorPosY(postDummy);
            var note = _serverManager.GetNoteForUid(Pair.UserData.UID);
            if (!string.IsNullOrEmpty(note))
            {
                UiSharedService.ColorText(note, ImGuiColors.DalamudGrey);
            }
            string status = Pair.IsVisible ? Loc.Get("StandaloneProfile.Status.Visible") : (Pair.IsOnline ? Loc.Get("StandaloneProfile.Status.Online") : Loc.Get("StandaloneProfile.Status.Offline"));
            UiSharedService.ColorText(status, (Pair.IsVisible || Pair.IsOnline) ? ImGuiColors.HealerGreen : UiSharedService.AccentColor);

            if (currentTexture != null)
            {
                var padding = ImGui.GetStyle().WindowPadding.X / 2;
                bool tallerThanWide = currentTexture.Height >= currentTexture.Width;
                var stretchFactor = tallerThanWide ? 256f * ImGuiHelpers.GlobalScale / currentTexture.Height : 256f * ImGuiHelpers.GlobalScale / currentTexture.Width;
                var newWidth = currentTexture.Width * stretchFactor;
                var newHeight = currentTexture.Height * stretchFactor;
                var remainingWidth = (256f * ImGuiHelpers.GlobalScale - newWidth) / 2f;
                var remainingHeight = (256f * ImGuiHelpers.GlobalScale - newHeight) / 2f;
                drawList.AddImage(currentTexture.Handle, new Vector2(rectMin.X + padding + remainingWidth, rectMin.Y + spacing.Y + pos.Y + remainingHeight),
                    new Vector2(rectMin.X + padding + remainingWidth + newWidth, rectMin.Y + spacing.Y + pos.Y + remainingHeight + newHeight));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during draw standalone profile");
        }
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}
