using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MareSynchronos.FileCache;
using MareSynchronos.Interop;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.PlayerData.Services;
using MareSynchronos.Services;
using MareSynchronos.Services.Events;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Services.Notifications;
using MareSynchronos.UI;
using MareSynchronos.UI.Components;
using MareSynchronos.UI.Components.Popup;
using MareSynchronos.UI.Handlers;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.Files;
using MareSynchronos.WebAPI.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MareSynchronos.Services.CharaData;

using MareSynchronos;

namespace Umbra;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IHost _host;

#pragma warning disable CA2211, CS8618, MA0069, S1104, S2223
    public static Plugin Self;
#pragma warning restore CA2211, CS8618, MA0069, S1104, S2223
    public Action<IFramework>? RealOnFrameworkUpdate { get; set; }
    public void OnFrameworkUpdate(IFramework framework)
    {
        RealOnFrameworkUpdate?.Invoke(framework);
    }

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IDataManager gameData,
        IFramework framework, IObjectTable objectTable, IClientState clientState, ICondition condition, IChatGui chatGui,
        IGameGui gameGui, IDtrBar dtrBar, IToastGui toastGui, IPluginLog pluginLog, ITargetManager targetManager, INotificationManager notificationManager,
        ITextureProvider textureProvider, IContextMenu contextMenu, IGameInteropProvider gameInteropProvider,
        INamePlateGui namePlateGui, IGameConfig gameConfig, IPartyList partyList)
    {
        Plugin.Self = this;
        _host = new HostBuilder()
        .UseContentRoot(pluginInterface.ConfigDirectory.FullName)
        .ConfigureLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddDalamudLogging(pluginLog);
            lb.SetMinimumLevel(LogLevel.Trace);
        })
        .ConfigureServices(collection =>
        {
            collection.AddSingleton(new WindowSystem("MareSynchronos"));
            collection.AddSingleton<FileDialogManager>();

            // add dalamud services
            collection.AddSingleton(_ => pluginInterface);
            collection.AddSingleton(_ => pluginInterface.UiBuilder);
            collection.AddSingleton(_ => commandManager);
            collection.AddSingleton(_ => gameData);
            collection.AddSingleton(_ => framework);
            collection.AddSingleton(_ => objectTable);
            collection.AddSingleton(_ => clientState);
            collection.AddSingleton(_ => condition);
            collection.AddSingleton(_ => chatGui);
            collection.AddSingleton(_ => gameGui);
            collection.AddSingleton(_ => dtrBar);
            collection.AddSingleton(_ => toastGui);
            collection.AddSingleton(_ => pluginLog);
            collection.AddSingleton(_ => targetManager);
            collection.AddSingleton(_ => notificationManager);
            collection.AddSingleton(_ => textureProvider);
            collection.AddSingleton(_ => contextMenu);
            collection.AddSingleton(_ => gameInteropProvider);
            collection.AddSingleton(_ => namePlateGui);
            collection.AddSingleton(_ => gameConfig);
            collection.AddSingleton(_ => partyList);

            // add mare related singletons
            collection.AddSingleton<MareMediator>();
            collection.AddSingleton<FileCacheManager>();
            collection.AddSingleton<ServerConfigurationManager>();
            collection.AddSingleton<ApiController>();
            collection.AddSingleton<PerformanceCollectorService>();
            collection.AddSingleton<HubFactory>();
            collection.AddSingleton<FileUploadManager>();
            collection.AddSingleton<FileTransferOrchestrator>();
            collection.AddSingleton<MareSynchronos.Services.AutoDetect.DiscoveryConfigProvider>();
            collection.AddSingleton<MareSynchronos.WebAPI.AutoDetect.DiscoveryApiClient>();
            collection.AddSingleton<MareSynchronos.Services.AutoDetect.AutoDetectRequestService>();
            collection.AddSingleton<MareSynchronos.Services.AutoDetect.NearbyDiscoveryService>();
            collection.AddSingleton<MareSynchronos.Services.AutoDetect.NearbyPendingService>();
            collection.AddSingleton<MareSynchronos.Services.AutoDetect.AutoDetectSuppressionService>();
            collection.AddSingleton<MareSynchronos.Services.AutoDetect.SyncshellDiscoveryService>();
            collection.AddSingleton<MarePlugin>();
            collection.AddSingleton<MareProfileManager>();
            collection.AddSingleton<GameObjectHandlerFactory>();
            collection.AddSingleton<FileDownloadManagerFactory>();
            collection.AddSingleton<PairHandlerFactory>();
            collection.AddSingleton<PairAnalyzerFactory>();
            collection.AddSingleton<PairFactory>();
            collection.AddSingleton<XivDataAnalyzer>();
            collection.AddSingleton<CharacterAnalyzer>();
            collection.AddSingleton<TokenProvider>();
            collection.AddSingleton<AccountRegistrationService>();
            collection.AddSingleton<PluginWarningNotificationService>();
            collection.AddSingleton<FileCompactor>();
            collection.AddSingleton<TagHandler>();
            collection.AddSingleton<SyncDefaultsService>();
            collection.AddSingleton<UidDisplayHandler>();
            collection.AddSingleton<PluginWatcherService>();
            collection.AddSingleton<PlayerPerformanceService>();

            collection.AddSingleton<CharaDataManager>();
            collection.AddSingleton<CharaDataFileHandler>();
            collection.AddSingleton<CharaDataCharacterHandler>();
            collection.AddSingleton<CharaDataNearbyManager>();
            collection.AddSingleton<CharaDataGposeTogetherManager>();
            collection.AddSingleton<McdfShareManager>();

            collection.AddSingleton<VfxSpawnManager>();
            collection.AddSingleton<BlockedCharacterHandler>();
            collection.AddSingleton<IpcProvider>();
            collection.AddSingleton<VisibilityService>();
            collection.AddSingleton<EventAggregator>();
            collection.AddSingleton<DalamudUtilService>();
            collection.AddSingleton<DtrEntry>();
            collection.AddSingleton<PairManager>();
            collection.AddSingleton<RedrawManager>();
            collection.AddSingleton<IpcCallerPenumbra>();
            collection.AddSingleton<IpcCallerGlamourer>();
            collection.AddSingleton<IpcCallerCustomize>();
            collection.AddSingleton<IpcCallerHeels>();
            collection.AddSingleton<IpcCallerHonorific>();
            collection.AddSingleton<IpcCallerMoodles>();
            collection.AddSingleton<IpcCallerPetNames>();
            collection.AddSingleton<IpcCallerBrio>();
            collection.AddSingleton<IpcCallerMare>();
            collection.AddSingleton<IpcManager>();
            collection.AddSingleton<NotificationService>();
            collection.AddSingleton<TemporarySyncshellNotificationService>();
            collection.AddSingleton<PartyListTypingService>();
            collection.AddSingleton<TypingIndicatorStateService>();
            collection.AddSingleton<ChatTwoCompatibilityService>();
            collection.AddSingleton<NotificationTracker>();

            collection.AddSingleton((s) => new MareConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new NotesConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerTagConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new SyncshellConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new TransientConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new XivDataStorageService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new PlayerPerformanceConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerBlockConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new CharaDataConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new RemoteConfigCacheService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<MareConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<ServerConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<NotesConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<ServerTagConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<SyncshellConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<TransientConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<XivDataStorageService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<PlayerPerformanceConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<ServerBlockConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<CharaDataConfigService>());
            collection.AddSingleton<IConfigService<IMareConfiguration>>(s => s.GetRequiredService<RemoteConfigCacheService>());
            collection.AddSingleton<ConfigurationMigrator>();
            collection.AddSingleton<ConfigurationSaveService>();

            collection.AddSingleton<HubFactory>();

            // add scoped services
            collection.AddScoped<CacheMonitor>();
            collection.AddScoped<UiFactory>();
            collection.AddScoped<SettingsUi>();
            collection.AddScoped<CompactUi>();
            collection.AddScoped<EditProfileUi>();
            collection.AddScoped<DataAnalysisUi>();
            collection.AddScoped<CharaDataHubUi>();
            collection.AddScoped<AutoDetectUi>();
            collection.AddScoped<WindowMediatorSubscriberBase>(sp => sp.GetRequiredService<SettingsUi>());
            collection.AddScoped<WindowMediatorSubscriberBase>(sp => sp.GetRequiredService<CompactUi>());
            collection.AddScoped<WindowMediatorSubscriberBase, IntroUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DownloadUi>();
            collection.AddScoped<WindowMediatorSubscriberBase>(sp => sp.GetRequiredService<AutoDetectUi>());
            collection.AddScoped<WindowMediatorSubscriberBase, ChangelogUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, PopoutProfileUi>();
            collection.AddScoped<WindowMediatorSubscriberBase>(sp => sp.GetRequiredService<DataAnalysisUi>());
            collection.AddScoped<WindowMediatorSubscriberBase, EventViewerUI>();
            collection.AddScoped<WindowMediatorSubscriberBase, CharaDataHubUi>();
            collection.AddScoped<WindowMediatorSubscriberBase>(sp => sp.GetRequiredService<EditProfileUi>());
            collection.AddScoped<WindowMediatorSubscriberBase, PopupHandler>();
            collection.AddScoped<WindowMediatorSubscriberBase, TypingIndicatorOverlay>();
            collection.AddScoped<IPopupHandler, ReportPopupHandler>();
            collection.AddScoped<IPopupHandler, BanUserPopupHandler>();
            collection.AddScoped<CacheCreationService>();
            collection.AddScoped<TransientResourceManager>();
            collection.AddScoped<PlayerDataFactory>();
            collection.AddScoped<OnlinePlayerManager>();
            collection.AddScoped<UiService>();
            collection.AddScoped<CommandManagerService>();
            collection.AddScoped<UiSharedService>();
            collection.AddScoped<ChatService>();
            collection.AddScoped<GuiHookService>();
            collection.AddScoped<ChatTypingDetectionService>();

            collection.AddHostedService(p => p.GetRequiredService<PluginWatcherService>());
            collection.AddHostedService(p => p.GetRequiredService<ConfigurationSaveService>());
            collection.AddHostedService(p => p.GetRequiredService<MareMediator>());
            collection.AddHostedService(p => p.GetRequiredService<NotificationService>());
            collection.AddHostedService(p => p.GetRequiredService<TemporarySyncshellNotificationService>());
            collection.AddHostedService(p => p.GetRequiredService<FileCacheManager>());
            collection.AddHostedService(p => p.GetRequiredService<ConfigurationMigrator>());
            collection.AddHostedService(p => p.GetRequiredService<DalamudUtilService>());
            collection.AddHostedService(p => p.GetRequiredService<PerformanceCollectorService>());
            collection.AddHostedService(p => p.GetRequiredService<DtrEntry>());
            collection.AddHostedService(p => p.GetRequiredService<EventAggregator>());
            collection.AddHostedService(p => p.GetRequiredService<MarePlugin>());
            collection.AddHostedService(p => p.GetRequiredService<IpcProvider>());
            collection.AddHostedService(p => p.GetRequiredService<MareSynchronos.Services.AutoDetect.NearbyDiscoveryService>());
            collection.AddHostedService(p => p.GetRequiredService<MareSynchronos.Services.AutoDetect.SyncshellDiscoveryService>());
            collection.AddHostedService(p => p.GetRequiredService<ChatTwoCompatibilityService>());
            collection.AddHostedService(p => p.GetRequiredService<MareSynchronos.Services.AutoDetect.AutoDetectSuppressionService>());
        })
        .Build();

        try
        {
            var partyListTypingService = _host.Services.GetRequiredService<PartyListTypingService>();
            pluginInterface.UiBuilder.Draw += partyListTypingService.Draw;
        }
        catch (Exception e)
        {
            pluginLog.Warning(e, "Failed to initialize PartyListTypingService draw hook");
        }

        _ = Task.Run(async () => {
            try
            {
                await _host.StartAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                pluginLog.Error(e, "HostBuilder startup exception");
            }
        }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
    }
}
