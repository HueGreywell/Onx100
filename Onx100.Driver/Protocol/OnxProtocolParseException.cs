namespace Onx100Driver.Protocol;

/// <summary>
/// Thrown when the parser encounters a response line that does not conform
/// to any known ONX-100 message format.
/// </summary>
public sealed class OnxProtocolParseException(string message)
    : FormatException(message);
