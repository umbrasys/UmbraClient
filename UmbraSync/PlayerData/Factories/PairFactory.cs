using Microsoft.Extensions.Logging;
using UmbraSync.API.Data;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.ServerConfiguration;

namespace UmbraSync.PlayerData.Factories;

public class PairFactory(ILoggerFactory loggerFactory, PairHandlerFactory cachedPlayerFactory,
    MareMediator mareMediator, MareConfigService mareConfig, ServerConfigurationManager serverConfigurationManager,
    PairStateCache pairStateCache)
{
    private readonly PairHandlerFactory _cachedPlayerFactory = cachedPlayerFactory;
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly MareMediator _mareMediator = mareMediator;
    private readonly MareConfigService _mareConfig = mareConfig;
    private readonly ServerConfigurationManager _serverConfigurationManager = serverConfigurationManager;
    private readonly PairStateCache _pairStateCache = pairStateCache;

    public Pair Create(UserData userData)
    {
        return new Pair(_loggerFactory.CreateLogger<Pair>(), userData, _cachedPlayerFactory, _mareMediator, _mareConfig, _serverConfigurationManager, _pairStateCache);
    }
}