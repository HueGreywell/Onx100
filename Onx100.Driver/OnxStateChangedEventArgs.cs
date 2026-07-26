namespace Onx100Driver;

/// <summary>
/// Raised whenever any field of <see cref="OnxDeviceState"/> changes,
/// whether from a command response or an unsolicited device event.
/// </summary>
public sealed class OnxStateChangedEventArgs(OnxDeviceState state) : EventArgs
{
    public OnxDeviceState State { get; } = state;
}
