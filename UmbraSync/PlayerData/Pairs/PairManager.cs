using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using UmbraSync.API.Data;
using UmbraSync.API.Data.Comparer;
using UmbraSync.API.Data.Extensions;
using UmbraSync.API.Dto.Group;
using UmbraSync.API.Dto.User;
using UmbraSync.MareConfiguration;
using UmbraSync.MareConfiguration.Models;
using UmbraSync.PlayerData.Factories;
using UmbraSync.Services;
using UmbraSync.Services.AutoDetect;
using UmbraSync.Services.Events;
using UmbraSync.Services.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UmbraSync.Localization;
using UmbraSync.WebAPI;

namespace UmbraSync.PlayerData.Pairs;

public sealed class PairManager : DisposableMediatorSubscriberBase
{
    private readonly ConcurrentDictionary<UserData, Pair> _allClientPairs = new(UserDataComparer.Instance);
    private readonly ConcurrentDictionary<GroupData, GroupFullInfoDto> _allGroups = new(GroupDataComparer.Instance);
    private readonly MareConfigService _configurationService;
    private readonly IContextMenu _dalamudContextMenu;
    private readonly NearbyDiscoveryService _nearbyDiscoveryService;
    private readonly AutoDetectRequestService _autoDetectRequestService;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly PairFactory _pairFactory;
    private readonly Lazy<ApiController> _apiController;
    private Lazy<List<Pair>> _directPairsInternal;
    private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> _groupPairsInternal;

    public PairManager(ILogger<PairManager> logger, PairFactory pairFactory,
                MareConfigService configurationService, MareMediator mediator,
                IContextMenu dalamudContextMenu, NearbyDiscoveryService nearbyDiscoveryService,
                AutoDetectRequestService autoDetectRequestService, DalamudUtilService dalamudUtilService,
                IServiceProvider serviceProvider) : base(logger, mediator)
    {
        _pairFactory = pairFactory;
        _configurationService = configurationService;
        _dalamudContextMenu = dalamudContextMenu;
        _nearbyDiscoveryService = nearbyDiscoveryService;
        _autoDetectRequestService = autoDetectRequestService;
        _dalamudUtilService = dalamudUtilService;
        _apiController = new Lazy<ApiController>(() => serviceProvider.GetRequiredService<ApiController>());
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => ClearPairs());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => ReapplyPairData());
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();

        _dalamudContextMenu.OnMenuOpened += DalamudContextMenuOnOnOpenGameObjectContextMenu;
    }

    public List<Pair> DirectPairs => _directPairsInternal.Value;

    public Dictionary<GroupFullInfoDto, List<Pair>> GroupPairs => _groupPairsInternal.Value;
    public Dictionary<GroupData, GroupFullInfoDto> Groups => _allGroups.ToDictionary(k => k.Key, k => k.Value);
    public Pair? LastAddedUser { get; internal set; }

    public void AddGroup(GroupFullInfoDto dto)
    {
        _allGroups[dto.Group] = dto;
        RecreateLazy();
    }

    public void AddGroupPair(GroupPairFullInfoDto dto, bool isInitialLoad = false)
    {
        if (!_allClientPairs.ContainsKey(dto.User))
            _allClientPairs[dto.User] = _pairFactory.Create(dto.User);

        var group = _allGroups[dto.Group];
        _allClientPairs[dto.User].GroupPair[group] = dto;
        RecreateLazy();

        if (!isInitialLoad)
        {
            Mediator.Publish(new ApplyDefaultGroupPermissionsMessage(dto));
        }
    }

    public Pair? GetPairByUID(string uid)
    {
        var existingPair = _allClientPairs.FirstOrDefault(f => uid.Equals(f.Key.UID, StringComparison.Ordinal));
        if (!Equals(existingPair, default(KeyValuePair<UserData, Pair>)))
        {
            return existingPair.Value;
        }

        return null;
    }

    public void AddUserPair(UserPairDto dto, bool addToLastAddedUser = true)
    {
        if (!_allClientPairs.ContainsKey(dto.User))
        {
            _allClientPairs[dto.User] = _pairFactory.Create(dto.User);
        }
        else
        {
            addToLastAddedUser = false;
        }

        _allClientPairs[dto.User].UserPair = dto;
        if (addToLastAddedUser)
            LastAddedUser = _allClientPairs[dto.User];
        _allClientPairs[dto.User].ApplyLastReceivedData();
        RecreateLazy();

        if (addToLastAddedUser)
        {
            Mediator.Publish(new ApplyDefaultPairPermissionsMessage(dto));
        }
    }

    public void ClearPairs()
    {
        Logger.LogDebug("Clearing all Pairs");
        DisposePairs();
        _allClientPairs.Clear();
        _allGroups.Clear();
        RecreateLazy();
    }

    public List<Pair> GetOnlineUserPairs() => _allClientPairs
        .Where(p => p.Value.IsOnline)
        .Select(p => p.Value)
        .ToList();

    public int GetVisibleUserCount() => _allClientPairs.Count(p => p.Value.IsVisible);

    public List<UserData> GetVisibleUsers() => _allClientPairs.Where(p => p.Value.IsVisible).Select(p => p.Key).ToList();

    public void MarkPairOffline(UserData user)
    {
        if (_allClientPairs.TryGetValue(user, out var pair))
        {
            Mediator.Publish(new ClearProfileDataMessage(pair.UserData));
            pair.MarkOffline();
        }

        RecreateLazy();
    }

    public void MarkPairOnline(OnlineUserIdentDto dto, bool sendNotif = true)
    {
        if (!_allClientPairs.ContainsKey(dto.User)) throw new InvalidOperationException("No user found for " + dto);

        Mediator.Publish(new ClearProfileDataMessage(dto.User));

        var pair = _allClientPairs[dto.User];
        if (pair.HasCachedPlayer)
        {
            RecreateLazy();
            return;
        }

        if (sendNotif && _configurationService.Current.ShowOnlineNotifications
            && (_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs && pair.UserPair != null
            || !_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs)
            && (_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs && !string.IsNullOrEmpty(pair.GetNote())
            || !_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs))
        {
            string? note = pair.GetNoteOrName();
            var msg = !string.IsNullOrEmpty(note)
                ? $"{note} ({pair.UserData.AliasOrUID}) is now online"
                : $"{pair.UserData.AliasOrUID} is now online";
            Mediator.Publish(new NotificationMessage("User online", msg, NotificationType.Info, TimeSpan.FromSeconds(5)));
        }

        pair.CreateCachedPlayer(dto);

        RecreateLazy();
    }

    public void ReceiveCharaData(OnlineUserCharaDataDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair)) throw new InvalidOperationException("No user found for " + dto.User);

        Mediator.Publish(new EventMessage(new Event(pair.UserData, nameof(PairManager), EventSeverity.Informational, "Received Character Data")));
        _allClientPairs[dto.User].ApplyData(dto);
    }

    public void RemoveGroup(GroupData data)
    {
        _allGroups.TryRemove(data, out _);

        foreach (var item in _allClientPairs.ToList())
        {
            foreach (var grpPair in item.Value.GroupPair.Select(k => k.Key).Where(grpPair => GroupDataComparer.Instance.Equals(grpPair.Group, data)).ToList())
            {
                _allClientPairs[item.Key].GroupPair.Remove(grpPair);
            }

            if (!_allClientPairs[item.Key].HasAnyConnection() && _allClientPairs.TryRemove(item.Key, out var pair))
            {
                pair.MarkOffline();
            }
        }

        RecreateLazy();
    }

    public void RemoveGroupPair(GroupPairDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            var group = _allGroups[dto.Group];
            pair.GroupPair.Remove(group);

            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(dto.User, out _);
            }
        }

        RecreateLazy();
    }

    public void RemoveUserPair(UserDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            pair.UserPair = null;

            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(dto.User, out _);
            }
        }

        RecreateLazy();
    }

    public void SetGroupInfo(GroupInfoDto dto)
    {
        if (!_allGroups.TryGetValue(dto.Group, out var groupInfo))
        {
            return;
        }

        groupInfo.Group = dto.Group;
        groupInfo.Owner = dto.Owner;
        groupInfo.GroupPermissions = dto.GroupPermissions;
        groupInfo.MaxUserCount = dto.MaxUserCount;
        groupInfo.AutoDetectVisible = dto.AutoDetectVisible;
        groupInfo.PasswordTemporarilyDisabled = dto.PasswordTemporarilyDisabled;
        groupInfo.IsTemporary = dto.IsTemporary;
        groupInfo.ExpiresAt = dto.ExpiresAt;

        RecreateLazy();
    }

    public void UpdatePairPermissions(UserPermissionsDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair == null) throw new InvalidOperationException("No direct pair for " + dto);

        if (pair.UserPair.OtherPermissions.IsPaused() != dto.Permissions.IsPaused()
            || pair.UserPair.OtherPermissions.IsPaired() != dto.Permissions.IsPaired())
        {
            Mediator.Publish(new ClearProfileDataMessage(dto.User));
        }

        pair.UserPair.OtherPermissions = dto.Permissions;

        Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}",
            pair.UserPair.OtherPermissions.IsPaused(),
            pair.UserPair.OtherPermissions.IsDisableAnimations(),
            pair.UserPair.OtherPermissions.IsDisableSounds(),
            pair.UserPair.OtherPermissions.IsDisableVFX());

        if (!pair.IsPaused)
            pair.ApplyLastReceivedData();

        RecreateLazy();
    }

    public void UpdateSelfPairPermissions(UserPermissionsDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair == null) throw new InvalidOperationException("No direct pair for " + dto);

        if (pair.UserPair.OwnPermissions.IsPaused() != dto.Permissions.IsPaused()
            || pair.UserPair.OwnPermissions.IsPaired() != dto.Permissions.IsPaired())
        {
            Mediator.Publish(new ClearProfileDataMessage(dto.User));
        }

        pair.UserPair.OwnPermissions = dto.Permissions;

        Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}",
            pair.UserPair.OwnPermissions.IsPaused(),
            pair.UserPair.OwnPermissions.IsDisableAnimations(),
            pair.UserPair.OwnPermissions.IsDisableSounds(),
            pair.UserPair.OwnPermissions.IsDisableVFX());

        if (!pair.IsPaused)
            pair.ApplyLastReceivedData();

        RecreateLazy();
    }

    internal void ReceiveUploadStatus(UserDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var existingPair) && existingPair.IsVisible)
        {
            existingPair.SetIsUploading();
        }
    }

    internal void SetGroupPairStatusInfo(GroupPairUserInfoDto dto)
    {
        var group = _allGroups[dto.Group];
        _allClientPairs[dto.User].GroupPair[group].GroupPairStatusInfo = dto.GroupUserInfo;
        RecreateLazy();
    }

    internal void SetGroupPairUserPermissions(GroupPairUserPermissionDto dto)
    {
        var group = _allGroups[dto.Group];
        var prevPermissions = _allClientPairs[dto.User].GroupPair[group].GroupUserPermissions;
        _allClientPairs[dto.User].GroupPair[group].GroupUserPermissions = dto.GroupPairPermissions;
        if (prevPermissions.IsDisableAnimations() != dto.GroupPairPermissions.IsDisableAnimations()
            || prevPermissions.IsDisableSounds() != dto.GroupPairPermissions.IsDisableSounds()
            || prevPermissions.IsDisableVFX() != dto.GroupPairPermissions.IsDisableVFX())
        {
            _allClientPairs[dto.User].ApplyLastReceivedData();
        }
        RecreateLazy();
    }

    internal void SetGroupPermissions(GroupPermissionDto dto)
    {
        var prevPermissions = _allGroups[dto.Group].GroupPermissions;
        _allGroups[dto.Group].GroupPermissions = dto.Permissions;
        if (prevPermissions.IsDisableAnimations() != dto.Permissions.IsDisableAnimations()
            || prevPermissions.IsDisableSounds() != dto.Permissions.IsDisableSounds()
            || prevPermissions.IsDisableVFX() != dto.Permissions.IsDisableVFX())
        {
            RecreateLazy();
            var group = _allGroups[dto.Group];
            GroupPairs[group].ForEach(p => p.ApplyLastReceivedData());
        }
        RecreateLazy();
    }

    internal void SetGroupStatusInfo(GroupPairUserInfoDto dto)
    {
        _allGroups[dto.Group].GroupUserInfo = dto.GroupUserInfo;
        RecreateLazy();
    }

    internal void SetGroupUserPermissions(GroupPairUserPermissionDto dto)
    {
        var prevPermissions = _allGroups[dto.Group].GroupUserPermissions;
        _allGroups[dto.Group].GroupUserPermissions = dto.GroupPairPermissions;
        if (prevPermissions.IsDisableAnimations() != dto.GroupPairPermissions.IsDisableAnimations()
            || prevPermissions.IsDisableSounds() != dto.GroupPairPermissions.IsDisableSounds()
            || prevPermissions.IsDisableVFX() != dto.GroupPairPermissions.IsDisableVFX())
        {
            RecreateLazy();
            var group = _allGroups[dto.Group];
            GroupPairs[group].ForEach(p => p.ApplyLastReceivedData());
        }
        RecreateLazy();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _dalamudContextMenu.OnMenuOpened -= DalamudContextMenuOnOnOpenGameObjectContextMenu;

        DisposePairs();
    }

    private void DalamudContextMenuOnOnOpenGameObjectContextMenu(Dalamud.Game.Gui.ContextMenu.IMenuOpenedArgs args)
    {
        if (args.MenuType == Dalamud.Game.Gui.ContextMenu.ContextMenuType.Inventory) return;
        if (!_configurationService.Current.EnableRightClickMenus) return;
        try
        {
            Logger.LogDebug("[ContextMenu] Opened: Type={type}, TargetType={tgtType}", args.MenuType, args.Target?.GetType().Name ?? "null");
        }
        catch { /* logging should never break menu */ }

        TryAddAutoDetectPairRequestItem(args);

        foreach (var pair in _allClientPairs.Where((p => p.Value.IsVisible)))
        {
            pair.Value.AddContextMenu(args);
        }
    }

    private void TryAddAutoDetectPairRequestItem(IMenuOpenedArgs args)
    {
        if (!_configurationService.Current.EnableAutoDetectDiscovery) { Logger.LogDebug("[ContextMenu] Skipped pair request: AutoDetectDiscovery disabled"); return; }
        if (!_configurationService.Current.AllowAutoDetectPairRequests) { Logger.LogDebug("[ContextMenu] Skipped pair request: PairRequests not allowed"); return; }
        if (args.Target is not MenuTargetDefault target) { Logger.LogDebug("[ContextMenu] Skipped pair request: Target not MenuTargetDefault (was {t})", args.Target?.GetType().Name ?? "null"); return; }

        uint targetObjectId = (uint)target.TargetObjectId;
        if (targetObjectId == 0 || targetObjectId == uint.MaxValue) { Logger.LogDebug("[ContextMenu] Skipped pair request: invalid targetObjectId={id}", targetObjectId); return; }

        if (_allClientPairs.Any(p => p.Value.GetPlayerCharacterId() == targetObjectId))
        {
            Logger.LogDebug("[ContextMenu] Skipped pair request: target is already a known pair (objId={id})", targetObjectId);
            return;
        }

        if (!_dalamudUtilService.TryGetPlayerCharacterByObjectId(targetObjectId, out var clickedPlayer))
        {
            Logger.LogDebug("[ContextMenu] Skipped pair request: could not resolve player for objId={id}", targetObjectId);
            return;
        }

        var entries = _nearbyDiscoveryService.SnapshotEntries();
        var clickedPlayerName = clickedPlayer.Name ?? string.Empty;

        static bool NamesEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            return CompareInfo.Compare(left, right, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreSymbols) == 0;
        }

        static bool NameMatches(UmbraSync.Services.Mediator.NearbyEntry entry, string clicked) =>
            NamesEqual(entry.DisplayName ?? string.Empty, clicked) ||
            NamesEqual(entry.Name, clicked);

        var nearbyEntry = entries.FirstOrDefault(e =>
            e.IsMatch
            && NameMatches(e, clickedPlayerName)
            && e.WorldId == (ushort)clickedPlayer.HomeWorldId);

        if (nearbyEntry == null)
        {
            Logger.LogDebug("[ContextMenu] No AutoDetect match for {name}@{world} (entries={cnt}). Example entries: {examples}",
                clickedPlayerName,
                (ushort)clickedPlayer.HomeWorldId,
                entries.Count,
                string.Join(", ", entries.Take(5).Select(e => $"{e.Name}/{e.DisplayName}@{e.WorldId}")));
            return;
        }

        bool canSendRequest = nearbyEntry.AcceptPairRequests && !string.IsNullOrEmpty(nearbyEntry.Token);
        bool canAddPair = !string.IsNullOrEmpty(nearbyEntry.Uid)
            && !_allClientPairs.Keys.Any(p => string.Equals(p.UID, nearbyEntry.Uid, StringComparison.Ordinal));

        if (!canSendRequest && !canAddPair)
        {
            Logger.LogDebug("[ContextMenu] AutoDetect entry for {name}@{world} is not actionable (Token={hasToken}, Uid={hasUid}, Accepts={accepts})",
                clickedPlayerName, (ushort)clickedPlayer.HomeWorldId,
                !string.IsNullOrEmpty(nearbyEntry.Token),
                !string.IsNullOrEmpty(nearbyEntry.Uid),
                nearbyEntry.AcceptPairRequests);
            return;
        }

        Logger.LogDebug("[ContextMenu] AutoDetect match found for {name}@{world}: entry={entryName}/{display}@{entryWorld} (Uid={uid}, Token={hasToken})",
            clickedPlayerName, (ushort)clickedPlayer.HomeWorldId,
            nearbyEntry.Name, nearbyEntry.DisplayName ?? "<null>", nearbyEntry.WorldId, nearbyEntry.Uid ?? "<null>", !string.IsNullOrEmpty(nearbyEntry.Token));

        if (canSendRequest)
        {
            args.AddMenuItem(new MenuItem
            {
                Name = Loc.Get("ContextMenu.AutoDetect.SendRequest"),
                // Umbra-branded context entry styling: purple background with 'U'
                PrefixColor = 708,
                PrefixChar = 'U',
                UseDefaultPrefix = false,
                IsEnabled = true,
                IsSubmenu = false,
                IsReturn = false,
                Priority = 1,
                OnClicked = args =>
                {
                    _ = _autoDetectRequestService.SendRequestAsync(
                        nearbyEntry.Token!,
                        nearbyEntry.Uid,
                        nearbyEntry.DisplayName ?? nearbyEntry.Name);
                }
            });
        }

        if (canAddPair)
        {
            args.AddMenuItem(new MenuItem
            {
                Name = Loc.Get("ContextMenu.AutoDetect.AddPair"),
                PrefixColor = 708,
                PrefixChar = '+',
                UseDefaultPrefix = false,
                IsEnabled = true,
                IsSubmenu = false,
                IsReturn = false,
                Priority = 0,
                OnClicked = args =>
                {
                    _ = _apiController.Value.UserAddPair(new UserDto(new UserData(nearbyEntry.Uid!)));
                }
            });
        }

        Logger.LogDebug("[ContextMenu] Added auto-detect menu for {name}@{world} (Uid={uid}) Request={reqEnabled} AddPair={addEnabled}",
            clickedPlayerName, (ushort)clickedPlayer.HomeWorldId, nearbyEntry.Uid, canSendRequest, canAddPair);
    }

    private Lazy<List<Pair>> DirectPairsLazy() => new(() => _allClientPairs.Select(k => k.Value)
        .Where(k => k.UserPair != null).ToList());

    private void DisposePairs()
    {
        Logger.LogDebug("Disposing all Pairs");
        Parallel.ForEach(_allClientPairs, item =>
        {
            item.Value.MarkOffline(wait: false);
        });

        RecreateLazy();
    }

    private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> GroupPairsLazy()
    {
        return new Lazy<Dictionary<GroupFullInfoDto, List<Pair>>>(() =>
        {
            Dictionary<GroupFullInfoDto, List<Pair>> outDict = new();
            foreach (var group in _allGroups)
            {
                outDict[group.Value] = _allClientPairs.Select(p => p.Value).Where(p => p.GroupPair.Any(g => GroupDataComparer.Instance.Equals(group.Key, g.Key.Group))).ToList();
            }
            return outDict;
        });
    }

    private void ReapplyPairData()
    {
        foreach (var pair in _allClientPairs.Select(k => k.Value))
        {
            pair.ApplyLastReceivedData(forced: true);
        }
    }

    private void RecreateLazy()
    {
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
    }

    private static readonly CompareInfo CompareInfo = CultureInfo.InvariantCulture.CompareInfo;
}
