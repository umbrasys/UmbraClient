using Dalamud.Plugin;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace MareSynchronos.Services;

public sealed class NoSnapService : IHostedService, IMediatorSubscriber
{
    private record NoSnapConfig
    {
        [JsonPropertyName("listOfPlugins")]
        public string[]? ListOfPlugins { get; set; }
    }

    private readonly ILogger<NoSnapService> _logger;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Dictionary<string, bool> _listOfPlugins = new(StringComparer.Ordinal)
    {
        ["Snapper"] = false,
        ["Snappy"] = false,
        ["Meddle.Plugin"] = false,
    };
    private static readonly HashSet<int> _gposers = new();
    private static readonly HashSet<string> _gposersNamed = new(StringComparer.Ordinal);
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly IpcManager _ipcManager;
    private readonly RemoteConfigurationService _remoteConfig;

    public static bool AnyLoaded { get; private set; } = false;
    public static string ActivePlugins { get; private set; } = string.Empty;

    public MareMediator Mediator { get; init; }

    public NoSnapService(ILogger<NoSnapService> logger, IDalamudPluginInterface pluginInterface, MareMediator mediator,
        IHostApplicationLifetime hostApplicationLifetime, DalamudUtilService dalamudUtilService, IpcManager ipcManager,
        RemoteConfigurationService remoteConfig)
    {
        _logger = logger;
        _pluginInterface = pluginInterface;
        Mediator = mediator;
        _hostApplicationLifetime = hostApplicationLifetime;
        _dalamudUtilService = dalamudUtilService;
        _ipcManager = ipcManager;
        _remoteConfig = remoteConfig;

        Mediator.Subscribe<GposeEndMessage>(this, msg => ClearGposeList());
        Mediator.Subscribe<CutsceneEndMessage>(this, msg => ClearGposeList());
    }

    public void AddGposer(int objectIndex)
    {
        if (AnyLoaded || _hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _logger.LogTrace("Immediately reverting object index {id}", objectIndex);
            RevertAndRedraw(objectIndex);
            return;
        }

        _logger.LogTrace("Registering gposer object index {id}", objectIndex);
        lock (_gposers)
            _gposers.Add(objectIndex);
    }

    public void RemoveGposer(int objectIndex)
    {
        _logger.LogTrace("Un-registering gposer object index {id}", objectIndex);
        lock (_gposers)
            _gposers.Remove(objectIndex);
    }

    public void AddGposerNamed(string name)
    {
        if (AnyLoaded || _hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _logger.LogTrace("Immediately reverting {name}", name);
            RevertAndRedraw(name);
            return;
        }

        _logger.LogTrace("Registering gposer {name}", name);
        lock (_gposers)
            _gposersNamed.Add(name);
    }

    private void ClearGposeList()
    {
        if (_gposers.Count > 0 || _gposersNamed.Count > 0)
            _logger.LogTrace("Clearing gposer list");
        lock (_gposers)
            _gposers.Clear();
        lock (_gposersNamed)
            _gposersNamed.Clear();
    }

    private void RevertAndRedraw(int objIndex, Guid applicationId = default)
    {
        if (applicationId == default)
            applicationId = Guid.NewGuid();

        try
        {
            _ipcManager.Glamourer.RevertNow(_logger, applicationId, objIndex);
            _ipcManager.Penumbra.RedrawNow(_logger, applicationId, objIndex);
        }
        catch { }
    }

    private void RevertAndRedraw(string name, Guid applicationId = default)
    {
        if (applicationId == default)
            applicationId = Guid.NewGuid();

        try
        {
            _ipcManager.Glamourer.RevertByNameNow(_logger, applicationId, name);
            var addr = _dalamudUtilService.GetPlayerCharacterFromCachedTableByName(name);
            if (addr != 0)
            {
                var obj = _dalamudUtilService.CreateGameObject(addr);
                if (obj != null)
                    _ipcManager.Penumbra.RedrawNow(_logger, applicationId, obj.ObjectIndex);
            }
        }
        catch { }
    }

    private void RevertGposers()
    {
        List<int>? gposersList = null;
        List<string>? gposersList2 = null;

        lock (_gposers)
        {
            if (_gposers.Count > 0)
            {
                gposersList = _gposers.ToList();
                _gposers.Clear();
            }
        }

        lock (_gposersNamed)
        {
            if (_gposersNamed.Count > 0)
            {
                gposersList2 = _gposersNamed.ToList();
                _gposersNamed.Clear();
            }
        }

        if (gposersList == null && gposersList2 == null)
            return;

        _logger.LogInformation("Reverting gposers");

        _dalamudUtilService.RunOnFrameworkThread(() =>
        {
            Guid applicationId = Guid.NewGuid();

            foreach (var gposer in gposersList ?? [])
                RevertAndRedraw(gposer, applicationId);

            foreach (var gposerName in gposersList2 ?? [])
                RevertAndRedraw(gposerName, applicationId);
        }).GetAwaiter().GetResult();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = await _remoteConfig.GetConfigAsync<NoSnapConfig>("noSnap").ConfigureAwait(false) ?? new();

        if (config.ListOfPlugins != null)
        {
            _listOfPlugins.Clear();
            foreach (var pluginName in config.ListOfPlugins)
                _listOfPlugins.TryAdd(pluginName, value: false);
        }

        foreach (var pluginName in _listOfPlugins.Keys)
        {
            _listOfPlugins[pluginName] = PluginWatcherService.GetInitialPluginState(_pluginInterface, pluginName)?.IsLoaded ?? false;
            Mediator.SubscribeKeyed<PluginChangeMessage>(this, pluginName, (msg) =>
            {
                _listOfPlugins[pluginName] = msg.IsLoaded;
                _logger.LogDebug("{pluginName} isLoaded = {isLoaded}", pluginName, msg.IsLoaded);
                Update();
            });
        }

        Update();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        RevertGposers();
        return Task.CompletedTask;
    }

    private void Update()
    {
        bool anyLoadedNow = _listOfPlugins.Values.Any(p => p);

        if (AnyLoaded != anyLoadedNow)
        {
            AnyLoaded = anyLoadedNow;
            Mediator.Publish(new RecalculatePerformanceMessage(null));

            if (AnyLoaded)
            {
                RevertGposers();
                var pluginList = string.Join(", ", _listOfPlugins.Where(p => p.Value).Select(p => p.Key));
                Mediator.Publish(new NotificationMessage("Incompatible plugin loaded", $"Synced player appearances will not apply until incompatible plugins are disabled: {pluginList}.",
                    NotificationType.Error));
                ActivePlugins = pluginList;
            }
            else
            {
                ActivePlugins = string.Empty;
            }
        }
    }
}