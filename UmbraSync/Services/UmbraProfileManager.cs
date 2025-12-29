using UmbraSync.API.Data;
using UmbraSync.API.Data.Comparer;
using UmbraSync.MareConfiguration;
using UmbraSync.Services.Mediator;
using UmbraSync.WebAPI;
using UmbraSync.PlayerData.Pairs;
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
    private readonly PairManager _pairManager;
    private readonly ConcurrentDictionary<(UserData User, string? CharName, uint? WorldId), UmbraProfileData> _umbraProfiles = new();

    private readonly UmbraProfileData _defaultProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, _noDescription);
    private readonly UmbraProfileData _loadingProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, "Loading Data from server...");
    private readonly UmbraProfileData _nsfwProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, _nsfw);

    public UmbraProfileManager(ILogger<UmbraProfileManager> logger, MareConfigService mareConfigService,
        RpConfigService rpConfigService, MareMediator mediator, ApiController apiController,
        PairManager pairManager) : base(logger, mediator)
    {
        _mareConfigService = mareConfigService;
        _apiController = apiController;
        _pairManager = pairManager;

        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData != null)
            {
                foreach (var k in _umbraProfiles.Keys.Where(k => 
                    string.Equals(k.User.UID, msg.UserData.UID, StringComparison.Ordinal) && 
                    (msg.CharacterName == null || string.Equals(k.CharName, msg.CharacterName, StringComparison.Ordinal)) &&
                    (msg.WorldId == null || k.WorldId == msg.WorldId)).ToList())
                {
                    _umbraProfiles.TryRemove(k, out _);
                }
            }
            else
                _umbraProfiles.Clear();
        });
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => _umbraProfiles.Clear());
    }

    public UmbraProfileData GetUmbraProfile(UserData data)
    {
        var pair = _pairManager.GetPairByUID(data.UID);
        return GetUmbraProfile(data, pair?.PlayerName, pair?.WorldId);
    }

    public UmbraProfileData GetUmbraProfile(UserData data, string? charName, uint? worldId)
    {
        var key = (data, charName, worldId);
        if (!_umbraProfiles.TryGetValue(key, out var profile))
        {
            _ = Task.Run(() => GetUmbraProfileFromService(data, charName, worldId));
            return (_loadingProfileData);
        }

        return (profile);
    }

    private async Task GetUmbraProfileFromService(UserData data, string? charName = null, uint? worldId = null)
    {
        var key = (data, charName, worldId);
        try
        {
            _umbraProfiles[key] = _loadingProfileData;
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
                _umbraProfiles[key] = _nsfwProfileData;
            }
            else
            {
                _umbraProfiles[key] = profileData;
            }
        }
        catch (Exception ex)
        {
            // if fails save DefaultProfileData to dict
            Logger.LogWarning(ex, "Failed to get Profile from service for user {user}", data);
            _umbraProfiles[key] = _defaultProfileData;
        }
    }
}
