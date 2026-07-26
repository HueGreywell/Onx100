using Onx100Driver.Protocol;

namespace Onx100Driver;

/// <summary>
/// Raised when the device sends an unsolicited EVT SIGNAL event,
/// indicating a signal appeared or was lost on a specific input.
/// </summary>
public sealed class OnxSignalChangedEventArgs(int signal, OnxSignalStatus status) : EventArgs
{
    public int Signal { get; } = signal;
    public OnxSignalStatus Status { get; } = status;
}
