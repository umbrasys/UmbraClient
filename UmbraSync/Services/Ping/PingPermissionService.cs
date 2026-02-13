using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using UmbraSync.API.Data.Extensions;
using UmbraSync.API.Dto.Group;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services.Mediator;
using UmbraSync.WebAPI;

namespace UmbraSync.Services.Ping;

public sealed class PingPermissionService : IMediatorSubscriber, IDisposable
{
    private readonly IPartyList _partyList;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly ICondition _condition;
    private readonly PairManager _pairManager;
    private readonly ApiController _apiController;
    private readonly MareConfigService _configService;
    private bool _isInInstance;

    public PingPermissionService(
        MareMediator mediator,
        IPartyList partyList,
        IClientState clientState,
        IObjectTable objectTable,
        ICondition condition,
        PairManager pairManager,
        ApiController apiController,
        MareConfigService configService)
    {
        Mediator = mediator;
        _partyList = partyList;
        _clientState = clientState;
        _objectTable = objectTable;
        _condition = condition;
        _pairManager = pairManager;
        _apiController = apiController;
        _configService = configService;

        mediator.Subscribe<InstanceOrDutyStartMessage>(this, _ => _isInInstance = true);
        mediator.Subscribe<InstanceOrDutyEndMessage>(this, _ => _isInInstance = false);
    }

    public MareMediator Mediator { get; }

    public void Dispose()
    {
        Mediator.UnsubscribeAll(this);
    }

    public bool IsGloballyEnabled()
    {
        if (!_configService.Current.PingEnabled) return false;
        if (!_clientState.IsLoggedIn) return false;
        if (string.IsNullOrEmpty(_apiController.UID)) return false;
        if (IsInInstance()) return false;
        return true;
    }

    public bool IsInInstance()
    {
        return _isInInstance
            || _condition[ConditionFlag.BoundByDuty]
            || _condition[ConditionFlag.BoundByDuty56]
            || _condition[ConditionFlag.BoundByDuty95];
    }

    public bool IsPartyLeader()
    {
        if (_partyList.Length <= 1) return false;

        var localPlayer = _objectTable.LocalPlayer;
        if (localPlayer == null) return false;

        var leaderIndex = _partyList.PartyLeaderIndex;
        var leader = _partyList[(int)leaderIndex];
        if (leader == null) return false;

        return leader.EntityId == localPlayer.EntityId;
    }

    public bool CanPlacePingsInParty()
    {
        if (!IsGloballyEnabled()) return false;
        if (!_configService.Current.PingShowInParty) return false;
        return IsPartyLeader();
    }

    public bool CanPlacePingsInSyncshell(GroupFullInfoDto group)
    {
        if (!IsGloballyEnabled()) return false;
        if (!_configService.Current.PingShowInSyncshell) return false;

        // Owner can always place pings
        if (string.Equals(group.OwnerUID, _apiController.UID, StringComparison.Ordinal))
            return true;

        // Moderators can always place pings
        if (group.GroupUserInfo.IsModerator())
            return true;

        // Users with CanPlacePings flag
        if (group.GroupUserInfo.CanPlacePings())
            return true;

        return false;
    }

    public List<GroupFullInfoDto> GetPingableGroups()
    {
        if (!IsGloballyEnabled()) return [];

        return _pairManager.Groups.Values
            .Where(CanPlacePingsInSyncshell)
            .ToList();
    }

    public bool CanPlacePingsAnywhere()
    {
        return CanPlacePingsInParty() || GetPingableGroups().Count > 0;
    }
}
