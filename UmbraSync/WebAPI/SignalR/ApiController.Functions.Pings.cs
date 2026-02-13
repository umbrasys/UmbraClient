using Microsoft.AspNetCore.SignalR.Client;
using UmbraSync.API.Dto.Group;
using UmbraSync.API.Dto.Ping;

namespace UmbraSync.WebAPI.SignalR;

public partial class ApiController
{
    public async Task GroupSendPing(GroupDto group, PingMarkerDto ping)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupSendPing), group, ping).ConfigureAwait(false);
    }

    public async Task GroupRemovePing(GroupDto group, PingMarkerRemoveDto remove)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupRemovePing), group, remove).ConfigureAwait(false);
    }

    public async Task GroupClearPings(GroupDto group)
    {
        CheckConnection();
        await _mareHub!.SendAsync(nameof(GroupClearPings), group).ConfigureAwait(false);
    }
}
