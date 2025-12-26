using UmbraSync.API.Dto.Group;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services.AutoDetect;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;
using UmbraSync.Services.Notification;
using UmbraSync.UI;
using UmbraSync.UI.Components.Popup;
using UmbraSync.WebAPI;
using Microsoft.Extensions.Logging;

namespace UmbraSync.Services;

public class UiFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mareMediator;
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly UmbraProfileManager _umbraProfileManager;
    private readonly PerformanceCollectorService _performanceCollectorService;
    private readonly SyncshellDiscoveryService _syncshellDiscoveryService;
    private readonly NotificationTracker _notificationTracker;
    private readonly DalamudUtilService _dalamudUtilService;

    public UiFactory(ILoggerFactory loggerFactory, MareMediator mareMediator, ApiController apiController,
        UiSharedService uiSharedService, PairManager pairManager, SyncshellDiscoveryService syncshellDiscoveryService, ServerConfigurationManager serverConfigManager,
        UmbraProfileManager umbraProfileManager, PerformanceCollectorService performanceCollectorService, NotificationTracker notificationTracker,
        DalamudUtilService dalamudUtilService)
    {
        _loggerFactory = loggerFactory;
        _mareMediator = mareMediator;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _syncshellDiscoveryService = syncshellDiscoveryService;
        _serverConfigManager = serverConfigManager;
        _umbraProfileManager = umbraProfileManager;
        _performanceCollectorService = performanceCollectorService;
        _notificationTracker = notificationTracker;
        _dalamudUtilService = dalamudUtilService;
    }

    public SyncshellAdminUI CreateSyncshellAdminUi(GroupFullInfoDto dto)
    {
        return new SyncshellAdminUI(_loggerFactory.CreateLogger<SyncshellAdminUI>(), _mareMediator,
            _apiController, _uiSharedService, _pairManager, _syncshellDiscoveryService, dto, _performanceCollectorService, _notificationTracker, _dalamudUtilService);
    }

    public StandaloneProfileUi CreateStandaloneProfileUi(Pair pair)
    {
        return new StandaloneProfileUi(_loggerFactory.CreateLogger<StandaloneProfileUi>(), _mareMediator,
            _uiSharedService, _serverConfigManager, _umbraProfileManager, _apiController, pair, _performanceCollectorService);
    }

    public PermissionWindowUI CreatePermissionPopupUi(Pair pair)
    {
        return new PermissionWindowUI(_loggerFactory.CreateLogger<PermissionWindowUI>(), pair,
            _mareMediator, _uiSharedService, _apiController, _performanceCollectorService);
    }

    public PlayerAnalysisUI CreatePlayerAnalysisUi(Pair pair)
    {
        return new PlayerAnalysisUI(_loggerFactory.CreateLogger<PlayerAnalysisUI>(), pair,
            _mareMediator, _uiSharedService, _performanceCollectorService);
    }
}
