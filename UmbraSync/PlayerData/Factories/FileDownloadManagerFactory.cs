using Microsoft.Extensions.Logging;
using UmbraSync.FileCache;
using UmbraSync.MareConfiguration;
using UmbraSync.Services.Mediator;
using UmbraSync.WebAPI.Files;

namespace UmbraSync.PlayerData.Factories;

public class FileDownloadManagerFactory
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileCompactor _fileCompactor;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly MareConfigService _mareConfigService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mareMediator;

    public FileDownloadManagerFactory(ILoggerFactory loggerFactory, MareMediator mareMediator, FileTransferOrchestrator fileTransferOrchestrator,
        FileCacheManager fileCacheManager, FileCompactor fileCompactor, MareConfigService mareConfigService)
    {
        _loggerFactory = loggerFactory;
        _mareMediator = mareMediator;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCacheManager = fileCacheManager;
        _fileCompactor = fileCompactor;
        _mareConfigService = mareConfigService;
    }

    public FileDownloadManager Create()
    {
        return new FileDownloadManager(_loggerFactory.CreateLogger<FileDownloadManager>(), _mareMediator, _fileTransferOrchestrator, _fileCacheManager, _fileCompactor, _mareConfigService);
    }
}