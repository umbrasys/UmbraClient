using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;

public class IpcProvider : IHostedService, IMediatorSubscriber
{
    private readonly ILogger<IpcProvider> _logger;
    private readonly IDalamudPluginInterface _pi;
    private readonly MareConfigService _mareConfig;
    private readonly CharaDataManager _charaDataManager;
    private ICallGateProvider<string, IGameObject, bool>? _loadFileProvider;
    private ICallGateProvider<string, IGameObject, Task<bool>>? _loadFileAsyncProvider;
    private ICallGateProvider<List<nint>>? _handledGameAddresses;
    private readonly List<GameObjectHandler> _activeGameObjectHandlers = [];

    private ICallGateProvider<string, IGameObject, bool>? _loadFileProviderMare;
    private ICallGateProvider<string, IGameObject, Task<bool>>? _loadFileAsyncProviderMare;
    private ICallGateProvider<List<nint>>? _handledGameAddressesMare;

    private bool _marePluginEnabled = false;
    private bool _impersonating = false;
    private DateTime _unregisterTime = DateTime.UtcNow;
    private CancellationTokenSource _registerDelayCts = new();

    public bool MarePluginEnabled => _marePluginEnabled;
    public bool ImpersonationActive => _impersonating;

    public MareMediator Mediator { get; init; }

    public IpcProvider(ILogger<IpcProvider> logger, IDalamudPluginInterface pi, MareConfigService mareConfig,
        CharaDataManager charaDataManager, MareMediator mareMediator)
    {
        _logger = logger;
        _pi = pi;
        _mareConfig = mareConfig;
        _charaDataManager = charaDataManager;
        Mediator = mareMediator;

        Mediator.Subscribe<GameObjectHandlerCreatedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.Add(msg.GameObjectHandler);
        });
        Mediator.Subscribe<GameObjectHandlerDestroyedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.Remove(msg.GameObjectHandler);
        });

        _marePluginEnabled = PluginWatcherService.GetInitialPluginState(pi, "MareSynchronos")?.IsLoaded ?? false;
        Mediator.SubscribeKeyed<PluginChangeMessage>(this, "MareSynchronos", p => {
            _marePluginEnabled = p.IsLoaded;
            HandleMareImpersonation(automatic: true);
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting IpcProvider Service");
        _loadFileProvider = _pi.GetIpcProvider<string, IGameObject, bool>("UmbraSyncSync.LoadMcdf");
        _loadFileProvider.RegisterFunc(LoadMcdf);
        _loadFileAsyncProvider = _pi.GetIpcProvider<string, IGameObject, Task<bool>>("UmbraSyncSync.LoadMcdfAsync");
        _loadFileAsyncProvider.RegisterFunc(LoadMcdfAsync);
        _handledGameAddresses = _pi.GetIpcProvider<List<nint>>("UmbraSyncSync.GetHandledAddresses");
        _handledGameAddresses.RegisterFunc(GetHandledAddresses);

        _loadFileProviderMare = _pi.GetIpcProvider<string, IGameObject, bool>("MareSynchronos.LoadMcdf");
        _loadFileAsyncProviderMare = _pi.GetIpcProvider<string, IGameObject, Task<bool>>("MareSynchronos.LoadMcdfAsync");
        _handledGameAddressesMare = _pi.GetIpcProvider<List<nint>>("MareSynchronos.GetHandledAddresses");
        HandleMareImpersonation(automatic: true);

        _logger.LogInformation("Started IpcProviderService");
        return Task.CompletedTask;
    }

    public void HandleMareImpersonation(bool automatic = false)
    {
        if (_marePluginEnabled)
        {
            if (_impersonating)
            {
                _loadFileProviderMare?.UnregisterFunc();
                _loadFileAsyncProviderMare?.UnregisterFunc();
                _handledGameAddressesMare?.UnregisterFunc();
                _impersonating = false;
                _unregisterTime = DateTime.UtcNow;
                _logger.LogDebug("Unregistered MareSynchronos API");
            }
        }
        else
        {
            if (_mareConfig.Current.MareAPI)
            {
                var cancelToken = _registerDelayCts.Token;
                Task.Run(async () =>
                {
                    // Wait before registering to reduce the chance of a race condition
                    if (automatic)
                        await Task.Delay(5000);

                    if (cancelToken.IsCancellationRequested)
                        return;

                    if (_marePluginEnabled)
                    {
                        _logger.LogDebug("Not registering MareSynchronos API: Mare plugin is loaded");
                        return;
                    }

                    _loadFileProviderMare?.RegisterFunc(LoadMcdf);
                    _loadFileAsyncProviderMare?.RegisterFunc(LoadMcdfAsync);
                    _handledGameAddressesMare?.RegisterFunc(GetHandledAddresses);
                    _impersonating = true;
                    _logger.LogDebug("Registered MareSynchronos API");
                }, cancelToken);
            }
            else
            {
                _registerDelayCts = _registerDelayCts.CancelRecreate();
                if (_impersonating)
                {
                    _loadFileProviderMare?.UnregisterFunc();
                    _loadFileAsyncProviderMare?.UnregisterFunc();
                    _handledGameAddressesMare?.UnregisterFunc();
                    _impersonating = false;
                    _unregisterTime = DateTime.UtcNow;
                    _logger.LogDebug("Unregistered MareSynchronos API");
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping IpcProvider Service");
        _loadFileProvider?.UnregisterFunc();
        _loadFileAsyncProvider?.UnregisterFunc();
        _handledGameAddresses?.UnregisterFunc();

        _registerDelayCts.Cancel();
        if (_impersonating)
        {
            _loadFileProviderMare?.UnregisterFunc();
            _loadFileAsyncProviderMare?.UnregisterFunc();
            _handledGameAddressesMare?.UnregisterFunc();
        }

        Mediator.UnsubscribeAll(this);
        return Task.CompletedTask;
    }

    private async Task<bool> LoadMcdfAsync(string path, IGameObject target)
    {
        await ApplyFileAsync(path, target).ConfigureAwait(false);

        return true;
    }

    private bool LoadMcdf(string path, IGameObject target)
    {
        _ = Task.Run(async () => await ApplyFileAsync(path, target).ConfigureAwait(false)).ConfigureAwait(false);

        return true;
    }

    private async Task ApplyFileAsync(string path, IGameObject target)
    {
        _charaDataManager.LoadMcdf(path);
        await (_charaDataManager.LoadedMcdfHeader ?? Task.CompletedTask).ConfigureAwait(false);
        _charaDataManager.McdfApplyToTarget(target.Name.TextValue);
    }

    private List<nint> GetHandledAddresses()
    {
        if (!_impersonating)
        {
            if ((DateTime.UtcNow - _unregisterTime).TotalSeconds >= 1.0)
            {
                _logger.LogWarning("GetHandledAddresses called when it should not be registered");
                _handledGameAddressesMare?.UnregisterFunc();
            }
            return [];
        }

        return _activeGameObjectHandlers.Where(g => g.Address != nint.Zero).Select(g => g.Address).Distinct().ToList();
    }
}
