using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using UmbraSync.API.Data;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;

namespace UmbraSync.Services;

public class UmbraProfileManager : MediatorSubscriberBase
{
    private const string _noDescription = "-- User has no description set --";
    private const string _nsfw = "Profile not displayed - NSFW";
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly MareConfigService _mareConfigService;
    private readonly RpConfigService _rpConfigService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly ConcurrentDictionary<(UserData User, string? CharName, uint? WorldId), UmbraProfileData> _umbraProfiles = new();

    private readonly UmbraProfileData _defaultProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, _noDescription);
    private readonly UmbraProfileData _loadingProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, "Loading Data from server...");
    private readonly UmbraProfileData _nsfwProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, _nsfw);

    public UmbraProfileManager(ILogger<UmbraProfileManager> logger, MareConfigService mareConfigService,
        RpConfigService rpConfigService, MareMediator mediator, ApiController apiController,
        PairManager pairManager, DalamudUtilService dalamudUtil, ServerConfigurationManager serverConfigurationManager) : base(logger, mediator)
    {
        _mareConfigService = mareConfigService;
        _rpConfigService = rpConfigService;
        _apiController = apiController;
        _pairManager = pairManager;
        _dalamudUtil = dalamudUtil;
        _serverConfigurationManager = serverConfigurationManager;

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
        string? charName;
        uint? worldId;

        if (pair != null)
        {
            // Utilisateur online : utiliser les données du pair
            charName = pair.PlayerName;
            worldId = pair.WorldId == 0 ? null : pair.WorldId;
        }
        else if (string.Equals(data.UID, _apiController.UID, StringComparison.Ordinal))
        {
            // C'est nous-même : utiliser nos propres données
            charName = _dalamudUtil.GetPlayerName();
            worldId = _dalamudUtil.GetWorldId();
        }
        else
        {
            // Utilisateur offline : utiliser les dernières données connues
            charName = _serverConfigurationManager.GetNameForUid(data.UID);
            worldId = _serverConfigurationManager.GetWorldIdForUid(data.UID);
        }

        return GetUmbraProfile(data, charName, worldId);
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

    public void SetPreviewProfile(UserData data, string? charName, uint? worldId, UmbraProfileData profileData)
    {
        var key = (data, charName, worldId);
        _umbraProfiles[key] = profileData;
    }

    public async Task GetUmbraProfileFromService(UserData data, string? charName = null, uint? worldId = null)
    {
        var key = (data, charName, worldId);
        try
        {
            _umbraProfiles[key] = _loadingProfileData;
            var profile = await _apiController.UserGetProfile(new API.Dto.User.UserDto(data)
            {
                CharacterName = charName,
                WorldId = worldId
            }).ConfigureAwait(false);

            UmbraProfileData profileData = new(profile.Disabled, profile.IsNSFW ?? false,
                string.IsNullOrEmpty(profile.ProfilePictureBase64) ? string.Empty : profile.ProfilePictureBase64,
                string.IsNullOrEmpty(profile.Description) ? _noDescription : profile.Description,
                profile.RpProfilePictureBase64, profile.RpDescription, profile.IsRpNSFW ?? false,
                profile.RpFirstName, profile.RpLastName, profile.RpTitle, profile.RpAge,
                profile.RpHeight, profile.RpBuild, profile.RpOccupation, profile.RpAffiliation,
                profile.RpAlignment, profile.RpAdditionalInfo);

            if (_apiController.IsConnected && string.Equals(_apiController.UID, data.UID, StringComparison.Ordinal) && charName != null && worldId != null)
            {
                var localRpProfile = _rpConfigService.GetCharacterProfile(charName, worldId.Value);
                bool changed = false;
                if (!string.Equals(localRpProfile.RpFirstName, profileData.RpFirstName, StringComparison.Ordinal)) { localRpProfile.RpFirstName = profileData.RpFirstName ?? string.Empty; changed = true; }
                if (!string.Equals(localRpProfile.RpLastName, profileData.RpLastName, StringComparison.Ordinal)) { localRpProfile.RpLastName = profileData.RpLastName ?? string.Empty; changed = true; }
                if (!string.Equals(localRpProfile.RpTitle, profileData.RpTitle, StringComparison.Ordinal)) { localRpProfile.RpTitle = profileData.RpTitle ?? string.Empty; changed = true; }
                if (!string.Equals(localRpProfile.RpDescription, profileData.RpDescription, StringComparison.Ordinal)) { localRpProfile.RpDescription = profileData.RpDescription ?? string.Empty; changed = true; }
                if (!string.Equals(localRpProfile.RpAge, profileData.RpAge, StringComparison.Ordinal)) { localRpProfile.RpAge = profileData.RpAge ?? string.Empty; changed = true; }
                if (!string.Equals(localRpProfile.RpHeight, profileData.RpHeight, StringComparison.Ordinal)) { localRpProfile.RpHeight = profileData.RpHeight ?? string.Empty; changed = true; }
                if (!string.Equals(localRpProfile.RpBuild, profileData.RpBuild, StringComparison.Ordinal)) { localRpProfile.RpBuild = profileData.RpBuild ?? string.Empty; changed = true; }
                if (!string.Equals(localRpProfile.RpOccupation, profileData.RpOccupation, StringComparison.Ordinal)) { localRpProfile.RpOccupation = profileData.RpOccupation ?? string.Empty; changed = true; }
                if (!string.Equals(localRpProfile.RpAffiliation, profileData.RpAffiliation, StringComparison.Ordinal)) { localRpProfile.RpAffiliation = profileData.RpAffiliation ?? string.Empty; changed = true; }
                if (!string.Equals(localRpProfile.RpAlignment, profileData.RpAlignment, StringComparison.Ordinal)) { localRpProfile.RpAlignment = profileData.RpAlignment ?? string.Empty; changed = true; }
                if (!string.Equals(localRpProfile.RpAdditionalInfo, profileData.RpAdditionalInfo, StringComparison.Ordinal)) { localRpProfile.RpAdditionalInfo = profileData.RpAdditionalInfo ?? string.Empty; changed = true; }
                if (localRpProfile.IsRpNsfw != profileData.IsRpNSFW) { localRpProfile.IsRpNsfw = profileData.IsRpNSFW; changed = true; }
                if (!string.Equals(localRpProfile.RpProfilePictureBase64, profileData.Base64RpProfilePicture, StringComparison.Ordinal)) { localRpProfile.RpProfilePictureBase64 = profileData.Base64RpProfilePicture ?? string.Empty; changed = true; }

                if (changed)
                {
                    Logger.LogInformation("Local RP profile updated from server for {uid}", data.UID);
                    _rpConfigService.Save();
                }
            }

            bool isSelf = string.Equals(_apiController.UID, data.UID, StringComparison.Ordinal);
            if (profileData.IsNSFW && !_mareConfigService.Current.ProfilesAllowNsfw && !isSelf)
            {
                _umbraProfiles[key] = _nsfwProfileData;
            }
            else if (profileData.IsRpNSFW && !_mareConfigService.Current.ProfilesAllowRpNsfw && !isSelf)
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
            Logger.LogWarning(ex, "Failed to get Profile from service for user {user}", data);
            _umbraProfiles[key] = _defaultProfileData;
        }
    }
}