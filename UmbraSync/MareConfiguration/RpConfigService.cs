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
    public string CurrentCharacterKey => $"{_dalamudUtil.GetPlayerName()}@{_dalamudUtil.GetWorldId()}";
    public CharacterRpProfile GetCurrentCharacterProfile()
    {
        var key = CurrentCharacterKey;
        if (!Current.CharacterProfiles.TryGetValue(key, out var profile))
        {
            profile = new CharacterRpProfile();
            Current.CharacterProfiles[key] = profile;
        }
        return profile;
    }
}
