using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using UmbraSync.API.Data;
using UmbraSync.API.Dto.User;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;
using UmbraSync.Utils;
using UmbraSync.WebAPI;
using UmbraSync.Localization;
using UmbraSync.Services.Notification;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Globalization;
using NotificationType = UmbraSync.MareConfiguration.Models.NotificationType;

namespace UmbraSync.UI;

public class EditProfileUi : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly FileDialogManager _fileDialogManager;
    private readonly MareProfileManager _mareProfileManager;
    private readonly UiSharedService _uiSharedService;
    private bool _adjustedForScollBarsLocalProfile = false;
    private bool _adjustedForScollBarsOnlineProfile = false;
    private string _descriptionText = string.Empty;
    private IDalamudTextureWrap? _pfpTextureWrap;
    private string _profileDescription = string.Empty;
    private byte[] _profileImage = [];
    private bool _showFileDialogError = false;
    private bool _wasOpen;
    private bool _vanityModalOpen = false;
    private string _vanityInput = string.Empty;

    public EditProfileUi(ILogger<EditProfileUi> logger, MareMediator mediator,
        ApiController apiController, UiSharedService uiSharedService, FileDialogManager fileDialogManager,
        MareProfileManager mareProfileManager, PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, $"{Loc.Get("EditProfile.WindowTitle")}###UmbraSyncEditProfileUI", performanceCollectorService)
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
        _uiSharedService.BigText(Loc.Get("EditProfile.CurrentProfile"));
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
        ImGui.Checkbox(Loc.Get("EditProfile.IsNsfwLabel"), ref nsfw);
        ImGui.EndDisabled();

        ImGui.Separator();
        _uiSharedService.BigText(Loc.Get("EditProfile.SettingsTitle"));

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.FileUpload, Loc.Get("EditProfile.UploadPicture")))
        {
            _fileDialogManager.OpenFileDialog(Loc.Get("EditProfile.UploadDialogTitle"), ".png", (success, file) =>
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
        UiSharedService.AttachToolTip(Loc.Get("EditProfile.UploadPictureTooltip"));
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, Loc.Get("EditProfile.ClearPicture")))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, IsNSFW: null, "", Description: null));
        }
        UiSharedService.AttachToolTip(Loc.Get("EditProfile.ClearPictureTooltip"));
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Tag, Loc.Get("EditProfile.SetCustomId.Button")))
        {
            _vanityInput = string.Empty;
            _vanityModalOpen = true;
            ImGui.OpenPopup("SetCustomIdModal");
        }
        UiSharedService.AttachToolTip(Loc.Get("EditProfile.SetCustomId.Tooltip"));
        if (_showFileDialogError)
        {
            UiSharedService.ColorTextWrapped(Loc.Get("EditProfile.UploadPictureError"), UiSharedService.AccentColor);
        }
        var isNsfw = profile.IsNSFW;
        if (ImGui.Checkbox(Loc.Get("EditProfile.ProfileIsNsfw"), ref isNsfw))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, isNsfw, ProfilePictureBase64: null, Description: null));
        }
        _uiSharedService.DrawHelpText(Loc.Get("EditProfile.ProfileIsNsfwHelp"));
        var widthTextBox = 400;
        var posX = ImGui.GetCursorPosX();
        ImGui.TextUnformatted(string.Format(CultureInfo.CurrentCulture, Loc.Get("EditProfile.DescriptionCounter"), _descriptionText.Length));
        ImGui.SetCursorPosX(posX);
        ImGuiHelpers.ScaledRelativeSameLine(widthTextBox, ImGui.GetStyle().ItemSpacing.X);
        ImGui.TextUnformatted(Loc.Get("EditProfile.DescriptionPreview"));
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

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, Loc.Get("EditProfile.SaveDescription")))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, IsNSFW: null, ProfilePictureBase64: null, _descriptionText));
        }
        UiSharedService.AttachToolTip(Loc.Get("EditProfile.SaveDescriptionTooltip"));
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, Loc.Get("EditProfile.ClearDescription")))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, IsNSFW: null, ProfilePictureBase64: null, ""));
        }
        UiSharedService.AttachToolTip(Loc.Get("EditProfile.ClearDescriptionTooltip"));
        
        if (_vanityModalOpen && ImGui.BeginPopupModal("SetCustomIdModal", ref _vanityModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted(Loc.Get("EditProfile.SetCustomId.Title"));
            ImGuiHelpers.ScaledDummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y / 2));
            ImGui.InputTextWithHint("##customId", Loc.Get("EditProfile.SetCustomId.Placeholder"), ref _vanityInput, 64);
            UiSharedService.AttachToolTip(Loc.Get("EditProfile.SetCustomId.FormatHint"));

            ImGuiHelpers.ScaledDummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y));
            if (ImGui.Button(Loc.Get("EditProfile.SetCustomId.Confirm")))
            {
                _ = SubmitVanityAsync(_vanityInput);
            }
            ImGui.SameLine();
            if (ImGui.Button(Loc.Get("Common.Cancel")))
            {
                _vanityModalOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _pfpTextureWrap?.Dispose();
    }

    private async Task SubmitVanityAsync(string input)
    {
        try
        {
            await _apiController.UserSetAlias(string.IsNullOrWhiteSpace(input) ? null : input).ConfigureAwait(false);
            Mediator.Publish(new NotificationMessage(Loc.Get("EditProfile.SetCustomId.SentTitle"), Loc.Get("EditProfile.SetCustomId.SentBody"), NotificationType.Info));
        }
        catch
        {
            Mediator.Publish(new NotificationMessage(Loc.Get("EditProfile.SetCustomId.ErrorTitle"), Loc.Get("EditProfile.SetCustomId.ErrorBody"), NotificationType.Error));
        }
        finally
        {
            _vanityModalOpen = false;
        }
    }
}
