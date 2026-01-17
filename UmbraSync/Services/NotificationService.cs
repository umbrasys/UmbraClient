using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UmbraSync.Localization;
using UmbraSync.MareConfiguration;
using UmbraSync.MareConfiguration.Models;
using UmbraSync.Services.Mediator;
using DalamudNotification = Dalamud.Interface.ImGuiNotification.Notification;
using DalamudNotificationType = Dalamud.Interface.ImGuiNotification.NotificationType;
using NotificationType = UmbraSync.MareConfiguration.Models.NotificationType;

namespace UmbraSync.Services;

public class NotificationService : DisposableMediatorSubscriberBase, IHostedService
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly INotificationManager _notificationManager;
    private readonly IChatGui _chatGui;
    private readonly MareConfigService _configurationService;
    private readonly Services.Notification.NotificationTracker _notificationTracker;
    private readonly PlayerData.Pairs.PairManager _pairManager;

    public NotificationService(ILogger<NotificationService> logger, MareMediator mediator,
        DalamudUtilService dalamudUtilService,
        INotificationManager notificationManager,
        IChatGui chatGui, MareConfigService configurationService,
        Services.Notification.NotificationTracker notificationTracker,
        PlayerData.Pairs.PairManager pairManager) : base(logger, mediator)
    {
        _dalamudUtilService = dalamudUtilService;
        _notificationManager = notificationManager;
        _chatGui = chatGui;
        _configurationService = configurationService;
        _notificationTracker = notificationTracker;
        _pairManager = pairManager;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Mediator.Subscribe<NotificationMessage>(this, ShowNotification);
        Mediator.Subscribe<DualNotificationMessage>(this, ShowDualNotification);
        Mediator.Subscribe<Services.Mediator.SyncshellAutoDetectStateChanged>(this, OnSyncshellAutoDetectStateChanged);
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
        var effectiveMessage = forceChat && appendInstruction ? AppendAutoDetectInstruction(msg.Message) : msg.Message;
        var adjustedMsg = forceChat && appendInstruction ? msg with { Message = effectiveMessage } : msg;

        var suppressToast = IsAutoDetectPairRequest(adjustedMsg);

        switch (adjustedMsg.Type)
        {
            case NotificationType.Info:
                ShowNotificationLocationBased(adjustedMsg, _configurationService.Current.InfoNotification, forceChat, suppressToast);
                break;

            case NotificationType.Warning:
                ShowNotificationLocationBased(adjustedMsg, _configurationService.Current.WarningNotification, forceChat, suppressToast);
                break;

            case NotificationType.Error:
                ShowNotificationLocationBased(adjustedMsg, _configurationService.Current.ErrorNotification, forceChat, suppressToast);
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

    private void OnSyncshellAutoDetectStateChanged(SyncshellAutoDetectStateChanged msg)
    {
        try
        {
            if (msg.Visible) return; // only handle transition to not visible

            var gid = msg.Gid;
            // Try to resolve alias from PairManager snapshot; fallback to gid
            var alias = _pairManager.Groups.Values.FirstOrDefault(g => string.Equals(g.GID, gid, StringComparison.OrdinalIgnoreCase))?.GroupAliasOrGID ?? gid;

            var title = $"Syncshell non publique: {alias}";
            var message = "La Syncshell n'est plus visible via AutoDetect.";

            // Show toast + chat
            ShowDualNotification(new DualNotificationMessage(title, message, NotificationType.Info, TimeSpan.FromSeconds(4)));

            // Persist into notification center
            _notificationTracker.Upsert(Services.Notification.NotificationEntry.SyncshellNotPublic(gid, alias));
        }
        catch
        {
            // ignore failures
        }
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

    private static string AppendAutoDetectInstruction(string? message)
    {
        return message ?? string.Empty;
    }

    private void ShowNotificationLocationBased(NotificationMessage msg, NotificationLocation location, bool forceChat, bool suppressToast)
    {
        bool showToast = !suppressToast && location is NotificationLocation.Toast or NotificationLocation.Both;
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
        DalamudNotificationType dalamudType = msg.Type switch
        {
            NotificationType.Error => DalamudNotificationType.Error,
            NotificationType.Warning => DalamudNotificationType.Warning,
            NotificationType.Info => DalamudNotificationType.Info,
            _ => DalamudNotificationType.Info
        };

        _notificationManager.AddNotification(new DalamudNotification()
        {
            Content = msg.Message,
            Title = msg.Title,
            Type = dalamudType,
            Minimized = false,
            InitialDuration = msg.TimeShownOnScreen ?? TimeSpan.FromSeconds(3)
        });
    }

    private static bool IsAutoDetectPairRequest(NotificationMessage msg)
    {
        if (msg.Type != NotificationType.Info) return false;
        var incomingTitle = Loc.Get("AutoDetect.Notification.IncomingTitle");
        return string.Equals(msg.Title, incomingTitle, StringComparison.Ordinal);
    }
}