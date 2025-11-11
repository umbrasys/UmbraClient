using UmbraSync.WebAPI;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UmbraSync.MareConfiguration;

public class ConfigurationMigrator(ILogger<ConfigurationMigrator> logger, MareConfigService mareConfig) : IHostedService
{
    private readonly ILogger<ConfigurationMigrator> _logger = logger;
    private readonly MareConfigService _mareConfig = mareConfig;

    public void Migrate()
    {
        try
        {
            var path = _mareConfig.ConfigurationPath;
            if (!File.Exists(path)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            bool changed = false;

            if (root.TryGetProperty("EnableAutoSyncDiscovery", out var enableAutoSync))
            {
                var val = enableAutoSync.GetBoolean();
                if (_mareConfig.Current.EnableAutoDetectDiscovery != val)
                {
                    _mareConfig.Current.EnableAutoDetectDiscovery = val;
                    changed = true;
                }
            }
            if (root.TryGetProperty("AllowAutoSyncPairRequests", out var allowAutoSync))
            {
                var val = allowAutoSync.GetBoolean();
                if (_mareConfig.Current.AllowAutoDetectPairRequests != val)
                {
                    _mareConfig.Current.AllowAutoDetectPairRequests = val;
                    changed = true;
                }
            }
            if (root.TryGetProperty("AutoSyncMaxDistanceMeters", out var maxDistSync) && maxDistSync.TryGetInt32(out var md))
            {
                if (_mareConfig.Current.AutoDetectMaxDistanceMeters != md)
                {
                    _mareConfig.Current.AutoDetectMaxDistanceMeters = md;
                    changed = true;
                }
            }
            if (root.TryGetProperty("AutoSyncMuteMinutes", out var muteSync) && muteSync.TryGetInt32(out var mm))
            {
                if (_mareConfig.Current.AutoDetectMuteMinutes != mm)
                {
                    _mareConfig.Current.AutoDetectMuteMinutes = mm;
                    changed = true;
                }
            }

            if (changed)
            {
                _logger.LogInformation("Migrated config: AutoSync -> AutoDetect fields");
                _mareConfig.Save();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Configuration migration failed");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Migrate();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
