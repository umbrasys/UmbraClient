using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using UmbraSync.API.Data;
using UmbraSync.API.Dto.User;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Factories;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;
using UmbraSync.Utils;
using UmbraSync.WebAPI;
using UmbraSync.Localization;
using OtterGui.Raii;
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
    private readonly PairManager _pairManager;
    private readonly PairFactory _pairFactory;
    private readonly RpConfigService _rpConfigService;
    private readonly UiSharedService _uiSharedService;
    private bool _adjustedForScollBarsLocalProfile = false;
    private bool _adjustedForScollBarsOnlineProfile = false;
    private string _descriptionText = string.Empty;
    private string _rpDescriptionText = string.Empty;
    private IDalamudTextureWrap? _pfpTextureWrap;
    private IDalamudTextureWrap? _rpPfpTextureWrap;
    private string _profileDescription = string.Empty;
    private string _rpProfileDescription = string.Empty;
    private byte[] _profileImage = [];
    private byte[] _rpProfileImage = [];
    private bool _showFileDialogError = false;
    private bool _wasOpen;
    private bool _vanityModalOpen = false;
    private string _vanityInput = string.Empty;

    public EditProfileUi(ILogger<EditProfileUi> logger, MareMediator mediator,
        ApiController apiController, UiSharedService uiSharedService, FileDialogManager fileDialogManager,
        MareProfileManager mareProfileManager, PairManager pairManager, PairFactory pairFactory,
        RpConfigService rpConfigService, PerformanceCollectorService performanceCollectorService)
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
        _pairManager = pairManager;
        _pairFactory = pairFactory;
        _rpConfigService = rpConfigService;

        Mediator.Subscribe<GposeStartMessage>(this, (_) => { _wasOpen = IsOpen; IsOpen = false; });
        Mediator.Subscribe<GposeEndMessage>(this, (_) => IsOpen = _wasOpen);
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData == null || string.Equals(msg.UserData.UID, _apiController.UID, StringComparison.Ordinal))
            {
                _pfpTextureWrap?.Dispose();
                _pfpTextureWrap = null;
                _rpPfpTextureWrap?.Dispose();
                _rpPfpTextureWrap = null;
            }
        });
    }

    protected override void DrawInternal()
    {
        var accent = UiSharedService.AccentColor;
        if (accent.W <= 0f) accent = ImGuiColors.ParsedPurple;

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Eye, "Prévisualiser son profil"))
        {
            var myUserData = new UserData(_apiController.UID, _apiController.DisplayName);
            var pair = _pairManager.GetPairByUID(_apiController.UID) ?? _pairFactory.Create(myUserData);
            Mediator.Publish(new ProfileOpenStandaloneMessage(pair));
        }

        using (var topTabHoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, accent))
        using (var topTabActiveColor = ImRaii.PushColor(ImGuiCol.TabActive, accent))
        {
            if (ImGui.BeginTabBar("ProfileTabBarV2", ImGuiTabBarFlags.None))
            {
                if (ImGui.BeginTabItem("RP"))
                {
                    DrawProfileContent(true);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("HRP"))
                {
                    DrawProfileContent(false);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
    }

    public void DrawInline()
    {
        var accent = UiSharedService.AccentColor;
        if (accent.W <= 0f) accent = ImGuiColors.ParsedPurple;

        using (var topTabHoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, accent))
        using (var topTabActiveColor = ImRaii.PushColor(ImGuiCol.TabActive, accent))
        {
            if (ImGui.BeginTabBar("ProfileTabBarInlineV2", ImGuiTabBarFlags.None))
            {
                if (ImGui.BeginTabItem("RP"))
                {
                    DrawProfileContent(true);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("HRP"))
                {
                    DrawProfileContent(false);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
    }

    private void DrawProfileContent(bool isRp)
    {
        _uiSharedService.BigText(isRp ? "Profil RP" : Loc.Get("EditProfile.CurrentProfile"));
        ImGuiHelpers.ScaledDummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y / 2));

        var profile = _mareProfileManager.GetMareProfile(new UserData(_apiController.UID));

        if (profile.IsFlagged)
        {
            UiSharedService.ColorTextWrapped(profile.Description, UiSharedService.AccentColor);
            return;
        }

        if (isRp)
        {
            if (!_rpProfileImage.SequenceEqual(profile.RpImageData.Value))
            {
                _rpProfileImage = profile.RpImageData.Value;
                _rpPfpTextureWrap?.Dispose();
                _rpPfpTextureWrap = _uiSharedService.LoadImage(_rpProfileImage);
            }

            if (!string.Equals(_rpProfileDescription, profile.RpDescription ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                _rpProfileDescription = profile.RpDescription ?? string.Empty;
                _rpDescriptionText = _rpProfileDescription;
            }
        }
        else
        {
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
        }

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var pfpTexture = isRp ? _rpPfpTextureWrap : _pfpTextureWrap;
        if (pfpTexture != null)
        {
            ImGui.Image(pfpTexture.Handle, ImGuiHelpers.ScaledVector2(pfpTexture.Width, pfpTexture.Height));
            ImGuiHelpers.ScaledRelativeSameLine(256, spacing);
        }
        using (_uiSharedService.GameFont.Push())
        {
            using var _ = ImRaii.PushId(isRp ? "rp" : "hrp");
            var descriptionText = isRp ? _rpDescriptionText : _descriptionText;
            if (ImGui.InputTextMultiline(Loc.Get("EditProfile.Description"), ref descriptionText, 1000,
                    ImGuiHelpers.ScaledVector2(512, 256)))
            {
                if (isRp) _rpDescriptionText = descriptionText;
                else _descriptionText = descriptionText;
            }
        }

        if (isRp)
        {
            var isRpNsfw = _rpConfigService.Current.IsRpNsfw;
            if (ImGui.Checkbox("Est-ce un profil RP NSFW ?", ref isRpNsfw))
            {
                _rpConfigService.Current.IsRpNsfw = isRpNsfw;
                _rpConfigService.Save();
            }
        }
        else
        {
            var isHrpNsfw = _apiController.IsProfileNsfw;
            if (ImGui.Checkbox(Loc.Get("EditProfile.IsNsfw"), ref isHrpNsfw))
            {
                _apiController.IsProfileNsfw = isHrpNsfw;
            }
        }

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, Loc.Get("EditProfile.SaveButton")))
        {
            if (isRp)
            {
                _rpConfigService.Current.RpDescription = _rpDescriptionText;
                _rpConfigService.Save();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var curProfile = await _apiController.UserGetProfile(new UserDto(new UserData(_apiController.UID))).ConfigureAwait(false);
                    if (isRp)
                    {
                        await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), curProfile.Disabled, curProfile.IsNSFW, curProfile.ProfilePictureBase64, curProfile.Description,
                            _rpConfigService.Current.RpProfilePictureBase64, _rpConfigService.Current.RpDescription, _rpConfigService.Current.IsRpNsfw)).ConfigureAwait(false);
                    }
                    else
                    {
                        await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), curProfile.Disabled, _apiController.IsProfileNsfw, curProfile.ProfilePictureBase64, _descriptionText,
                            curProfile.RpProfilePictureBase64, curProfile.RpDescription, curProfile.IsRpNSFW)).ConfigureAwait(false);
                    }
                    Mediator.Publish(new ClearProfileDataMessage(new UserData(_apiController.UID)));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save profile");
                }
            });
        }
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.FileImage, Loc.Get("EditProfile.SelectImageButton")))
        {
            _fileDialogManager.OpenFileDialog(Loc.Get("EditProfile.SelectImageButton"), "Image files{.png,.jpg,.jpeg}", (success, name) =>
            {
                if (!success) return;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var file = await File.ReadAllBytesAsync(name).ConfigureAwait(false);
                        if (file.Length > 250 * 1024)
                        {
                            _showFileDialogError = true;
                            return;
                        }

                        var curProfile = await _apiController.UserGetProfile(new UserDto(new UserData(_apiController.UID))).ConfigureAwait(false);
                        if (isRp)
                        {
                            _rpConfigService.Current.RpProfilePictureBase64 = Convert.ToBase64String(file);
                            _rpConfigService.Save();
                            await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), curProfile.Disabled, curProfile.IsNSFW, curProfile.ProfilePictureBase64, curProfile.Description,
                                _rpConfigService.Current.RpProfilePictureBase64, _rpConfigService.Current.RpDescription, _rpConfigService.Current.IsRpNsfw)).ConfigureAwait(false);
                        }
                        else
                        {
                            await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), curProfile.Disabled, _apiController.IsProfileNsfw, Convert.ToBase64String(file), _descriptionText,
                                curProfile.RpProfilePictureBase64, curProfile.RpDescription, curProfile.IsRpNSFW)).ConfigureAwait(false);
                        }
                        Mediator.Publish(new ClearProfileDataMessage(new UserData(_apiController.UID)));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to upload profile image");
                    }
                });
            });
        }
        if (_showFileDialogError)
        {
            UiSharedService.ColorTextWrapped(Loc.Get("EditProfile.ImageSizeError"), ImGuiColors.DalamudRed);
        }
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
