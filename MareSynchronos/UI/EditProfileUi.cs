using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.User;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class EditProfileUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly FileDialogManager _fileDialogManager;
    private readonly MareProfileManager _mareProfileManager;
    private readonly UiSharedService _uiSharedService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private bool _adjustedForScollBarsLocalProfile = false;
    private bool _adjustedForScollBarsOnlineProfile = false;
    private string _descriptionText = string.Empty;
    private IDalamudTextureWrap? _pfpTextureWrap;
    private string _profileDescription = string.Empty;
    private byte[] _profileImage = [];
    private bool _showFileDialogError = false;
    private bool _wasOpen;

    public EditProfileUi(ILogger<EditProfileUi> logger, MareMediator mediator,
        ApiController apiController, UiSharedService uiSharedService, FileDialogManager fileDialogManager,
        ServerConfigurationManager serverConfigurationManager,
        MareProfileManager mareProfileManager, PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "Umbra Edit Profile###UmbraSyncEditProfileUI", performanceCollectorService)
    {
        IsOpen = false;
        this.SizeConstraints = new()
        {
            MinimumSize = new(768, 512),
            MaximumSize = new(768, 2000)
        };
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _fileDialogManager = fileDialogManager;
        _serverConfigurationManager = serverConfigurationManager;
        _mareProfileManager = mareProfileManager;

        Mediator.Subscribe<GposeStartMessage>(this, (_) => { _wasOpen = IsOpen; IsOpen = false; });
        Mediator.Subscribe<GposeEndMessage>(this, (_) => IsOpen = _wasOpen);
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData == null || string.Equals(msg.UserData.UID, _apiController.UID, StringComparison.Ordinal))
            {
                _pfpTextureWrap?.Dispose();
                _pfpTextureWrap = null;
            }
        });
    }

    protected override void DrawInternal()
    {
        DrawProfileContent();
    }

    public void DrawInline()
    {
        DrawProfileContent();
    }

    private void DrawProfileContent()
    {
        _uiSharedService.BigText("Current Profile (as saved on server)");
        ImGuiHelpers.ScaledDummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y / 2));

        var profile = _mareProfileManager.GetMareProfile(new UserData(_apiController.UID));

        if (profile.IsFlagged)
        {
            UiSharedService.ColorTextWrapped(profile.Description, UiSharedService.AccentColor);
            return;
        }

        if (!_profileImage.SequenceEqual(profile.ImageData.Value))
        {
            _profileImage = profile.ImageData.Value;
            _pfpTextureWrap?.Dispose();
            _pfpTextureWrap = _uiSharedService.LoadImage(_profileImage);
        }

        if (!string.Equals(_profileDescription, profile.Description, StringComparison.OrdinalIgnoreCase))
        {
            _profileDescription = profile.Description;
            _descriptionText = _profileDescription;
        }

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        if (_pfpTextureWrap != null)
        {
            ImGui.Image(_pfpTextureWrap.Handle, ImGuiHelpers.ScaledVector2(_pfpTextureWrap.Width, _pfpTextureWrap.Height));
            ImGuiHelpers.ScaledRelativeSameLine(256, spacing);
        }
        using (_uiSharedService.GameFont.Push())
        {
            var descriptionTextSize = ImGui.CalcTextSize(profile.Description, hideTextAfterDoubleHash: false, 256f);
            var childFrame = ImGuiHelpers.ScaledVector2(256 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 256);
            if (descriptionTextSize.Y > childFrame.Y)
            {
                _adjustedForScollBarsOnlineProfile = true;
            }
            else
            {
                _adjustedForScollBarsOnlineProfile = false;
            }
            childFrame = childFrame with
            {
                X = childFrame.X + (_adjustedForScollBarsOnlineProfile ? ImGui.GetStyle().ScrollbarSize : 0),
            };
            if (ImGui.BeginChildFrame(101, childFrame))
            {
                UiSharedService.TextWrapped(profile.Description);
            }
            ImGui.EndChildFrame();
        }

        var nsfw = profile.IsNSFW;
        ImGui.BeginDisabled();
        ImGui.Checkbox("Is NSFW", ref nsfw);
        ImGui.EndDisabled();

        ImGui.Separator();
        _uiSharedService.BigText("Profile Settings");

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.FileUpload, "Upload new profile picture"))
        {
            _fileDialogManager.OpenFileDialog("Select new Profile picture", ".png", (success, file) =>
            {
                if (!success) return;
                _ = Task.Run(async () =>
                {
                    var fileContent = File.ReadAllBytes(file);
                    using MemoryStream ms = new(fileContent);
                    var format = PngHdr.TryExtractDimensions(ms);

                    if (format.Width > 256 || format.Height > 256 || (fileContent.Length > 250 * 1024))
                    {
                        _showFileDialogError = true;
                        return;
                    }

                    _showFileDialogError = false;
                    await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, IsNSFW: null, Convert.ToBase64String(fileContent), Description: null))
                        .ConfigureAwait(false);
                });
            });
        }
        UiSharedService.AttachToolTip("Select and upload a new profile picture");
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear uploaded profile picture"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, IsNSFW: null, "", Description: null));
        }
        UiSharedService.AttachToolTip("Clear your currently uploaded profile picture");
        if (_showFileDialogError)
        {
            UiSharedService.ColorTextWrapped("The profile picture must be a PNG file with a maximum height and width of 256px and 250KiB size", UiSharedService.AccentColor);
        }
        var isNsfw = profile.IsNSFW;
        if (ImGui.Checkbox("Profile is NSFW", ref isNsfw))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, isNsfw, ProfilePictureBase64: null, Description: null));
        }
        _uiSharedService.DrawHelpText("If your profile description or image can be considered NSFW, toggle this to ON");
        var widthTextBox = 400;
        var posX = ImGui.GetCursorPosX();
        ImGui.TextUnformatted($"Description {_descriptionText.Length}/1500");
        ImGui.SetCursorPosX(posX);
        ImGuiHelpers.ScaledRelativeSameLine(widthTextBox, ImGui.GetStyle().ItemSpacing.X);
        ImGui.TextUnformatted("Preview (approximate)");
        using (_uiSharedService.GameFont.Push())
            ImGui.InputTextMultiline("##description", ref _descriptionText, 1500, ImGuiHelpers.ScaledVector2(widthTextBox, 200));

        ImGui.SameLine();

        using (_uiSharedService.GameFont.Push())
        {
            var descriptionTextSizeLocal = ImGui.CalcTextSize(_descriptionText, hideTextAfterDoubleHash: false, 256f);
            var childFrameLocal = ImGuiHelpers.ScaledVector2(256 + ImGui.GetStyle().WindowPadding.X + ImGui.GetStyle().WindowBorderSize, 200);
            if (descriptionTextSizeLocal.Y > childFrameLocal.Y)
            {
                _adjustedForScollBarsLocalProfile = true;
            }
            else
            {
                _adjustedForScollBarsLocalProfile = false;
            }
            childFrameLocal = childFrameLocal with
            {
                X = childFrameLocal.X + (_adjustedForScollBarsLocalProfile ? ImGui.GetStyle().ScrollbarSize : 0),
            };
            if (ImGui.BeginChildFrame(102, childFrameLocal))
            {
                UiSharedService.TextWrapped(_descriptionText);
            }
            ImGui.EndChildFrame();
        }

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, "Save Description"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, IsNSFW: null, ProfilePictureBase64: null, _descriptionText));
        }
        UiSharedService.AttachToolTip("Sets your profile description text");
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Clear Description"))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, IsNSFW: null, ProfilePictureBase64: null, ""));
        }
        UiSharedService.AttachToolTip("Clears your profile description text");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _pfpTextureWrap?.Dispose();
    }
}
