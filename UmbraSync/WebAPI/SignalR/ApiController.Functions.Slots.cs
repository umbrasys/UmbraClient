using UmbraSync.API.Dto.Group;
using UmbraSync.API.Dto.Slot;
using Microsoft.AspNetCore.SignalR.Client;

namespace UmbraSync.WebAPI.SignalR;

public partial class ApiController
{
    public async Task<SlotInfoResponseDto?> SlotGetInfo(SlotLocationDto location)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<SlotInfoResponseDto?>(nameof(SlotGetInfo), location).ConfigureAwait(false);
    }

    public async Task<SlotInfoResponseDto?> SlotGetNearby(uint serverId, uint territoryId, float x, float y, float z)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<SlotInfoResponseDto?>(nameof(SlotGetNearby), serverId, territoryId, x, y, z).ConfigureAwait(false);
    }

    public async Task<bool> SlotUpdate(SlotUpdateRequestDto request)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<bool>(nameof(SlotUpdate), request).ConfigureAwait(false);
    }

    public async Task<List<SlotInfoResponseDto>> SlotGetInfoForGroup(GroupDto group)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<List<SlotInfoResponseDto>>(nameof(SlotGetInfoForGroup), group).ConfigureAwait(false);
    }

    public async Task<bool> SlotJoin(Guid slotId)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<bool>(nameof(SlotJoin), slotId).ConfigureAwait(false);
    }
}
