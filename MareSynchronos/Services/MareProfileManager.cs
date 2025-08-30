using MareSynchronos.API.Data;
using MareSynchronos.API.Data.Comparer;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MareSynchronos.Services;

public class MareProfileManager : MediatorSubscriberBase
{
    private const string _noDescription = "-- User has no description set --";
    private const string _nsfw = "Profile not displayed - NSFW";
    private readonly ApiController _apiController;
    private readonly MareConfigService _mareConfigService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly ConcurrentDictionary<UserData, MareProfileData> _mareProfiles = new(UserDataComparer.Instance);

    private readonly MareProfileData _defaultProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, _noDescription);
    private readonly MareProfileData _loadingProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, "Loading Data from server...");
    private readonly MareProfileData _nsfwProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, _nsfw);

    public MareProfileManager(ILogger<MareProfileManager> logger, MareConfigService mareConfigService,
        MareMediator mediator, ApiController apiController, ServerConfigurationManager serverConfigurationManager) : base(logger, mediator)
    {
        _mareConfigService = mareConfigService;
        _apiController = apiController;
        _serverConfigurationManager = serverConfigurationManager;

        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData != null)
                _mareProfiles.Remove(msg.UserData, out _);
            else
                _mareProfiles.Clear();
        });
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => _mareProfiles.Clear());
    }

    public MareProfileData GetMareProfile(UserData data)
    {
        if (!_mareProfiles.TryGetValue(data, out var profile))
        {
            _ = Task.Run(() => GetMareProfileFromService(data));
            return (_loadingProfileData);
        }

        return (profile);
    }

    private async Task GetMareProfileFromService(UserData data)
    {
        try
        {
            _mareProfiles[data] = _loadingProfileData;
            var profile = await _apiController.UserGetProfile(new API.Dto.User.UserDto(data)).ConfigureAwait(false);
            MareProfileData profileData = new(profile.Disabled, profile.IsNSFW ?? false,
                string.IsNullOrEmpty(profile.ProfilePictureBase64) ? string.Empty : profile.ProfilePictureBase64,
                string.IsNullOrEmpty(profile.Description) ? _noDescription : profile.Description);
            if (profileData.IsNSFW && !_mareConfigService.Current.ProfilesAllowNsfw && !string.Equals(_apiController.UID, data.UID, StringComparison.Ordinal))
            {
                _mareProfiles[data] = _nsfwProfileData;
            }
            else
            {
                _mareProfiles[data] = profileData;
            }
        }
        catch (Exception ex)
        {
            // if fails save DefaultProfileData to dict
            Logger.LogWarning(ex, "Failed to get Profile from service for user {user}", data);
            _mareProfiles[data] = _defaultProfileData;
        }
    }
}