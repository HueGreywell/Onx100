namespace Onx100Driver.Transport;

/// <summary>
/// Abstracts the wire-level transport for the ONX-100 protocol.
/// Commands are sent as ASCII text terminated by CR (0x0D).
/// Responses are terminated by CR LF (0x0D 0x0A).
/// </summary>
public interface IOnxTransport : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a single command. The implementation appends the CR terminator.
    /// </summary>
    Task SendCommandAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the next CR LF-terminated line, stripping the terminator.
    /// Returns <c>null</c> when the remote end closes the connection.
    /// </summary>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}
