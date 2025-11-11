using UmbraSync.FileCache;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace UmbraSync.PlayerData.Factories;

public class PairAnalyzerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mareMediator;
    private readonly FileCacheManager _fileCacheManager;
    private readonly XivDataAnalyzer _modelAnalyzer;

    public PairAnalyzerFactory(ILoggerFactory loggerFactory, MareMediator mareMediator,
        FileCacheManager fileCacheManager, XivDataAnalyzer modelAnalyzer)
    {
        _loggerFactory = loggerFactory;
        _fileCacheManager = fileCacheManager;
        _mareMediator = mareMediator;
        _modelAnalyzer = modelAnalyzer;
    }

    public PairAnalyzer Create(Pair pair)
    {
        return new PairAnalyzer(_loggerFactory.CreateLogger<PairAnalyzer>(), pair, _mareMediator,
            _fileCacheManager, _modelAnalyzer);
    }
}