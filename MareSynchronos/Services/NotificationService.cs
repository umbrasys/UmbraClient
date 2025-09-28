using System;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotificationType = MareSynchronos.MareConfiguration.Models.NotificationType;

namespace MareSynchronos.Services;

public class NotificationService : DisposableMediatorSubscriberBase, IHostedService
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly INotificationManager _notificationManager;
    private readonly IChatGui _chatGui;
    private readonly MareConfigService _configurationService;

    public NotificationService(ILogger<NotificationService> logger, MareMediator mediator,
        DalamudUtilService dalamudUtilService,
        INotificationManager notificationManager,
        IChatGui chatGui, MareConfigService configurationService) : base(logger, mediator)
    {
        _dalamudUtilService = dalamudUtilService;
        _notificationManager = notificationManager;
        _chatGui = chatGui;
        _configurationService = configurationService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Mediator.Subscribe<NotificationMessage>(this, ShowNotification);
        Mediator.Subscribe<DualNotificationMessage>(this, ShowDualNotification);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void PrintErrorChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[UmbraSync] Error: " + message);
        _chatGui.PrintError(se.BuiltString);
    }

    private void PrintInfoChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[UmbraSync] Info: ").AddItalics(message ?? string.Empty);
        _chatGui.Print(se.BuiltString);
    }

    private void PrintWarnChat(string? message)
    {
        SeStringBuilder se = new SeStringBuilder().AddText("[UmbraSync] ").AddUiForeground("Warning: " + (message ?? string.Empty), 31).AddUiForegroundOff();
        _chatGui.Print(se.BuiltString);
    }

    private void ShowChat(NotificationMessage msg)
    {
        switch (msg.Type)
        {
            case NotificationType.Info:
                PrintInfoChat(msg.Message);
                break;

            case NotificationType.Warning:
                PrintWarnChat(msg.Message);
                break;

            case NotificationType.Error:
                PrintErrorChat(msg.Message);
                break;
        }
    }

    private void ShowNotification(NotificationMessage msg)
    {
        Logger.LogInformation("{msg}", msg.ToString());

        if (!_dalamudUtilService.IsLoggedIn) return;

        bool appendInstruction;
        bool forceChat = ShouldForceChat(msg, out appendInstruction);
        var effectiveMessage = forceChat && appendInstruction ? AppendUsyncInstruction(msg.Message) : msg.Message;
        var adjustedMsg = forceChat && appendInstruction ? msg with { Message = effectiveMessage } : msg;

        switch (adjustedMsg.Type)
        {
            case NotificationType.Info:
                ShowNotificationLocationBased(adjustedMsg, _configurationService.Current.InfoNotification, forceChat);
                break;

            case NotificationType.Warning:
                ShowNotificationLocationBased(adjustedMsg, _configurationService.Current.WarningNotification, forceChat);
                break;

            case NotificationType.Error:
                ShowNotificationLocationBased(adjustedMsg, _configurationService.Current.ErrorNotification, forceChat);
                break;
        }
    }

    private void ShowDualNotification(DualNotificationMessage message)
    {
        if (!_dalamudUtilService.IsLoggedIn) return;

        var baseMsg = new NotificationMessage(message.Title, message.Message, message.Type, message.ToastDuration);
        ShowToast(baseMsg);
        ShowChat(baseMsg);
    }

    private static bool ShouldForceChat(NotificationMessage msg, out bool appendInstruction)
    {
        appendInstruction = false;

        bool IsNearbyRequestText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.Contains("Nearby request", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Nearby Request", StringComparison.Ordinal);
        }

        bool IsNearbyAcceptText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.Contains("Nearby Accept", StringComparison.OrdinalIgnoreCase);
        }

        bool isAccept = IsNearbyAcceptText(msg.Title) || IsNearbyAcceptText(msg.Message);
        if (isAccept)
            return false;

        bool isRequest = IsNearbyRequestText(msg.Title) || IsNearbyRequestText(msg.Message);
        if (isRequest)
        {
            appendInstruction = !IsRequestSentConfirmation(msg);
            return true;
        }

        return false;
    }

    private static bool IsRequestSentConfirmation(NotificationMessage msg)
    {
        if (string.Equals(msg.Title, "Nearby request sent", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(msg.Message) && msg.Message.Contains("The other user will receive a request notification.", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string AppendUsyncInstruction(string? message)
    {
        const string suffix = " | Ouvrez /usync pour voir l'invitation.";
        if (string.IsNullOrWhiteSpace(message))
            return suffix.TrimStart(' ', '|');

        if (message.Contains("/usync", StringComparison.OrdinalIgnoreCase))
            return message;

        return message.TrimEnd() + suffix;
    }

    private void ShowNotificationLocationBased(NotificationMessage msg, NotificationLocation location, bool forceChat)
    {
        bool showToast = location is NotificationLocation.Toast or NotificationLocation.Both;
        bool showChat = forceChat || location is NotificationLocation.Chat or NotificationLocation.Both;

        if (showToast)
        {
            ShowToast(msg);
        }

        if (showChat)
        {
            ShowChat(msg);
        }
    }

    private void ShowToast(NotificationMessage msg)
    {
        Dalamud.Interface.ImGuiNotification.NotificationType dalamudType = msg.Type switch
        {
            NotificationType.Error => Dalamud.Interface.ImGuiNotification.NotificationType.Error,
            NotificationType.Warning => Dalamud.Interface.ImGuiNotification.NotificationType.Warning,
            NotificationType.Info => Dalamud.Interface.ImGuiNotification.NotificationType.Info,
            _ => Dalamud.Interface.ImGuiNotification.NotificationType.Info
        };

        _notificationManager.AddNotification(new Notification()
        {
            Content = msg.Message ?? string.Empty,
            Title = msg.Title,
            Type = dalamudType,
            Minimized = false,
            InitialDuration = msg.TimeShownOnScreen ?? TimeSpan.FromSeconds(3)
        });
    }
}
