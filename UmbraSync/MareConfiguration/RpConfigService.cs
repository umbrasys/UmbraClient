using UmbraSync.MareConfiguration.Configurations;
using UmbraSync.Services;

namespace UmbraSync.MareConfiguration;

public class RpConfigService : ConfigurationServiceBase<RpConfig>
{
    public const string ConfigName = "rp_profile.json";
    private readonly DalamudUtilService _dalamudUtil;

    public RpConfigService(string configDir, DalamudUtilService dalamudUtil) : base(configDir)
    {
        _dalamudUtil = dalamudUtil;
    }
    public override string ConfigurationName => ConfigName;
    public static string GetCharacterKey(string charName, uint worldId) => $"{charName}@{worldId}";
    public string CurrentCharacterKey => GetCharacterKey(_dalamudUtil.GetPlayerName(), _dalamudUtil.GetWorldId());
    public CharacterRpProfile GetCharacterProfile(string charName, uint worldId)
    {
        var key = GetCharacterKey(charName, worldId);
        if (!Current.CharacterProfiles.TryGetValue(key, out var profile))
        {
            profile = new CharacterRpProfile();
            Current.CharacterProfiles[key] = profile;
        }
        return profile;
    }
    public CharacterRpProfile GetCurrentCharacterProfile()
    {
        return GetCharacterProfile(_dalamudUtil.GetPlayerName(), _dalamudUtil.GetWorldId());
    }
}
