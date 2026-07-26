namespace Onx100Api;

public sealed record DeviceStateDto(
    bool IsConnected,
    string? Model,
    string? Firmware,
    string? Power,
    int? SelectedInput,
    int? Volume,
    bool? IsMuted,
    Dictionary<string, string> Signals)
{
    public static DeviceStateDto Disconnected { get; } =
        new(false, null, null, null, null, null, null, new Dictionary<string, string>());
}

public sealed record DeviceEventDto(string Type, DeviceStateDto State, string? Message);

public sealed record ConnectRequest(string Host, int Port);
