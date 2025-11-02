using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.AutoDetect;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Services.Notifications;
using MareSynchronos.UI;
using MareSynchronos.UI.Components.Popup;
using MareSynchronos.WebAPI;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public class UiFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mareMediator;
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly MareProfileManager _mareProfileManager;
    private readonly PerformanceCollectorService _performanceCollectorService;
    private readonly SyncshellDiscoveryService _syncshellDiscoveryService;
    private readonly NotificationTracker _notificationTracker;

    public UiFactory(ILoggerFactory loggerFactory, MareMediator mareMediator, ApiController apiController,
        UiSharedService uiSharedService, PairManager pairManager, SyncshellDiscoveryService syncshellDiscoveryService, ServerConfigurationManager serverConfigManager,
        MareProfileManager mareProfileManager, PerformanceCollectorService performanceCollectorService, NotificationTracker notificationTracker)
    {
        _loggerFactory = loggerFactory;
        _mareMediator = mareMediator;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _syncshellDiscoveryService = syncshellDiscoveryService;
        _serverConfigManager = serverConfigManager;
        _mareProfileManager = mareProfileManager;
        _performanceCollectorService = performanceCollectorService;
        _notificationTracker = notificationTracker;
    }

    public SyncshellAdminUI CreateSyncshellAdminUi(GroupFullInfoDto dto)
    {
        return new SyncshellAdminUI(_loggerFactory.CreateLogger<SyncshellAdminUI>(), _mareMediator,
            _apiController, _uiSharedService, _pairManager, _syncshellDiscoveryService, dto, _performanceCollectorService, _notificationTracker);
    }

    public StandaloneProfileUi CreateStandaloneProfileUi(Pair pair)
    {
        return new StandaloneProfileUi(_loggerFactory.CreateLogger<StandaloneProfileUi>(), _mareMediator,
            _uiSharedService, _serverConfigManager, _mareProfileManager, pair, _performanceCollectorService);
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
