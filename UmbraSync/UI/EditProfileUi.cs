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
    private readonly UmbraProfileManager _umbraProfileManager;
    private readonly PairManager _pairManager;
    private readonly PairFactory _pairFactory;
    private readonly RpConfigService _rpConfigService;
    private readonly UiSharedService _uiSharedService;
    private bool _adjustedForScollBarsLocalProfile = false;
    private bool _adjustedForScollBarsOnlineProfile = false;
    private string _descriptionText = string.Empty;
    private string _rpDescriptionText = string.Empty;
    private string _rpFirstNameText = string.Empty;
    private string _rpLastNameText = string.Empty;
    private string _rpTitleText = string.Empty;
    private string _rpAgeText = string.Empty;
    private string _rpHeightText = string.Empty;
    private string _rpBuildText = string.Empty;
    private string _rpOccupationText = string.Empty;
    private string _rpAffiliationText = string.Empty;
    private string _rpAlignmentText = string.Empty;
    private string _rpAdditionalInfoText = string.Empty;
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
        UmbraProfileManager umbraProfileManager, PairManager pairManager, PairFactory pairFactory,
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
        _umbraProfileManager = umbraProfileManager;
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

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Eye, Loc.Get("EditProfile.PreviewButton")))
        {
            var myUserData = new UserData(_apiController.UID, _apiController.DisplayName);
            var pair = _pairManager.GetPairByUID(_apiController.UID) ?? _pairFactory.Create(myUserData);
            Mediator.Publish(new ProfileOpenStandaloneMessage(pair));
        }

        using (var topTabHoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, accent))
        using (var topTabActiveColor = ImRaii.PushColor(ImGuiCol.TabActive, accent))
        {
            using var tabBar = ImRaii.TabBar("ProfileTabBarV2");
            if (tabBar)
            {
                using (var tabItem = ImRaii.TabItem("RP"))
                {
                    if (tabItem) DrawProfileContent(true);
                }
                using (var tabItem = ImRaii.TabItem("HRP"))
                {
                    if (tabItem) DrawProfileContent(false);
                }
            }
        }
    }

    public void DrawInline()
    {
        var accent = UiSharedService.AccentColor;
        if (accent.W <= 0f) accent = ImGuiColors.ParsedPurple;

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Eye, Loc.Get("EditProfile.PreviewButton")))
        {
            var myUserData = new UserData(_apiController.UID, _apiController.DisplayName);
            var pair = _pairManager.GetPairByUID(_apiController.UID) ?? _pairFactory.Create(myUserData);
            Mediator.Publish(new ProfileOpenStandaloneMessage(pair));
        }

        using (var topTabHoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, accent))
        using (var topTabActiveColor = ImRaii.PushColor(ImGuiCol.TabActive, accent))
        {
            using var tabBar = ImRaii.TabBar("ProfileTabBarInlineV2");
            if (tabBar)
            {
                using (var tabItem = ImRaii.TabItem("RP"))
                {
                    if (tabItem) DrawProfileContent(true);
                }
                using (var tabItem = ImRaii.TabItem("HRP"))
                {
                    if (tabItem) DrawProfileContent(false);
                }
            }
        }
    }

    private void DrawProfileContent(bool isRp)
    {
        _uiSharedService.BigText(isRp ? "Profil RP" : Loc.Get("EditProfile.CurrentProfile"));
        ImGuiHelpers.ScaledDummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y / 2));

        var umbraProfile = _umbraProfileManager.GetUmbraProfile(new UserData(_apiController.UID));

        if (umbraProfile.IsFlagged)
        {
            UiSharedService.ColorTextWrapped(umbraProfile.Description, UiSharedService.AccentColor);
            return;
        }

        if (isRp)
        {
            if (!_rpProfileImage.SequenceEqual(_rpConfigService.Current.RpProfilePictureBase64 != null ? Convert.FromBase64String(_rpConfigService.Current.RpProfilePictureBase64) : []))
            {
                _rpProfileImage = _rpConfigService.Current.RpProfilePictureBase64 != null ? Convert.FromBase64String(_rpConfigService.Current.RpProfilePictureBase64) : [];
                _rpPfpTextureWrap?.Dispose();
                _rpPfpTextureWrap = _uiSharedService.LoadImage(_rpProfileImage);
            }

            if (!string.Equals(_rpProfileDescription, _rpConfigService.Current.RpDescription ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                _rpProfileDescription = _rpConfigService.Current.RpDescription ?? string.Empty;
                _rpDescriptionText = _rpProfileDescription;
                _rpFirstNameText = _rpConfigService.Current.RpFirstName ?? string.Empty;
                _rpLastNameText = _rpConfigService.Current.RpLastName ?? string.Empty;
                _rpTitleText = _rpConfigService.Current.RpTitle ?? string.Empty;
                _rpAgeText = _rpConfigService.Current.RpAge ?? string.Empty;
                _rpHeightText = _rpConfigService.Current.RpHeight ?? string.Empty;
                _rpBuildText = _rpConfigService.Current.RpBuild ?? string.Empty;
                _rpOccupationText = _rpConfigService.Current.RpOccupation ?? string.Empty;
                _rpAffiliationText = _rpConfigService.Current.RpAffiliation ?? string.Empty;
                _rpAlignmentText = _rpConfigService.Current.RpAlignment ?? string.Empty;
                _rpAdditionalInfoText = _rpConfigService.Current.RpAdditionalInfo ?? string.Empty;
            }
        }
        else
        {
            if (!_profileImage.SequenceEqual(umbraProfile.ImageData.Value))
            {
                _profileImage = umbraProfile.ImageData.Value;
                _pfpTextureWrap?.Dispose();
                _pfpTextureWrap = _uiSharedService.LoadImage(_profileImage);
            }

            if (!string.Equals(_profileDescription, umbraProfile.Description, StringComparison.OrdinalIgnoreCase))
            {
                _profileDescription = umbraProfile.Description;
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
        
        if (isRp)
        {
            ImGui.BeginGroup();
            var inputWidth = 200f;
            var labelWidth = 120f * ImGuiHelpers.GlobalScale;

            void DrawLabeledInput(string label, ref string text, int maxLength, bool sameLine = false)
            {
                if (sameLine)
                {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 20f * ImGuiHelpers.GlobalScale);
                }
                
                var startPosX = ImGui.GetCursorPosX();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(label);
                ImGui.SameLine();
                ImGui.SetCursorPosX(startPosX + labelWidth);
                ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * inputWidth);
                ImGui.InputText("##" + label, ref text, maxLength);
            }

            DrawLabeledInput(Loc.Get("UserProfile.RpFirstName"), ref _rpFirstNameText, 100);
            DrawLabeledInput(Loc.Get("UserProfile.RpLastName"), ref _rpLastNameText, 100, true);

            DrawLabeledInput(Loc.Get("UserProfile.RpTitle"), ref _rpTitleText, 100);
            DrawLabeledInput(Loc.Get("UserProfile.RpAge"), ref _rpAgeText, 50, true);

            DrawLabeledInput(Loc.Get("UserProfile.RpHeight"), ref _rpHeightText, 50);
            DrawLabeledInput(Loc.Get("UserProfile.RpBuild"), ref _rpBuildText, 100, true);

            DrawLabeledInput(Loc.Get("UserProfile.RpOccupation"), ref _rpOccupationText, 100);
            DrawLabeledInput(Loc.Get("UserProfile.RpAffiliation"), ref _rpAffiliationText, 100, true);

            DrawLabeledInput(Loc.Get("UserProfile.RpAlignment"), ref _rpAlignmentText, 100);
            ImGui.EndGroup();
        }

        using (_uiSharedService.GameFont.Push())
        {
            using var _ = ImRaii.PushId(isRp ? "rp" : "hrp");
            var descriptionText = isRp ? _rpDescriptionText : _descriptionText;
            ImGui.TextUnformatted(Loc.Get("EditProfile.Description"));
            if (ImGui.InputTextMultiline("##description_multi", ref descriptionText, 1000,
                    ImGuiHelpers.ScaledVector2(600, 150)))
            {
                if (isRp) _rpDescriptionText = descriptionText;
                else _descriptionText = descriptionText;
            }
        }

        if (isRp)
        {
            using (_uiSharedService.GameFont.Push())
            {
                ImGui.TextUnformatted(Loc.Get("UserProfile.RpAdditionalInfo"));
                ImGui.InputTextMultiline("##additional_info", ref _rpAdditionalInfoText, 3000,
                    ImGuiHelpers.ScaledVector2(600, 150));
            }
        }

        if (isRp)
        {
            var isRpNsfw = _rpConfigService.Current.IsRpNsfw;
            if (ImGui.Checkbox(Loc.Get("UserProfile.RpNsfw"), ref isRpNsfw))
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
                _rpConfigService.Current.RpFirstName = _rpFirstNameText;
                _rpConfigService.Current.RpLastName = _rpLastNameText;
                _rpConfigService.Current.RpTitle = _rpTitleText;
                _rpConfigService.Current.RpAge = _rpAgeText;
                _rpConfigService.Current.RpHeight = _rpHeightText;
                _rpConfigService.Current.RpBuild = _rpBuildText;
                _rpConfigService.Current.RpOccupation = _rpOccupationText;
                _rpConfigService.Current.RpAffiliation = _rpAffiliationText;
                _rpConfigService.Current.RpAlignment = _rpAlignmentText;
                _rpConfigService.Current.RpAdditionalInfo = _rpAdditionalInfoText;
                _rpConfigService.Save();
            }
            else
            {
                _profileDescription = _descriptionText;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var curProfile = await _apiController.UserGetProfile(new UserDto(new UserData(_apiController.UID))).ConfigureAwait(false);
                    if (isRp)
                    {
                        await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), curProfile.Disabled, curProfile.IsNSFW, curProfile.ProfilePictureBase64, curProfile.Description,
                            _rpConfigService.Current.RpProfilePictureBase64, _rpConfigService.Current.RpDescription, _rpConfigService.Current.IsRpNsfw,
                            _rpConfigService.Current.RpFirstName, _rpConfigService.Current.RpLastName, _rpConfigService.Current.RpTitle, _rpConfigService.Current.RpAge,
                            _rpConfigService.Current.RpHeight, _rpConfigService.Current.RpBuild, _rpConfigService.Current.RpOccupation, _rpConfigService.Current.RpAffiliation,
                            _rpConfigService.Current.RpAlignment, _rpConfigService.Current.RpAdditionalInfo)).ConfigureAwait(false);
                    }
                    else
                    {
                        await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), curProfile.Disabled, _apiController.IsProfileNsfw, curProfile.ProfilePictureBase64, _descriptionText,
                            curProfile.RpProfilePictureBase64, curProfile.RpDescription, curProfile.IsRpNSFW)).ConfigureAwait(false);
                    }
                    Mediator.Publish(new ClearProfileDataMessage(new UserData(_apiController.UID)));
                    Mediator.Publish(new NotificationMessage(Loc.Get("EditProfile.SaveSuccessTitle"), Loc.Get("EditProfile.SaveSuccessBody"), NotificationType.Info));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save profile");
                    Mediator.Publish(new NotificationMessage(Loc.Get("EditProfile.SaveErrorTitle"), Loc.Get("EditProfile.SaveErrorBody"), NotificationType.Error));
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
                                _rpConfigService.Current.RpProfilePictureBase64, _rpConfigService.Current.RpDescription, _rpConfigService.Current.IsRpNsfw,
                                _rpConfigService.Current.RpFirstName, _rpConfigService.Current.RpLastName, _rpConfigService.Current.RpTitle, _rpConfigService.Current.RpAge,
                                _rpConfigService.Current.RpHeight, _rpConfigService.Current.RpBuild, _rpConfigService.Current.RpOccupation, _rpConfigService.Current.RpAffiliation,
                                _rpConfigService.Current.RpAlignment, _rpConfigService.Current.RpAdditionalInfo)).ConfigureAwait(false);
                        }
                        else
                        {
                            await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), curProfile.Disabled, _apiController.IsProfileNsfw, Convert.ToBase64String(file), _descriptionText,
                                curProfile.RpProfilePictureBase64, curProfile.RpDescription, curProfile.IsRpNSFW)).ConfigureAwait(false);
                        }
                        Mediator.Publish(new ClearProfileDataMessage(new UserData(_apiController.UID)));
                        Mediator.Publish(new NotificationMessage(Loc.Get("EditProfile.SaveSuccessTitle"), Loc.Get("EditProfile.SaveSuccessBody"), NotificationType.Info));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to upload profile image");
                        Mediator.Publish(new NotificationMessage(Loc.Get("EditProfile.SaveErrorTitle"), Loc.Get("EditProfile.SaveErrorBody"), NotificationType.Error));
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
        var isNsfw = umbraProfile.IsNSFW;
        if (ImGui.Checkbox(Loc.Get("EditProfile.ProfileIsNsfw"), ref isNsfw))
        {
            _ = _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID), Disabled: false, isNsfw, ProfilePictureBase64: null, Description: null));
        }
        _uiSharedService.DrawHelpText(Loc.Get("EditProfile.ProfileIsNsfwHelp"));
        ImGui.Separator();
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
