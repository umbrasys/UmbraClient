using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace MareSynchronos.Services;

/* Reflection code based almost entirely on ECommons DalamudReflector

MIT License

Copyright (c) 2023 NightmareXIV

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

public sealed class RepoChangeService : IHostedService
{
    #region Reflection Helpers
    private const BindingFlags AllFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static object GetFoP(object obj, string name)
    {
        Type? type = obj.GetType();
        while (type != null)
        {
            var fieldInfo = type.GetField(name, AllFlags);
            if (fieldInfo != null)
            {
                return fieldInfo.GetValue(obj)!;
            }
            var propertyInfo = type.GetProperty(name, AllFlags);
            if (propertyInfo != null)
            {
                return propertyInfo.GetValue(obj)!;
            }
            type = type.BaseType;
        }
        throw new Exception($"Reflection GetFoP failed (not found: {obj.GetType().Name}.{name})");
    }

    private static T GetFoP<T>(object obj, string name)
    {
        return (T)GetFoP(obj, name);
    }

    private static void SetFoP(object obj, string name, object value)
    {
        var type = obj.GetType();
        var field = type.GetField(name, AllFlags);
        if (field != null)
        {
            field.SetValue(obj, value);
        }
        else
        {
            var prop = type.GetProperty(name, AllFlags)!;
            if (prop == null)
                throw new Exception($"Reflection SetFoP failed (not found: {type.Name}.{name})");
            prop.SetValue(obj, value);
        }
    }

    private static object? Call(object obj, string name, object[] @params, bool matchExactArgumentTypes = false)
    {
        MethodInfo? info;
        var type = obj.GetType();
        if (!matchExactArgumentTypes)
        {
            info = type.GetMethod(name, AllFlags);
        }
        else
        {
            info = type.GetMethod(name, AllFlags, @params.Select(x => x.GetType()).ToArray());
        }
        if (info == null)
            throw new Exception($"Reflection Call failed (not found: {type.Name}.{name})");
        return info.Invoke(obj, @params);
    }

    private static T Call<T>(object obj, string name, object[] @params, bool matchExactArgumentTypes = false)
    {
        return (T)Call(obj, name, @params, matchExactArgumentTypes)!;
    }
    #endregion

    #region Dalamud Reflection
    public object GetService(string serviceFullName)
    {
        return _pluginInterface.GetType().Assembly.
                GetType("Dalamud.Service`1", true)!.MakeGenericType(_pluginInterface.GetType().Assembly.GetType(serviceFullName, true)!).
                GetMethod("Get")!.Invoke(null, BindingFlags.Default, null, Array.Empty<object>(), null)!;
    }

    private object GetPluginManager()
    {
        return _pluginInterface.GetType().Assembly.
                GetType("Dalamud.Service`1", true)!.MakeGenericType(_pluginInterface.GetType().Assembly.GetType("Dalamud.Plugin.Internal.PluginManager", true)!).
                GetMethod("Get")!.Invoke(null, BindingFlags.Default, null, Array.Empty<object>(), null)!;
    }

    private void ReloadPluginMasters()
    {
        var mgr = GetService("Dalamud.Plugin.Internal.PluginManager");
        var pluginReload = mgr.GetType().GetMethod("SetPluginReposFromConfigAsync", BindingFlags.Instance | BindingFlags.Public)!;
        pluginReload.Invoke(mgr, [true]);
    }

    public void SaveDalamudConfig()
    {
        var conf = GetService("Dalamud.Configuration.Internal.DalamudConfiguration");
        var configSave = conf?.GetType().GetMethod("QueueSave", BindingFlags.Instance | BindingFlags.Public);
        configSave?.Invoke(conf, null);
    }

    private IEnumerable<object> GetRepoByURL(string repoURL)
    {
        var conf = GetService("Dalamud.Configuration.Internal.DalamudConfiguration");
        var repolist = (System.Collections.IEnumerable)GetFoP(conf, "ThirdRepoList");
        foreach (var r in repolist)
        {
            if (((string)GetFoP(r, "Url")).Equals(repoURL, StringComparison.OrdinalIgnoreCase))
                yield return r;
        }
    }

    private bool HasRepo(string repoURL)
    {
        var conf = GetService("Dalamud.Configuration.Internal.DalamudConfiguration");
        var repolist = (System.Collections.IEnumerable)GetFoP(conf, "ThirdRepoList");
        foreach (var r in repolist)
        {
            if (((string)GetFoP(r, "Url")).Equals(repoURL, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void AddRepo(string repoURL, bool enabled)
    {
        var conf = GetService("Dalamud.Configuration.Internal.DalamudConfiguration");
        var repolist = (System.Collections.IEnumerable)GetFoP(conf, "ThirdRepoList");
        foreach (var r in repolist)
        {
            if (((string)GetFoP(r, "Url")).Equals(repoURL, StringComparison.OrdinalIgnoreCase))
                return;
        }
        var instance = Activator.CreateInstance(_pluginInterface.GetType().Assembly.GetType("Dalamud.Configuration.ThirdPartyRepoSettings")!)!;
        SetFoP(instance, "Url", repoURL);
        SetFoP(instance, "IsEnabled", enabled);
        GetFoP<System.Collections.IList>(conf, "ThirdRepoList").Add(instance!);
    }

    private void RemoveRepo(string repoURL)
    {
        var toRemove = new List<object>();
        var conf = GetService("Dalamud.Configuration.Internal.DalamudConfiguration");
        var repolist = (System.Collections.IList)GetFoP(conf, "ThirdRepoList");
        foreach (var r in repolist)
        {
            if (((string)GetFoP(r, "Url")).Equals(repoURL, StringComparison.OrdinalIgnoreCase))
                toRemove.Add(r);
        }
        foreach (var r in toRemove)
            repolist.Remove(r);
    }

    public List<(object LocalPlugin, string InstalledFromUrl)> GetLocalPluginsByName(string internalName)
    {
        List<(object LocalPlugin, string RepoURL)> result = [];

        var pluginManager = GetPluginManager();
        var installedPlugins = (System.Collections.IList)pluginManager.GetType().GetProperty("InstalledPlugins")!.GetValue(pluginManager)!;

        foreach (var plugin in installedPlugins)
        {
            if (((string)plugin.GetType().GetProperty("InternalName")!.GetValue(plugin)!).Equals(internalName, StringComparison.Ordinal))
            {
                var type = plugin.GetType();
                if (type.Name.Equals("LocalDevPlugin", StringComparison.Ordinal))
                    continue;
                var manifest = GetFoP(plugin, "manifest");
                string installedFromUrl = (string)GetFoP(manifest, "InstalledFromUrl");
                result.Add((plugin, installedFromUrl));
            }
        }

        return result;
    }
    #endregion

    private readonly ILogger<RepoChangeService> _logger;
    private readonly RemoteConfigurationService _remoteConfig;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IFramework _framework;

    public RepoChangeService(ILogger<RepoChangeService> logger, RemoteConfigurationService remoteConfig, IDalamudPluginInterface pluginInterface, IFramework framework)
    {
        _logger = logger;
        _remoteConfig = remoteConfig;
        _pluginInterface = pluginInterface;
        _framework = framework;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting RepoChange Service");
        var repoChangeConfig = await _remoteConfig.GetConfigAsync<RepoChangeConfig>("repoChange").ConfigureAwait(false) ?? new();

        var currentRepo = repoChangeConfig.CurrentRepo;
        var validRepos = (repoChangeConfig.ValidRepos ?? []).ToList();

        if (!currentRepo.IsNullOrEmpty() && !validRepos.Contains(currentRepo, StringComparer.Ordinal))
            validRepos.Add(currentRepo);

        if (validRepos.Count == 0)
        {
            _logger.LogInformation("No valid repos configured, skipping");
            return;
        }

        await _framework.RunOnTick(() =>
        {
            try
            {
                var internalName = Assembly.GetExecutingAssembly().GetName().Name!;
                var localPlugins = GetLocalPluginsByName(internalName);

                var suffix = string.Empty;

                if (localPlugins.Count == 0)
                {
                    _logger.LogInformation("Skipping: No intalled plugin found");
                    return;
                }

                var hasValidCustomRepoUrl = false;

                foreach (var vr in validRepos)
                {
                    var vrCN = vr.Replace(".json", "_CN.json", StringComparison.Ordinal);
                    var vrKR = vr.Replace(".json", "_KR.json", StringComparison.Ordinal);
                    if (HasRepo(vr) || HasRepo(vrCN) || HasRepo(vrKR))
                    {
                        hasValidCustomRepoUrl = true;
                        break;
                    }
                }

                List<string> oldRepos = [];
                var pluginRepoUrl = localPlugins[0].InstalledFromUrl;

                if (pluginRepoUrl.Contains("_CN.json", StringComparison.Ordinal))
                    suffix = "_CN";
                else if (pluginRepoUrl.Contains("_KR.json", StringComparison.Ordinal))
                    suffix = "_KR";

                bool hasOldPluginRepoUrl = false;

                foreach (var plugin in localPlugins)
                {
                    foreach (var vr in validRepos)
                    {
                        var validRepo = vr.Replace(".json", $"{suffix}.json");
                        if (!plugin.InstalledFromUrl.Equals(validRepo, StringComparison.Ordinal))
                        {
                            oldRepos.Add(plugin.InstalledFromUrl);
                            hasOldPluginRepoUrl = true;
                        }
                    }
                }

                if (hasValidCustomRepoUrl)
                {
                    if (hasOldPluginRepoUrl)
                        _logger.LogInformation("Result: Repo URL is up to date, but plugin install source is incorrect");
                    else
                        _logger.LogInformation("Result: Repo URL is up to date");
                }
                else
                {
                    _logger.LogInformation("Result: Repo URL needs to be replaced");
                }

                if (currentRepo.IsNullOrEmpty())
                {
                    _logger.LogWarning("No current repo URL configured");
                    return;
                }

                // Pre-test plugin repo url rewriting to ensure it succeeds before replacing the custom repo URL
                if (hasOldPluginRepoUrl)
                {
                    foreach (var plugin in localPlugins)
                    {
                        var manifest = GetFoP(plugin.LocalPlugin, "manifest");
                        if (manifest == null)
                            throw new Exception("Plugin manifest is null");
                        var manifestFile = GetFoP(plugin.LocalPlugin, "manifestFile");
                        if (manifestFile == null)
                            throw new Exception("Plugin manifestFile is null");
                        var repo = GetFoP(manifest, "InstalledFromUrl");
                        if (((string)repo).IsNullOrEmpty())
                            throw new Exception("Plugin repo url is null or empty");
                        SetFoP(manifest, "InstalledFromUrl", repo);
                    }
                }

                if (!hasValidCustomRepoUrl)
                {
                    try
                    {
                        foreach (var oldRepo in oldRepos)
                        {
                            _logger.LogInformation("* Removing old repo: {r}", oldRepo);
                            RemoveRepo(oldRepo);
                        }
                    }
                    finally
                    {
                        _logger.LogInformation("* Adding current repo: {r}", currentRepo);
                        AddRepo(currentRepo, true);
                    }
                }

                // This time do it for real, and crash the game if we fail, to avoid saving a broken state
                if (hasOldPluginRepoUrl)
                {
                    try
                    {
                        _logger.LogInformation("* Updating plugins");
                        foreach (var plugin in localPlugins)
                        {
                            var manifest = GetFoP(plugin.LocalPlugin, "manifest");
                            if (manifest == null)
                                throw new Exception("Plugin manifest is null");
                            var manifestFile = GetFoP(plugin.LocalPlugin, "manifestFile");
                            if (manifestFile == null)
                                throw new Exception("Plugin manifestFile is null");
                            var repo = GetFoP(manifest, "InstalledFromUrl");
                            if (((string)repo).IsNullOrEmpty())
                                throw new Exception("Plugin repo url is null or empty");
                            SetFoP(manifest, "InstalledFromUrl", currentRepo);
                            Call(manifest, "Save", [manifestFile, "RepoChange"]);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception while changing plugin install repo");
                        foreach (var oldRepo in oldRepos)
                        {
                            _logger.LogInformation("* Restoring old repo: {r}", oldRepo);
                            AddRepo(oldRepo, true);
                        }
                    }
                }

                if (!hasValidCustomRepoUrl || hasOldPluginRepoUrl)
                {
                    _logger.LogInformation("* Saving dalamud config");
                    SaveDalamudConfig();
                    _logger.LogInformation("* Reloading plugin masters");
                    ReloadPluginMasters();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in RepoChangeService");
            }
        }, default, 10, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Started RepoChangeService");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _logger.LogDebug("Stopping RepoChange Service");
        return Task.CompletedTask;
    }
}
