using System.Collections.Generic;
using UmbraSync.MareConfiguration.Models;
using UmbraSync.UI;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace UmbraSync.MareConfiguration.Configurations;

[Serializable]
public class MareConfig : IMareConfiguration
{
    public int ExpectedTOSVersion = 2;
    public int AcceptedTOSVersion { get; set; } = 0;
    public bool AcceptedAgreement { get; set; } = false;
    public string CacheFolder { get; set; } = string.Empty;
    public bool DisableOptionalPluginWarnings { get; set; } = false;
    public bool EnableDtrEntry { get; set; } = true;
    public int DtrStyle { get; set; } = 0;
    public bool ShowUidInDtrTooltip { get; set; } = true;
    public bool PreferNoteInDtrTooltip { get; set; } = false;
    public bool UseColorsInDtr { get; set; } = true;
    public DtrEntry.Colors DtrColorsDefault { get; set; } = default;
    public DtrEntry.Colors DtrColorsNotConnected { get; set; } = new(Glow: 0x0428FFu);
    public DtrEntry.Colors DtrColorsPairsInRange { get; set; } = new(Glow: 0x8D37C0u);
    public bool UseNameColors { get; set; } = false;
    public DtrEntry.Colors NameColors { get; set; } = new(Foreground: 0x67EBF5u, Glow: 0x00303Cu);
    public DtrEntry.Colors BlockedNameColors { get; set; } = new(Foreground: 0x8AADC7, Glow: 0x000080u);
    public bool EnableRightClickMenus { get; set; } = true;
    public NotificationLocation ErrorNotification { get; set; } = NotificationLocation.Both;
    public string ExportFolder { get; set; } = string.Empty;
    public bool FileScanPaused { get; set; } = false;
    public NotificationLocation InfoNotification { get; set; } = NotificationLocation.Toast;
    public bool InitialScanComplete { get; set; } = false;
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
    public bool LogPerformance { get; set; } = false;
    public bool LogEvents { get; set; } = true;
    public bool HoldCombatApplication { get; set; } = false;
    public double MaxLocalCacheInGiB { get; set; } = 100;
    public bool OpenGposeImportOnGposeStart { get; set; } = false;
    public bool OpenPopupOnAdd { get; set; } = true;
    public int ParallelDownloads { get; set; } = 10;
    public bool EnableDownloadQueue { get; set; } = false;
    public int DownloadSpeedLimitInBytes { get; set; } = 0;
    public DownloadSpeeds DownloadSpeedType { get; set; } = DownloadSpeeds.MBps;
    [Obsolete] public bool PreferNotesOverNamesForVisible { get; set; } = false;
    public float ProfileDelay { get; set; } = 1.5f;
    public bool ProfilePopoutRight { get; set; } = false;
    public bool ProfilesAllowNsfw { get; set; } = false;
    public bool ProfilesShow { get; set; } = false;
    public bool ShowSyncshellUsersInVisible { get; set; } = true;
    [Obsolete] public bool ShowCharacterNameInsteadOfNotesForVisible { get; set; } = false;
    public bool ShowCharacterNames { get; set; } = true;
    public bool ShowOfflineUsersSeparately { get; set; } = true;
    public bool ShowSyncshellOfflineUsersSeparately { get; set; } = true;
    public bool GroupUpSyncshells { get; set; } = true;
    public bool SerialApplication { get; set; } = false;
    public bool ShowOnlineNotifications { get; set; } = false;
    public bool ShowOnlineNotificationsOnlyForIndividualPairs { get; set; } = true;
    public bool ShowOnlineNotificationsOnlyForNamedPairs { get; set; } = false;
    public bool ShowTransferBars { get; set; } = true;
    public bool ShowTransferWindow { get; set; } = false;
    public bool ShowUploading { get; set; } = true;
    public bool ShowUploadingBigText { get; set; } = true;
    public bool ShowVisibleUsersSeparately { get; set; } = true;
    public string LastChangelogVersionSeen { get; set; } = string.Empty;
    public bool DefaultDisableSounds { get; set; } = false;
    public bool DefaultDisableAnimations { get; set; } = false;
    public bool DefaultDisableVfx { get; set; } = false;
    public Dictionary<string, SyncOverrideEntry> PairSyncOverrides { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, SyncOverrideEntry> GroupSyncOverrides { get; set; } = new(StringComparer.Ordinal);
    public bool EnableAutoDetectDiscovery { get; set; } = true;
    public bool AllowAutoDetectPairRequests { get; set; } = true;
    public int AutoDetectMaxDistanceMeters { get; set; } = 40;
    public int AutoDetectMuteMinutes { get; set; } = 5;
    public int TimeSpanBetweenScansInSeconds { get; set; } = 30;
    public int TransferBarsHeight { get; set; } = 12;
    public bool TransferBarsShowText { get; set; } = true;
    public int TransferBarsWidth { get; set; } = 250;
    public bool UseAlternativeFileUpload { get; set; } = false;
    public bool UseCompactor { get; set; } = false;
    public int Version { get; set; } = 1;
    public NotificationLocation WarningNotification { get; set; } = NotificationLocation.Both;

    public bool DisableSyncshellChat { get; set; } = false;
    public int ChatColor { get; set; } = 0; // 0 means "use plugin default"
    public int ChatLogKind { get; set; } = 1; // XivChatType.Debug
    public bool ExtraChatAPI { get; set; } = false;
    public bool ExtraChatTags { get; set; } = false;
    public bool TypingIndicatorShowOnNameplates { get; set; } = true;
    public bool TypingIndicatorShowOnPartyList { get; set; } = true;
    public bool TypingIndicatorEnabled { get; set; } = true;
    public bool TypingIndicatorShowSelf { get; set; } = true;
    public TypingIndicatorBubbleSize TypingIndicatorBubbleSize { get; set; } = TypingIndicatorBubbleSize.Large;

    public bool UmbraAPI { get; set; } = true;

    [Obsolete]
    [JsonPropertyName("MareAPI")]
    public bool MareAPI { get => UmbraAPI; set => UmbraAPI = value; }
}
