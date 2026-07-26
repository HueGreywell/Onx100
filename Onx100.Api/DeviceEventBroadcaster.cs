using Microsoft.AspNetCore.SignalR;

namespace Onx100Api;

public sealed class DeviceEventBroadcaster : IHostedService
{
    private readonly DeviceManager _manager;
    private readonly IHubContext<DeviceHub> _hubContext;

    public DeviceEventBroadcaster(DeviceManager manager, IHubContext<DeviceHub> hubContext)
    {
        _manager = manager;
        _hubContext = hubContext;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _manager.OnEvent = BroadcastEvent;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _manager.OnEvent = null;
        await _manager.DisposeAsync();
    }

    private void BroadcastEvent(DeviceEventDto evt)
    {
        _ = _hubContext.Clients.All.SendAsync("DeviceEvent", evt);
    }
}
