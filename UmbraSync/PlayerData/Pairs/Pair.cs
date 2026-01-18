using Dalamud.Game.Gui.ContextMenu;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using UmbraSync.API.Data;
using UmbraSync.API.Data.Comparer;
using UmbraSync.API.Data.Extensions;
using UmbraSync.API.Dto.Group;
using UmbraSync.API.Dto.User;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Factories;
using UmbraSync.PlayerData.Handlers;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;
using UmbraSync.Utils;

namespace UmbraSync.PlayerData.Pairs;

public class Pair : DisposableMediatorSubscriberBase
{
    private readonly PairHandlerFactory _cachedPlayerFactory;
    private readonly SemaphoreSlim _creationSemaphore = new(1);
    private readonly ILogger<Pair> _logger;
    private readonly MareConfigService _mareConfig;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly PairStateCache _pairStateCache;
    private CancellationTokenSource _applicationCts = new();
    private OnlineUserIdentDto? _onlineUserIdentDto = null;
    private ushort? _worldId = null;

    public Pair(ILogger<Pair> logger, UserData userData, PairHandlerFactory cachedPlayerFactory,
        MareMediator mediator, MareConfigService mareConfig, ServerConfigurationManager serverConfigurationManager,
        PairStateCache pairStateCache)
        : base(logger, mediator)
    {
        _logger = logger;
        _cachedPlayerFactory = cachedPlayerFactory;
        _mareConfig = mareConfig;
        _serverConfigurationManager = serverConfigurationManager;
        _pairStateCache = pairStateCache;

        UserData = userData;

        Mediator.SubscribeKeyed<HoldPairApplicationMessage>(this, UserData.UID, (msg) => HoldApplication(msg.Source));
        Mediator.SubscribeKeyed<UnholdPairApplicationMessage>(this, UserData.UID, (msg) => UnholdApplication(msg.Source));
    }

    public Dictionary<GroupFullInfoDto, GroupPairFullInfoDto> GroupPair { get; set; } = new(GroupDtoComparer.Instance);
    public bool HasCachedPlayer => CachedPlayer != null && !string.IsNullOrEmpty(CachedPlayer.PlayerName) && _onlineUserIdentDto != null;
    public bool IsOnline => CachedPlayer != null;
    
    public bool IsPaused
    {
        get
        { 
            if (_serverConfigurationManager.IsUidPaused(UserData.UID))
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                    _logger.LogTrace("IsPaused: true (LocalPaused) for {uid}", UserData.UID);
                return true;
            }

            if (UserPair != null && UserPair.OwnPermissions.IsPaused())
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                    _logger.LogTrace("IsPaused: true (Individual OwnPaused) for {uid}", UserData.UID);
                return true;
            }

            if (GroupPair.Count > 0)
            {
                foreach (var p in GroupPair)
                {
                    if (p.Key.GroupUserPermissions.IsPaused())
                    {
                        if (_logger.IsEnabled(LogLevel.Trace))
                            _logger.LogTrace("IsPaused: true (Group {gid} OwnPaused) for {uid}", p.Key.Group.GID, UserData.UID);
                        return true;
                    }
                    if (p.Key.GroupPermissions.IsPaused())
                    {
                        if (_logger.IsEnabled(LogLevel.Trace))
                            _logger.LogTrace("IsPaused: true (Group {gid} GroupPaused) for {uid}", p.Key.Group.GID, UserData.UID);
                        return true;
                    }
                }
            }

            return false;
        }
    }

    // Download locks apply earlier in the process than Application locks
    private ConcurrentDictionary<string, int> HoldDownloadLocks { get; set; } = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, int> HoldApplicationLocks { get; set; } = new(StringComparer.Ordinal);

    public bool IsDownloadBlocked => HoldDownloadLocks.Values.Any(f => f > 0);
    public bool IsApplicationBlocked => HoldApplicationLocks.Values.Any(f => f > 0) || IsDownloadBlocked;

    public IEnumerable<string> HoldDownloadReasons => HoldDownloadLocks.Keys;
    public IEnumerable<string> HoldApplicationReasons => Enumerable.Concat(HoldDownloadLocks.Keys, HoldApplicationLocks.Keys);

    public bool IsVisible => CachedPlayer?.IsVisible ?? false;
    public uint WorldId => _worldId ?? 0;

    public CharacterData? LastReceivedCharacterData { get; set; }

    public string? PlayerName => GetPlayerName();
    public uint PlayerCharacterId => GetPlayerCharacterId();
    public long LastAppliedDataBytes => CachedPlayer?.LastAppliedDataBytes ?? -1;
    public long LastAppliedDataTris { get; set; } = -1;
    public long LastAppliedApproximateVRAMBytes { get; set; } = -1;
    public string Ident => _onlineUserIdentDto?.Ident ?? string.Empty;
    public PairAnalyzer? PairAnalyzer => CachedPlayer?.PairAnalyzer;
    public UserData UserData { get; init; }
    public UserPairDto? UserPair { get; set; }
    private PairHandler? CachedPlayer { get; set; }
    public PairHandler? Handler => CachedPlayer;

    public void AddContextMenu(IMenuOpenedArgs args)
    {
        if (CachedPlayer == null || (args.Target is not MenuTargetDefault target) || target.TargetObjectId != CachedPlayer.PlayerCharacterId || IsPaused) return;

        void Add(string name, Action<IMenuItemClickedArgs>? action)
        {
            args.AddMenuItem(new MenuItem()
            {
                Name = name,
                OnClicked = action,
                PrefixColor = 708,
                PrefixChar = 'U',
                UseDefaultPrefix = false,
                IsEnabled = true,
                IsSubmenu = false,
                IsReturn = false,
                Priority = 1,
            });
        }

        bool isBlocked = IsApplicationBlocked;
        bool isBlacklisted = _serverConfigurationManager.IsUidBlacklisted(UserData.UID);
        bool isWhitelisted = _serverConfigurationManager.IsUidWhitelisted(UserData.UID);

        Add("Ouvrir le profil", _ => Mediator.Publish(new ProfileOpenStandaloneMessage(this)));

        Add(IsPaused ? "Reprendre la synchronisation" : "Mettre en pause", _ => Mediator.Publish(new PauseMessage(UserData)));

        if (!isBlocked && !isBlacklisted)
            Add("Toujours bloquer les apparences moddées.", _ =>
            {
                _serverConfigurationManager.AddBlacklistUid(UserData.UID);
                HoldApplication("Blacklist", maxValue: 1);
                ApplyLastReceivedData(forced: true);
            });
        else if (isBlocked && !isWhitelisted)
            Add("Toujours autoriser les apparences moddées", _ =>
            {
                _serverConfigurationManager.AddWhitelistUid(UserData.UID);
                UnholdApplication("Blacklist", skipApplication: true);
                ApplyLastReceivedData(forced: true);
            });

        if (isWhitelisted)
            Add("Retirer de la liste blanche", _ =>
            {
                _serverConfigurationManager.RemoveWhitelistUid(UserData.UID);
                ApplyLastReceivedData(forced: true);
            });
        else if (isBlacklisted)
            Add("Retirer de la liste noire", _ =>
            {
                _serverConfigurationManager.RemoveBlacklistUid(UserData.UID);
                UnholdApplication("Blacklist", skipApplication: true);
                ApplyLastReceivedData(forced: true);
            });

        Add("Réappliquer les dernières données", _ => ApplyLastReceivedData(forced: true));

        if (UserPair != null)
        {
            Add("Changer les permissions", _ => Mediator.Publish(new OpenPermissionWindow(this)));
        }
    }


    public void ApplyData(OnlineUserCharaDataDto data)
    {
        _applicationCts = _applicationCts.CancelRecreate();
        LastReceivedCharacterData = data.CharaData;

        // Stocke les données dans le cache pour pouvoir les récupérer après un unpause
        if (!string.IsNullOrEmpty(Ident) && data.CharaData != null)
        {
            _pairStateCache.Store(Ident, data.CharaData);
            _logger.LogDebug("Stored character data in cache for {uid} (ident: {ident})", data.User.UID, Ident);
        }

        if (CachedPlayer == null)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Received Data for {uid} but CachedPlayer does not exist, waiting", data.User.UID);
            _ = Task.Run(async () =>
            {
                using var timeoutCts = new CancellationTokenSource();
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));
                var appToken = _applicationCts.Token;
                using var combined = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, appToken);
                while (CachedPlayer == null && !combined.Token.IsCancellationRequested)
                {
                    await Task.Delay(250, combined.Token).ConfigureAwait(false);
                }

                if (!combined.IsCancellationRequested)
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("Applying delayed data for {uid}", data.User.UID);
                    ApplyLastReceivedData();
                }
            });
            return;
        }

        ApplyLastReceivedData();
    }

    public void ApplyLastReceivedData(bool forced = false)
    {
        _logger.LogDebug("ApplyLastReceivedData called for {uid} (forced: {forced}, Ident: {ident}, HasCachedPlayer: {hasCP}, HasLastData: {hasData})",
            UserData.UID, forced, Ident, CachedPlayer != null, LastReceivedCharacterData != null);

        // Si CachedPlayer n'existe pas mais qu'on a un Ident valide, essayer de le créer
        if (CachedPlayer == null && !string.IsNullOrEmpty(Ident))
        {
            _logger.LogDebug("CachedPlayer null but Ident available for {uid}, attempting to create", UserData.UID);
            CreateCachedPlayer();
        }

        if (CachedPlayer == null)
        {
            _logger.LogDebug("ApplyLastReceivedData: CachedPlayer still null for {uid}, aborting", UserData.UID);
            return;
        }

        // Si LastReceivedCharacterData est null, tente de récupérer du cache
        if (LastReceivedCharacterData == null && !string.IsNullOrEmpty(Ident))
        {
            var cachedData = _pairStateCache.TryLoad(Ident);
            if (cachedData != null)
            {
                _logger.LogDebug("Recovered character data from cache for {uid} (ident: {ident})", UserData.UID, Ident);
                LastReceivedCharacterData = cachedData;
            }
            else
            {
                _logger.LogDebug("No cached data found for {uid} (ident: {ident})", UserData.UID, Ident);
            }
        }

        if (LastReceivedCharacterData == null)
        {
            _logger.LogDebug("ApplyLastReceivedData: LastReceivedCharacterData is null for {uid}, aborting", UserData.UID);
            return;
        }

        if (IsDownloadBlocked || IsPaused)
        {
            _logger.LogDebug("ApplyLastReceivedData: Blocked or paused for {uid} (DownloadBlocked: {db}, Paused: {p})", UserData.UID, IsDownloadBlocked, IsPaused);
            return;
        }

        if (_serverConfigurationManager.IsUidBlacklisted(UserData.UID))
            HoldApplication("Blacklist", maxValue: 1);

        _logger.LogDebug("ApplyLastReceivedData: Applying character data for {uid}", UserData.UID);
        CachedPlayer.ApplyCharacterData(Guid.NewGuid(), RemoveNotSyncedFiles(LastReceivedCharacterData.DeepClone())!, forced);
    }

    public void CreateCachedPlayer(OnlineUserIdentDto? dto = null)
    {
        try
        {
            _creationSemaphore.Wait();

            if (CachedPlayer != null) return;

            if (dto == null && _onlineUserIdentDto == null)
            {
                CachedPlayer?.Dispose();
                CachedPlayer = null;
                return;
            }
            if (dto != null)
            {
                _onlineUserIdentDto = dto;
            }

            CachedPlayer?.Dispose();
            CachedPlayer = _cachedPlayerFactory.Create(this);
        }
        finally
        {
            _creationSemaphore.Release();
        }
    }

    public string? GetNote()
    {
        return _serverConfigurationManager.GetNoteForUid(UserData.UID);
    }

    public string? GetPlayerName()
    {
        if (CachedPlayer != null && CachedPlayer.PlayerName != null)
            return CachedPlayer.PlayerName;
        else
            return _serverConfigurationManager.GetNameForUid(UserData.UID);
    }

    public uint GetPlayerCharacterId()
    {
        if (CachedPlayer != null)
            return CachedPlayer.PlayerCharacterId;
        return uint.MaxValue;
    }

    public void SetWorldId(ushort worldId)
    {
        _worldId = worldId;
    }

    public string? GetNoteOrName()
    {
        string? note = GetNote();
        if (_mareConfig.Current.ShowCharacterNames || IsVisible)
            return note ?? GetPlayerName();
        else
            return note;
    }

    public string GetPairSortKey()
    {
        string? noteOrName = GetNoteOrName();

        if (noteOrName != null)
            return $"0{noteOrName}";
        else
            return $"9{UserData.AliasOrUID}";
    }

    public string GetPlayerNameHash()
    {
        return CachedPlayer?.PlayerNameHash ?? string.Empty;
    }

    public bool HasAnyConnection()
    {
        return UserPair != null || GroupPair.Count > 0;
    }

    public void MarkOffline(bool wait = true)
    {
        try
        {
            if (wait)
                _creationSemaphore.Wait();
            var player = CachedPlayer;
            CachedPlayer = null;
            player?.Dispose();
            _onlineUserIdentDto = null;
        }
        finally
        {
            if (wait)
                _creationSemaphore.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            _applicationCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // token source already disposed, nothing to cancel
        }

        _applicationCts.Dispose();
    }

    public void SetNote(string note)
    {
        _serverConfigurationManager.SetNoteForUid(UserData.UID, note);
    }

    internal void SetIsUploading()
    {
        CachedPlayer?.SetUploading();
    }

    public void HoldApplication(string source, int maxValue = int.MaxValue)
    {
        _logger.LogDebug("Mise en attente de {uid} pour la raison {raison}", UserData.UID, source);
        bool wasHeld = IsApplicationBlocked;
        HoldApplicationLocks.AddOrUpdate(source, 1, (k, v) => Math.Min(maxValue, v + 1));
        if (!wasHeld)
            CachedPlayer?.UndoApplication();
    }

    public void UnholdApplication(string source, bool skipApplication = false)
    {
        _logger.LogDebug("Fin d'attente de {uid} pour la raison {raison}", UserData.UID, source);
        bool wasHeld = IsApplicationBlocked;
        HoldApplicationLocks.AddOrUpdate(source, 0, (k, v) => Math.Max(0, v - 1));
        HoldApplicationLocks.TryRemove(new(source, 0));
        if (!skipApplication && wasHeld && !IsApplicationBlocked)
            ApplyLastReceivedData(forced: true);
    }

    public void HoldDownloads(string source, int maxValue = int.MaxValue)
    {
        _logger.LogDebug("Blocage des téléchargements pour {uid} à cause de {raison}", UserData.UID, source);
        bool wasHeld = IsApplicationBlocked;
        HoldDownloadLocks.AddOrUpdate(source, 1, (k, v) => Math.Min(maxValue, v + 1));
        if (!wasHeld)
            CachedPlayer?.UndoApplication();
    }

    public void UnholdDownloads(string source, bool skipApplication = false)
    {
        _logger.LogDebug("Déblocage des téléchargements pour {uid} (raison {raison})", UserData.UID, source);
        bool wasHeld = IsApplicationBlocked;
        HoldDownloadLocks.AddOrUpdate(source, 0, (k, v) => Math.Max(0, v - 1));
        HoldDownloadLocks.TryRemove(new(source, 0));
        if (!skipApplication && wasHeld && !IsApplicationBlocked)
            ApplyLastReceivedData(forced: true);
    }
    
    private CharacterData? RemoveNotSyncedFiles(CharacterData? data)
    {
        _logger.LogTrace("Removing not synced files");
        if (data == null)
        {
            _logger.LogTrace("Nothing to remove");
            return data;
        }

        var ActiveGroupPairs = GroupPair.Where(p => !p.Key.GroupUserPermissions.IsPaused()).ToList();
        bool disableIndividualAnimations = UserPair != null && UserPair.OwnPermissions.IsDisableAnimations();
        bool disableIndividualVFX = UserPair != null && UserPair.OwnPermissions.IsDisableVFX();
        bool disableIndividualSounds = UserPair != null && UserPair.OwnPermissions.IsDisableSounds();
        bool disableGroupAnimations = ActiveGroupPairs.Any() && ActiveGroupPairs.All(pair => pair.Key.GroupPermissions.IsDisableAnimations() || pair.Key.GroupUserPermissions.IsDisableAnimations());
        bool disableGroupSounds = ActiveGroupPairs.Any() && ActiveGroupPairs.All(pair => pair.Key.GroupPermissions.IsDisableSounds() || pair.Key.GroupUserPermissions.IsDisableSounds());
        bool disableGroupVFX = ActiveGroupPairs.Any() && ActiveGroupPairs.All(pair => pair.Key.GroupPermissions.IsDisableVFX() || pair.Key.GroupUserPermissions.IsDisableVFX());
        bool disableAnimations = (UserPair != null && disableIndividualAnimations) || (UserPair == null && disableGroupAnimations);
        bool disableSounds = (UserPair != null && disableIndividualSounds) || (UserPair == null && disableGroupSounds);
        bool disableVFX = (UserPair != null && disableIndividualVFX) || (UserPair == null && disableGroupVFX);

        _logger.LogTrace("Disable: Sounds: {disableSounds}, Anims: {disableAnimations}, VFX: {disableVFX}",
            disableSounds, disableAnimations, disableVFX);

        if (disableAnimations || disableSounds || disableVFX)
        {
            _logger.LogTrace("Data cleaned up: Animations disabled: {disableAnimations}, Sounds disabled: {disableSounds}, VFX disabled: {disableVFX}",
                disableAnimations, disableSounds, disableVFX);
            foreach (var objectKind in data.FileReplacements.Select(k => k.Key))
            {
                if (disableSounds)
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("scd", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if (disableAnimations)
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("tmb", StringComparison.OrdinalIgnoreCase) || p.EndsWith("pap", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                if (disableVFX)
                    data.FileReplacements[objectKind] = data.FileReplacements[objectKind]
                        .Where(f => !f.GamePaths.Any(p => p.EndsWith("atex", StringComparison.OrdinalIgnoreCase) || p.EndsWith("avfx", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
            }
        }

        return data;
    }
}