using Onx100Driver.Protocol;

namespace Onx100Driver;

/// <summary>
/// Immutable snapshot of everything the driver knows about the ONX-100.
/// Updated atomically on every command response and unsolicited event.
/// Nullable fields indicate values that have not been queried yet.
/// </summary>
public sealed record OnxDeviceState(
    bool IsConnected,
    string? Model,
    string? Firmware,
    OnxPowerState? Power,
    int? SelectedInput,
    int? Volume,
    bool? IsMuted,
    IReadOnlyDictionary<int, OnxSignalStatus> Signals)
{
    public bool IsOperational => IsConnected && Power == OnxPowerState.On;

    public static OnxDeviceState Disconnected { get; } = new(
        false, null, null, null, null, null, null,
        new Dictionary<int, OnxSignalStatus>());
}
