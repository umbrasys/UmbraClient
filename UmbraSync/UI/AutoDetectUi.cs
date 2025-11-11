using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using UmbraSync.API.Dto.Group;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Pairs;
using UmbraSync.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Globalization;
using System.Text;
using UmbraSync.Services;
using UmbraSync.Services.AutoDetect;
using NotificationType = UmbraSync.MareConfiguration.Models.NotificationType;

namespace UmbraSync.UI;

public class AutoDetectUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamud;
    private readonly AutoDetectRequestService _requestService;
    private readonly NearbyDiscoveryService _discoveryService;
    private readonly NearbyPendingService _pendingService;
    private readonly PairManager _pairManager;
    private List<Services.Mediator.NearbyEntry> _entries;
    private readonly HashSet<string> _acceptInFlight = new(StringComparer.Ordinal);
    private readonly SyncshellDiscoveryService _syncshellDiscoveryService;
    private List<SyncshellDiscoveryEntryDto> _syncshellEntries = [];
    private bool _syncshellInitialized;
    private readonly HashSet<string> _syncshellJoinInFlight = new(StringComparer.OrdinalIgnoreCase);
    private string? _syncshellLastError;

    public AutoDetectUi(ILogger<AutoDetectUi> logger, MareMediator mediator,
        MareConfigService configService, DalamudUtilService dalamudUtilService,
        AutoDetectRequestService requestService, NearbyPendingService pendingService, PairManager pairManager,
        NearbyDiscoveryService discoveryService, SyncshellDiscoveryService syncshellDiscoveryService,
        PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "AutoDetect", performanceCollectorService)
    {
        _configService = configService;
        _dalamud = dalamudUtilService;
        _requestService = requestService;
        _pendingService = pendingService;
        _pairManager = pairManager;
        _discoveryService = discoveryService;
        _syncshellDiscoveryService = syncshellDiscoveryService;
        Mediator.Subscribe<Services.Mediator.DiscoveryListUpdated>(this, OnDiscoveryUpdated);
        Mediator.Subscribe<SyncshellDiscoveryUpdated>(this, OnSyncshellDiscoveryUpdated);
        _entries = _discoveryService.SnapshotEntries();

        Flags |= ImGuiWindowFlags.NoScrollbar;
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(350, 220),
            MaximumSize = new Vector2(600, 600),
        };
    }

    public override bool DrawConditions()
    {
        return true;
    }

    protected override void DrawInternal()
    {
        using var idScope = ImRaii.PushId("autodetect-ui");

        var incomingInvites = _pendingService.Pending.ToList();
        var outgoingInvites = _requestService.GetPendingRequestsSnapshot();

        Vector4 accent = UiSharedService.AccentColor;
        if (accent.W <= 0f) accent = ImGuiColors.ParsedPurple;
        Vector4 inactiveTab = new(accent.X * 0.45f, accent.Y * 0.45f, accent.Z * 0.45f, Math.Clamp(accent.W + 0.15f, 0f, 1f));
        Vector4 hoverTab = UiSharedService.AccentHoverColor;

        using var tabs = ImRaii.TabBar("AutoDetectTabs");
        if (!tabs.Success) return;

        var incomingCount = incomingInvites.Count;
        DrawStyledTab($"Invitations ({incomingCount})", accent, inactiveTab, hoverTab, () =>
        {
            DrawInvitationsTab(incomingInvites, outgoingInvites);
        });

        DrawStyledTab("Proximité", accent, inactiveTab, hoverTab, DrawNearbyTab);
        DrawStyledTab("Syncshell", accent, inactiveTab, hoverTab, DrawSyncshellTab);
    }

    public void DrawInline()
    {
        DrawInternal();
    }

    private static void DrawStyledTab(string label, Vector4 accent, Vector4 inactive, Vector4 hover, Action draw, bool disabled = false)
    {
        var tabColor = disabled ? ImGuiColors.DalamudGrey3 : inactive;
        var tabHover = disabled ? ImGuiColors.DalamudGrey3 : hover;
        var tabActive = disabled ? ImGuiColors.DalamudGrey2 : accent;
        using var baseColor = ImRaii.PushColor(ImGuiCol.Tab, tabColor);
        using var hoverColor = ImRaii.PushColor(ImGuiCol.TabHovered, tabHover);
        using var activeColor = ImRaii.PushColor(ImGuiCol.TabActive, tabActive);
        using var activeText = ImRaii.PushColor(ImGuiCol.Text, disabled ? ImGuiColors.DalamudGrey2 : Vector4.One, false);
        using var tab = ImRaii.TabItem(label);
        if (tab.Success)
        {
            draw();
        }
    }

    private void DrawInvitationsTab(List<KeyValuePair<string, string>> incomingInvites, IReadOnlyCollection<AutoDetectRequestService.PendingRequestInfo> outgoingInvites)
    {
        if (incomingInvites.Count == 0 && outgoingInvites.Count == 0)
        {
            UiSharedService.ColorTextWrapped("Aucune invitation en attente. Cette page regroupera les demandes reçues et celles que vous avez envoyées.", ImGuiColors.DalamudGrey3);
            return;
        }

        if (incomingInvites.Count == 0)
        {
            UiSharedService.ColorTextWrapped("Vous n'avez aucune invitation de pair en attente pour le moment.", ImGuiColors.DalamudGrey3);
        }

        ImGuiHelpers.ScaledDummy(4);
        float leftWidth = Math.Max(220f * ImGuiHelpers.GlobalScale, ImGui.CalcTextSize("Invitations reçues (00)").X + ImGui.GetStyle().FramePadding.X * 4f);
        var avail = ImGui.GetContentRegionAvail();

        ImGui.BeginChild("incoming-requests", new Vector2(leftWidth, avail.Y), true);
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"Invitations reçues ({incomingInvites.Count})");
        ImGui.Separator();
        if (incomingInvites.Count == 0)
        {
            ImGui.TextDisabled("Aucune invitation reçue.");
        }
        else
        {
            foreach (var (uid, name) in incomingInvites.OrderBy(k => k.Value, StringComparer.OrdinalIgnoreCase))
            {
                using var id = ImRaii.PushId(uid);
                bool processing = _acceptInFlight.Contains(uid);
                ImGui.TextUnformatted(name);
                ImGui.TextDisabled(uid);
                if (processing)
                {
                    ImGui.TextDisabled("Traitement en cours...");
                }
                else
                {
                    if (ImGui.Button("Accepter"))
                    {
                        TriggerAccept(uid);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Refuser"))
                    {
                        _pendingService.Remove(uid);
                    }
                }
                ImGui.Separator();
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("outgoing-requests", new Vector2(0, avail.Y), true);
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"Invitations envoyées ({outgoingInvites.Count})");
        ImGui.Separator();
        if (outgoingInvites.Count == 0)
        {
            ImGui.TextDisabled("Aucune invitation envoyée en attente.");
            ImGui.EndChild();
            return;
        }

        foreach (var info in outgoingInvites.OrderByDescending(i => i.SentAt))
        {
            using var id = ImRaii.PushId(info.Key);
            ImGui.TextUnformatted(info.TargetDisplayName);
            if (!string.IsNullOrEmpty(info.Uid))
            {
                ImGui.TextDisabled(info.Uid);
            }

            ImGui.TextDisabled($"Envoyée il y a {FormatDuration(DateTime.UtcNow - info.SentAt)}");
            if (ImGui.Button("Retirer"))
            {
                _requestService.RemovePendingRequestByKey(info.Key);
            }
            UiSharedService.AttachToolTip("Retire uniquement cette entrée locale de suivi.");
            ImGui.Separator();
        }

        ImGui.EndChild();
    }

    private void DrawNearbyTab()
    {
        if (!_configService.Current.EnableAutoDetectDiscovery)
        {
            UiSharedService.ColorTextWrapped("AutoDetect est désactivé. Activez-le dans les paramètres pour détecter les utilisateurs Umbra à proximité.", ImGuiColors.DalamudYellow);
            ImGuiHelpers.ScaledDummy(6);
        }

        int maxDist = Math.Clamp(_configService.Current.AutoDetectMaxDistanceMeters, 5, 100);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Max distance (m)");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("##autodetect-dist", ref maxDist, 5, 100))
        {
            _configService.Current.AutoDetectMaxDistanceMeters = maxDist;
            _configService.Save();
        }

        ImGuiHelpers.ScaledDummy(6);

        var sourceEntries = _entries.Count > 0 ? _entries : _discoveryService.SnapshotEntries();
        var orderedEntries = sourceEntries
            .Where(e => e.IsMatch)
            .OrderBy(e => float.IsNaN(e.Distance) ? float.MaxValue : e.Distance)
            .ToList();

        if (orderedEntries.Count == 0)
        {
            UiSharedService.ColorTextWrapped("Aucune présence UmbraSync détectée à proximité pour le moment.", ImGuiColors.DalamudGrey3);
            return;
        }

        if (!ImGui.BeginTable("autodetect-nearby", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Nom");
        ImGui.TableSetupColumn("Monde");
        ImGui.TableSetupColumn("Distance");
        ImGui.TableSetupColumn("Statut");
        ImGui.TableSetupColumn("Action");
        ImGui.TableHeadersRow();

        for (int i = 0; i < orderedEntries.Count; i++)
        {
            var entry = orderedEntries[i];
            bool alreadyPaired = IsAlreadyPairedByUidOrAlias(entry);
            bool overDistance = !float.IsNaN(entry.Distance) && entry.Distance > maxDist;
            bool canRequest = entry.AcceptPairRequests && !string.IsNullOrEmpty(entry.Token) && !alreadyPaired;

            string displayName = entry.DisplayName ?? entry.Name;
            string worldName = entry.WorldId == 0
                ? "-"
                : (_dalamud.WorldData.Value.TryGetValue(entry.WorldId, out var mappedWorld) ? mappedWorld : entry.WorldId.ToString(CultureInfo.InvariantCulture));
            string distanceText = float.IsNaN(entry.Distance) ? "-" : $"{entry.Distance:0.0} m";

            string status = alreadyPaired
                ? "Déjà appairé"
                : overDistance
                    ? $"Hors portée (> {maxDist} m)"
                    : !entry.AcceptPairRequests
                        ? "Invitations refusées"
                        : string.IsNullOrEmpty(entry.Token)
                            ? "Indisponible"
                            : "Disponible";

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(displayName);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(worldName);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(distanceText);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(status);

            ImGui.TableNextColumn();
            using (ImRaii.PushId(i))
            {
                if (canRequest && !overDistance)
                {
                    if (ImGui.Button("Envoyer invitation"))
                    {
                        _ = _requestService.SendRequestAsync(entry.Token!, entry.Uid, entry.DisplayName);
                    }
                    UiSharedService.AttachToolTip("Envoie une demande d'appairage via AutoDetect.");
                }
                else
                {
                    string reason = alreadyPaired
                        ? "Vous êtes déjà appairé avec ce joueur."
                        : overDistance
                            ? $"Ce joueur est au-delà de la distance maximale configurée ({maxDist} m)."
                            : !entry.AcceptPairRequests
                                ? "Ce joueur a désactivé la réception automatique des invitations."
                                : string.IsNullOrEmpty(entry.Token)
                                    ? "Impossible d'obtenir un jeton d'invitation pour ce joueur."
                                    : string.Empty;

                    ImGui.TextDisabled(status);
                    if (!string.IsNullOrEmpty(reason))
                    {
                        UiSharedService.AttachToolTip(reason);
                    }
                }
            }
        }

        ImGui.EndTable();
    }

    private async Task JoinSyncshellAsync(SyncshellDiscoveryEntryDto entry)
    {
        if (!_syncshellJoinInFlight.Add(entry.GID))
        {
            return;
        }

        try
        {
            var joined = await _syncshellDiscoveryService.JoinAsync(entry.GID, CancellationToken.None).ConfigureAwait(false);
            if (joined)
            {
                Mediator.Publish(new NotificationMessage("AutoDetect Syncshell", $"Rejoint {entry.Alias ?? entry.GID}.", NotificationType.Info, TimeSpan.FromSeconds(5)));
                await _syncshellDiscoveryService.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                _syncshellLastError = $"Impossible de rejoindre {entry.Alias ?? entry.GID}.";
                Mediator.Publish(new NotificationMessage("AutoDetect Syncshell", _syncshellLastError, NotificationType.Warning, TimeSpan.FromSeconds(5)));
            }
        }
        catch (Exception ex)
        {
            _syncshellLastError = $"Erreur lors de l'adhésion : {ex.Message}";
            Mediator.Publish(new NotificationMessage("AutoDetect Syncshell", _syncshellLastError, NotificationType.Error, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            _syncshellJoinInFlight.Remove(entry.GID);
        }
    }

    private void DrawSyncshellTab()
    {
        if (!_syncshellInitialized)
        {
            _syncshellInitialized = true;
            _ = _syncshellDiscoveryService.RefreshAsync(CancellationToken.None);
        }

        bool isRefreshing = _syncshellDiscoveryService.IsRefreshing;
        var serviceError = _syncshellDiscoveryService.LastError;

        if (ImGui.Button("Actualiser la liste"))
        {
            _ = _syncshellDiscoveryService.RefreshAsync(CancellationToken.None);
        }
        UiSharedService.AttachToolTip("Met à jour la liste des Syncshells ayant activé l'AutoDetect.");

        if (isRefreshing)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("Actualisation...");
        }

        ImGuiHelpers.ScaledDummy(4);
        UiSharedService.TextWrapped("Les Syncshells affichées ont temporairement désactivé leur mot de passe pour permettre un accès direct via AutoDetect. Rejoignez-les uniquement si vous faites confiance aux administrateurs.");

        if (!string.IsNullOrEmpty(serviceError))
        {
            UiSharedService.ColorTextWrapped(serviceError, ImGuiColors.DalamudRed);
        }
        else if (!string.IsNullOrEmpty(_syncshellLastError))
        {
            UiSharedService.ColorTextWrapped(_syncshellLastError!, ImGuiColors.DalamudOrange);
        }

        var entries = _syncshellEntries.Count > 0 ? _syncshellEntries : _syncshellDiscoveryService.Entries.ToList();
        if (entries.Count == 0)
        {
            ImGuiHelpers.ScaledDummy(4);
            UiSharedService.ColorTextWrapped("Aucune Syncshell n'est actuellement visible dans AutoDetect.", ImGuiColors.DalamudGrey3);
            return;
        }

        if (!ImGui.BeginTable("autodetect-syncshells", 5, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Nom");
        ImGui.TableSetupColumn("Propriétaire");
        ImGui.TableSetupColumn("Membres");
        ImGui.TableSetupColumn("Invitations");
        ImGui.TableSetupColumn("Action");
        ImGui.TableHeadersRow();

        foreach (var entry in entries.OrderBy(e => e.Alias ?? e.GID, StringComparer.OrdinalIgnoreCase))
        {
            bool alreadyMember = _pairManager.Groups.Keys.Any(g => string.Equals(g.GID, entry.GID, StringComparison.OrdinalIgnoreCase));
            bool joining = _syncshellJoinInFlight.Contains(entry.GID);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(string.IsNullOrEmpty(entry.Alias) ? entry.GID : $"{entry.Alias} ({entry.GID})");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(string.IsNullOrEmpty(entry.OwnerAlias) ? entry.OwnerUID : $"{entry.OwnerAlias} ({entry.OwnerUID})");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.MemberCount.ToString(CultureInfo.InvariantCulture));

            ImGui.TableNextColumn();
            string inviteMode = entry.AutoAcceptPairs ? "Auto" : "Manuel";
            ImGui.TextUnformatted(inviteMode);
            if (!entry.AutoAcceptPairs)
            {
                UiSharedService.AttachToolTip("L'administrateur doit approuver manuellement les nouveaux membres.");
            }

            ImGui.TableNextColumn();
            using (ImRaii.Disabled(alreadyMember || joining))
            {
                if (alreadyMember)
                {
                    ImGui.TextDisabled("Déjà membre");
                }
                else if (joining)
                {
                    ImGui.TextDisabled("Connexion...");
                }
                else if (ImGui.Button("Rejoindre"))
                {
                    _syncshellLastError = null;
                    _ = JoinSyncshellAsync(entry);
                }
            }
        }

        ImGui.EndTable();
    }

    private void OnDiscoveryUpdated(Services.Mediator.DiscoveryListUpdated msg)
    {
        _entries = msg.Entries;
    }

    private void OnSyncshellDiscoveryUpdated(SyncshellDiscoveryUpdated msg)
    {
        _syncshellEntries = msg.Entries;
    }

    private bool IsAlreadyPairedByUidOrAlias(Services.Mediator.NearbyEntry e)
    {
        try
        {
            // 1) Match by UID when available (authoritative)
            if (!string.IsNullOrEmpty(e.Uid))
            {
                foreach (var p in _pairManager.DirectPairs)
                {
                    if (string.Equals(p.UserData.UID, e.Uid, StringComparison.Ordinal))
                        return true;
                }
            }
            var key = NormalizeKey(e.DisplayName ?? e.Name);
            if (string.IsNullOrEmpty(key)) return false;
            foreach (var p in _pairManager.DirectPairs)
            {
                if (NormalizeKey(p.UserData.AliasOrUID) == key) return true;
                if (!string.IsNullOrEmpty(p.UserData.Alias) && NormalizeKey(p.UserData.Alias!) == key) return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static string NormalizeKey(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var formD = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private void TriggerAccept(string uid)
    {
        if (!_acceptInFlight.Add(uid)) return;

        Task.Run(async () =>
        {
            try
            {
                bool ok = await _pendingService.AcceptAsync(uid).ConfigureAwait(false);
                if (!ok)
                {
                    Mediator.Publish(new NotificationMessage("AutoDetect", $"Impossible d'accepter l'invitation {uid}.", NotificationType.Warning, TimeSpan.FromSeconds(5)));
                }
            }
            finally
            {
                _acceptInFlight.Remove(uid);
            }
        });
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalMinutes >= 1)
        {
            var minutes = Math.Max(1, (int)Math.Round(span.TotalMinutes));
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }

        var seconds = Math.Max(1, (int)Math.Round(span.TotalSeconds));
        return seconds == 1 ? "1 seconde" : $"{seconds} secondes";
    }
}
