using System.Net.Sockets;
using System.Text;

namespace Onx100Driver.Transport;

/// <summary>
/// TCP implementation of <see cref="IOnxTransport"/>.
/// Buffers incoming bytes and extracts CR LF-delimited response lines.
/// </summary>
public sealed class OnxTcpTransport : IOnxTransport
{
    private const int ReadBufferSize = 4096;
    private const int MaxLineLength = 4096;

    private readonly string _host;
    private readonly int _port;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly List<byte> _lineBuffer = [];
    private readonly object _connectionLock = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _disposed;

    public OnxTcpTransport(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

        _host = host;
        _port = port;
    }

    public bool IsConnected
    {
        get
        {
            lock (_connectionLock)
            {
                return _stream is not null;
            }
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await DisconnectAsync().ConfigureAwait(false);

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();

            lock (_connectionLock)
            {
                _client = client;
                _stream = stream;
                _lineBuffer.Clear();
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(command);

        if (command.Contains('\r') || command.Contains('\n'))
            throw new ArgumentException("Command must not contain line terminators.", nameof(command));

        var stream = GetStream();
        var bytes = Encoding.ASCII.GetBytes(command + "\r");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            throw new OnxTransportException("Failed to send command to the ONX-100.", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var stream = GetStream();
        var buffer = new byte[ReadBufferSize];

        while (true)
        {
            var line = ExtractLine();
            if (line is not null) return line;

            int bytesRead;
            try
            {
                bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                throw new OnxTransportException("Failed to read from the ONX-100.", ex);
            }

            if (bytesRead == 0)
            {
                if (_lineBuffer.Count != 0)
                    throw new OnxConnectionClosedException("The device closed the connection in the middle of a response line.");

                return null;
            }

            _lineBuffer.AddRange(buffer.AsSpan(0, bytesRead).ToArray());

            if (_lineBuffer.Count > MaxLineLength)
                throw new OnxTransportException($"Response line exceeded the {MaxLineLength}-byte safety limit.");
        }
    }

    public async Task DisconnectAsync()
    {
        TcpClient? client;
        NetworkStream? stream;

        lock (_connectionLock)
        {
            client = _client;
            stream = _stream;
            _client = null;
            _stream = null;
            _lineBuffer.Clear();
        }

        if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);

        client?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }

    /// <summary>
    /// Scans the line buffer for a CR LF (0x0D 0x0A) sequence.
    /// If found, extracts and returns the line text (without the terminator)
    /// and removes the consumed bytes from the buffer.
    /// </summary>
    private string? ExtractLine()
    {
        for (var i = 0; i < _lineBuffer.Count - 1; i++)
        {
            if (_lineBuffer[i] != '\r' || _lineBuffer[i + 1] != '\n') continue;

            var line = Encoding.ASCII.GetString(_lineBuffer.GetRange(0, i).ToArray());
            _lineBuffer.RemoveRange(0, i + 2);
            return line;
        }

        return null;
    }

    private NetworkStream GetStream()
    {
        lock (_connectionLock)
        {
            return _stream ?? throw new OnxNotConnectedException();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
