using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace MareSynchronos.UI;

public class AutoDetectUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly DalamudUtilService _dalamud;
    private readonly IObjectTable _objectTable;
    private readonly Services.AutoDetect.AutoDetectRequestService _requestService;
    private List<Services.Mediator.NearbyEntry> _entries = new();

    public AutoDetectUi(ILogger<AutoDetectUi> logger, MareMediator mediator,
        MareConfigService configService, DalamudUtilService dalamudUtilService, IObjectTable objectTable,
        Services.AutoDetect.AutoDetectRequestService requestService,
        PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "Umbra Nearby", performanceCollectorService)
    {
        _configService = configService;
        _dalamud = dalamudUtilService;
        _objectTable = objectTable;
        _requestService = requestService;

        Flags |= ImGuiWindowFlags.NoScrollbar;
        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(350, 220),
            MaximumSize = new Vector2(600, 600),
        };
    }

    public override bool DrawConditions()
    {
        // Visible when explicitly opened; allow drawing even if discovery is disabled to show hint
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

            var data = _entries.Count > 0 ? _entries : BuildLocalSnapshot(maxDist);
            foreach (var e in data)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(e.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(e.WorldId == 0 ? "-" : (_dalamud.WorldData.Value.TryGetValue(e.WorldId, out var w) ? w : e.WorldId.ToString()));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(float.IsNaN(e.Distance) ? "-" : $"{e.Distance:0.0} m");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(e.IsMatch ? "On Umbra" : "Unknown");
                ImGui.TableNextColumn();
                bool allowRequests = _configService.Current.AllowAutoDetectPairRequests;
                using (ImRaii.Disabled(!allowRequests || !e.IsMatch || string.IsNullOrEmpty(e.Token)))
                {
                    if (ImGui.Button($"Send request##{e.Name}"))
                    {
                        _ = _requestService.SendRequestAsync(e.Token!);
                    }
                }
                if (!allowRequests)
                {
                    UiSharedService.AttachToolTip("Enable 'Allow pair requests' in Settings to send a request.");
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
            list.Add(new Services.Mediator.NearbyEntry(name, worldId, dist, false, null));
        }
        return list;
    }
}
