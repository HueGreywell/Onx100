namespace Onx100Driver.Transport;

/// <summary>
/// Thrown when a transport-level I/O operation fails (connection refused,
/// socket closed unexpectedly, read/write error, etc.).
/// </summary>
public class OnxTransportException(string message, Exception? innerException = null)
    : IOException(message, innerException);

/// <summary>
/// Thrown when another client already holds the device's single connection.
/// </summary>
public sealed class OnxConnectionTakenException()
    : OnxTransportException("The ONX-100 already has a connected client.");

/// <summary>
/// Thrown when an operation is attempted while the transport is not connected.
/// </summary>
public sealed class OnxNotConnectedException()
    : OnxTransportException("The ONX-100 transport is not connected.");

/// <summary>
/// Thrown when the device closes the connection unexpectedly.
/// </summary>
public sealed class OnxConnectionClosedException(string message)
    : OnxTransportException(message);

/// <summary>
/// Thrown when the device sends an unexpected or unrecognized response
/// where a specific message type was required.
/// </summary>
public sealed class OnxUnexpectedResponseException(string message)
    : OnxTransportException(message);
