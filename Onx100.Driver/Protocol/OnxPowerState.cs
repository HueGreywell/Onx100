namespace Onx100Driver.Protocol;

/// <summary>
/// The power lifecycle of the ONX-100.
/// Transitions: Off → Warm → On → Cool → Off.
/// </summary>
public enum OnxPowerState
{
    Off,
    Warm,
    On,
    Cool
}
