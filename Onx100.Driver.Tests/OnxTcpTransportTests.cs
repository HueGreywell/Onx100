using System.Net;
using System.Net.Sockets;
using System.Text;
using Onx100Driver.Transport;

namespace Onx100Driver.Tests;

public sealed class OnxTcpTransportTests
{
    [Fact]
    public async Task ReadsFragmentedCrLfLinesAndSendsCrCommands()
    {
        await using var server = new TestTcpServer();
        await using var transport = new OnxTcpTransport("127.0.0.1", server.Port);

        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync();
        await using var peer = await acceptTask;

        // Simulate the device sending a greeting in fragments.
        await peer.WriteAsync("*HELLO ONX-100 ");
        await peer.WriteAsync("FW:2.13\r");
        await peer.WriteAsync("\n");

        Assert.Equal("*HELLO ONX-100 FW:2.13", await transport.ReadLineAsync());

        // Driver sends a command terminated with CR.
        await transport.SendCommandAsync("PWR ?");
        Assert.Equal("PWR ?\r", await peer.ReadUntilAsync("\r"));

        // Simulate a response in fragments.
        await peer.WriteAsync("PWR ");
        await peer.WriteAsync("ON\r");
        await peer.WriteAsync("\n");

        Assert.Equal("PWR ON", await transport.ReadLineAsync());
    }

    [Fact]
    public async Task DoesNotTreatLoneLineFeedAsTerminator()
    {
        await using var server = new TestTcpServer();
        await using var transport = new OnxTcpTransport("127.0.0.1", server.Port);

        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync();
        await using var peer = await acceptTask;

        // LF without preceding CR is not a line terminator.
        await peer.WriteAsync("BAD\nOK\r\n");

        Assert.Equal("BAD\nOK", await transport.ReadLineAsync());
    }

    [Fact]
    public async Task SendsMultipleCommandsSequentially()
    {
        await using var server = new TestTcpServer();
        await using var transport = new OnxTcpTransport("127.0.0.1", server.Port);

        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync();
        await using var peer = await acceptTask;

        // Send first command, read its response, then send the next.
        await transport.SendCommandAsync("PWR ?");
        Assert.Equal("PWR ?\r", await peer.ReadUntilAsync("\r"));
        await peer.WriteAsync("PWR ON\r\n");
        Assert.Equal("PWR ON", await transport.ReadLineAsync());

        await transport.SendCommandAsync("VOL ?");
        Assert.Equal("VOL ?\r", await peer.ReadUntilAsync("\r"));
        await peer.WriteAsync("VOL 4B\r\n");
        Assert.Equal("VOL 4B", await transport.ReadLineAsync());
    }

    [Theory]
    [InlineData("PWR\rON")]
    [InlineData("PWR\nON")]
    public async Task SendCommandRejectsLineTerminatorsInCommand(string command)
    {
        await using var server = new TestTcpServer();
        await using var transport = new OnxTcpTransport("127.0.0.1", server.Port);

        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync();
        await using var _ = await acceptTask;

        await Assert.ThrowsAsync<ArgumentException>(() => transport.SendCommandAsync(command));
    }

    [Fact]
    public async Task ThrowsWhenConnectionClosedMidLine()
    {
        await using var server = new TestTcpServer();
        await using var transport = new OnxTcpTransport("127.0.0.1", server.Port);

        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync();
        var peer = await acceptTask;

        // Send partial data without CRLF, then close.
        await peer.WriteAsync("PWR O");
        await peer.DisposeAsync();

        await Assert.ThrowsAsync<OnxConnectionClosedException>(() => transport.ReadLineAsync());
    }

    [Fact]
    public async Task ReadLineExtractsMultipleLinesFromSingleRead()
    {
        await using var server = new TestTcpServer();
        await using var transport = new OnxTcpTransport("127.0.0.1", server.Port);

        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync();
        await using var peer = await acceptTask;

        // Device sends a response and an EVT in a single write.
        await peer.WriteAsync("PWR ON\r\nEVT SIGNAL 2 OK\r\n");

        Assert.Equal("PWR ON", await transport.ReadLineAsync());
        Assert.Equal("EVT SIGNAL 2 OK", await transport.ReadLineAsync());
    }

    [Fact]
    public async Task SendCommandThrowsWhenNotConnected()
    {
        await using var transport = new OnxTcpTransport("127.0.0.1", 1);

        await Assert.ThrowsAsync<OnxNotConnectedException>(() => transport.SendCommandAsync("PWR ?"));
    }

    [Fact]
    public async Task CanReconnectAfterDisconnect()
    {
        await using var server = new TestTcpServer();
        await using var transport = new OnxTcpTransport("127.0.0.1", server.Port);

        // First connection.
        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync();
        await using var peer1 = await acceptTask;

        await transport.SendCommandAsync("PWR ?");
        Assert.Equal("PWR ?\r", await peer1.ReadUntilAsync("\r"));

        // Disconnect, then reconnect.
        await transport.DisconnectAsync();
        Assert.False(transport.IsConnected);

        var acceptTask2 = server.AcceptAsync();
        await transport.ConnectAsync();
        Assert.True(transport.IsConnected);
        await using var peer2 = await acceptTask2;

        await transport.SendCommandAsync("VOL ?");
        Assert.Equal("VOL ?\r", await peer2.ReadUntilAsync("\r"));
    }

    [Fact]
    public async Task ReturnsNullOnCleanClose()
    {
        await using var server = new TestTcpServer();
        await using var transport = new OnxTcpTransport("127.0.0.1", server.Port);

        var acceptTask = server.AcceptAsync();
        await transport.ConnectAsync();
        var peer = await acceptTask;
        await peer.DisposeAsync();

        Assert.Null(await transport.ReadLineAsync());
    }

    private sealed class TestTcpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public TestTcpServer() => _listener.Start();
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public async Task<TestTcpPeer> AcceptAsync() => new(await _listener.AcceptTcpClientAsync());

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestTcpPeer(TcpClient client) : IAsyncDisposable
    {
        private readonly NetworkStream _stream = client.GetStream();
        private readonly TcpClient _client = client;

        public async Task WriteAsync(string text)
        {
            await _stream.WriteAsync(Encoding.ASCII.GetBytes(text));
            await _stream.FlushAsync();
        }

        public async Task<string> ReadUntilAsync(string terminator)
        {
            var bytes = new List<byte>();
            var term = Encoding.ASCII.GetBytes(terminator);
            var buf = new byte[1];

            while (!EndsWith(bytes, term))
            {
                var n = await _stream.ReadAsync(buf);
                Assert.NotEqual(0, n);
                bytes.Add(buf[0]);
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync();
            _client.Dispose();
        }

        private static bool EndsWith(List<byte> bytes, byte[] suffix)
        {
            if (bytes.Count < suffix.Length) return false;
            for (var i = 1; i <= suffix.Length; i++)
            {
                if (bytes[^i] != suffix[^i]) return false;
            }
            return true;
        }
    }
}
