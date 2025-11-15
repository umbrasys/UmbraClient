using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using UmbraSync.API.Data;
using UmbraSync.API.Dto.Group;
using UmbraSync.Services.Mediator;
using UmbraSync.WebAPI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UmbraSync.Services.AutoDetect;

public sealed class SyncshellDiscoveryService : IHostedService, IMediatorSubscriber
{
    private readonly ILogger<SyncshellDiscoveryService> _logger;
    private readonly MareMediator _mediator;
    private readonly ApiController _apiController;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly object _entriesLock = new();
    private List<SyncshellDiscoveryEntryDto> _entries = [];
    private string? _lastError;
    private bool _isRefreshing;

    public SyncshellDiscoveryService(ILogger<SyncshellDiscoveryService> logger, MareMediator mediator, ApiController apiController)
    {
        _logger = logger;
        _mediator = mediator;
        _apiController = apiController;
    }

    public MareMediator Mediator => _mediator;

    public IReadOnlyList<SyncshellDiscoveryEntryDto> Entries
    {
        get
        {
            lock (_entriesLock)
            {
                return _entries.AsReadOnly();
            }
        }
    }

    public bool IsRefreshing => _isRefreshing;
    public string? LastError => _lastError;

    public async Task<bool> JoinAsync(string gid, CancellationToken ct)
    {
        try
        {
            return await _apiController.SyncshellDiscoveryJoin(new GroupDto(new GroupData(gid))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Join syncshell discovery failed for {gid}", gid);
            return false;
        }
    }

    public async Task<SyncshellDiscoveryStateDto?> GetStateAsync(string gid, CancellationToken ct)
    {
        try
        {
            return await _apiController.SyncshellDiscoveryGetState(new GroupDto(new GroupData(gid))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch syncshell discovery state for {gid}", gid);
            return null;
        }
    }

    public async Task<bool> SetVisibilityAsync(string gid, bool visible, CancellationToken ct)
    {
        return await SetVisibilityAsync(gid, visible, null, null, null, null, null, ct).ConfigureAwait(false);
    }

    public async Task<bool> SetVisibilityAsync(string gid, bool visible, int? displayDurationHours,
        int[]? activeWeekdays, TimeSpan? timeStartLocal, TimeSpan? timeEndLocal, string? timeZone,
        CancellationToken ct)
    {
        try
        {
            var request = new SyncshellDiscoveryVisibilityRequestDto
            {
                GID = gid,
                AutoDetectVisible = visible,
                DisplayDurationHours = displayDurationHours,
                ActiveWeekdays = activeWeekdays,
                TimeStartLocal = timeStartLocal.HasValue ? new DateTime(timeStartLocal.Value.Ticks).ToString("HH:mm", CultureInfo.InvariantCulture) : null,
                TimeEndLocal = timeEndLocal.HasValue ? new DateTime(timeEndLocal.Value.Ticks).ToString("HH:mm", CultureInfo.InvariantCulture) : null,
                TimeZone = timeZone,
            };
            var success = await _apiController.SyncshellDiscoverySetVisibility(request).ConfigureAwait(false);
            if (!success) return false;

            var state = await GetStateAsync(gid, ct).ConfigureAwait(false);
            if (state != null)
            {
                _mediator.Publish(new SyncshellAutoDetectStateChanged(state.GID, state.AutoDetectVisible, state.PasswordTemporarilyDisabled));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set syncshell visibility for {gid}", gid);
            return false;
        }
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (!await _refreshSemaphore.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _isRefreshing = true;
            var discovered = await _apiController.SyncshellDiscoveryList().ConfigureAwait(false);
            lock (_entriesLock)
            {
                _entries = discovered ?? [];
            }
            _lastError = null;
            _mediator.Publish(new SyncshellDiscoveryUpdated(Entries.ToList()));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh syncshell discovery list");
            _lastError = ex.Message;
        }
        finally
        {
            _isRefreshing = false;
            _refreshSemaphore.Release();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _mediator.Subscribe<ConnectedMessage>(this, msg =>
        {
            _ = RefreshAsync(CancellationToken.None);
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _mediator.UnsubscribeAll(this);
        return Task.CompletedTask;
    }
}
