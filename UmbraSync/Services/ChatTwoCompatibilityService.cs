using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;
using UmbraSync.MareConfiguration.Models;
using UmbraSync.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UmbraSync.Services;

public sealed class ChatTwoCompatibilityService : MediatorSubscriberBase, IHostedService
{
    private const string ChatTwoInternalName = "ChatTwo";
    private readonly IDalamudPluginInterface _pluginInterface;
    private bool _warningShown;

    public ChatTwoCompatibilityService(ILogger<ChatTwoCompatibilityService> logger, IDalamudPluginInterface pluginInterface, MareMediator mediator)
        : base(logger, mediator)
    {
        _pluginInterface = pluginInterface;

        Mediator.SubscribeKeyed<PluginChangeMessage>(this, ChatTwoInternalName, OnChatTwoStateChanged);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var initialState = PluginWatcherService.GetInitialPluginState(_pluginInterface, ChatTwoInternalName);
            if (initialState?.IsLoaded == true)
            {
                ShowWarning();
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to inspect ChatTwo initial state");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Mediator.UnsubscribeAll(this);
        return Task.CompletedTask;
    }

    private void OnChatTwoStateChanged(PluginChangeMessage message)
    {
        if (message.IsLoaded)
        {
            ShowWarning();
        }
    }

    private void ShowWarning()
    {
        if (_warningShown) return;
        _warningShown = true;

        const string warningTitle = "ChatTwo détecté";
        const string warningBody = "Actuellement, le plugin ChatTwo n'est pas compatible avec la bulle d'écriture d'UmbraSync. Désactivez ChatTwo si vous souhaitez conserver l'indicateur de saisie.";

        Mediator.Publish(new NotificationMessage(warningTitle, warningBody, NotificationType.Warning, TimeSpan.FromSeconds(10)));
    }
}
