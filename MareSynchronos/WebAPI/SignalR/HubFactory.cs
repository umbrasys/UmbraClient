using MareSynchronos.API.SignalR;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI.SignalR.Utils;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.WebAPI.SignalR;

public class HubFactory : MediatorSubscriberBase
{
    private readonly ILoggerProvider _loggingProvider;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly RemoteConfigurationService _remoteConfig;
    private readonly TokenProvider _tokenProvider;
    private HubConnection? _instance;
    private string _cachedConfigFor = string.Empty;
    private HubConnectionConfig? _cachedConfig;
    private bool _isDisposed = false;

    public HubFactory(ILogger<HubFactory> logger, MareMediator mediator,
        ServerConfigurationManager serverConfigurationManager, RemoteConfigurationService remoteConfig,
        TokenProvider tokenProvider, ILoggerProvider pluginLog) : base(logger, mediator)
    {
        _serverConfigurationManager = serverConfigurationManager;
        _remoteConfig = remoteConfig;
        _tokenProvider = tokenProvider;
        _loggingProvider = pluginLog;
    }

    public async Task DisposeHubAsync()
    {
        if (_instance == null || _isDisposed) return;

        Logger.LogDebug("Disposing current HubConnection");

        _isDisposed = true;

        _instance.Closed -= HubOnClosed;
        _instance.Reconnecting -= HubOnReconnecting;
        _instance.Reconnected -= HubOnReconnected;

        await _instance.StopAsync().ConfigureAwait(false);
        await _instance.DisposeAsync().ConfigureAwait(false);

        _instance = null;

        Logger.LogDebug("Current HubConnection disposed");
    }

    public async Task<HubConnection> GetOrCreate(CancellationToken ct)
    {
        if (!_isDisposed && _instance != null) return _instance;

        _cachedConfig = await ResolveHubConfig().ConfigureAwait(false);
        _cachedConfigFor = _serverConfigurationManager.CurrentApiUrl;

        return BuildHubConnection(_cachedConfig, ct);
    }

    private async Task<HubConnectionConfig> ResolveHubConfig()
    {
        if (_cachedConfig != null && _serverConfigurationManager.CurrentApiUrl.Equals(_cachedConfigFor, StringComparison.Ordinal))
        {
            return _cachedConfig;
        }
        var defaultConfig = new HubConnectionConfig
        {
            HubUrl = _serverConfigurationManager.CurrentApiUrl.TrimEnd('/') + IMareHub.Path,
            Transports = []
        };

        if (_serverConfigurationManager.CurrentApiUrl.Equals(ApiController.UmbraServiceUri, StringComparison.Ordinal))
        {
            var mainServerConfig = await _remoteConfig.GetConfigAsync<HubConnectionConfig>("mainServer").ConfigureAwait(false) ?? new();
            if (string.IsNullOrEmpty(mainServerConfig.ApiUrl))
                mainServerConfig.ApiUrl = ApiController.UmbraServiceApiUri;
            if (string.IsNullOrEmpty(mainServerConfig.HubUrl))
                mainServerConfig.HubUrl = ApiController.UmbraServiceHubUri;

            mainServerConfig.Transports ??= defaultConfig.Transports ?? [];
            return mainServerConfig;
        }
        return defaultConfig;
    }

    private HubConnection BuildHubConnection(HubConnectionConfig hubConfig, CancellationToken ct)
    {
        Logger.LogDebug("Building new HubConnection");

        _instance = new HubConnectionBuilder()
            .WithUrl(hubConfig.HubUrl, options =>
            {
                var transports =  hubConfig.TransportType;
                options.AccessTokenProvider = () => _tokenProvider.GetOrUpdateToken(ct);
                options.SkipNegotiation = hubConfig.SkipNegotiation && (transports == HttpTransportType.WebSockets);
                options.Transports = transports;
            })
            .AddMessagePackProtocol(opt =>
            {
                var resolver = CompositeResolver.Create(StandardResolverAllowPrivate.Instance,
                    BuiltinResolver.Instance,
                    AttributeFormatterResolver.Instance,
                    DynamicEnumAsStringResolver.Instance,
                    DynamicGenericResolver.Instance,
                    DynamicUnionResolver.Instance,
                    DynamicObjectResolver.Instance,
                    PrimitiveObjectResolver.Instance,
                    StandardResolver.Instance);

                opt.SerializerOptions =
                    MessagePackSerializerOptions.Standard
                        .WithCompression(MessagePackCompression.Lz4Block)
                        .WithResolver(resolver);
            })
            .WithAutomaticReconnect(new ForeverRetryPolicy(Mediator))
            .ConfigureLogging(a =>
            {
                a.ClearProviders().AddProvider(_loggingProvider);
                a.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        _instance.Closed += HubOnClosed;
        _instance.Reconnecting += HubOnReconnecting;
        _instance.Reconnected += HubOnReconnected;

        _isDisposed = false;

        return _instance;
    }

    private Task HubOnClosed(Exception? arg)
    {
        Mediator.Publish(new HubClosedMessage(arg));
        return Task.CompletedTask;
    }

    private Task HubOnReconnected(string? arg)
    {
        Mediator.Publish(new HubReconnectedMessage(arg));
        return Task.CompletedTask;
    }

    private Task HubOnReconnecting(Exception? arg)
    {
        Mediator.Publish(new HubReconnectingMessage(arg));
        return Task.CompletedTask;
    }
}