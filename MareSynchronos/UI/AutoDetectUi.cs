using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Globalization;
using System.Text;
using MareSynchronos.Services;
using MareSynchronos.Services.AutoDetect;
using NotificationType = MareSynchronos.MareConfiguration.Models.NotificationType;

namespace MareSynchronos.UI;

public class AutoDetectUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamud;
    private readonly AutoDetectRequestService _requestService;
    private readonly NearbyPendingService _pendingService;
    private readonly PairManager _pairManager;
    private List<Services.Mediator.NearbyEntry> _entries = new();
    private readonly HashSet<string> _acceptInFlight = new(StringComparer.Ordinal);

    public AutoDetectUi(ILogger<AutoDetectUi> logger, MareMediator mediator,
        MareConfigService configService, DalamudUtilService dalamudUtilService,
        AutoDetectRequestService requestService, NearbyPendingService pendingService, PairManager pairManager,
        PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "AutoDetect", performanceCollectorService)
    {
        _configService = configService;
        _dalamud = dalamudUtilService;
        _requestService = requestService;
        _pendingService = pendingService;
        _pairManager = pairManager;

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

        using (ImRaii.Disabled(true))
        {
            DrawStyledTab("Syncshell", accent, inactiveTab, hoverTab, () =>
            {
                UiSharedService.ColorTextWrapped("Disponible prochainement.", ImGuiColors.DalamudGrey3);
            }, true);
        }
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

        // Table header
        if (ImGui.BeginTable("autodetect-nearby", 5, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("World");
            ImGui.TableSetupColumn("Distance");
            ImGui.TableSetupColumn("Status");
            ImGui.TableSetupColumn("Action");
            ImGui.TableHeadersRow();

            var data = _entries.Count > 0 ? _entries.Where(e => e.IsMatch).ToList() : new List<Services.Mediator.NearbyEntry>();
            foreach (var e in data)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(e.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(e.WorldId == 0 ? "-" : (_dalamud.WorldData.Value.TryGetValue(e.WorldId, out var w) ? w : e.WorldId.ToString()));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(float.IsNaN(e.Distance) ? "-" : $"{e.Distance:0.0} m");
                ImGui.TableNextColumn();
                bool alreadyPaired = IsAlreadyPairedByUidOrAlias(e);
                string status = alreadyPaired ? "Paired" : (string.IsNullOrEmpty(e.Token) ? "Requests disabled" : "On Umbra");
                ImGui.TextUnformatted(status);
                ImGui.TableNextColumn();
                using (ImRaii.Disabled(alreadyPaired || string.IsNullOrEmpty(e.Token)))
                {
                    if (alreadyPaired)
                    {
                        ImGui.Button($"Already sync##{e.Name}");
                    }
                    else if (string.IsNullOrEmpty(e.Token))
                    {
                        ImGui.Button($"Requests disabled##{e.Name}");
                    }
                    else if (ImGui.Button($"Send request##{e.Name}"))
                    {
                        _ = _requestService.SendRequestAsync(e.Token!, e.Uid, e.DisplayName);
                    }
                }
            }

            ImGui.EndTable();
        }
    }

    public override void OnOpen()
    {
        base.OnOpen();
        Mediator.Subscribe<Services.Mediator.DiscoveryListUpdated>(this, OnDiscoveryUpdated);
    }

    public override void OnClose()
    {
        Mediator.Unsubscribe<Services.Mediator.DiscoveryListUpdated>(this);
        base.OnClose();
    }

    private void OnDiscoveryUpdated(Services.Mediator.DiscoveryListUpdated msg)
    {
        _entries = msg.Entries;
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
