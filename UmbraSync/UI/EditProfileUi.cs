using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Microsoft.Extensions.Logging;
using OtterGui.Raii;
using System.Numerics;
using UmbraSync.API.Data;
using UmbraSync.API.Dto.User;
using UmbraSync.Localization;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Factories;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.Notification;
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
    private readonly DalamudUtilService _dalamudUtil;
    private readonly NotificationTracker _notificationTracker;
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
    private byte[] _profileImage = [];
    private byte[] _rpProfileImage = [];
    private bool _showFileDialogError = false;
    private bool _wasOpen;
    private bool _rpLoaded = false;
    private bool _hrpLoaded = false;

    public EditProfileUi(ILogger<EditProfileUi> logger, MareMediator mediator,
        ApiController apiController, UiSharedService uiSharedService, FileDialogManager fileDialogManager,
        UmbraProfileManager umbraProfileManager, PairManager pairManager, PairFactory pairFactory,
        RpConfigService rpConfigService, DalamudUtilService dalamudUtil, NotificationTracker notificationTracker,
        PerformanceCollectorService performanceCollectorService)
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
        _dalamudUtil = dalamudUtil;
        _notificationTracker = notificationTracker;

        Mediator.Subscribe<GposeStartMessage>(this, (_) => { _wasOpen = IsOpen; IsOpen = false; });
        Mediator.Subscribe<GposeEndMessage>(this, (_) => IsOpen = _wasOpen);
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) =>
        {
            _rpLoaded = false;
            _hrpLoaded = false;
        });
        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData != null && string.Equals(msg.UserData.UID, _apiController.UID, StringComparison.Ordinal))
            {
                _pfpTextureWrap?.Dispose();
                _pfpTextureWrap = null;
                _rpPfpTextureWrap?.Dispose();
                _rpPfpTextureWrap = null;
                _rpLoaded = false;
                _hrpLoaded = false;
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

            var currentProfile = _rpConfigService.GetCurrentCharacterProfile();
            var previewProfileData = new UmbraProfileData(
                IsFlagged: false,
                IsNSFW: _apiController.IsProfileNsfw,
                Base64ProfilePicture: string.Empty,
                Description: _descriptionText,
                Base64RpProfilePicture: currentProfile.RpProfilePictureBase64,
                RpDescription: _rpDescriptionText,
                IsRpNSFW: currentProfile.IsRpNsfw,
                RpFirstName: _rpFirstNameText,
                RpLastName: _rpLastNameText,
                RpTitle: _rpTitleText,
                RpAge: _rpAgeText,
                RpHeight: _rpHeightText,
                RpBuild: _rpBuildText,
                RpOccupation: _rpOccupationText,
                RpAffiliation: _rpAffiliationText,
                RpAlignment: _rpAlignmentText,
                RpAdditionalInfo: _rpAdditionalInfoText
            );

            _umbraProfileManager.SetPreviewProfile(pair.UserData, pair.PlayerName, pair.WorldId, previewProfileData);
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

            var currentProfile = _rpConfigService.GetCurrentCharacterProfile();
            var previewProfileData = new UmbraProfileData(
                IsFlagged: false,
                IsNSFW: _apiController.IsProfileNsfw,
                Base64ProfilePicture: string.Empty,
                Description: _descriptionText,
                Base64RpProfilePicture: currentProfile.RpProfilePictureBase64,
                RpDescription: _rpDescriptionText,
                IsRpNSFW: currentProfile.IsRpNsfw,
                RpFirstName: _rpFirstNameText,
                RpLastName: _rpLastNameText,
                RpTitle: _rpTitleText,
                RpAge: _rpAgeText,
                RpHeight: _rpHeightText,
                RpBuild: _rpBuildText,
                RpOccupation: _rpOccupationText,
                RpAffiliation: _rpAffiliationText,
                RpAlignment: _rpAlignmentText,
                RpAdditionalInfo: _rpAdditionalInfoText
            );

            _umbraProfileManager.SetPreviewProfile(pair.UserData, pair.PlayerName, pair.WorldId, previewProfileData);
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
            if (!_rpLoaded)
            {
                var profile = _rpConfigService.GetCurrentCharacterProfile();
                _rpDescriptionText = profile.RpDescription;
                _rpFirstNameText = profile.RpFirstName;
                _rpLastNameText = profile.RpLastName;
                _rpTitleText = profile.RpTitle;
                _rpAgeText = profile.RpAge;
                _rpHeightText = profile.RpHeight;
                _rpBuildText = profile.RpBuild;
                _rpOccupationText = profile.RpOccupation;
                _rpAffiliationText = profile.RpAffiliation;
                _rpAlignmentText = profile.RpAlignment;
                _rpAdditionalInfoText = profile.RpAdditionalInfo;

                try
                {
                    _rpProfileImage = !string.IsNullOrEmpty(profile.RpProfilePictureBase64) ? Convert.FromBase64String(profile.RpProfilePictureBase64) : [];
                    _rpPfpTextureWrap?.Dispose();
                    _rpPfpTextureWrap = _rpProfileImage.Length > 0 ? _uiSharedService.LoadImage(_rpProfileImage) : null;
                    _logger.LogDebug("Loaded RP profile image, size: {size}", _rpProfileImage.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load RP profile image");
                    _rpProfileImage = [];
                    _rpPfpTextureWrap = null;
                }

                _rpLoaded = true;
            }
        }
        else
        {
            if (!_hrpLoaded)
            {
                _descriptionText = umbraProfile.Description;

                try
                {
                    _profileImage = umbraProfile.ImageData.Value;
                    _pfpTextureWrap?.Dispose();
                    _pfpTextureWrap = _profileImage.Length > 0 ? _uiSharedService.LoadImage(_profileImage) : null;
                    _logger.LogDebug("Loaded HRP profile image, size: {size}", _profileImage.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load HRP profile image");
                    _profileImage = [];
                    _pfpTextureWrap = null;
                }

                _hrpLoaded = true;
            }
        }

        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var pfpTexture = isRp ? _rpPfpTextureWrap : _pfpTextureWrap;
        var pfpBytes = isRp ? _rpProfileImage : _profileImage;

        float previewSize = 200f * ImGuiHelpers.GlobalScale;
        ImGui.BeginGroup();
        if (pfpTexture != null && pfpBytes.Length > 0)
        {
            float ratio = (float)pfpTexture.Width / pfpTexture.Height;
            Vector2 drawSize;
            if (ratio > 1) drawSize = new Vector2(previewSize, previewSize / ratio);
            else drawSize = new Vector2(previewSize * ratio, previewSize);

            var cursor = ImGui.GetCursorPos();
            ImGui.SetCursorPos(cursor + new Vector2((previewSize - drawSize.X) / 2, (previewSize - drawSize.Y) / 2));
            ImGui.Image(pfpTexture.Handle, drawSize);
            ImGui.SetCursorPos(cursor + new Vector2(0, previewSize + ImGui.GetStyle().ItemSpacing.Y));
        }
        else
        {
            var cursor = ImGui.GetCursorPos();
            var cursorScreen = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRect(cursorScreen, cursorScreen + new Vector2(previewSize), ImGui.GetColorU32(ImGuiCol.Border));
            var iconSize = UiSharedService.GetIconSize(FontAwesomeIcon.Image);
            ImGui.SetCursorPos(cursor + new Vector2((previewSize - iconSize.X) / 2, (previewSize - iconSize.Y) / 2));
            _uiSharedService.IconText(FontAwesomeIcon.Image, ImGuiColors.DalamudGrey);
            ImGui.SetCursorPos(cursor + new Vector2(0, previewSize + ImGui.GetStyle().ItemSpacing.Y));
        }

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.FileImage, Loc.Get("EditProfile.SelectImageButton"), previewSize))
        {
            _fileDialogManager.OpenFileDialog(Loc.Get("EditProfile.SelectImageButton"), "Image files{.png,.jpg,.jpeg}", (success, name) =>
            {
                if (!success) return;
                var rpProfile = isRp ? _rpConfigService.GetCurrentCharacterProfile() : null;
                var charName = _dalamudUtil.GetPlayerName();
                var worldId = _dalamudUtil.GetWorldId();
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

                        var curProfile = await _apiController.UserGetProfile(new UserDto(new UserData(_apiController.UID, _apiController.DisplayName))).ConfigureAwait(false);
                        if (isRp && rpProfile != null)
                        {
                            rpProfile.RpProfilePictureBase64 = Convert.ToBase64String(file);
                            _rpConfigService.Save();
                            _logger.LogInformation("Uploading RP image for {uid}", _apiController.UID);
                            await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID, _apiController.DisplayName), curProfile.Disabled, curProfile.IsNSFW, curProfile.ProfilePictureBase64, curProfile.Description)
                            {
                                CharacterName = charName,
                                WorldId = worldId,
                                RpProfilePictureBase64 = rpProfile.RpProfilePictureBase64,
                                RpDescription = rpProfile.RpDescription,
                                IsRpNSFW = rpProfile.IsRpNsfw,
                                RpFirstName = rpProfile.RpFirstName,
                                RpLastName = rpProfile.RpLastName,
                                RpTitle = rpProfile.RpTitle,
                                RpAge = rpProfile.RpAge,
                                RpHeight = rpProfile.RpHeight,
                                RpBuild = rpProfile.RpBuild,
                                RpOccupation = rpProfile.RpOccupation,
                                RpAffiliation = rpProfile.RpAffiliation,
                                RpAlignment = rpProfile.RpAlignment,
                                RpAdditionalInfo = rpProfile.RpAdditionalInfo
                            }).ConfigureAwait(false);
                        }
                        else
                        {
                            _logger.LogInformation("Uploading HRP image for {uid}", _apiController.UID);
                            await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID, _apiController.DisplayName), curProfile.Disabled, _apiController.IsProfileNsfw, Convert.ToBase64String(file), _descriptionText)
                            {
                                CharacterName = charName,
                                WorldId = worldId,
                                RpProfilePictureBase64 = curProfile.RpProfilePictureBase64,
                                RpDescription = curProfile.RpDescription,
                                IsRpNSFW = curProfile.IsRpNSFW,
                                RpFirstName = curProfile.RpFirstName,
                                RpLastName = curProfile.RpLastName,
                                RpTitle = curProfile.RpTitle,
                                RpAge = curProfile.RpAge,
                                RpHeight = curProfile.RpHeight,
                                RpBuild = curProfile.RpBuild,
                                RpOccupation = curProfile.RpOccupation,
                                RpAffiliation = curProfile.RpAffiliation,
                                RpAlignment = curProfile.RpAlignment,
                                RpAdditionalInfo = curProfile.RpAdditionalInfo
                            }).ConfigureAwait(false);
                        }
                        Mediator.Publish(new ClearProfileDataMessage(new UserData(_apiController.UID), charName, worldId));
                        Mediator.Publish(new NotificationMessage(Loc.Get("EditProfile.SaveSuccessTitle"), Loc.Get("EditProfile.SaveSuccessBody"), NotificationType.Info));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to upload profile image");
                        Mediator.Publish(new NotificationMessage(Loc.Get("EditProfile.SaveErrorTitle"), Loc.Get("EditProfile.SaveErrorBody"), NotificationType.Error));
                        _notificationTracker.Upsert(NotificationEntry.ProfileSaveFailed());
                    }
                });
            });
        }
        if (_showFileDialogError)
        {
            UiSharedService.ColorTextWrapped(Loc.Get("EditProfile.ImageSizeError"), ImGuiColors.DalamudRed, previewSize);
        }

        ImGui.EndGroup();

        ImGui.SameLine(previewSize + spacing * 2);

        if (isRp)
        {
            ImGui.BeginGroup();
            var labelWidth = 70f * ImGuiHelpers.GlobalScale;

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

                if (sameLine)
                {
                    ImGui.SetNextItemWidth(-1);
                }
                else
                {
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X / 2 - 10f * ImGuiHelpers.GlobalScale);
                }

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

        if (!isRp)
        {
            using (_uiSharedService.GameFont.Push())
            {
                using var _ = ImRaii.PushId("hrp_desc");
                ImGui.TextUnformatted(Loc.Get("EditProfile.Description"));
                ImGui.InputTextMultiline("##description_multi", ref _descriptionText, 1000,
                        new Vector2(-1, 150 * ImGuiHelpers.GlobalScale));
            }
        }

        if (isRp)
        {
            using (_uiSharedService.GameFont.Push())
            {
                ImGui.TextUnformatted(Loc.Get("UserProfile.RpAdditionalInfo"));
                ImGui.InputTextMultiline("##additional_info", ref _rpAdditionalInfoText, 3000,
                    new Vector2(-1, 150 * ImGuiHelpers.GlobalScale));
            }
        }

        if (isRp)
        {
            var profile = _rpConfigService.GetCurrentCharacterProfile();
            var isRpNsfw = profile.IsRpNsfw;
            if (ImGui.Checkbox(Loc.Get("UserProfile.RpNsfw"), ref isRpNsfw))
            {
                var charName = _dalamudUtil.GetPlayerName();
                var worldId = _dalamudUtil.GetWorldId();
                _ = Task.Run(async () =>
                {
                    var curProfile = await _apiController.UserGetProfile(new UserDto(new UserData(_apiController.UID, _apiController.DisplayName))).ConfigureAwait(false);
                    _logger.LogInformation("Setting RP NSFW flag to {flag} for {uid}", isRpNsfw, _apiController.UID);
                    profile.IsRpNsfw = isRpNsfw;
                    _rpConfigService.Save();
                    await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID, _apiController.DisplayName), curProfile.Disabled, curProfile.IsNSFW, curProfile.ProfilePictureBase64, curProfile.Description)
                    {
                        CharacterName = charName,
                        WorldId = worldId,
                        RpProfilePictureBase64 = curProfile.RpProfilePictureBase64,
                        RpDescription = curProfile.RpDescription,
                        IsRpNSFW = isRpNsfw,
                        RpFirstName = curProfile.RpFirstName,
                        RpLastName = curProfile.RpLastName,
                        RpTitle = curProfile.RpTitle,
                        RpAge = curProfile.RpAge,
                        RpHeight = curProfile.RpHeight,
                        RpBuild = curProfile.RpBuild,
                        RpOccupation = curProfile.RpOccupation,
                        RpAffiliation = curProfile.RpAffiliation,
                        RpAlignment = curProfile.RpAlignment,
                        RpAdditionalInfo = curProfile.RpAdditionalInfo
                    }).ConfigureAwait(false);
                    Mediator.Publish(new ClearProfileDataMessage(new UserData(_apiController.UID), charName, worldId));
                });
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
                var profile = _rpConfigService.GetCurrentCharacterProfile();
                profile.RpDescription = _rpDescriptionText;
                profile.RpFirstName = _rpFirstNameText;
                profile.RpLastName = _rpLastNameText;
                profile.RpTitle = _rpTitleText;
                profile.RpAge = _rpAgeText;
                profile.RpHeight = _rpHeightText;
                profile.RpBuild = _rpBuildText;
                profile.RpOccupation = _rpOccupationText;
                profile.RpAffiliation = _rpAffiliationText;
                profile.RpAlignment = _rpAlignmentText;
                profile.RpAdditionalInfo = _rpAdditionalInfoText;
                _rpConfigService.Save();
            }
            else
            {
                // Nothing to do
            }

            var charName = _dalamudUtil.GetPlayerName();
            var worldId = _dalamudUtil.GetWorldId();
            var localRpProfile = _rpConfigService.GetCurrentCharacterProfile();

            _ = Task.Run(async () =>
            {
                try
                {
                    var curProfile = await _apiController.UserGetProfile(new UserDto(new UserData(_apiController.UID, _apiController.DisplayName))).ConfigureAwait(false);
                    if (isRp)
                    {
                        _logger.LogInformation("Saving RP profile for {uid}: {first} {last}", _apiController.UID, localRpProfile.RpFirstName, localRpProfile.RpLastName);
                        await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID, _apiController.DisplayName), curProfile.Disabled, curProfile.IsNSFW, curProfile.ProfilePictureBase64, curProfile.Description)
                        {
                            CharacterName = charName,
                            WorldId = worldId,
                            RpProfilePictureBase64 = localRpProfile.RpProfilePictureBase64,
                            RpDescription = localRpProfile.RpDescription,
                            IsRpNSFW = localRpProfile.IsRpNsfw,
                            RpFirstName = localRpProfile.RpFirstName,
                            RpLastName = localRpProfile.RpLastName,
                            RpTitle = localRpProfile.RpTitle,
                            RpAge = localRpProfile.RpAge,
                            RpHeight = localRpProfile.RpHeight,
                            RpBuild = localRpProfile.RpBuild,
                            RpOccupation = localRpProfile.RpOccupation,
                            RpAffiliation = localRpProfile.RpAffiliation,
                            RpAlignment = localRpProfile.RpAlignment,
                            RpAdditionalInfo = localRpProfile.RpAdditionalInfo
                        }).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogInformation("Saving HRP profile for {uid}, keeping local RP data", _apiController.UID);
                        await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID, _apiController.DisplayName), curProfile.Disabled, _apiController.IsProfileNsfw, curProfile.ProfilePictureBase64, _descriptionText)
                        {
                            CharacterName = charName,
                            WorldId = worldId,
                            RpProfilePictureBase64 = localRpProfile.RpProfilePictureBase64,
                            RpDescription = localRpProfile.RpDescription,
                            IsRpNSFW = localRpProfile.IsRpNsfw,
                            RpFirstName = localRpProfile.RpFirstName,
                            RpLastName = localRpProfile.RpLastName,
                            RpTitle = localRpProfile.RpTitle,
                            RpAge = localRpProfile.RpAge,
                            RpHeight = localRpProfile.RpHeight,
                            RpBuild = localRpProfile.RpBuild,
                            RpOccupation = localRpProfile.RpOccupation,
                            RpAffiliation = localRpProfile.RpAffiliation,
                            RpAlignment = localRpProfile.RpAlignment,
                            RpAdditionalInfo = localRpProfile.RpAdditionalInfo
                        }).ConfigureAwait(false);
                    }
                    Mediator.Publish(new ClearProfileDataMessage(new UserData(_apiController.UID), charName, worldId));
                    Mediator.Publish(new NotificationMessage(Loc.Get("EditProfile.SaveSuccessTitle"), Loc.Get("EditProfile.SaveSuccessBody"), NotificationType.Info));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save profile");
                    Mediator.Publish(new NotificationMessage(Loc.Get("EditProfile.SaveErrorTitle"), Loc.Get("EditProfile.SaveErrorBody"), NotificationType.Error));
                    _notificationTracker.Upsert(NotificationEntry.ProfileSaveFailed());
                }
            });
        }
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Tag, Loc.Get("EditProfile.SetCustomId.Button")))
        {
            ImGui.OpenPopup("SetCustomIdModal");
        }
        UiSharedService.AttachToolTip(Loc.Get("EditProfile.SetCustomId.Tooltip"));
        if (!isRp)
        {
            var isNsfw = umbraProfile.IsNSFW;
            if (ImGui.Checkbox(Loc.Get("EditProfile.ProfileIsNsfw"), ref isNsfw))
            {
                var charName = _dalamudUtil.GetPlayerName();
                var worldId = _dalamudUtil.GetWorldId();
                _ = Task.Run(async () =>
                {
                    var curProfile = await _apiController.UserGetProfile(new UserDto(new UserData(_apiController.UID, _apiController.DisplayName))).ConfigureAwait(false);
                    _logger.LogInformation("Setting global NSFW flag to {flag} for {uid}", isNsfw, _apiController.UID);
                    await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID, _apiController.DisplayName), curProfile.Disabled, isNsfw, curProfile.ProfilePictureBase64, curProfile.Description)
                    {
                        CharacterName = charName,
                        WorldId = worldId,
                        RpProfilePictureBase64 = curProfile.RpProfilePictureBase64,
                        RpDescription = curProfile.RpDescription,
                        IsRpNSFW = curProfile.IsRpNSFW,
                        RpFirstName = curProfile.RpFirstName,
                        RpLastName = curProfile.RpLastName,
                        RpTitle = curProfile.RpTitle,
                        RpAge = curProfile.RpAge,
                        RpHeight = curProfile.RpHeight,
                        RpBuild = curProfile.RpBuild,
                        RpOccupation = curProfile.RpOccupation,
                        RpAffiliation = curProfile.RpAffiliation,
                        RpAlignment = curProfile.RpAlignment,
                        RpAdditionalInfo = curProfile.RpAdditionalInfo
                    }).ConfigureAwait(false);
                    Mediator.Publish(new ClearProfileDataMessage(new UserData(_apiController.UID), charName, worldId));
                });
            }
            _uiSharedService.DrawHelpText(Loc.Get("EditProfile.ProfileIsNsfwHelp"));

            var isRpNsfw = umbraProfile.IsRpNSFW;
            if (ImGui.Checkbox(Loc.Get("UserProfile.RpNsfw"), ref isRpNsfw))
            {
                var charName = _dalamudUtil.GetPlayerName();
                var worldId = _dalamudUtil.GetWorldId();
                _ = Task.Run(async () =>
                {
                    var curProfile = await _apiController.UserGetProfile(new UserDto(new UserData(_apiController.UID, _apiController.DisplayName))).ConfigureAwait(false);
                    _logger.LogInformation("Setting RP NSFW flag to {flag} for {uid} (via HRP tab)", isRpNsfw, _apiController.UID);
                    var localProfile = _rpConfigService.GetCharacterProfile(charName, worldId);
                    localProfile.IsRpNsfw = isRpNsfw;
                    _rpConfigService.Save();
                    await _apiController.UserSetProfile(new UserProfileDto(new UserData(_apiController.UID, _apiController.DisplayName), curProfile.Disabled, curProfile.IsNSFW, curProfile.ProfilePictureBase64, curProfile.Description)
                    {
                        CharacterName = charName,
                        WorldId = worldId,
                        RpProfilePictureBase64 = curProfile.RpProfilePictureBase64,
                        RpDescription = curProfile.RpDescription,
                        IsRpNSFW = isRpNsfw,
                        RpFirstName = curProfile.RpFirstName,
                        RpLastName = curProfile.RpLastName,
                        RpTitle = curProfile.RpTitle,
                        RpAge = curProfile.RpAge,
                        RpHeight = curProfile.RpHeight,
                        RpBuild = curProfile.RpBuild,
                        RpOccupation = curProfile.RpOccupation,
                        RpAffiliation = curProfile.RpAffiliation,
                        RpAlignment = curProfile.RpAlignment,
                        RpAdditionalInfo = curProfile.RpAdditionalInfo
                    }).ConfigureAwait(false);
                    Mediator.Publish(new ClearProfileDataMessage(new UserData(_apiController.UID), charName, worldId));
                });
            }
            _uiSharedService.DrawHelpText(Loc.Get("UserProfile.RpNsfwHelp"));
        }
        ImGui.Separator();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _pfpTextureWrap?.Dispose();
    }
}