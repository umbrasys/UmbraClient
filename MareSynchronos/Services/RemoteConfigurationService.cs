using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services
{
    /// <summary>
    /// Stub minimal : renvoie toujours null (pas de conf distante).
    /// </summary>
    public class RemoteConfigurationService
    {
        private readonly ILogger<RemoteConfigurationService> _logger;

        public RemoteConfigurationService(ILogger<RemoteConfigurationService> logger)
        {
            _logger = logger;
        }

        public Task<T?> GetConfigAsync<T>(string key) where T : class
            => Task.FromResult<T?>(null);
    }
}
