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

    public AutoDetectUi(ILogger<AutoDetectUi> logger, MareMediator mediator,
        MareConfigService configService, DalamudUtilService dalamudUtilService, IObjectTable objectTable,
        PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, "Umbra Nearby", performanceCollectorService)
    {
        _configService = configService;
        _dalamud = dalamudUtilService;
        _objectTable = objectTable;

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
        using var _ = ImRaii.PushId("autosync-ui");

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
        if (ImGui.BeginTable("autosync-nearby", 3, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("World");
            ImGui.TableSetupColumn("Distance");
            ImGui.TableHeadersRow();

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
                if (obj is IPlayerCharacter pc)
                {
                    worldId = (ushort)pc.HomeWorld.RowId;
                }
                string world = worldId == 0 ? "-" : (_dalamud.WorldData.Value.TryGetValue(worldId, out var w) ? w : worldId.ToString());

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(world);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(float.IsNaN(dist) ? "-" : $"{dist:0.0} m");
            }

            ImGui.EndTable();
        }
    }
}