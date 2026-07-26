using Microsoft.AspNetCore.SignalR;

namespace Onx100Api;

public sealed class DeviceHub : Hub
{
    private readonly DeviceManager _manager;

    public DeviceHub(DeviceManager manager)
    {
        _manager = manager;
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("DeviceEvent",
            new DeviceEventDto("stateChanged", _manager.CurrentState, null));
        await base.OnConnectedAsync();
    }
}
