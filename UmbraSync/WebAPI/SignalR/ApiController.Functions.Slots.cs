using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using UmbraSync.API.Dto.Group;
using UmbraSync.API.Dto.Slot;

namespace UmbraSync.WebAPI.SignalR;

public partial class ApiController
{
    public async Task<SlotInfoResponseDto?> SlotGetInfo(SlotLocationDto location)
    {
        if (!IsConnected) return null;
        try
        {
            return await _mareHub!.InvokeAsync<SlotInfoResponseDto?>(nameof(SlotGetInfo), location).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error getting slot info for {location}", location);
            return null;
        }
    }

    public async Task<SlotInfoResponseDto?> SlotGetNearby(uint serverId, uint territoryId, float x, float y, float z)
    {
        if (!IsConnected) return null;
        try
        {
            return await _mareHub!.InvokeAsync<SlotInfoResponseDto?>(nameof(SlotGetNearby), serverId, territoryId, x, y, z).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error getting nearby slot info at {serverId}:{territoryId}", serverId, territoryId);
            return null;
        }
    }

    public async Task<bool> SlotUpdate(SlotUpdateRequestDto request)
    {
        CheckConnection();
        try
        {
            return await _mareHub!.InvokeAsync<bool>(nameof(SlotUpdate), request).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error updating slot info for {gid}", request.Group.GID);
            return false;
        }
    }

    public async Task<List<SlotInfoResponseDto>> SlotGetInfoForGroup(GroupDto group)
    {
        if (!IsConnected) return [];
        try
        {
            return await _mareHub!.InvokeAsync<List<SlotInfoResponseDto>>(nameof(SlotGetInfoForGroup), group).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error getting slot info for group {gid}", group.GID);
            return [];
        }
    }

    public async Task<bool> SlotJoin(Guid slotId)
    {
        if (!IsConnected) return false;
        try
        {
            return await _mareHub!.InvokeAsync<bool>(nameof(SlotJoin), slotId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error joining slot {slotId}", slotId);
            return false;
        }
    }
}