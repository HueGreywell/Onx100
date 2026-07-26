namespace Onx100Api;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/state", (DeviceManager dm) => dm.CurrentState);

        api.MapPost("/connect", async (ConnectRequest req, DeviceManager dm) =>
            await dm.ConnectAsync(req.Host, req.Port));

        api.MapPost("/disconnect", async (DeviceManager dm) =>
            await dm.DisconnectAsync());

        api.MapPost("/power/on", async (DeviceManager dm) =>
            await dm.PowerOnAsync());

        api.MapPost("/power/off", async (DeviceManager dm) =>
            await dm.PowerOffAsync());

        api.MapPost("/input/{id:int}", async (int id, DeviceManager dm) =>
            await dm.SelectInputAsync(id));

        api.MapPost("/volume/{level:int}", async (int level, DeviceManager dm) =>
            await dm.SetVolumeAsync(level));

        api.MapPost("/mute/{enabled:bool}", async (bool enabled, DeviceManager dm) =>
            await dm.SetMuteAsync(enabled));
    }
}
