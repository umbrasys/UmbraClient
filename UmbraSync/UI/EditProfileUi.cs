using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Utility;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using OtterGui.Raii;
using System.Numerics;
using System.Text.RegularExpressions;
using UmbraSync.API.Data;
using UmbraSync.API.Dto.User;
using UmbraSync.Localization;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Factories;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.Interop.Ipc;
using UmbraSync.Models;
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
    private readonly IpcManager _ipcManager;
    private readonly NotificationTracker _notificationTracker;
    private readonly IDataManager _dataManager;
    private string _localMoodlesJson = string.Empty;
    private string _descriptionText = string.Empty;
    private string _rpDescriptionText = string.Empty;
    private string _rpFirstNameText = string.Empty;
    private string _rpLastNameText = string.Empty;
    private string _rpTitleText = string.Empty;
    private string _rpAgeText = string.Empty;
    private string _rpRaceText = string.Empty;
    private string _rpEthnicityText = string.Empty;
    private string _rpHeightText = string.Empty;
    private string _rpBuildText = string.Empty;
    private string _rpResidenceText = string.Empty;
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
    private bool _vanityModalOpen = false;
    private string _vanityInput = string.Empty;
    private int _activeTab;
    private bool _moodleOperationInProgress;
    private bool _addMoodlePopupOpen;
    private int _newMoodleIconId = 210456;
    private string _newMoodleTitle = "";
    private string _newMoodleDescription = "";
    private int _newMoodleType = 0;
    private bool _iconSelectorOpen;
    private string _iconIdInput = "210456";
    private string _iconSearchText = "";
    private record struct StatusIconInfo(uint IconId, string Name);
    private readonly Lazy<List<StatusIconInfo>>? _statusIcons;
    private List<StatusIconInfo>? _filteredIcons;
    private string _lastIconSearchText = "";

    private static readonly (string Name, Vector4 Color)[] MoodleColors =
    [
        ("Red",        new Vector4(0.90f, 0.20f, 0.20f, 1f)),
        ("Orange",     new Vector4(1.00f, 0.60f, 0.20f, 1f)),
        ("Yellow",     new Vector4(1.00f, 0.95f, 0.30f, 1f)),
        ("Gold",       new Vector4(0.85f, 0.75f, 0.20f, 1f)),
        ("Green",      new Vector4(0.20f, 0.80f, 0.20f, 1f)),
        ("LightGreen", new Vector4(0.60f, 1.00f, 0.60f, 1f)),
        ("LightBlue",  new Vector4(0.50f, 0.80f, 1.00f, 1f)),
        ("DarkBlue",   new Vector4(0.20f, 0.40f, 0.90f, 1f)),
        ("Pink",       new Vector4(1.00f, 0.50f, 0.80f, 1f)),
        ("White",      new Vector4(1.00f, 1.00f, 1.00f, 1f)),
    ];

    public EditProfileUi(ILogger<EditProfileUi> logger, MareMediator mediator,
        ApiController apiController, UiSharedService uiSharedService, FileDialogManager fileDialogManager,
        UmbraProfileManager umbraProfileManager, PairManager pairManager, PairFactory pairFactory,
        RpConfigService rpConfigService, DalamudUtilService dalamudUtil, IpcManager ipcManager,
        NotificationTracker notificationTracker, IDataManager dataManager,
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
        _ipcManager = ipcManager;
        _notificationTracker = notificationTracker;
        _dataManager = dataManager;
        _statusIcons = new Lazy<List<StatusIconInfo>>(() => LoadStatusIcons());

        Mediator.Subscribe<GposeStartMessage>(this, (_) => { _wasOpen = IsOpen; IsOpen = false; });
        Mediator.Subscribe<GposeEndMessage>(this, (_) => IsOpen = _wasOpen);
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) =>
        {
            _rpLoaded = false;
            _hrpLoaded = false;
            _localMoodlesJson = string.Empty;
            _lastMoodlesFetch = DateTime.MinValue;
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
        Mediator.Subscribe<MoodlesMessage>(this, (msg) => Task.Run(RefreshLocalMoodlesAsync));
    }

    private bool _moodlesFetching;
    private DateTime _lastMoodlesFetch = DateTime.MinValue;

    private async Task RefreshLocalMoodlesAsync()
    {
        if (_moodlesFetching) return;
        _moodlesFetching = true;
        try
        {
            if (!_ipcManager.Moodles.APIAvailable) return;
            var ptr = await _dalamudUtil.GetPlayerPointerAsync().ConfigureAwait(false);
            if (ptr == IntPtr.Zero) return;
            _localMoodlesJson = await _ipcManager.Moodles.GetStatusAsync(ptr).ConfigureAwait(false) ?? string.Empty;
            _lastMoodlesFetch = DateTime.UtcNow;
        }
        finally
        {
            _moodlesFetching = false;
        }
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
            var hrpImageBase64 = _profileImage.Length > 0 ? Convert.ToBase64String(_profileImage) : string.Empty;
            var previewProfileData = new UmbraProfileData(
                IsFlagged: false,
                IsNSFW: _apiController.IsProfileNsfw,
                Base64ProfilePicture: hrpImageBase64,
                Description: _descriptionText,
                Base64RpProfilePicture: currentProfile.RpProfilePictureBase64,
                RpDescription: _rpDescriptionText,
                IsRpNSFW: currentProfile.IsRpNsfw,
                RpFirstName: _rpFirstNameText,
                RpLastName: _rpLastNameText,
                RpTitle: _rpTitleText,
                RpAge: _rpAgeText,
                RpRace: _rpRaceText,
                RpEthnicity: _rpEthnicityText,
                RpHeight: _rpHeightText,
                RpBuild: _rpBuildText,
                RpResidence: _rpResidenceText,
                RpOccupation: _rpOccupationText,
                RpAffiliation: _rpAffiliationText,
                RpAlignment: _rpAlignmentText,
                RpAdditionalInfo: _rpAdditionalInfoText
            );

            _umbraProfileManager.SetPreviewProfile(pair.UserData, pair.PlayerName, pair.WorldId, previewProfileData);
            Mediator.Publish(new ProfileOpenStandaloneMessage(pair));
        }
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Tag, Loc.Get("EditProfile.SetCustomId.Button")))
        {
            _vanityInput = string.Empty;
            _vanityModalOpen = true;
            ImGui.OpenPopup(Loc.Get("EditProfile.SetCustomId.Title"));
        }
        UiSharedService.AttachToolTip(Loc.Get("EditProfile.SetCustomId.Tooltip"));

        DrawVanityPopup();

        DrawProfileTabButtons(accent);
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
            var hrpImageBase64 = _profileImage.Length > 0 ? Convert.ToBase64String(_profileImage) : string.Empty;
            var previewProfileData = new UmbraProfileData(
                IsFlagged: false,
                IsNSFW: _apiController.IsProfileNsfw,
                Base64ProfilePicture: hrpImageBase64,
                Description: _descriptionText,
                Base64RpProfilePicture: currentProfile.RpProfilePictureBase64,
                RpDescription: _rpDescriptionText,
                IsRpNSFW: currentProfile.IsRpNsfw,
                RpFirstName: _rpFirstNameText,
                RpLastName: _rpLastNameText,
                RpTitle: _rpTitleText,
                RpAge: _rpAgeText,
                RpRace: _rpRaceText,
                RpEthnicity: _rpEthnicityText,
                RpHeight: _rpHeightText,
                RpBuild: _rpBuildText,
                RpResidence: _rpResidenceText,
                RpOccupation: _rpOccupationText,
                RpAffiliation: _rpAffiliationText,
                RpAlignment: _rpAlignmentText,
                RpAdditionalInfo: _rpAdditionalInfoText
            );

            _umbraProfileManager.SetPreviewProfile(pair.UserData, pair.PlayerName, pair.WorldId, previewProfileData);
            Mediator.Publish(new ProfileOpenStandaloneMessage(pair));
        }
        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Tag, Loc.Get("EditProfile.SetCustomId.Button")))
        {
            _vanityInput = string.Empty;
            _vanityModalOpen = true;
            ImGui.OpenPopup(Loc.Get("EditProfile.SetCustomId.Title"));
        }
        UiSharedService.AttachToolTip(Loc.Get("EditProfile.SetCustomId.Tooltip"));

        DrawVanityPopup();

        DrawProfileTabButtons(accent);
    }

    private void DrawProfileTabButtons(Vector4 accent)
    {
        var labels = new[] { "RP", "HRP" };
        var icons = new[] { FontAwesomeIcon.Scroll, FontAwesomeIcon.User };
        const float btnH = 32f;
        const float btnSpacing = 8f;
        const float rounding = 4f;
        const float iconTextGap = 6f;

        var dl = ImGui.GetWindowDrawList();
        var availWidth = ImGui.GetContentRegionAvail().X;
        var btnW = (availWidth - btnSpacing * (labels.Length - 1)) / labels.Length;

        var borderColor = new Vector4(0.29f, 0.21f, 0.41f, 0.7f);
        var bgColor = new Vector4(0.11f, 0.11f, 0.11f, 0.9f);
        var hoverBg = new Vector4(0.17f, 0.13f, 0.22f, 1f);

        for (int i = 0; i < labels.Length; i++)
        {
            if (i > 0) ImGui.SameLine(0, btnSpacing);

            var p = ImGui.GetCursorScreenPos();
            bool clicked = ImGui.InvisibleButton($"##profileTab_{i}", new Vector2(btnW, btnH));
            bool hovered = ImGui.IsItemHovered();
            bool isActive = _activeTab == i;

            var bg = isActive ? accent : hovered ? hoverBg : bgColor;
            dl.AddRectFilled(p, p + new Vector2(btnW, btnH), ImGui.GetColorU32(bg), rounding);
            if (!isActive)
                dl.AddRect(p, p + new Vector2(btnW, btnH), ImGui.GetColorU32(borderColor with { W = hovered ? 0.9f : 0.5f }), rounding);

            ImGui.PushFont(UiBuilder.IconFont);
            var iconStr = icons[i].ToIconString();
            var iconSz = ImGui.CalcTextSize(iconStr);
            ImGui.PopFont();

            var labelSz = ImGui.CalcTextSize(labels[i]);
            var totalW = iconSz.X + iconTextGap + labelSz.X;
            var startX = p.X + (btnW - totalW) / 2f;

            var textColor = isActive ? new Vector4(1f, 1f, 1f, 1f) : hovered ? new Vector4(0.9f, 0.85f, 1f, 1f) : new Vector4(0.7f, 0.65f, 0.8f, 1f);
            var textColorU32 = ImGui.GetColorU32(textColor);

            ImGui.PushFont(UiBuilder.IconFont);
            dl.AddText(new Vector2(startX, p.Y + (btnH - iconSz.Y) / 2f), textColorU32, iconStr);
            ImGui.PopFont();

            dl.AddText(new Vector2(startX + iconSz.X + iconTextGap, p.Y + (btnH - labelSz.Y) / 2f), textColorU32, labels[i]);

            if (hovered) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (clicked) _activeTab = i;
        }

        ImGuiHelpers.ScaledDummy(4f);

        DrawProfileContent(_activeTab == 0);
    }

    private void DrawProfileContent(bool isRp)
    {
        if (string.IsNullOrEmpty(_localMoodlesJson) && !_moodlesFetching
            && (DateTime.UtcNow - _lastMoodlesFetch).TotalSeconds > 3)
        {
            _ = RefreshLocalMoodlesAsync();
        }

        var umbraProfile = _umbraProfileManager.GetUmbraProfile(new UserData(_apiController.UID));

        if (umbraProfile.IsFlagged)
        {
            _uiSharedService.BigText(isRp ? "Profil RP" : Loc.Get("EditProfile.CurrentProfile"));
            UiSharedService.ColorTextWrapped(umbraProfile.Description, UiSharedService.AccentColor);
            return;
        }

        _uiSharedService.BigText(isRp ? "Profil RP" : Loc.Get("EditProfile.CurrentProfile"));
        ImGui.SameLine();
        DrawHeaderButtons(isRp, umbraProfile);
        ImGuiHelpers.ScaledDummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y / 2));

        if (isRp)
        {
            if (!_rpLoaded && !string.Equals(umbraProfile.Description, "Loading Data from server...", StringComparison.Ordinal))
            {
                _rpDescriptionText = umbraProfile.RpDescription ?? string.Empty;
                _rpFirstNameText = umbraProfile.RpFirstName ?? string.Empty;
                _rpLastNameText = umbraProfile.RpLastName ?? string.Empty;
                _rpTitleText = umbraProfile.RpTitle ?? string.Empty;
                _rpAgeText = umbraProfile.RpAge ?? string.Empty;
                _rpRaceText = umbraProfile.RpRace ?? string.Empty;
                _rpEthnicityText = umbraProfile.RpEthnicity ?? string.Empty;
                _rpHeightText = umbraProfile.RpHeight ?? string.Empty;
                _rpBuildText = umbraProfile.RpBuild ?? string.Empty;
                _rpResidenceText = umbraProfile.RpResidence ?? string.Empty;
                _rpOccupationText = umbraProfile.RpOccupation ?? string.Empty;
                _rpAffiliationText = umbraProfile.RpAffiliation ?? string.Empty;
                _rpAlignmentText = umbraProfile.RpAlignment ?? string.Empty;
                _rpAdditionalInfoText = umbraProfile.RpAdditionalInfo ?? string.Empty;

                try
                {
                    _rpProfileImage = !string.IsNullOrEmpty(umbraProfile.Base64RpProfilePicture) ? Convert.FromBase64String(umbraProfile.Base64RpProfilePicture) : [];
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
            if (!_hrpLoaded && !string.Equals(umbraProfile.Description, "Loading Data from server...", StringComparison.Ordinal))
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

        var pfpTexture = isRp ? _rpPfpTextureWrap : _pfpTextureWrap;
        var pfpBytes = isRp ? _rpProfileImage : _profileImage;

        var w = ImGui.GetContentRegionAvail().X;
        float imgSz = 150f * ImGuiHelpers.GlobalScale;
        float spacing = 16f * ImGuiHelpers.GlobalScale;

        ImGui.BeginGroup();
        if (pfpTexture != null && pfpBytes.Length > 0)
        {
            float ratio = (float)pfpTexture.Width / pfpTexture.Height;
            Vector2 drawSize;
            if (ratio > 1) drawSize = new Vector2(imgSz, imgSz / ratio);
            else drawSize = new Vector2(imgSz * ratio, imgSz);

            var cursor = ImGui.GetCursorPos();
            ImGui.SetCursorPos(cursor + new Vector2((imgSz - drawSize.X) / 2, (imgSz - drawSize.Y) / 2));
            ImGui.Image(pfpTexture.Handle, drawSize);
            ImGui.SetCursorPos(cursor + new Vector2(0, imgSz + ImGui.GetStyle().ItemSpacing.Y));
        }
        else
        {
            var cursor = ImGui.GetCursorPos();
            var cursorScreen = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddRect(cursorScreen, cursorScreen + new Vector2(imgSz), ImGui.GetColorU32(ImGuiCol.Border));
            var iconSize = UiSharedService.GetIconSize(FontAwesomeIcon.Image);
            ImGui.SetCursorPos(cursor + new Vector2((imgSz - iconSize.X) / 2, (imgSz - iconSize.Y) / 2));
            _uiSharedService.IconText(FontAwesomeIcon.Image, ImGuiColors.DalamudGrey);
            ImGui.SetCursorPos(cursor + new Vector2(0, imgSz + ImGui.GetStyle().ItemSpacing.Y));
        }

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.FileImage, Loc.Get("EditProfile.SelectImageButton"), imgSz))
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
                        if (file.Length > 5 * 1024 * 1024)
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
                                RpRace = rpProfile.RpRace,
                                RpEthnicity = rpProfile.RpEthnicity,
                                RpHeight = rpProfile.RpHeight,
                                RpBuild = rpProfile.RpBuild,
                                RpResidence = rpProfile.RpResidence,
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
            UiSharedService.ColorTextWrapped(Loc.Get("EditProfile.ImageSizeError"), ImGuiColors.DalamudRed, imgSz);
        }
        ImGui.EndGroup();
        ImGui.SameLine(0, spacing);
        ImGui.BeginGroup();

        var fieldW = w - imgSz - spacing - 12f * ImGuiHelpers.GlobalScale;
        var halfFieldW = (fieldW - ImGui.GetStyle().ItemSpacing.X) / 2;

        void DrawField(string label, ref string text, int maxLength, float inputWidth)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, label);
            ImGui.SetNextItemWidth(inputWidth);
            ImGui.InputText("##" + label, ref text, maxLength);
        }

        void DrawFieldPair(string label1, ref string text1, int max1, string label2, ref string text2, int max2)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, label1);
            ImGui.SameLine(halfFieldW + ImGui.GetStyle().ItemSpacing.X);
            ImGui.TextColored(ImGuiColors.DalamudGrey, label2);
            ImGui.SetNextItemWidth(halfFieldW);
            ImGui.InputText("##" + label1, ref text1, max1);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(halfFieldW);
            ImGui.InputText("##" + label2, ref text2, max2);
        }

        if (isRp)
        {
            DrawFieldPair(Loc.Get("UserProfile.RpFirstName"), ref _rpFirstNameText, 30, Loc.Get("UserProfile.RpLastName"), ref _rpLastNameText, 30);

            var vanillaFirstName = GetVanillaFirstName();
            if (!string.IsNullOrEmpty(_rpFirstNameText) && vanillaFirstName != null
                && !RpFirstNameContainsVanilla(_rpFirstNameText, vanillaFirstName))
            {
                UiSharedService.ColorTextWrapped(string.Format(Loc.Get("EditProfile.RpFirstNameMismatch"), vanillaFirstName), ImGuiColors.DalamudRed);
            }

            DrawFieldPair(Loc.Get("UserProfile.RpTitle"), ref _rpTitleText, 100, Loc.Get("UserProfile.RpAge"), ref _rpAgeText, 50);
            DrawFieldPair(Loc.Get("UserProfile.RpRace"), ref _rpRaceText, 50, Loc.Get("UserProfile.RpEthnicity"), ref _rpEthnicityText, 50);
            DrawFieldPair(Loc.Get("UserProfile.RpHeight"), ref _rpHeightText, 50, Loc.Get("UserProfile.RpBuild"), ref _rpBuildText, 100);
        }
        else
        {
            using (_uiSharedService.GameFont.Push())
            {
                using var _ = ImRaii.PushId("hrp_desc");
                ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.Get("PopoutProfile.DescriptionLabel"));
                ImGui.SetNextItemWidth(fieldW);
                ImGui.InputTextMultiline("##description_multi", ref _descriptionText, 1000,
                        new Vector2(fieldW, 150 * ImGuiHelpers.GlobalScale));
            }
        }
        ImGui.EndGroup();

        if (isRp)
        {
            ImGuiHelpers.ScaledDummy(new Vector2(0f, 4f));
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(new Vector2(0f, 4f));

            DrawField(Loc.Get("UserProfile.RpResidence"), ref _rpResidenceText, 100, w);
            DrawField(Loc.Get("UserProfile.RpOccupation"), ref _rpOccupationText, 100, w);
            DrawField(Loc.Get("UserProfile.RpAffiliation"), ref _rpAffiliationText, 100, w);
            DrawField(Loc.Get("UserProfile.RpAlignment"), ref _rpAlignmentText, 100, w);

            ImGuiHelpers.ScaledDummy(new Vector2(0f, 4f));

            using (_uiSharedService.GameFont.Push())
            {
                ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.Get("UserProfile.RpAdditionalInfo"));
                ImGui.InputTextMultiline("##additional_info", ref _rpAdditionalInfoText, 3000,
                    new Vector2(w, 150 * ImGuiHelpers.GlobalScale));
            }

            ImGuiHelpers.ScaledDummy(new Vector2(0f, 4f));
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(new Vector2(0f, 4f));

            ImGui.TextColored(ImGuiColors.DalamudGrey, "Traits du personnage");
            ImGui.SameLine();
            if (_moodleOperationInProgress)
            {
                ImGui.TextColored(ImGuiColors.DalamudYellow, "(...)");
            }
            else if (_ipcManager.Moodles.APIAvailable)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiSharedService.ThemeButtonHovered);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiSharedService.ThemeButtonActive);
                if (_uiSharedService.IconButton(FontAwesomeIcon.Plus))
                {
                    _newMoodleIconId = 210456;
                    _iconIdInput = "210456";
                    _newMoodleTitle = "";
                    _newMoodleDescription = "";
                    _newMoodleType = 0;
                    _iconSelectorOpen = false;
                    _addMoodlePopupOpen = true;
                    ImGui.OpenPopup("##AddMoodlePopup");
                }
                UiSharedService.AttachToolTip("Ajouter un trait");
                ImGui.PopStyleColor(3);
            }

            DrawAddMoodlePopup();

            if (!string.IsNullOrEmpty(_localMoodlesJson))
            {
                DrawEditableMoodles();
            }
        }

    }

    private void DrawEditableMoodles()
    {
        var moodles = MoodleStatusInfo.ParseMoodles(_localMoodlesJson);
        if (moodles.Count == 0) return;

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        const float iconHeight = 40f;
        var scaledHeight = iconHeight * ImGuiHelpers.GlobalScale;
        var textureProvider = _uiSharedService.TextureProvider;

        var items = new List<(MoodleStatusInfo moodle, ImTextureID handle, Vector2 size, int index)>();
        float totalWidth = 0f;
        for (int i = 0; i < moodles.Count; i++)
        {
            var moodle = moodles[i];
            if (moodle.IconID <= 0) continue;
            var wrap = textureProvider.GetFromGameIcon(new GameIconLookup((uint)moodle.IconID)).GetWrapOrEmpty();
            if (wrap.Handle == IntPtr.Zero) continue;
            var aspect = wrap.Height > 0 ? (float)wrap.Width / wrap.Height : 1f;
            var displaySize = new Vector2(scaledHeight * aspect, scaledHeight);
            items.Add((moodle, wrap.Handle, displaySize, i));
            totalWidth += displaySize.X;
        }
        if (items.Count == 0) return;

        totalWidth += (items.Count - 1) * spacing;
        var baseX = ImGui.GetCursorPosX();
        var startX = baseX + (availableWidth - totalWidth) / 2f;
        if (startX < baseX) startX = baseX;

        ImGui.SetCursorPosX(startX);

        for (int i = 0; i < items.Count; i++)
        {
            var (moodle, handle, size, moodleIndex) = items[i];

            if (i > 0)
                ImGui.SameLine();

            var groupPos = ImGui.GetCursorPos();
            var screenPos = ImGui.GetCursorScreenPos();
            ImGui.BeginGroup();

            ImGui.Image(handle, size);

            // Small X button overlay in top-right corner
            if (!_moodleOperationInProgress)
            {
                var btnSize = 16f * ImGuiHelpers.GlobalScale;
                var btnScreenPos = new Vector2(screenPos.X + size.X - btnSize + 2, screenPos.Y - 2);
                ImGui.SetCursorScreenPos(btnScreenPos);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, btnSize / 2f);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.1f, 0.1f, 0.85f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 0.3f, 0.3f, 1f));
                if (ImGui.Button($"X##removeMoodle_{moodleIndex}", new Vector2(btnSize, btnSize)))
                {
                    var idx = moodleIndex;
                    _ = Task.Run(() => RemoveMoodleAsync(idx));
                }
                ImGui.PopStyleColor(3);
                ImGui.PopStyleVar(2);
            }

            ImGui.EndGroup();

            // Reset cursor for next item positioning
            if (i < items.Count - 1)
                ImGui.SetCursorPos(new Vector2(groupPos.X + size.X + spacing, groupPos.Y));

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20f);
                var title = moodle.CleanTitle;
                if (!string.IsNullOrEmpty(title))
                {
                    var typeColor = moodle.Type switch
                    {
                        0 => new Vector4(0.4f, 0.9f, 0.4f, 1f),
                        1 => new Vector4(0.9f, 0.4f, 0.4f, 1f),
                        _ => new Vector4(0.5f, 0.6f, 1f, 1f),
                    };
                    ImGui.TextColored(typeColor, title);
                }
                var desc = moodle.CleanDescription;
                if (!string.IsNullOrEmpty(desc))
                    ImGui.TextUnformatted(desc);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }
    }

    private void DrawAddMoodlePopup()
    {
        if (!_addMoodlePopupOpen) return;

        ImGui.SetNextWindowSize(new Vector2(500, 0) * ImGuiHelpers.GlobalScale, ImGuiCond.Always);
        if (ImGui.BeginPopupModal("##AddMoodlePopup", ref _addMoodlePopupOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextColored(UiSharedService.AccentColor, "Ajouter un trait");
            ImGuiHelpers.ScaledDummy(new Vector2(0f, 4f));

            // Icon preview
            var textureProvider = _uiSharedService.TextureProvider;
            try
            {
                var previewWrap = textureProvider.GetFromGameIcon(new GameIconLookup((uint)_newMoodleIconId)).GetWrapOrEmpty();
                if (previewWrap.Handle != IntPtr.Zero)
                {
                    var previewSize = 48f * ImGuiHelpers.GlobalScale;
                    var aspect = previewWrap.Height > 0 ? (float)previewWrap.Width / previewWrap.Height : 1f;
                    ImGui.Image(previewWrap.Handle, new Vector2(previewSize * aspect, previewSize));
                    ImGui.SameLine();
                }
            }
            catch { /* Icon not found */ }

            ImGui.BeginGroup();
            ImGui.TextUnformatted($"Icône : {_newMoodleIconId}");
            if (ImGui.Button(_iconSelectorOpen ? "Fermer le sélecteur" : "Choisir une icône"))
            {
                _iconSelectorOpen = !_iconSelectorOpen;
            }
            ImGui.EndGroup();

            // Direct icon ID input
            ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputText("##iconIdDirect", ref _iconIdInput, 10, ImGuiInputTextFlags.CharsDecimal))
            {
                if (int.TryParse(_iconIdInput, out var parsed) && parsed > 0)
                    _newMoodleIconId = parsed;
            }
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudGrey, "ID direct");

            // Icon selector grid
            if (_iconSelectorOpen)
            {
                ImGuiHelpers.ScaledDummy(new Vector2(0f, 4f));
                DrawIconSelectorGrid(textureProvider);
            }

            ImGuiHelpers.ScaledDummy(new Vector2(0f, 4f));
            ImGui.Separator();
            ImGuiHelpers.ScaledDummy(new Vector2(0f, 4f));

            // Title
            ImGui.TextColored(ImGuiColors.DalamudGrey, "Titre");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##moodleTitle", ref _newMoodleTitle, 100);

            // Color buttons
            var btnSize = 20f * ImGuiHelpers.GlobalScale;
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4f * ImGuiHelpers.GlobalScale, 0));
            for (int ci = 0; ci < MoodleColors.Length; ci++)
            {
                var (colorName, colorVec) = MoodleColors[ci];
                if (ci > 0) ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, colorVec with { W = 0.85f });
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colorVec);
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, colorVec with { W = 0.6f });
                if (ImGui.Button($"##color_{colorName}", new Vector2(btnSize, btnSize)))
                {
                    _newMoodleTitle = WrapTitleWithColor(_newMoodleTitle, colorName);
                }
                ImGui.PopStyleColor(3);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(colorName);
                    ImGui.EndTooltip();
                }
            }
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.4f, 0.4f, 0.85f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.6f, 0.3f, 0.3f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.8f, 0.2f, 0.2f, 1f));
            if (ImGui.Button("X##clearColor", new Vector2(btnSize, btnSize)))
            {
                _newMoodleTitle = StripColorTags(_newMoodleTitle);
            }
            ImGui.PopStyleColor(3);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Retirer la couleur");
                ImGui.EndTooltip();
            }
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudGrey, "(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Syntaxe BBCode : [color=NomCouleur]Texte[/color]");
                ImGui.TextUnformatted("Couleurs : Red, Orange, Yellow, Gold, Green,");
                ImGui.TextUnformatted("LightGreen, LightBlue, DarkBlue, Pink, White");
                ImGui.EndTooltip();
            }
            ImGui.PopStyleVar(3);

            // Description
            ImGui.TextColored(ImGuiColors.DalamudGrey, "Description");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextMultiline("##moodleDesc", ref _newMoodleDescription, 500,
                new Vector2(-1, 80 * ImGuiHelpers.GlobalScale));

            // Type
            ImGui.TextColored(ImGuiColors.DalamudGrey, "Type");
            ImGui.SetNextItemWidth(-1);
            var typeNames = new[] { "Positif (Buff)", "Négatif (Debuff)", "Neutre" };
            ImGui.Combo("##moodleType", ref _newMoodleType, typeNames, typeNames.Length);

            ImGuiHelpers.ScaledDummy(new Vector2(0f, 8f));

            // Buttons
            var buttonWidth = 120 * ImGuiHelpers.GlobalScale;
            var totalButtonsWidth = buttonWidth * 2 + ImGui.GetStyle().ItemSpacing.X;
            ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - totalButtonsWidth) / 2f + ImGui.GetCursorPosX());

            var canAdd = !string.IsNullOrWhiteSpace(_newMoodleTitle) && !_moodleOperationInProgress;
            if (!canAdd) ImGui.BeginDisabled();
            if (ImGui.Button("Ajouter", new Vector2(buttonWidth, 0)))
            {
                var moodle = new MoodleFullStatus
                {
                    IconID = _newMoodleIconId,
                    Title = _newMoodleTitle,
                    Description = _newMoodleDescription,
                    Type = _newMoodleType,
                };
                _ = Task.Run(() => AddMoodleAsync(moodle));
                _addMoodlePopupOpen = false;
                ImGui.CloseCurrentPopup();
            }
            if (!canAdd) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Annuler", new Vector2(buttonWidth, 0)))
            {
                _addMoodlePopupOpen = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void DrawIconSelectorGrid(ITextureProvider textureProvider)
    {
        // Search field
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##iconSearch", "Rechercher (nom ou ID)...", ref _iconSearchText, 64);

        // Get or filter icon list
        var allIcons = _statusIcons?.Value ?? [];
        if (allIcons.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "Aucune icône disponible.");
            return;
        }

        if (_filteredIcons == null || !string.Equals(_lastIconSearchText, _iconSearchText, StringComparison.Ordinal))
        {
            _lastIconSearchText = _iconSearchText;
            if (string.IsNullOrWhiteSpace(_iconSearchText))
            {
                _filteredIcons = allIcons;
            }
            else
            {
                var search = _iconSearchText.Trim();
                _filteredIcons = allIcons.Where(i =>
                    i.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || i.IconId.ToString().Contains(search, StringComparison.Ordinal)
                ).ToList();
            }
        }

        var icons = _filteredIcons;
        if (icons.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "Aucun résultat.");
            return;
        }

        const int iconsPerRow = 10;
        var iconSize = 40f * ImGuiHelpers.GlobalScale;
        var spacing = 4f * ImGuiHelpers.GlobalScale;
        var rowHeight = iconSize + spacing;
        var totalRows = (icons.Count + iconsPerRow - 1) / iconsPerRow;
        var childHeight = 200f * ImGuiHelpers.GlobalScale;

        ImGui.BeginChild("##iconGrid", new Vector2(-1, childHeight), true);

        // Virtual scrolling
        var scrollY = ImGui.GetScrollY();
        var firstVisibleRow = Math.Max(0, (int)(scrollY / rowHeight) - 1);
        var visibleRows = (int)(childHeight / rowHeight) + 3;
        var lastVisibleRow = Math.Min(totalRows - 1, firstVisibleRow + visibleRows);

        if (firstVisibleRow > 0)
            ImGui.Dummy(new Vector2(0, firstVisibleRow * rowHeight));

        for (int row = firstVisibleRow; row <= lastVisibleRow; row++)
        {
            for (int col = 0; col < iconsPerRow; col++)
            {
                var iconIndex = row * iconsPerRow + col;
                if (iconIndex >= icons.Count) break;
                var info = icons[iconIndex];

                if (col > 0) ImGui.SameLine(0, spacing);

                IDalamudTextureWrap? wrap;
                try
                {
                    wrap = textureProvider.GetFromGameIcon(new GameIconLookup(info.IconId)).GetWrapOrEmpty();
                }
                catch
                {
                    ImGui.Dummy(new Vector2(iconSize, iconSize));
                    continue;
                }

                if (wrap.Handle == IntPtr.Zero)
                {
                    ImGui.Dummy(new Vector2(iconSize, iconSize));
                    continue;
                }

                bool isSelected = _newMoodleIconId == (int)info.IconId;
                if (isSelected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, UiSharedService.ThemeButtonActive);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiSharedService.ThemeButtonActive);
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiSharedService.ThemeButtonHovered);
                }

                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
                ImGui.PushID((int)info.IconId);
                if (ImGui.ImageButton(wrap.Handle, new Vector2(iconSize, iconSize)))
                {
                    _newMoodleIconId = (int)info.IconId;
                    _iconIdInput = info.IconId.ToString();
                }
                ImGui.PopID();
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(2);

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted($"#{info.IconId} — {info.Name}");
                    ImGui.EndTooltip();
                }
            }
        }

        var remainingRows = totalRows - lastVisibleRow - 1;
        if (remainingRows > 0)
            ImGui.Dummy(new Vector2(0, remainingRows * rowHeight));

        ImGui.EndChild();
    }

    private async Task RemoveMoodleAsync(int index)
    {
        if (_moodleOperationInProgress) return;
        _moodleOperationInProgress = true;
        try
        {
            if (!_ipcManager.Moodles.APIAvailable) return;
            var ptr = await _dalamudUtil.GetPlayerPointerAsync().ConfigureAwait(false);
            if (ptr == IntPtr.Zero) return;

            var freshJson = await _ipcManager.Moodles.GetStatusAsync(ptr).ConfigureAwait(false);
            if (string.IsNullOrEmpty(freshJson)) return;

            var newJson = MoodleStatusInfo.RemoveMoodleAtIndex(freshJson, index);
            await _ipcManager.Moodles.SetStatusAsync(ptr, newJson).ConfigureAwait(false);
            _localMoodlesJson = await _ipcManager.Moodles.GetStatusAsync(ptr).ConfigureAwait(false) ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove moodle at index {index}", index);
        }
        finally
        {
            _moodleOperationInProgress = false;
        }
    }

    private async Task AddMoodleAsync(MoodleFullStatus moodle)
    {
        if (_moodleOperationInProgress) return;
        _moodleOperationInProgress = true;
        try
        {
            if (!_ipcManager.Moodles.APIAvailable) return;
            var ptr = await _dalamudUtil.GetPlayerPointerAsync().ConfigureAwait(false);
            if (ptr == IntPtr.Zero) return;

            var freshJson = await _ipcManager.Moodles.GetStatusAsync(ptr).ConfigureAwait(false) ?? string.Empty;
            var newJson = MoodleStatusInfo.AddMoodle(freshJson, moodle);
            await _ipcManager.Moodles.SetStatusAsync(ptr, newJson).ConfigureAwait(false);
            _localMoodlesJson = await _ipcManager.Moodles.GetStatusAsync(ptr).ConfigureAwait(false) ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add moodle");
        }
        finally
        {
            _moodleOperationInProgress = false;
        }
    }

    private List<StatusIconInfo> LoadStatusIcons()
    {
        var sheet = _dataManager.GetExcelSheet<Status>(Dalamud.Game.ClientLanguage.English);
        if (sheet == null) return [];

        var seen = new HashSet<uint>();
        var list = new List<StatusIconInfo>();
        foreach (var row in sheet)
        {
            if (row.Icon == 0) continue;
            var name = row.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!seen.Add(row.Icon)) continue;
            list.Add(new StatusIconInfo(row.Icon, name));
        }
        list.Sort((a, b) => a.IconId.CompareTo(b.IconId));
        return list;
    }

    private static string WrapTitleWithColor(string title, string colorName)
    {
        var stripped = StripColorTags(title);
        return $"[color={colorName}]{stripped}[/color]";
    }

    private static string StripColorTags(string text)
    {
        return Regex.Replace(text, @"\[/?color(?:=[^\]]*)?]", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
    }

    private void DrawVanityPopup()
    {
        if (_vanityModalOpen && ImGui.BeginPopupModal(Loc.Get("EditProfile.SetCustomId.Title"), ref _vanityModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped(Loc.Get("EditProfile.SetCustomId.FormatHint"));
            ImGuiHelpers.ScaledDummy(new Vector2(0f, 5f));

            ImGui.SetNextItemWidth(400 * ImGuiHelpers.GlobalScale);
            ImGui.InputTextWithHint("##customId", Loc.Get("EditProfile.SetCustomId.Placeholder"), ref _vanityInput, 64);

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

    private void DrawHeaderButtons(bool isRp, UmbraProfileData umbraProfile)
    {
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
                        RpRace = curProfile.RpRace,
                        RpEthnicity = curProfile.RpEthnicity,
                        RpHeight = curProfile.RpHeight,
                        RpBuild = curProfile.RpBuild,
                        RpResidence = curProfile.RpResidence,
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
                        RpRace = curProfile.RpRace,
                        RpEthnicity = curProfile.RpEthnicity,
                        RpHeight = curProfile.RpHeight,
                        RpBuild = curProfile.RpBuild,
                        RpResidence = curProfile.RpResidence,
                        RpOccupation = curProfile.RpOccupation,
                        RpAffiliation = curProfile.RpAffiliation,
                        RpAlignment = curProfile.RpAlignment,
                        RpAdditionalInfo = curProfile.RpAdditionalInfo
                    }).ConfigureAwait(false);
                    Mediator.Publish(new ClearProfileDataMessage(new UserData(_apiController.UID), charName, worldId));
                });
            }
            _uiSharedService.DrawHelpText(Loc.Get("EditProfile.ProfileIsNsfwHelp"));
        }

        ImGui.SameLine();
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Save, Loc.Get("EditProfile.SaveButton")))
        {
            if (isRp)
            {
                var vanillaFirst = GetVanillaFirstName();
                if (vanillaFirst != null && !string.IsNullOrEmpty(_rpFirstNameText)
                    && !RpFirstNameContainsVanilla(_rpFirstNameText, vanillaFirst))
                {
                    Mediator.Publish(new NotificationMessage(Loc.Get("EditProfile.SaveErrorTitle"),
                        string.Format(Loc.Get("EditProfile.RpFirstNameMismatch"), vanillaFirst), NotificationType.Error));
                    return;
                }

                var profile = _rpConfigService.GetCurrentCharacterProfile();
                profile.RpDescription = _rpDescriptionText;
                profile.RpFirstName = _rpFirstNameText;
                profile.RpLastName = _rpLastNameText;
                profile.RpTitle = _rpTitleText;
                profile.RpAge = _rpAgeText;
                profile.RpRace = _rpRaceText;
                profile.RpEthnicity = _rpEthnicityText;
                profile.RpHeight = _rpHeightText;
                profile.RpBuild = _rpBuildText;
                profile.RpResidence = _rpResidenceText;
                profile.RpOccupation = _rpOccupationText;
                profile.RpAffiliation = _rpAffiliationText;
                profile.RpAlignment = _rpAlignmentText;
                profile.RpAdditionalInfo = _rpAdditionalInfoText;
                _rpConfigService.Save();
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
                            RpRace = localRpProfile.RpRace,
                            RpEthnicity = localRpProfile.RpEthnicity,
                            RpHeight = localRpProfile.RpHeight,
                            RpBuild = localRpProfile.RpBuild,
                            RpResidence = localRpProfile.RpResidence,
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
                            RpRace = localRpProfile.RpRace,
                            RpEthnicity = localRpProfile.RpEthnicity,
                            RpHeight = localRpProfile.RpHeight,
                            RpBuild = localRpProfile.RpBuild,
                            RpResidence = localRpProfile.RpResidence,
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
    }

    private string? GetVanillaFirstName()
    {
        var fullName = _dalamudUtil.GetPlayerName();
        if (string.IsNullOrEmpty(fullName)) return null;
        var spaceIndex = fullName.IndexOf(' ');
        return spaceIndex >= 0 ? fullName[..spaceIndex] : fullName;
    }

    private static bool RpFirstNameContainsVanilla(string rpFirstName, string vanillaFirstName)
    {
        foreach (var part in rpFirstName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part, vanillaFirstName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _pfpTextureWrap?.Dispose();
    }
}