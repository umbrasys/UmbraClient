using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Globalization;
using System.Text;

namespace MareSynchronos.UI;

public class AutoDetectUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamud;
    private readonly IObjectTable _objectTable;
    private readonly Services.AutoDetect.AutoDetectRequestService _requestService;
    private readonly PairManager _pairManager;
    private List<Services.Mediator.NearbyEntry> _entries = new();

    public AutoDetectUi(ILogger<AutoDetectUi> logger, MareMediator mediator,
        MareConfigService configService, DalamudUtilService dalamudUtilService, IObjectTable objectTable,
        Services.AutoDetect.AutoDetectRequestService requestService, PairManager pairManager,
        PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "AutoDetect", performanceCollectorService)
    {
        _configService = configService;
        _dalamud = dalamudUtilService;
        _objectTable = objectTable;
        _requestService = requestService;
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

        if (!_configService.Current.EnableAutoDetectDiscovery)
        {
            UiSharedService.ColorTextWrapped("Nearby detection is disabled. Enable it in Settings to start detecting nearby Umbra users.", ImGuiColors.DalamudYellow);
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
                        _ = _requestService.SendRequestAsync(e.Token!);
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

    private List<Services.Mediator.NearbyEntry> BuildLocalSnapshot(int maxDist)
    {
        var list = new List<Services.Mediator.NearbyEntry>();
        var local = _dalamud.GetPlayerCharacter();
        var localPos = local?.Position ?? Vector3.Zero;
        for (int i = 0; i < 200; i += 2)
        {
            var obj = _objectTable[i];
            if (obj == null || obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;
            if (local != null && obj.Address == local.Address) continue;
            float dist = local == null ? float.NaN : Vector3.Distance(localPos, obj.Position);
            if (!float.IsNaN(dist) && dist > maxDist) continue;
            string name = obj.Name.ToString();
            ushort worldId = 0;
            if (obj is IPlayerCharacter pc) worldId = (ushort)pc.HomeWorld.RowId;
            list.Add(new Services.Mediator.NearbyEntry(name, worldId, dist, false, null, null, null));
        }
        return list;
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
}
