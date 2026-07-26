namespace Onx100Driver;

/// <summary>
/// Raised when automatic reconnection has exhausted all retry attempts
/// and the client has given up reconnecting.
/// </summary>
public sealed class OnxReconnectFailedEventArgs(int attemptsExhausted, Exception lastException) : EventArgs
{
    /// <summary>Total number of reconnection attempts that were made.</summary>
    public int Attempts { get; } = attemptsExhausted;

    /// <summary>The exception from the final failed attempt.</summary>
    public Exception LastException { get; } = lastException;
}
