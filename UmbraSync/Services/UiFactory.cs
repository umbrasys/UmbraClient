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

public class UiFactory(
    ILoggerFactory loggerFactory,
    MareMediator mareMediator,
    ApiController apiController,
    UiSharedService uiSharedService,
    PairManager pairManager,
    SyncshellDiscoveryService syncshellDiscoveryService,
    ServerConfigurationManager serverConfigManager,
    UmbraProfileManager umbraProfileManager,
    PerformanceCollectorService performanceCollectorService,
    NotificationTracker notificationTracker,
    DalamudUtilService dalamudUtilService)
{
    public SyncshellAdminUI CreateSyncshellAdminUi(GroupFullInfoDto dto)
    {
        return new SyncshellAdminUI(loggerFactory.CreateLogger<SyncshellAdminUI>(), mareMediator,
            apiController, uiSharedService, pairManager, syncshellDiscoveryService, dto, performanceCollectorService, notificationTracker, dalamudUtilService);
    }

    public StandaloneProfileUi CreateStandaloneProfileUi(Pair pair)
    {
        return new StandaloneProfileUi(loggerFactory.CreateLogger<StandaloneProfileUi>(), mareMediator,
            uiSharedService, serverConfigManager, umbraProfileManager, apiController, pair, performanceCollectorService);
    }

    public PermissionWindowUI CreatePermissionPopupUi(Pair pair)
    {
        return new PermissionWindowUI(loggerFactory.CreateLogger<PermissionWindowUI>(), pair,
            mareMediator, uiSharedService, apiController, performanceCollectorService);
    }

    public PlayerAnalysisUI CreatePlayerAnalysisUi(Pair pair)
    {
        return new PlayerAnalysisUI(loggerFactory.CreateLogger<PlayerAnalysisUI>(), pair,
            mareMediator, uiSharedService, performanceCollectorService);
    }
}
