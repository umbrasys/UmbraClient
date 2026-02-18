using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Globalization;
using UmbraSync.Localization;

namespace UmbraSync.UI;

public sealed partial class CharaDataHubUi
{
    private string _housingShareDescription = string.Empty;
    private bool _housingShareInitialized;

    private void DrawHousingShare()
    {
        if (!_uiSharedService.ApiController.IsConnected)
        {
            ImGuiHelpers.ScaledDummy(5);
            UiSharedService.DrawGroupedCenteredColorText(Loc.Get("HousingShare.ServerRequired"), UiSharedService.AccentColor);
            ImGuiHelpers.ScaledDummy(5);
            return;
        }

        var housingShareManager = _housingShareManager_housing;
        var scanner = _housingScanner;
        if (housingShareManager == null || scanner == null) return;

        if (!_housingShareInitialized && !housingShareManager.IsBusy)
        {
            _housingShareInitialized = true;
            _ = housingShareManager.RefreshAsync();
        }

        _uiSharedService.BigText(Loc.Get("HousingShare.Title"));

        if (housingShareManager.IsBusy)
        {
            UiSharedService.ColorTextWrapped(Loc.Get("HousingShare.Processing"), ImGuiColors.DalamudYellow);
        }
        if (!string.IsNullOrEmpty(housingShareManager.LastError))
        {
            UiSharedService.ColorTextWrapped(housingShareManager.LastError!, ImGuiColors.DalamudRed);
        }
        else if (!string.IsNullOrEmpty(housingShareManager.LastSuccess))
        {
            UiSharedService.ColorTextWrapped(housingShareManager.LastSuccess!, ImGuiColors.HealerGreen);
        }

        ImGuiHelpers.ScaledDummy(5);

        var currentLocation = _dalamudUtilService.GetMapDataAsync().GetAwaiter().GetResult();
        bool isInHousing = currentLocation.HouseId != 0 && _dalamudUtilService.IsInHousingMode;

        if (!isInHousing)
        {
            UiSharedService.ColorTextWrapped(Loc.Get("HousingShare.NotInHousing"), ImGuiColors.DalamudGrey3);
        }
        else
        {
            ImGui.TextUnformatted(string.Format(CultureInfo.CurrentCulture, Loc.Get("HousingShare.ServerInfo"),
                currentLocation.ServerId, currentLocation.TerritoryId, currentLocation.WardId, currentLocation.HouseId));
            ImGuiHelpers.ScaledDummy(3);

            // Scanner section
            UiSharedService.DistanceSeparator();
            _uiSharedService.BigText(Loc.Get("HousingShare.Scanner"));

            if (scanner.IsScanning)
            {
                UiSharedService.ColorTextWrapped(
                    string.Format(CultureInfo.CurrentCulture, Loc.Get("HousingShare.ScanResult"), scanner.CollectedFileCount),
                    UiSharedService.AccentColor);
                ImGuiHelpers.ScaledDummy(3);

                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Stop, Loc.Get("HousingShare.StopScan")))
                {
                    scanner.StopScan();
                }
            }
            else
            {
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Search, Loc.Get("HousingShare.ScanButton")))
                {
                    scanner.StartScan(currentLocation);
                }
            }

            // Publish section
            if (scanner.CollectedFileCount > 0)
            {
                ImGuiHelpers.ScaledDummy(5);
                UiSharedService.DistanceSeparator();
                _uiSharedService.BigText(Loc.Get("HousingShare.PublishButton"));

                UiSharedService.ColorTextWrapped(
                    string.Format(CultureInfo.CurrentCulture, Loc.Get("HousingShare.ScanResult"), scanner.CollectedFileCount),
                    ImGuiColors.HealerGreen);

                ImGui.SetNextItemWidth(300);
                ImGui.InputTextWithHint("##housingShareDesc", Loc.Get("HousingShare.Description"), ref _housingShareDescription, 128);

                using (ImRaii.Disabled(housingShareManager.IsBusy))
                {
                    if (_uiSharedService.IconTextButton(FontAwesomeIcon.Upload, Loc.Get("HousingShare.PublishButton")))
                    {
                        _ = housingShareManager.PublishAsync(currentLocation, _housingShareDescription);
                        _housingShareDescription = string.Empty;
                    }
                }
            }

            // Applied mods status
            if (housingShareManager.IsApplied)
            {
                ImGuiHelpers.ScaledDummy(5);
                UiSharedService.DistanceSeparator();
                UiSharedService.ColorTextWrapped(Loc.Get("HousingShare.ModsCurrentlyApplied"), ImGuiColors.HealerGreen);
                if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, Loc.Get("HousingShare.RemoveMods")))
                {
                    _ = housingShareManager.RemoveAppliedModsAsync();
                }
            }
        }

        // Own shares list
        ImGuiHelpers.ScaledDummy(5);
        UiSharedService.DistanceSeparator();
        _uiSharedService.BigText(Loc.Get("HousingShare.OwnShares"));

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Sync, Loc.Get("HousingShare.Refresh")))
        {
            _ = housingShareManager.RefreshAsync();
        }

        ImGuiHelpers.ScaledDummy(3);

        if (housingShareManager.OwnShares.Count == 0)
        {
            ImGui.TextDisabled(Loc.Get("HousingShare.NoOwnShares"));
        }
        else if (ImGui.BeginTable("housing-own-shares", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter))
        {
            ImGui.TableSetupColumn(Loc.Get("HousingShare.Description"));
            ImGui.TableSetupColumn(Loc.Get("HousingShare.Location"));
            ImGui.TableSetupColumn(Loc.Get("HousingShare.CreatedAt"));
            ImGui.TableSetupColumn(Loc.Get("HousingShare.Actions"), ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableHeadersRow();

            foreach (var entry in housingShareManager.OwnShares)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(string.IsNullOrEmpty(entry.Description) ? entry.Id.ToString("D", CultureInfo.InvariantCulture) : entry.Description);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"S{entry.Location.ServerId} W{entry.Location.WardId} H{entry.Location.HouseId}");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

                ImGui.TableNextColumn();
                using (ImRaii.PushId("housingShare" + entry.Id))
                {
                    if (ImGui.SmallButton(Loc.Get("HousingShare.Delete")))
                    {
                        _ = housingShareManager.DeleteAsync(entry.Id);
                    }
                }
            }

            ImGui.EndTable();
        }
    }
}
