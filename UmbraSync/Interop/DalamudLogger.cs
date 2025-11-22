using Dalamud.Plugin.Services;
using UmbraSync.MareConfiguration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Globalization;

namespace UmbraSync.Interop;

internal sealed class DalamudLogger : ILogger
{
    private readonly MareConfigService _mareConfigService;
    private readonly string _name;
    private readonly IPluginLog _pluginLog;

    public DalamudLogger(string name, MareConfigService mareConfigService, IPluginLog pluginLog)
    {
        _name = name;
        _mareConfigService = mareConfigService;
        _pluginLog = pluginLog;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel)
    {
        return (int)_mareConfigService.Current.LogLevel <= (int)logLevel;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        if ((int)logLevel <= (int)LogLevel.Information)
        {
            _pluginLog.Information(string.Format(CultureInfo.InvariantCulture, "[{0}]{{{1}}} {2}", _name, (int)logLevel, state));
        }
        else
        {
            StringBuilder sb = new();
            sb.Append(string.Format(CultureInfo.InvariantCulture, "[{0}]{{{1}}} {2}: {3}", _name, (int)logLevel, state, exception?.Message));
            var stackTrace = exception?.StackTrace;
            if (!string.IsNullOrWhiteSpace(stackTrace))
            {
                sb.AppendLine(stackTrace);
            }
            var innerException = exception?.InnerException;
            while (innerException != null)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "InnerException {0}: {1}", innerException, innerException.Message));
                sb.AppendLine(innerException.StackTrace);
                innerException = innerException.InnerException;
            }
            if (logLevel == LogLevel.Warning)
                _pluginLog.Warning(sb.ToString());
            else if (logLevel == LogLevel.Error)
                _pluginLog.Error(sb.ToString());
            else
                _pluginLog.Fatal(sb.ToString());
        }
    }
}
