using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UmbraSync.API.Dto.McdfShare;
using UmbraSync.MareConfiguration.Models;
using UmbraSync.Localization;
using UmbraSync.Services.Mediator;
using UmbraSync.Services.Notification;
using UmbraSync.Services.ServerConfiguration;
using UmbraSync.WebAPI;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace UmbraSync.Services.CharaData;

public sealed class McdfShareManager(ILogger<McdfShareManager> logger, ApiController apiController,
    CharaDataFileHandler fileHandler, CharaDataManager charaDataManager,
    ServerConfigurationManager serverConfigurationManager, MareMediator mediator, NotificationTracker notificationTracker)
{
    private readonly ILogger<McdfShareManager> _logger = logger;
    private readonly ApiController _apiController = apiController;
    private readonly CharaDataFileHandler _fileHandler = fileHandler;
    private readonly CharaDataManager _charaDataManager = charaDataManager;
    private readonly ServerConfigurationManager _serverConfigurationManager = serverConfigurationManager;
    private readonly MareMediator _mediator = mediator;
    private readonly NotificationTracker _notificationTracker = notificationTracker;
    private readonly SemaphoreSlim _operationSemaphore = new(1, 1);
    private readonly List<McdfShareEntryDto> _ownShares = new();
    private readonly List<McdfShareEntryDto> _sharedWithMe = new();
    private Task? _currentTask;

    public IReadOnlyList<McdfShareEntryDto> OwnShares => _ownShares;
    public IReadOnlyList<McdfShareEntryDto> SharedShares => _sharedWithMe;
    public bool IsBusy => _currentTask is { IsCompleted: false };
    public string? LastError { get; private set; }
    public string? LastSuccess { get; private set; }

    public Task RefreshAsync(CancellationToken token)
    {
        return RunOperation(() => InternalRefreshAsync(token));
    }

    public Task CreateShareAsync(string description, IReadOnlyList<string> allowedIndividuals, IReadOnlyList<string> allowedSyncshells, DateTime? expiresAtUtc, CancellationToken token)
    {
        return RunOperation(async () =>
        {
            token.ThrowIfCancellationRequested();

            var mcdfBytes = await _fileHandler.CreateCharaFileBytesAsync(description, token).ConfigureAwait(false);
            if (mcdfBytes == null || mcdfBytes.Length == 0)
            {
                LastError = "Impossible de préparer les données MCDF.";
                return;
            }

            var secretKey = _serverConfigurationManager.GetSecretKey(out bool hasMultiple);
            if (hasMultiple)
            {
                LastError = "Plusieurs clés secrètes sont configurées pour ce personnage. Corrigez cela dans les paramètres.";
                return;
            }

            if (string.IsNullOrEmpty(secretKey))
            {
                LastError = "Aucune clé secrète n'est configurée pour ce personnage.";
                return;
            }

            var shareId = Guid.NewGuid();
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] key = DeriveKey(secretKey, shareId, salt);

            byte[] cipher = new byte[mcdfBytes.Length];
            byte[] tag = new byte[16];

            using (var aes = new AesGcm(key, 16))
            {
                aes.Encrypt(nonce, mcdfBytes, cipher, tag);
            }

            var uploadDto = new McdfShareUploadRequestDto
            {
                ShareId = shareId,
                Description = description,
                CipherData = cipher,
                Nonce = nonce,
                Salt = salt,
                Tag = tag,
                ExpiresAtUtc = expiresAtUtc,
                AllowedIndividuals = allowedIndividuals.ToList(),
                AllowedSyncshells = allowedSyncshells.ToList()
            };

            await _apiController.McdfShareUpload(uploadDto).ConfigureAwait(false);
            await InternalRefreshAsync(token).ConfigureAwait(false);
            LastSuccess = "Partage MCDF créé.";
            NotifyShareCreated(shareId, description, uploadDto.AllowedIndividuals.Count, uploadDto.AllowedSyncshells.Count);
            _logger.LogInformation("MCDF share {ShareId} uploaded ({Individuals} UID / {Syncshells} syncshells). Description: {Description}", shareId, uploadDto.AllowedIndividuals.Count, uploadDto.AllowedSyncshells.Count, description);
        });
    }

    public Task DeleteShareAsync(Guid shareId)
    {
        return RunOperation(async () =>
        {
            var result = await _apiController.McdfShareDelete(shareId).ConfigureAwait(false);
            if (!result)
            {
                LastError = "Le serveur a refusé de supprimer le partage MCDF.";
                return;
            }

            _ownShares.RemoveAll(s => s.Id == shareId);
            _sharedWithMe.RemoveAll(s => s.Id == shareId);
            await InternalRefreshAsync(CancellationToken.None).ConfigureAwait(false);
            LastSuccess = "Partage MCDF supprimé.";
        });
    }

    public Task UpdateShareAsync(McdfShareUpdateRequestDto updateRequest)
    {
        return RunOperation(async () =>
        {
            var updated = await _apiController.McdfShareUpdate(updateRequest).ConfigureAwait(false);
            if (updated == null)
            {
                LastError = "Le serveur a refusé de mettre à jour le partage MCDF.";
                return;
            }

            var idx = _ownShares.FindIndex(s => s.Id == updated.Id);
            if (idx >= 0)
            {
                _ownShares[idx] = updated;
            }
            LastSuccess = "Partage MCDF mis à jour.";
        });
    }

    public Task ApplyShareAsync(Guid shareId, CancellationToken token)
    {
        return RunOperation(async () =>
        {
            token.ThrowIfCancellationRequested();
            var plainBytes = await DownloadAndDecryptShareAsync(shareId, token).ConfigureAwait(false);
            if (plainBytes == null)
            {
                LastError ??= "Échec du téléchargement du partage MCDF.";
                return;
            }

            var tempPath = await _charaDataManager.LoadMcdfFromBytes(plainBytes, token).ConfigureAwait(false);
            try
            {
                await _charaDataManager.McdfApplyToGposeTarget().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // ignored
                }
            }
            LastSuccess = "Partage MCDF appliqué sur la cible GPose.";
        });
    }

    public Task ExportShareAsync(Guid shareId, string filePath, CancellationToken token)
    {
        return RunOperation(async () =>
        {
            token.ThrowIfCancellationRequested();
            var plainBytes = await DownloadAndDecryptShareAsync(shareId, token).ConfigureAwait(false);
            if (plainBytes == null)
            {
                LastError ??= "Échec du téléchargement du partage MCDF.";
                return;
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(filePath, plainBytes, token).ConfigureAwait(false);
            LastSuccess = "Partage MCDF exporté.";
        });
    }

    public Task DownloadShareToFileAsync(McdfShareEntryDto entry, string filePath, CancellationToken token)
    {
        return ExportShareAsync(entry.Id, filePath, token);
    }

    private async Task<byte[]?> DownloadAndDecryptShareAsync(Guid shareId, CancellationToken token)
    {
        var payload = await _apiController.McdfShareDownload(shareId).ConfigureAwait(false);
        if (payload == null)
        {
            LastError = "Partage indisponible.";
            return null;
        }

        var secretKey = _serverConfigurationManager.GetSecretKey(out bool hasMultiple);
        if (hasMultiple)
        {
            LastError = "Plusieurs clés secrètes sont configurées pour ce personnage.";
            return null;
        }

        if (string.IsNullOrEmpty(secretKey))
        {
            LastError = "Aucune clé secrète n'est configurée pour ce personnage.";
            return null;
        }

        byte[] key = DeriveKey(secretKey, payload.ShareId, payload.Salt);
        byte[] plaintext = new byte[payload.CipherData.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(payload.Nonce, payload.CipherData, payload.Tag, plaintext);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt MCDF share {ShareId}", shareId);
            LastError = "Impossible de déchiffrer le partage MCDF.";
            return null;
        }

        token.ThrowIfCancellationRequested();
        return plaintext;
    }

    private async Task InternalRefreshAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var own = await _apiController.McdfShareGetOwn().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var shared = await _apiController.McdfShareGetShared().ConfigureAwait(false);
        _ownShares.Clear();
        _ownShares.AddRange(own);
        _sharedWithMe.Clear();
        _sharedWithMe.AddRange(shared);
        LastSuccess = "Partages MCDF actualisés.";
    }

    private Task RunOperation(Func<Task> operation)
    {
        async Task Wrapper()
        {
            await _operationSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                LastError = null;
                LastSuccess = null;
                await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during MCDF share operation");
                LastError = ex.Message;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        var task = Wrapper();
        _currentTask = task;
        return task;
    }

    private static byte[] DeriveKey(string secretKey, Guid shareId, byte[] salt)
    {
        byte[] secretBytes;
        try
        {
            secretBytes = Convert.FromHexString(secretKey);
        }
        catch (FormatException)
        {
            // fallback to UTF8 if not hex
            secretBytes = System.Text.Encoding.UTF8.GetBytes(secretKey);
        }

        byte[] shareBytes = shareId.ToByteArray();
        byte[] material = new byte[secretBytes.Length + shareBytes.Length + salt.Length];
        Buffer.BlockCopy(secretBytes, 0, material, 0, secretBytes.Length);
        Buffer.BlockCopy(shareBytes, 0, material, secretBytes.Length, shareBytes.Length);
        Buffer.BlockCopy(salt, 0, material, secretBytes.Length + shareBytes.Length, salt.Length);
        return SHA256.HashData(material);
    }

    private void NotifyShareCreated(Guid shareId, string description, int individualCount, int syncshellCount)
    {
        string safeDescription = string.IsNullOrWhiteSpace(description)
            ? shareId.ToString("D", CultureInfo.InvariantCulture)
            : description;
        string targetSummary = string.Format(CultureInfo.CurrentCulture, Loc.Get("Notification.McdfShare.Created.Summary"), individualCount, syncshellCount);
        string toastTitle = Loc.Get("Notification.McdfShare.Created.ToastTitle");
        string toastBody = string.Format(CultureInfo.CurrentCulture, Loc.Get("Notification.McdfShare.Created.ToastBody"), safeDescription, targetSummary);

        _mediator.Publish(new DualNotificationMessage(toastTitle, toastBody, NotificationType.Info, TimeSpan.FromSeconds(4)));
        _notificationTracker.Upsert(NotificationEntry.McdfShareCreated(shareId, safeDescription, individualCount, syncshellCount));
    }
}
