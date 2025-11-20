using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
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
    private readonly MareProfileManager _mareProfileManager;
    private readonly ServerConfigurationManager _serverManager;
    private readonly UiSharedService _uiSharedService;
    private bool _adjustedForScrollBars = false;
    private byte[] _lastProfilePicture = [];
    private IDalamudTextureWrap? _textureWrap;

    public StandaloneProfileUi(ILogger<StandaloneProfileUi> logger, MareMediator mediator, UiSharedService uiBuilder,
        ServerConfigurationManager serverManager, MareProfileManager mareProfileManager, Pair pair,
        PerformanceCollectorService performanceCollector)
        : base(logger, mediator, string.Format(System.Globalization.CultureInfo.CurrentCulture, Loc.Get("StandaloneProfile.WindowTitle"), pair.UserData.AliasOrUID) + "##UmbraSyncStandaloneProfileUI" + pair.UserData.AliasOrUID, performanceCollector)
    {
        _uiSharedService = uiBuilder;
        _serverManager = serverManager;
        _mareProfileManager = mareProfileManager;
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

            var mareProfile = _mareProfileManager.GetMareProfile(Pair.UserData);

            if (_textureWrap == null || !mareProfile.ImageData.Value.SequenceEqual(_lastProfilePicture))
            {
                _textureWrap?.Dispose();
                _lastProfilePicture = mareProfile.ImageData.Value;
                _textureWrap = _uiSharedService.LoadImage(_lastProfilePicture);
            }

            var drawList = ImGui.GetWindowDrawList();
            var rectMin = drawList.GetClipRectMin();
            var rectMax = drawList.GetClipRectMax();
            var headerSize = ImGui.GetCursorPosY() - ImGui.GetStyle().WindowPadding.Y;

            using (_uiSharedService.UidFont.Push())
                UiSharedService.ColorText(Pair.UserData.AliasOrUID, UiSharedService.AccentColor);

            var reportButtonSize = _uiSharedService.GetIconTextButtonSize(FontAwesomeIcon.ExclamationTriangle, Loc.Get("StandaloneProfile.ReportButton"));
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - reportButtonSize);
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.ExclamationTriangle, Loc.Get("StandaloneProfile.ReportButton")))
                Mediator.Publish(new OpenReportPopupMessage(Pair));

            ImGuiHelpers.ScaledDummy(new Vector2(spacing.Y, spacing.Y));
            ImGui.Separator();
            var pos = ImGui.GetCursorPos() with { Y = ImGui.GetCursorPosY() - headerSize };
            ImGuiHelpers.ScaledDummy(new Vector2(256, 256 + spacing.Y));
            var postDummy = ImGui.GetCursorPosY();
            ImGui.SameLine();
            var descriptionTextSize = ImGui.CalcTextSize(mareProfile.Description, hideTextAfterDoubleHash: false, 256f);
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
                ImGui.TextWrapped(mareProfile.Description);
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
            if (Pair.IsVisible)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"({Pair.PlayerName})");
            }
            if (Pair.UserPair != null)
            {
                ImGui.TextUnformatted(Loc.Get("StandaloneProfile.PairStatus.Direct"));
                if (Pair.UserPair.OwnPermissions.IsPaused())
                {
                    ImGui.SameLine();
                    UiSharedService.ColorText(Loc.Get("StandaloneProfile.PairStatus.YouPaused"), ImGuiColors.DalamudYellow);
                }
                if (Pair.UserPair.OtherPermissions.IsPaused())
                {
                    ImGui.SameLine();
                    UiSharedService.ColorText(Loc.Get("StandaloneProfile.PairStatus.TheyPaused"), ImGuiColors.DalamudYellow);
                }
            }

            if (Pair.GroupPair.Any())
            {
                ImGui.TextUnformatted(Loc.Get("StandaloneProfile.PairStatus.SyncshellHeader"));
                foreach (var groupPair in Pair.GroupPair.Select(k => k.Key))
                {
                    var groupNote = _serverManager.GetNoteForGid(groupPair.GID);
                    var groupName = groupPair.GroupAliasOrGID;
                    var groupString = string.IsNullOrEmpty(groupNote) ? groupName : $"{groupNote} ({groupName})";
                    ImGui.TextUnformatted("- " + groupString);
                }
            }

            var padding = ImGui.GetStyle().WindowPadding.X / 2;
            bool tallerThanWide = _textureWrap.Height >= _textureWrap.Width;
            var stretchFactor = tallerThanWide ? 256f * ImGuiHelpers.GlobalScale / _textureWrap.Height : 256f * ImGuiHelpers.GlobalScale / _textureWrap.Width;
            var newWidth = _textureWrap.Width * stretchFactor;
            var newHeight = _textureWrap.Height * stretchFactor;
            var remainingWidth = (256f * ImGuiHelpers.GlobalScale - newWidth) / 2f;
            var remainingHeight = (256f * ImGuiHelpers.GlobalScale - newHeight) / 2f;
            drawList.AddImage(_textureWrap.Handle, new Vector2(rectMin.X + padding + remainingWidth, rectMin.Y + spacing.Y + pos.Y + remainingHeight),
                new Vector2(rectMin.X + padding + remainingWidth + newWidth, rectMin.Y + spacing.Y + pos.Y + remainingHeight + newHeight));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during draw tooltip");
        }
    }

    public override void OnClose()
    {
        Mediator.Publish(new RemoveWindowMessage(this));
    }
}
