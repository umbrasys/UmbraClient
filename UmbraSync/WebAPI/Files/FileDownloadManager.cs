using Dalamud.Utility;
using K4os.Compression.LZ4.Streams;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using UmbraSync.API.Data;
using UmbraSync.API.Dto.Files;
using UmbraSync.API.Routes;
using UmbraSync.FileCache;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Handlers;
using UmbraSync.Services.Mediator;
using UmbraSync.Utils;
using UmbraSync.WebAPI.Files.Models;

namespace UmbraSync.WebAPI.Files;

public partial class FileDownloadManager : DisposableMediatorSubscriberBase
{
    private readonly Dictionary<string, FileDownloadStatus> _downloadStatus;
    private readonly FileCompactor _fileCompactor;
    private readonly FileCacheManager _fileDbManager;
    private readonly MareConfigService _mareConfigService;
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly FileDownloadDeduplicator _deduplicator;
    private readonly List<ThrottledStream> _activeDownloadStreams;
    private readonly Lock _queueLock = new();
    private SemaphoreSlim? _downloadQueueSemaphore;
    private int _downloadQueueCapacity = -1;

    // Circuit breaker for direct CDN downloads - disable after consecutive failures
    private const int MaxConsecutiveDirectDownloadFailures = 3;
    private int _consecutiveDirectDownloadFailures;
    private bool _disableDirectDownloads;

    public FileDownloadManager(ILogger<FileDownloadManager> logger, MareMediator mediator,
        FileTransferOrchestrator orchestrator,
        FileCacheManager fileCacheManager, FileCompactor fileCompactor, MareConfigService mareConfigService,
        FileDownloadDeduplicator deduplicator) : base(logger, mediator)
    {
        _downloadStatus = new Dictionary<string, FileDownloadStatus>(StringComparer.Ordinal);
        _orchestrator = orchestrator;
        _fileDbManager = fileCacheManager;
        _fileCompactor = fileCompactor;
        _mareConfigService = mareConfigService;
        _deduplicator = deduplicator;
        _activeDownloadStreams = [];

        Mediator.Subscribe<DownloadLimitChangedMessage>(this, (msg) =>
        {
            if (!_activeDownloadStreams.Any()) return;
            var newLimit = _orchestrator.DownloadLimitPerSlot();
            Logger.LogTrace("Setting new Download Speed Limit to {newLimit}", newLimit);
            foreach (var stream in _activeDownloadStreams)
            {
                stream.BandwidthLimit = newLimit;
            }
        });

        // Reset circuit breaker on reconnection
        Mediator.Subscribe<ConnectedMessage>(this, _ => ResetDirectDownloadCircuitBreaker());
    }

    private void ResetDirectDownloadCircuitBreaker()
    {
        if (_disableDirectDownloads)
        {
            Logger.LogInformation("Resetting direct CDN download circuit breaker");
        }
        _consecutiveDirectDownloadFailures = 0;
        _disableDirectDownloads = false;
    }

    public List<DownloadFileTransfer> CurrentDownloads { get; private set; } = [];

    public List<FileTransfer> ForbiddenTransfers => _orchestrator.ForbiddenTransfers;

    public bool IsDownloading => CurrentDownloads.Any();

    public void ClearDownload()
    {
        CurrentDownloads.Clear();
        _downloadStatus.Clear();
    }

    public async Task DownloadFiles(GameObjectHandler gameObject, List<FileReplacementData> fileReplacementDto, CancellationToken ct)
    {
        SemaphoreSlim? queueSemaphore = null;
        if (_mareConfigService.Current.EnableDownloadQueue)
        {
            queueSemaphore = GetQueueSemaphore();
            Logger.LogTrace("Queueing download for {name}. Currently queued: {queued}", gameObject.Name, queueSemaphore.CurrentCount);
            await queueSemaphore.WaitAsync(ct).ConfigureAwait(false);
        }

        Mediator.Publish(new HaltScanMessage(nameof(DownloadFiles)));
        try
        {
            await DownloadFilesInternal(gameObject, fileReplacementDto, ct).ConfigureAwait(false);
        }
        catch
        {
            ClearDownload();
        }
        finally
        {
            if (queueSemaphore != null)
            {
                queueSemaphore.Release();
            }

            Mediator.Publish(new DownloadFinishedMessage(gameObject));
            Mediator.Publish(new ResumeScanMessage(nameof(DownloadFiles)));
        }
    }

    protected override void Dispose(bool disposing)
    {
        ClearDownload();
        foreach (var stream in _activeDownloadStreams.ToList())
        {
            try
            {
                stream.Dispose();
            }
            catch
            {
                // do nothing
                //
            }
        }
        base.Dispose(disposing);
    }

    private static byte ConvertReadByte(int byteOrEof)
    {
        if (byteOrEof == -1)
        {
            throw new EndOfStreamException();
        }

        return (byte)byteOrEof;
    }

    private static (string fileHash, long fileLengthBytes) ReadBlockFileHeader(FileStream fileBlockStream)
    {
        List<char> hashName = [];
        List<char> fileLength = [];
        var separator = (char)ConvertReadByte(fileBlockStream.ReadByte());
        if (separator != '#') throw new InvalidDataException("Data is invalid, first char is not #");

        bool readHash = false;
        while (true)
        {
            int readByte = fileBlockStream.ReadByte();
            if (readByte == -1)
                throw new EndOfStreamException();

            var readChar = (char)ConvertReadByte(readByte);
            if (readChar == ':')
            {
                readHash = true;
                continue;
            }
            if (readChar == '#') break;
            if (!readHash) hashName.Add(readChar);
            else fileLength.Add(readChar);
        }
        if (fileLength.Count == 0)
            fileLength.Add('0');
        return (string.Join("", hashName), long.Parse(string.Join("", fileLength), CultureInfo.InvariantCulture));
    }

    private SemaphoreSlim GetQueueSemaphore()
    {
        var desiredCapacity = Math.Clamp(_mareConfigService.Current.ParallelDownloads, 1, 50);

        using (_queueLock.EnterScope())
        {
            if (_downloadQueueSemaphore == null || _downloadQueueCapacity != desiredCapacity)
            {
                _downloadQueueSemaphore = new SemaphoreSlim(desiredCapacity, desiredCapacity);
                _downloadQueueCapacity = desiredCapacity;
            }

            return _downloadQueueSemaphore;
        }
    }

    private async Task DownloadAndMungeFileHttpClient(string downloadGroup, Guid requestId, List<DownloadFileTransfer> fileTransfer, string tempPath, IProgress<long> progress, CancellationToken ct)
    {
        Logger.LogDebug("GUID {requestId} on server {uri} for files {files}", requestId, fileTransfer[0].DownloadUri, string.Join(", ", fileTransfer.Select(c => c.Hash).ToList()));

        await WaitForDownloadReady(fileTransfer, requestId, ct).ConfigureAwait(false);

        _downloadStatus[downloadGroup].DownloadStatus = DownloadStatus.Downloading;

        HttpResponseMessage response = null!;
        var requestUrl = MareFiles.CacheGetFullPath(fileTransfer[0].DownloadUri, requestId);

        Logger.LogDebug("Downloading {requestUrl} for request {id}", requestUrl, requestId);
        try
        {
            response = await _orchestrator.SendRequestAsync(HttpMethod.Get, requestUrl, ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Error during download of {requestUrl}, HttpStatusCode: {code}", requestUrl, ex.StatusCode);
            if (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            {
                throw new InvalidDataException($"Http error {ex.StatusCode} (cancelled: {ct.IsCancellationRequested}): {requestUrl}", ex);
            }
        }

        ThrottledStream? stream = null;
        try
        {
            var fileStream = File.Create(tempPath);
            await using (fileStream.ConfigureAwait(false))
            {
                var bufferSize = response.Content.Headers.ContentLength > 1024 * 1024 ? 65536 : 8196;
                var buffer = new byte[bufferSize];

                var bytesRead = 0;
                var limit = _orchestrator.DownloadLimitPerSlot();
                Logger.LogTrace("Starting Download of {id} with a speed limit of {limit} to {tempPath}", requestId, limit, tempPath);
                stream = new ThrottledStream(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), limit);
                _activeDownloadStreams.Add(stream);
                while ((bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);

                    progress.Report(bytesRead);
                }

                Logger.LogDebug("{requestUrl} downloaded to {tempPath}", requestUrl, tempPath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            try
            {
                if (!tempPath.IsNullOrEmpty())
                    File.Delete(tempPath);
            }
            catch
            {
                // ignore if file deletion fails
            }
            throw;
        }
        finally
        {
            if (stream != null)
            {
                _activeDownloadStreams.Remove(stream);
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

#pragma warning disable S1144 // Unused private method kept for future use
    private async Task<bool> DownloadFileDirectAsync(DownloadFileTransfer file, string destPath, IProgress<long> progress, CancellationToken ct)
#pragma warning restore S1144
    {
        if (!file.HasDirectDownload || file.DirectDownloadUri == null)
            return false;

        var url = file.DirectDownloadUri.ToString();
        Logger.LogDebug("Direct CDN download: {hash} from {url}", file.Hash, url);

        try
        {
            var response = await _orchestrator.SendRequestAsync(HttpMethod.Get, new Uri(url), ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.LogDebug("Direct CDN download 404 for {hash}, will fallback", file.Hash);
                return false;
            }

            response.EnsureSuccessStatusCode();

            ThrottledStream? stream = null;
            try
            {
                using var fileStream = File.Create(destPath);
                var bufferSize = response.Content.Headers.ContentLength > 1024 * 1024 ? 65536 : 8196;
                var buffer = new byte[bufferSize];

                var limit = _orchestrator.DownloadLimitPerSlot();
                stream = new ThrottledStream(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), limit);
                _activeDownloadStreams.Add(stream);

                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                    progress.Report(bytesRead);
                }

                Logger.LogDebug("Direct CDN download complete: {hash}", file.Hash);
                return true;
            }
            finally
            {
                if (stream != null)
                {
                    _activeDownloadStreams.Remove(stream);
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            Logger.LogDebug("Direct CDN download 404 for {hash}, will fallback", file.Hash);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Direct CDN download failed for {hash}, will fallback", file.Hash);
            if (File.Exists(destPath)) File.Delete(destPath);
            return false;
        }
    }

    private async Task<bool> DownloadAndDecompressDirectAsync(DownloadFileTransfer file, string tmpPath, string destPath, IProgress<long> progress, CancellationToken ct)
    {
        if (!file.HasDirectDownload || file.DirectDownloadUri == null)
            return false;

        var url = file.DirectDownloadUri.ToString();
        Logger.LogDebug("Direct CDN download+decompress: {hash} from {url}", file.Hash, url);

        // Clean up any existing .cdntmp file from a previous failed attempt
        if (File.Exists(tmpPath))
        {
            try
            {
                File.Delete(tmpPath);
                Logger.LogDebug("Deleted existing .cdntmp file before download: {path}", tmpPath);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Cannot delete existing .cdntmp file {path}, download may fail", tmpPath);
            }
        }

        try
        {
            var response = await _orchestrator.SendRequestAsync(HttpMethod.Get, new Uri(url), ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.LogDebug("Direct CDN 404 for {hash}, will fallback", file.Hash);
                return false;
            }

            response.EnsureSuccessStatusCode();

            ThrottledStream? throttledStream = null;
            try
            {
                var limit = _orchestrator.DownloadLimitPerSlot();
                throttledStream = new ThrottledStream(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), limit);
                _activeDownloadStreams.Add(throttledStream);

                byte[] calculatedHashBytes;
                using (var hashingStream = new HashingStream(
                    new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None),
                    SHA1.Create()))
                {
                    using var lz4Decoder = LZ4Stream.Decode(throttledStream, leaveOpen: true);

                    var buffer = new byte[65536];
                    int bytesRead;
                    long totalBytesWritten = 0;
                    while ((bytesRead = await lz4Decoder.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        await hashingStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                        progress.Report(bytesRead);
                        totalBytesWritten += bytesRead;
                    }

                    Logger.LogDebug("CDN download finished for {hash}, wrote {bytes} bytes, computing hash", file.Hash, totalBytesWritten);
                    calculatedHashBytes = hashingStream.Finish();
                }
                // FileStream is now closed, safe to rename

                var calculatedHash = BitConverter.ToString(calculatedHashBytes).Replace("-", "", StringComparison.Ordinal);
                if (!string.Equals(calculatedHash, file.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogWarning("CDN hash mismatch for {hash}: got {calculated}", file.Hash, calculatedHash);
                    if (File.Exists(tmpPath)) File.Delete(tmpPath);
                    return false;
                }

                Logger.LogDebug("CDN hash verified for {hash}, renaming to final destination", file.Hash);
                _fileCompactor.RenameAndCompact(destPath, tmpPath);

                // Verify the destination file exists after RenameAndCompact
                if (!File.Exists(destPath))
                {
                    Logger.LogWarning("RenameAndCompact did not create destination file for {hash}, cleaning up", file.Hash);
                    if (File.Exists(tmpPath)) File.Delete(tmpPath);
                    return false;
                }

                Logger.LogDebug("Direct CDN download+decompress complete: {hash}", file.Hash);
                return true;
            }
            finally
            {
                if (throttledStream != null)
                {
                    _activeDownloadStreams.Remove(throttledStream);
                    await throttledStream.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            Logger.LogDebug("Direct CDN 404 for {hash}, will fallback", file.Hash);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Direct CDN download failed for {hash}, will fallback", file.Hash);
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
            return false;
        }
    }

    public async Task<List<DownloadFileTransfer>> InitiateDownloadList(GameObjectHandler gameObjectHandler, List<FileReplacementData> fileReplacement, CancellationToken ct)
    {
        Logger.LogDebug("Download start: {id}", gameObjectHandler.Name);

        List<DownloadFileDto> downloadFileInfoFromService =
        [
            .. await FilesGetSizes(fileReplacement.Select(f => f.Hash).Distinct(StringComparer.Ordinal).ToList(), ct).ConfigureAwait(false),
        ];

        Logger.LogDebug("Files with size 0 or less: {files}", string.Join(", ", downloadFileInfoFromService.Where(f => f.Size <= 0).Select(f => f.Hash)));

        foreach (var dto in downloadFileInfoFromService.Where(c => c.IsForbidden))
        {
            if (!_orchestrator.ForbiddenTransfers.Exists(f => string.Equals(f.Hash, dto.Hash, StringComparison.Ordinal)))
            {
                _orchestrator.ForbiddenTransfers.Add(new DownloadFileTransfer(dto));
            }
        }

        CurrentDownloads = downloadFileInfoFromService.Distinct().Select(d => new DownloadFileTransfer(d))
            .Where(d => d.CanBeTransferred).ToList();

        return CurrentDownloads;
    }

    private async Task DownloadFilesInternal(GameObjectHandler gameObjectHandler, List<FileReplacementData> fileReplacement, CancellationToken ct)
    {
        var directDownloads = CurrentDownloads.Where(f => f.HasDirectDownload).ToList();
        var fallbackFiles = new List<DownloadFileTransfer>();

        // Track hashes that go to fallback - they should NOT be completed until fallback finishes
        // This is defined at method level so it can be cleaned up at the end if fallback fails
        var pendingFallbackHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Lock pendingFallbackLock = new();

        // Circuit breaker: skip direct downloads if too many consecutive failures
        if (_disableDirectDownloads && directDownloads.Count > 0)
        {
            Logger.LogWarning("Direct CDN downloads disabled due to {failures} consecutive failures, using fallback for all {count} files",
                _consecutiveDirectDownloadFailures, directDownloads.Count);
            fallbackFiles.AddRange(directDownloads);
            directDownloads.Clear();
        }

        if (directDownloads.Count > 0)
        {
            Logger.LogInformation("Attempting direct CDN download for {count} files", directDownloads.Count);

            const string cdnKey = "cdn-direct";
            _downloadStatus[cdnKey] = new FileDownloadStatus
            {
                DownloadStatus = DownloadStatus.Downloading,
                TotalBytes = directDownloads.Sum(f => f.Total),
                TotalFiles = directDownloads.Count,
                TransferredBytes = 0,
                TransferredFiles = 0
            };

            Mediator.Publish(new DownloadStartedMessage(gameObjectHandler, _downloadStatus));

            var parallelism = Math.Clamp(_mareConfigService.Current.ParallelDownloads, 1, 10);

            await Parallel.ForEachAsync(directDownloads, new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelism,
                CancellationToken = ct
            }, async (file, token) =>
            {
                var claim = _deduplicator.Claim(file.Hash);

                if (!claim.IsOwner)
                {
                    var ownerSuccess = await claim.Completion.ConfigureAwait(false);
                    if (ownerSuccess)
                    {
                        if (_downloadStatus.TryGetValue(cdnKey, out var status))
                            status.TransferredFiles++;
                    }
                    else
                    {
                        lock (fallbackFiles) { fallbackFiles.Add(file); }
                    }
                    return;
                }

                var fileData = fileReplacement.FirstOrDefault(f => string.Equals(f.Hash, file.Hash, StringComparison.OrdinalIgnoreCase));
                var fileExtension = fileData?.GamePaths[0].Split(".")[^1] ?? "tmp";
                var filePath = _fileDbManager.GetCacheFilePath(file.Hash, fileExtension);
                var tmpPath = filePath + ".cdntmp";

                Progress<long> progress = new(bytes =>
                {
                    if (_downloadStatus.TryGetValue(cdnKey, out var status))
                        status.TransferredBytes += bytes;
                });

                var success = false;
                var goesToFallback = false;
                try
                {
                    success = await DownloadAndDecompressDirectAsync(file, tmpPath, filePath, progress, token).ConfigureAwait(false);

                    if (success)
                    {
                        // Reset circuit breaker on success
                        Interlocked.Exchange(ref _consecutiveDirectDownloadFailures, 0);
                        _disableDirectDownloads = false;

                        PersistFileToStorage(file.Hash, filePath, file.Total);
                        if (_downloadStatus.TryGetValue(cdnKey, out var status))
                            status.TransferredFiles++;
                    }
                    else
                    {
                        // Increment failure counter and check circuit breaker threshold
                        var failures = Interlocked.Increment(ref _consecutiveDirectDownloadFailures);
                        if (failures >= MaxConsecutiveDirectDownloadFailures)
                        {
                            _disableDirectDownloads = true;
                            Logger.LogWarning("Direct CDN downloads disabled after {count} consecutive failures", failures);
                        }

                        // Mark as going to fallback - DO NOT complete the deduplicator yet
                        goesToFallback = true;
                        using (pendingFallbackLock.EnterScope()) { pendingFallbackHashes.Add(file.Hash); }
                        lock (fallbackFiles) { fallbackFiles.Add(file); }
                    }
                }
                finally
                {
                    // Only complete if NOT going to fallback - fallback will complete it later
                    if (!goesToFallback)
                    {
                        _deduplicator.Complete(file.Hash, success);
                    }
                }
            }).ConfigureAwait(false);

            _downloadStatus.Remove(cdnKey, out _);

            if (fallbackFiles.Count > 0)
                Logger.LogInformation("CDN fallback needed for {count} files", fallbackFiles.Count);
        }

        var queueFiles = CurrentDownloads.Where(f => !f.HasDirectDownload).Concat(fallbackFiles).ToList();
        if (queueFiles.Count == 0)
        {
            ClearDownload();
            return;
        }

        var downloadGroups = queueFiles
            .GroupBy(f => f.DownloadUri.Host + ":" + f.DownloadUri.Port, StringComparer.Ordinal)
            .ToList();

        foreach (var downloadGroup in downloadGroups)
        {
            _downloadStatus[downloadGroup.Key] = new FileDownloadStatus()
            {
                DownloadStatus = DownloadStatus.Initializing,
                TotalBytes = downloadGroup.Sum(c => c.Total),
                TotalFiles = 1,
                TransferredBytes = 0,
                TransferredFiles = 0
            };
        }

        Mediator.Publish(new DownloadStartedMessage(gameObjectHandler, _downloadStatus));

        await Parallel.ForEachAsync(downloadGroups, new ParallelOptions()
        {
            MaxDegreeOfParallelism = downloadGroups.Count,
            CancellationToken = ct,
        },
        async (fileGroup, token) =>
        {
            var firstFile = fileGroup.First();
            // let server predownload files
            var requestIdResponse = await _orchestrator.SendRequestAsync(HttpMethod.Post, MareFiles.RequestEnqueueFullPath(firstFile.DownloadUri),
                fileGroup.Select(c => c.Hash), token).ConfigureAwait(false);
            var fileCount = fileGroup.Count();
            Logger.LogDebug("Sent request for {n} files on server {uri} with result {result}", fileCount, firstFile.DownloadUri,
                await requestIdResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false));

            Guid requestId = Guid.Parse((await requestIdResponse.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim('"'));

            Logger.LogDebug("GUID {requestId} for {n} files on server {uri}", requestId, fileCount, firstFile.DownloadUri);

            var blockFile = _fileDbManager.GetCacheFilePath(requestId.ToString("N"), "blk");
            FileInfo fi = new(blockFile);
            try
            {
                _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.WaitingForSlot;
                await _orchestrator.WaitForDownloadSlotAsync(token).ConfigureAwait(false);
                _downloadStatus[fileGroup.Key].DownloadStatus = DownloadStatus.WaitingForQueue;
                Progress<long> progress = new((bytesDownloaded) =>
                {
                    try
                    {
                        if (!_downloadStatus.TryGetValue(fileGroup.Key, out FileDownloadStatus? value)) return;
                        value.TransferredBytes += bytesDownloaded;
                        if (value.TransferredBytes > value.TotalBytes)
                        {
                            value.TotalBytes = value.TransferredBytes;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Could not set download progress");
                    }
                });
                await DownloadAndMungeFileHttpClient(fileGroup.Key, requestId, [.. fileGroup], blockFile, progress, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _orchestrator.ReleaseDownloadSlot();
                if (File.Exists(blockFile))
                    File.Delete(blockFile);
                Logger.LogDebug("{dlName}: Detected cancellation of download for {id}, aborting file extraction", fi.Name, requestId);
                ClearDownload();
                return;
            }
            catch (Exception ex)
            {
                _orchestrator.ReleaseDownloadSlot();
                if (File.Exists(blockFile))
                    File.Delete(blockFile);
                Logger.LogError(ex, "{dlName}: Error during download of {id}", fi.Name, requestId);
                ClearDownload();
                return;
            }

            // Verify block file exists before attempting decompression
            if (!File.Exists(blockFile))
            {
                Logger.LogError("{dlName}: Block file {blockFile} does not exist, cannot proceed with decompression for {id}", fi.Name, fi.Name, requestId);
                _orchestrator.ReleaseDownloadSlot();
                ClearDownload();
                return;
            }

            FileStream? fileBlockStream = null;
            var threadCount = Math.Clamp((int)(Environment.ProcessorCount / 2.0f), 2, 8);
            var tasks = new List<Task>();
            try
            {
                if (_downloadStatus.TryGetValue(fileGroup.Key, out var status))
                {
                    status.TransferredFiles = 1;
                    status.DownloadStatus = DownloadStatus.Decompressing;
                }
                fileBlockStream = File.OpenRead(blockFile);
                while (fileBlockStream.Position < fileBlockStream.Length)
                {
                    (string fileHash, long fileLengthBytes) = ReadBlockFileHeader(fileBlockStream);
                    var chunkPosition = fileBlockStream.Position;
                    fileBlockStream.Position += fileLengthBytes;

                    while (tasks.Count > threadCount && tasks.Count(t => !t.IsCompleted) > 4)
                        await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);

                    var fileExtension = fileReplacement.First(f => string.Equals(f.Hash, fileHash, StringComparison.OrdinalIgnoreCase)).GamePaths[0].Split(".")[^1];
                    var tmpPath = _fileDbManager.GetCacheFilePath(Guid.NewGuid().ToString(), "tmp");
                    var filePath = _fileDbManager.GetCacheFilePath(fileHash, fileExtension);

                    Logger.LogDebug("{dlName}: Decompressing {file}:{le} => {dest}", fi.Name, fileHash, fileLengthBytes, filePath);

                    tasks.Add(Task.Run(() =>
                    {
                        var hashSuccess = false;
                        try
                        {
                            using var tmpFileStream = new HashingStream(new FileStream(tmpPath, new FileStreamOptions()
                            {
                                Mode = FileMode.CreateNew,
                                Access = FileAccess.Write,
                                Share = FileShare.None
                            }), SHA1.Create());

                            using var fileChunkStream = new FileStream(blockFile, new FileStreamOptions()
                            {
                                BufferSize = 80000,
                                Mode = FileMode.Open,
                                Access = FileAccess.Read
                            });
                            fileChunkStream.Position = chunkPosition;

                            using var innerFileStream = new LimitedStream(fileChunkStream, fileLengthBytes);
                            using var decoder = LZ4Frame.Decode(innerFileStream);
                            long startPos = fileChunkStream.Position;
                            decoder.AsStream().CopyTo(tmpFileStream);
                            long readBytes = fileChunkStream.Position - startPos;

                            if (readBytes != fileLengthBytes)
                            {
                                throw new EndOfStreamException();
                            }

                            string calculatedHash = BitConverter.ToString(tmpFileStream.Finish()).Replace("-", "", StringComparison.Ordinal);

                            if (!calculatedHash.Equals(fileHash, StringComparison.Ordinal))
                            {
                                Logger.LogError("Hash mismatch after extracting, got {hash}, expected {expectedHash}, deleting file", calculatedHash, fileHash);
                                return;
                            }

                            tmpFileStream.Close();
                            _fileCompactor.RenameAndCompact(filePath, tmpPath);
                            PersistFileToStorage(fileHash, filePath, fileLengthBytes);
                            hashSuccess = true;
                        }
                        catch (EndOfStreamException)
                        {
                            Logger.LogWarning("{dlName}: Failure to extract file {fileHash}, stream ended prematurely", fi.Name, fileHash);
                        }
                        catch (Exception e)
                        {
                            Logger.LogWarning(e, "{dlName}: Error during decompression of {hash}", fi.Name, fileHash);

                            foreach (var fr in fileReplacement)
                                Logger.LogWarning(" - {h}: {x}", fr.Hash, fr.GamePaths[0]);
                        }
                        finally
                        {
                            if (File.Exists(tmpPath))
                                File.Delete(tmpPath);
                            // Complete the deduplicator and remove from pending fallback hashes
                            _deduplicator.Complete(fileHash, hashSuccess);
                            using (pendingFallbackLock.EnterScope()) { pendingFallbackHashes.Remove(fileHash); }
                        }
                    }, CancellationToken.None));
                }

                Task.WaitAll([.. tasks], CancellationToken.None);
            }
            catch (EndOfStreamException)
            {
                Logger.LogDebug("{dlName}: Failure to extract file header data, stream ended", fi.Name);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{dlName}: Error during block file read", fi.Name);
            }
            finally
            {
                Task.WaitAll([.. tasks], CancellationToken.None);
                _orchestrator.ReleaseDownloadSlot();
                if (fileBlockStream != null)
                    await fileBlockStream.DisposeAsync().ConfigureAwait(false);
                File.Delete(blockFile);
            }
        }).ConfigureAwait(false);

        Logger.LogDebug("Download end: {id}", gameObjectHandler);

        // Clean up any pending fallback hashes that were never processed (e.g., if block file download failed)
        // This ensures other handlers waiting on these hashes don't stay blocked forever
        using (pendingFallbackLock.EnterScope())
        {
            if (pendingFallbackHashes.Count > 0)
            {
                Logger.LogWarning("Completing {count} unprocessed fallback hashes with failure", pendingFallbackHashes.Count);
                foreach (var hash in pendingFallbackHashes)
                {
                    _deduplicator.Complete(hash, false);
                }
                pendingFallbackHashes.Clear();
            }
        }

        ClearDownload();
    }

    private async Task<List<DownloadFileDto>> FilesGetSizes(List<string> hashes, CancellationToken ct)
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");
        // Prefer POST with JSON body (new server behavior). Add robust diagnostics and fallbacks for older deployments.
        var uri = MareFiles.ServerFilesGetSizesFullPath(_orchestrator.FilesCdnUri!);

        // Try POST first
        HttpResponseMessage? postResponse = null;
        try
        {
            postResponse = await _orchestrator
                .SendRequestAsync(HttpMethod.Post, uri, hashes, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "getFileSizes POST threw before response");
        }

        if (postResponse == null || !postResponse.IsSuccessStatusCode)
        {
            // If server hasn't been updated yet, it may reject POST. Retry with GET once.
            var postStatus = postResponse?.StatusCode;
            if (postStatus == HttpStatusCode.NotFound || postStatus == HttpStatusCode.MethodNotAllowed)
            {
                try
                {
                    var getFallback = await _orchestrator
                        .SendRequestAsync(HttpMethod.Get, uri, hashes, ct)
                        .ConfigureAwait(false);

                    getFallback.EnsureSuccessStatusCode();

                    // Old servers might return text/plain with a double-serialized JSON string
                    var body = await getFallback.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return ParseDownloadFileDtoList(body);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "getFileSizes GET fallback failed");
                }
            }
            try
            {
                var getRetry = await _orchestrator
                    .SendRequestAsync(HttpMethod.Get, uri, hashes, ct)
                    .ConfigureAwait(false);
                if (getRetry.IsSuccessStatusCode)
                {
                    var body = await getRetry.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return ParseDownloadFileDtoList(body);
                }
                else
                {
                    var getBody = await SafeReadBodySnippetAsync(getRetry, ct).ConfigureAwait(false);
                    Logger.LogWarning("getFileSizes GET retry failed: {code} {reason}. Headers: {headers}. Body: {body}",
                        (int)getRetry.StatusCode, getRetry.ReasonPhrase ?? string.Empty,
                        string.Join("; ", getRetry.Headers.Select(h => h.Key + ":" + string.Join(",", h.Value))),
                        getBody);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "getFileSizes GET retry threw");
            }
            if (postResponse != null)
            {
                var bodySnippet = await SafeReadBodySnippetAsync(postResponse, ct).ConfigureAwait(false);
                var reason = postResponse.ReasonPhrase ?? string.Empty;
                Logger.LogWarning("getFileSizes POST failed: {code} {reason}. Headers: {headers}. Body: {body}",
                    (int)postResponse.StatusCode, reason,
                    string.Join("; ", postResponse.Headers.Select(h => h.Key + ":" + string.Join(",", h.Value))),
                    bodySnippet);
            }

            return hashes.Select(h => new DownloadFileDto
            {
                Hash = h,
                FileExists = false,
                Url = string.Empty,
                Size = 0,
                IsForbidden = false,
                ForbiddenBy = string.Empty
            }).ToList();
        }

        try
        {
            var opts = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return await postResponse.Content
                .ReadFromJsonAsync<List<DownloadFileDto>>(options: opts, cancellationToken: ct)
                .ConfigureAwait(false) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            var body = await postResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            try
            {
                return ParseDownloadFileDtoList(body);
            }
            catch (Exception ex)
            {
                var snippet = body.Length > 2048 ? body[..2048] + "…" : body;
                throw new System.Text.Json.JsonException($"Failed to parse getFileSizes response. Snippet: {snippet}", ex);
            }
        }
    }

    private static List<DownloadFileDto> ParseDownloadFileDtoList(string body)
    {
        string json = body;
        try
        {
            if (!string.IsNullOrEmpty(body) && body.Length >= 2 && body[0] == '"' && body[^1] == '"')
            {
                var inner = System.Text.Json.JsonSerializer.Deserialize<string>(body);
                if (!string.IsNullOrEmpty(inner)) json = inner;
            }
        }
        catch
        {
            // ignore, fall back to using original body
        }

        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = System.Text.Json.JsonSerializer.Deserialize<List<DownloadFileDto>>(json, opts);
        return list ?? [];
    }

    private static async Task<string> SafeReadBodySnippetAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(body)) return string.Empty;
            return body.Length > 2048 ? body[..2048] + "…" : body;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void PersistFileToStorage(string fileHash, string filePath, long? compressedSize = null)
    {
        try
        {
            var entry = _fileDbManager.CreateCacheEntry(filePath, fileHash);
            if (entry != null && !string.Equals(entry.Hash, fileHash, StringComparison.OrdinalIgnoreCase))
            {
                _fileDbManager.RemoveHashedFile(entry.Hash, entry.PrefixedFilePath);
                entry = null;
            }
            if (entry != null)
                entry.CompressedSize = compressedSize;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error creating cache entry");
        }
    }

    private async Task WaitForDownloadReady(List<DownloadFileTransfer> downloadFileTransfer, Guid requestId, CancellationToken downloadCt)
    {
        bool alreadyCancelled = false;
        try
        {
            CancellationTokenSource localTimeoutCts = new();
            localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            CancellationTokenSource composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);

            while (!_orchestrator.IsDownloadReady(requestId))
            {
                try
                {
                    await Task.Delay(250, composite.Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    if (downloadCt.IsCancellationRequested) throw;

                    var req = await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCheckQueueFullPath(downloadFileTransfer[0].DownloadUri, requestId),
                        downloadFileTransfer.Select(c => c.Hash).ToList(), downloadCt).ConfigureAwait(false);
                    req.EnsureSuccessStatusCode();
                    localTimeoutCts.Dispose();
                    composite.Dispose();
                    localTimeoutCts = new();
                    localTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                    composite = CancellationTokenSource.CreateLinkedTokenSource(downloadCt, localTimeoutCts.Token);
                }
            }

            localTimeoutCts.Dispose();
            composite.Dispose();

            Logger.LogDebug("Download {requestId} ready", requestId);
        }
        catch (TaskCanceledException)
        {
            try
            {
                await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer[0].DownloadUri, requestId)).ConfigureAwait(false);
                alreadyCancelled = true;
            }
            catch
            {
                // ignore whatever happens here
            }

            throw;
        }
        finally
        {
            if (downloadCt.IsCancellationRequested && !alreadyCancelled)
            {
                try
                {
                    await _orchestrator.SendRequestAsync(HttpMethod.Get, MareFiles.RequestCancelFullPath(downloadFileTransfer[0].DownloadUri, requestId)).ConfigureAwait(false);
                }
                catch
                {
                    // ignore whatever happens here
                }
            }
            _orchestrator.ClearDownloadRequest(requestId);
        }
    }
}