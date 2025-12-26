using UmbraSync.API.Data;
using UmbraSync.API.Data.Comparer;
using UmbraSync.MareConfiguration;
using UmbraSync.Services.Mediator;
using UmbraSync.WebAPI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using API = UmbraSync.API;

namespace UmbraSync.Services;

public class UmbraProfileManager : MediatorSubscriberBase
{
    private const string _noDescription = "-- User has no description set --";
    private const string _nsfw = "Profile not displayed - NSFW";
    private readonly ApiController _apiController;
    private readonly MareConfigService _mareConfigService;
    private readonly RpConfigService _rpConfigService;
    private readonly ConcurrentDictionary<UserData, UmbraProfileData> _umbraProfiles = new(UserDataComparer.Instance);

    private readonly UmbraProfileData _defaultProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, _noDescription);
    private readonly UmbraProfileData _loadingProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, "Loading Data from server...");
    private readonly UmbraProfileData _nsfwProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, _nsfw);

    public UmbraProfileManager(ILogger<UmbraProfileManager> logger, MareConfigService mareConfigService,
        RpConfigService rpConfigService, MareMediator mediator, ApiController apiController) : base(logger, mediator)
    {
        _mareConfigService = mareConfigService;
        _rpConfigService = rpConfigService;
        _apiController = apiController;

        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData != null)
                _umbraProfiles.TryRemove(msg.UserData, out _);
            else
                _umbraProfiles.Clear();
        });
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => _umbraProfiles.Clear());
    }

    public UmbraProfileData GetUmbraProfile(UserData data)
    {
        if (!_umbraProfiles.TryGetValue(data, out var profile))
        {
            _ = Task.Run(() => GetUmbraProfileFromService(data));
            return (_loadingProfileData);
        }

        return (profile);
    }

    private async Task GetUmbraProfileFromService(UserData data)
    {
        try
        {
            _umbraProfiles[data] = _loadingProfileData;
            var profile = await _apiController.UserGetProfile(new API.Dto.User.UserDto(data)).ConfigureAwait(false);
            UmbraProfileData profileData = new(profile.Disabled, profile.IsNSFW ?? false,
                string.IsNullOrEmpty(profile.ProfilePictureBase64) ? string.Empty : profile.ProfilePictureBase64,
                string.IsNullOrEmpty(profile.Description) ? _noDescription : profile.Description,
                profile.RpProfilePictureBase64, profile.RpDescription, profile.IsRpNSFW ?? false,
                profile.RpFirstName, profile.RpLastName, profile.RpTitle, profile.RpAge,
                profile.RpHeight, profile.RpBuild, profile.RpOccupation, profile.RpAffiliation,
                profile.RpAlignment, profile.RpAdditionalInfo);
            if (profileData.IsNSFW && !_mareConfigService.Current.ProfilesAllowNsfw && !string.Equals(_apiController.UID, data.UID, StringComparison.Ordinal))
            {
                _umbraProfiles[data] = _nsfwProfileData;
            }
            else
            {
                _umbraProfiles[data] = profileData;
            }
        }
        catch (Exception ex)
        {
            // if fails save DefaultProfileData to dict
            Logger.LogWarning(ex, "Failed to get Profile from service for user {user}", data);
            _umbraProfiles[data] = _defaultProfileData;
        }
    }
}
