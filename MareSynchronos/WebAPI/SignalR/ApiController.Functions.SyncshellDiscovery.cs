using System.Collections.Generic;
using MareSynchronos.API.Dto.Group;
using Microsoft.AspNetCore.SignalR.Client;

namespace MareSynchronos.WebAPI;

public partial class ApiController
{
    public async Task<List<SyncshellDiscoveryEntryDto>> SyncshellDiscoveryList()
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<List<SyncshellDiscoveryEntryDto>>(nameof(SyncshellDiscoveryList)).ConfigureAwait(false);
    }

    public async Task<SyncshellDiscoveryStateDto?> SyncshellDiscoveryGetState(GroupDto group)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<SyncshellDiscoveryStateDto?>(nameof(SyncshellDiscoveryGetState), group).ConfigureAwait(false);
    }

    public async Task<bool> SyncshellDiscoverySetVisibility(SyncshellDiscoveryVisibilityRequestDto request)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<bool>(nameof(SyncshellDiscoverySetVisibility), request).ConfigureAwait(false);
    }

    public async Task<bool> SyncshellDiscoveryJoin(GroupDto group)
    {
        CheckConnection();
        return await _mareHub!.InvokeAsync<bool>(nameof(SyncshellDiscoveryJoin), group).ConfigureAwait(false);
    }
}
