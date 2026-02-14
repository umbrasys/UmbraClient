using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.Logging;
using System.Numerics;
using UmbraSync.Localization;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;

namespace UmbraSync.UI;

public class StandaloneProfileUi : WindowMediatorSubscriberBase
{
    private readonly UmbraProfileManager _umbraProfileManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private byte[] _lastProfilePicture = [];
    private byte[] _lastRpProfilePicture = [];
    private IDalamudTextureWrap? _textureWrap;
    private IDalamudTextureWrap? _rpTextureWrap;
    private bool _isRpTab = false;
    private bool _windowSizeInitialized = false;

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
        Flags = ImGuiWindowFlags.None;

        SizeConstraints = new()
        {
            MinimumSize = new(650, 400),
            MaximumSize = new(1200, 2000)
        };

        IsOpen = true;
    }

    public Pair Pair { get; init; }

    protected override void DrawInternal()
    {
        try
        {
            if (!_windowSizeInitialized)
            {
                ImGui.SetWindowSize(new Vector2(800, 600));
                _windowSizeInitialized = true;
            }

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

            var availableWidth = ImGui.GetContentRegionAvail().X;
            var imageSize = Math.Min(256f * ImGuiHelpers.GlobalScale, availableWidth * 0.4f);
            var contentWidth = availableWidth - imageSize - spacing.X * 2;

            var pos = ImGui.GetCursorPos();
            ImGui.BeginGroup();
            ImGuiHelpers.ScaledDummy(new Vector2(imageSize / ImGuiHelpers.GlobalScale, imageSize / ImGuiHelpers.GlobalScale + spacing.Y));
            ImGui.EndGroup();
            ImGui.SameLine();

            if (isNsfw && !_isRpTab) // HRP NSFW check (legacy)
            {
                // existing logic for NSFW could go here, but let's keep it simple for now as requested
            }

            var descriptionChildHeight = Math.Max(imageSize, ImGui.GetContentRegionAvail().Y - spacing.Y * 2);
            var childFrameWidth = contentWidth;

            if (ImGui.BeginChild("ProfileContent", new Vector2(childFrameWidth, descriptionChildHeight), false))
            {
                using var _ = _uiSharedService.GameFont.Push();
                if (_isRpTab)
                {
                    if (!string.IsNullOrEmpty(umbraProfile.RpFirstName))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpFirstName") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextWrapped(umbraProfile.RpFirstName);
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpLastName))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpLastName") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextWrapped(umbraProfile.RpLastName);
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpTitle))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpTitle") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextWrapped(umbraProfile.RpTitle);
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpAge))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpAge") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextWrapped(umbraProfile.RpAge);
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpHeight))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpHeight") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextWrapped(umbraProfile.RpHeight);
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpBuild))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpBuild") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextWrapped(umbraProfile.RpBuild);
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpOccupation))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpOccupation") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextWrapped(umbraProfile.RpOccupation);
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpAffiliation))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpAffiliation") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextWrapped(umbraProfile.RpAffiliation);
                    }
                    if (!string.IsNullOrEmpty(umbraProfile.RpAlignment))
                    {
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpAlignment") + " : ", accent);
                        ImGui.SameLine();
                        ImGui.TextWrapped(umbraProfile.RpAlignment);
                    }

                    if (!string.IsNullOrEmpty(description))
                    {
                        ImGui.Spacing();
                        ImGui.Spacing();
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpDescription") + " :", accent);
                        ImGui.TextWrapped(description);
                    }

                    if (!string.IsNullOrEmpty(umbraProfile.RpAdditionalInfo))
                    {
                        ImGui.Spacing();
                        ImGui.Spacing();
                        UiSharedService.ColorText(Loc.Get("UserProfile.RpAdditionalInfo") + " :", accent);
                        ImGui.TextWrapped(umbraProfile.RpAdditionalInfo);
                    }
                }
                else
                {
                    ImGui.TextWrapped(description);
                }
            }
            ImGui.EndChild();

            if (currentTexture != null)
            {
                var padding = ImGui.GetStyle().WindowPadding.X;
                bool tallerThanWide = currentTexture.Height >= currentTexture.Width;
                var stretchFactor = tallerThanWide ? imageSize / currentTexture.Height : imageSize / currentTexture.Width;
                var newWidth = currentTexture.Width * stretchFactor;
                var newHeight = currentTexture.Height * stretchFactor;
                var remainingWidth = (imageSize - newWidth) / 2f;
                var remainingHeight = (imageSize - newHeight) / 2f;
                drawList.AddImage(currentTexture.Handle, new Vector2(rectMin.X + padding + remainingWidth, rectMin.Y + spacing.Y + pos.Y + remainingHeight),
                    new Vector2(rectMin.X + padding + remainingWidth + newWidth, rectMin.Y + spacing.Y + pos.Y + remainingHeight + newHeight));
            }

            bool isSelf = string.Equals(Pair.UserData.UID, _apiController.UID, StringComparison.Ordinal);
            string status;
            Vector4 statusColor;

            if (isSelf)
            {
                status = _apiController.IsConnected ? Loc.Get("StandaloneProfile.Status.Online") : Loc.Get("StandaloneProfile.Status.Offline");
                statusColor = _apiController.IsConnected ? ImGuiColors.HealerGreen : UiSharedService.AccentColor;
            }
            else
            {
                status = Pair.IsVisible ? Loc.Get("StandaloneProfile.Status.Visible") : (Pair.IsOnline ? Loc.Get("StandaloneProfile.Status.Online") : Loc.Get("StandaloneProfile.Status.Offline"));
                statusColor = (Pair.IsVisible || Pair.IsOnline) ? ImGuiColors.HealerGreen : UiSharedService.AccentColor;
            }

            var statusTextPos = new Vector2(rectMin.X + ImGui.GetStyle().WindowPadding.X, rectMin.Y + spacing.Y + pos.Y + imageSize + spacing.Y);
            drawList.AddText(statusTextPos, ImGui.GetColorU32(statusColor), status);

            var note = _serverManager.GetNoteForUid(Pair.UserData.UID);
            if (!string.IsNullOrEmpty(note))
            {
                var noteTextSize = ImGui.CalcTextSize(status);
                var noteTextPos = new Vector2(rectMin.X + ImGui.GetStyle().WindowPadding.X, statusTextPos.Y + noteTextSize.Y + spacing.Y / 2);
                drawList.AddText(noteTextPos, ImGui.GetColorU32(ImGuiColors.DalamudGrey), note);
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