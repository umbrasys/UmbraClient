using MareSynchronos.FileCache;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Factories;

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