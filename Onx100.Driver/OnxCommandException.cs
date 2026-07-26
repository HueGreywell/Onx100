namespace Onx100Driver;

/// <summary>
/// Thrown when the ONX-100 responds to a command with an ERR code.
/// </summary>
public class OnxCommandException : Exception
{
    public OnxCommandException(string command, string message)
        : base(message)
    {
        Command = command;
    }

    /// <summary>The exact command text that was sent.</summary>
    public string Command { get; }
}

/// <summary>
/// ERR 01 — the device did not recognize the command.
/// </summary>
public sealed class OnxUnknownCommandException(string command)
    : OnxCommandException(command, $"ONX-100 did not recognize command '{command}'.");

/// <summary>
/// ERR 02 — a parameter was invalid or malformed.
/// </summary>
public sealed class OnxInvalidParameterException(string command)
    : OnxCommandException(command, $"ONX-100 command '{command}' received an invalid parameter.");

/// <summary>
/// ERR 03 — the command is unavailable in the current power state.
/// </summary>
public sealed class OnxUnavailableException(string command)
    : OnxCommandException(command, $"ONX-100 command '{command}' is unavailable in the current power state.");

/// <summary>
/// Thrown when the device sends an unexpected or unrecognized response
/// where a specific message type was required.
/// </summary>
public sealed class OnxUnexpectedResponseException(string message)
    : Exception(message);
