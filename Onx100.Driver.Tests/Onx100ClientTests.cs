using System.Threading.Channels;
using Onx100Driver;
using Onx100Driver.Protocol;
using Onx100Driver.Transport;

namespace Onx100.Driver.Tests;

public sealed class Onx100ClientTests
{
    private const string Greeting = "*HELLO ONX-100 FW:2.13";

    [Fact]
    public void Constructor_ZeroTimeout_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Onx100Client(new FakeTransport(), commandTimeout: TimeSpan.Zero));
    }

    [Fact]
    public void Constructor_NegativeTimeout_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Onx100Client(new FakeTransport(), commandTimeout: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Constructor_ZeroMaxReconnectAttempts_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Onx100Client(new FakeTransport(), maxReconnectAttempts: 0));
    }

    [Fact]
    public void Constructor_NegativeMaxReconnectAttempts_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Onx100Client(new FakeTransport(), maxReconnectAttempts: -1));
    }

    [Fact]
    public async Task ConnectAsync_HelloGreeting_SetsStateConnected()
    {
        var transport = new FakeTransport();
        transport.EnqueueLine(Greeting);
        transport.EnqueueResponse("PWR OFF");
        transport.EnqueueResponse("VOL 32");
        transport.EnqueueResponse("MUTE OFF");

        await using var client = new Onx100Client(transport);
        await client.ConnectAsync();

        Assert.True(client.IsConnected);
        var state = client.CurrentState;
        Assert.True(state.IsConnected);
        Assert.Equal("ONX-100", state.Model);
        Assert.Equal("2.13", state.Firmware);
    }

    [Fact]
    public async Task ConnectAsync_BusyGreeting_ThrowsConnectionTaken()
    {
        var transport = new FakeTransport();
        transport.EnqueueLine("*BUSY");

        await using var client = new Onx100Client(transport);
        await Assert.ThrowsAsync<OnxConnectionTakenException>(() => client.ConnectAsync());
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_UnexpectedGreeting_Throws()
    {
        var transport = new FakeTransport();
        transport.EnqueueLine("OK");

        await using var client = new Onx100Client(transport);
        await Assert.ThrowsAsync<OnxUnexpectedResponseException>(() => client.ConnectAsync());
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_NullGreeting_ThrowsConnectionClosed()
    {
        var transport = new FakeTransport();
        transport.EnqueueLine(null);

        await using var client = new Onx100Client(transport);
        await Assert.ThrowsAsync<OnxConnectionClosedException>(() => client.ConnectAsync());
        Assert.False(transport.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_AlreadyConnected_IsNoOp()
    {
        var transport = new FakeTransport();
        transport.EnqueueLine(Greeting);
        transport.EnqueueResponse("PWR OFF");
        transport.EnqueueResponse("VOL 00");
        transport.EnqueueResponse("MUTE OFF");

        await using var client = new Onx100Client(transport);
        await client.ConnectAsync();

        await client.ConnectAsync();

        Assert.Equal(1, transport.ConnectCount);
    }

    [Fact]
    public async Task DisconnectAsync_FiresDisconnectedEvent()
    {
        var (client, _) = await CreateConnectedClientAsync();
        await using var d = client;

        var disconnected = false;
        client.Disconnected += (_, _) => disconnected = true;

        await client.DisconnectAsync();

        Assert.True(disconnected);
        Assert.False(client.CurrentState.IsConnected);
    }

    [Theory]
    [InlineData("PWR ON", OnxPowerState.On)]
    [InlineData("PWR OFF", OnxPowerState.Off)]
    [InlineData("PWR WARM", OnxPowerState.Warm)]
    [InlineData("PWR COOL", OnxPowerState.Cool)]
    public async Task QueryPowerAsync_ReturnsCorrectState(string response, OnxPowerState expected)
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse(response);
        var result = await client.QueryPowerAsync();

        Assert.Equal(expected, result);
        Assert.Equal(expected, client.CurrentState.Power);
        Assert.Equal("PWR ?", transport.LastCommand);
    }

    [Fact]
    public async Task PowerOnAsync_SendsPwrOnAndSetsWarmState()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse("OK");
        await client.PowerOnAsync();

        Assert.Equal("PWR ON", transport.LastCommand);
        Assert.Equal(OnxPowerState.Warm, client.CurrentState.Power);
    }

    [Fact]
    public async Task PowerOffAsync_SendsPwrOffAndSetsCoolState()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        // First set state to On so PowerOff has a transition to make.
        transport.EnqueueResponse("PWR ON");
        await client.QueryPowerAsync();

        transport.EnqueueResponse("OK");
        await client.PowerOffAsync();

        Assert.Equal("PWR OFF", transport.LastCommand);
        Assert.Equal(OnxPowerState.Cool, client.CurrentState.Power);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task SelectInputAsync_ValidInput_SendsCommandAndUpdatesState(int input)
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse("OK");
        await client.SelectInputAsync(input);

        Assert.Equal($"IN {input}", transport.LastCommand);
        Assert.Equal(input, client.CurrentState.SelectedInput);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public async Task SelectInputAsync_OutOfRange_Throws(int input)
    {
        var (client, _) = await CreateConnectedClientAsync();
        await using var d = client;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.SelectInputAsync(input));
    }

    [Fact]
    public async Task QueryInputAsync_ReturnsInputAndUpdatesState()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse("IN 3");
        var result = await client.QueryInputAsync();

        Assert.Equal(3, result);
        Assert.Equal(3, client.CurrentState.SelectedInput);
        Assert.Equal("IN ?", transport.LastCommand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task SetVolumeAsync_ValidVolume_SendsCommandAndUpdatesState(int volume)
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse("OK");
        await client.SetVolumeAsync(volume);

        Assert.Equal($"VOL {volume}", transport.LastCommand);
        Assert.Equal(volume, client.CurrentState.Volume);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task SetVolumeAsync_OutOfRange_Throws(int volume)
    {
        var (client, _) = await CreateConnectedClientAsync();
        await using var d = client;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.SetVolumeAsync(volume));
    }

    [Fact]
    public async Task QueryVolumeAsync_ReturnsVolumeAndUpdatesState()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse("VOL 32"); // 0x32 = 50 decimal
        var result = await client.QueryVolumeAsync();

        Assert.Equal(50, result);
        Assert.Equal(50, client.CurrentState.Volume);
    }

    [Fact]
    public async Task SetMuteAsync_True_SendsMuteOn()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse("OK");
        await client.SetMuteAsync(true);

        Assert.Equal("MUTE ON", transport.LastCommand);
        Assert.True(client.CurrentState.IsMuted);
    }

    [Fact]
    public async Task SetMuteAsync_False_SendsMuteOff()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse("OK");
        await client.SetMuteAsync(false);

        Assert.Equal("MUTE OFF", transport.LastCommand);
        Assert.False(client.CurrentState.IsMuted);
    }

    [Theory]
    [InlineData("MUTE ON", true)]
    [InlineData("MUTE OFF", false)]
    public async Task QueryMuteAsync_ReturnsCorrectValue(string response, bool expected)
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse(response);
        var result = await client.QueryMuteAsync();

        Assert.Equal(expected, result);
        Assert.Equal(expected, client.CurrentState.IsMuted);
    }

    [Fact]
    public async Task Command_Err01_ThrowsUnknownCommand()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse("ERR 01");
        await Assert.ThrowsAsync<OnxUnknownCommandException>(() => client.QueryPowerAsync());
    }

    [Fact]
    public async Task Command_Err02_ThrowsInvalidParameter()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse("ERR 02");
        await Assert.ThrowsAsync<OnxInvalidParameterException>(() => client.SetVolumeAsync(50));
    }

    [Fact]
    public async Task Command_Err03_ThrowsUnavailable()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        transport.EnqueueResponse("ERR 03");
        await Assert.ThrowsAsync<OnxUnavailableException>(() => client.SelectInputAsync(1));
    }

    [Fact]
    public async Task Command_UnexpectedResponseType_ThrowsUnexpectedResponse()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        // QueryPower expects OnxPowerMessage but gets OnxInputMessage.
        transport.EnqueueResponse("IN 1");
        await Assert.ThrowsAsync<OnxUnexpectedResponseException>(() => client.QueryPowerAsync());
    }

    [Fact]
    public async Task UnsolicitedPowerEvent_UpdatesState()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        // Inject an unsolicited EVT PWR ON into the reader loop.
        transport.EnqueueLine("EVT PWR ON");
        // The power-on event triggers RefreshState which sends queries.
        transport.EnqueueResponse("PWR ON");
        transport.EnqueueResponse("VOL 32");
        transport.EnqueueResponse("MUTE OFF");
        transport.EnqueueResponse("IN 1");

        // Give the reader loop time to process.
        await Task.Delay(200);

        Assert.Equal(OnxPowerState.On, client.CurrentState.Power);
    }

    [Fact]
    public async Task UnsolicitedSignalEvent_FiresSignalChangedAndUpdatesState()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        var signals = new List<(int Signal, OnxSignalStatus Status)>();
        client.SignalChanged += (_, e) => signals.Add((e.Signal, e.Status));

        transport.EnqueueLine("EVT SIGNAL 2 OK");

        await Task.Delay(200);

        Assert.Single(signals);
        Assert.Equal(2, signals[0].Signal);
        Assert.Equal(OnxSignalStatus.Ok, signals[0].Status);
        Assert.Equal(OnxSignalStatus.Ok, client.CurrentState.Signals[2]);
    }

    [Fact]
    public async Task WaitForPowerStateAsync_AlreadyInState_ReturnsImmediately()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        // Set power to On via query.
        transport.EnqueueResponse("PWR ON");
        await client.QueryPowerAsync();

        // Should complete immediately since we're already On.
        await client.WaitForPowerStateAsync(OnxPowerState.On);
    }

    [Fact]
    public async Task WaitForPowerStateAsync_WaitsForTransition()
    {
        var (client, transport) = await CreateConnectedClientAsync();
        await using var d = client;

        // Start from Off.
        transport.EnqueueResponse("PWR OFF");
        await client.QueryPowerAsync();

        var waitTask = client.WaitForPowerStateAsync(OnxPowerState.On);
        Assert.False(waitTask.IsCompleted);

        // Simulate unsolicited power event transitioning to On.
        transport.EnqueueLine("EVT PWR ON");
        // RefreshState queries triggered by power-on event.
        transport.EnqueueResponse("PWR ON");
        transport.EnqueueResponse("VOL 32");
        transport.EnqueueResponse("MUTE OFF");
        transport.EnqueueResponse("IN 1");

        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Command_WhenNotConnected_ThrowsNotConnected()
    {
        var transport = new FakeTransport();

        await using var client = new Onx100Client(transport);
        await Assert.ThrowsAsync<OnxNotConnectedException>(() => client.QueryPowerAsync());
    }

    [Fact]
    public async Task AutoReconnect_ReconnectsOnDisconnect()
    {
        var transport = new FakeTransport();
        transport.EnqueueLine(Greeting);
        transport.EnqueueResponse("PWR OFF");
        transport.EnqueueResponse("VOL 00");
        transport.EnqueueResponse("MUTE OFF");

        await using var client = new Onx100Client(
            transport,
            autoReconnect: true,
            reconnectDelay: TimeSpan.FromMilliseconds(50),
            maxReconnectAttempts: 3);

        await client.ConnectAsync();

        // Simulate the device closing the connection.
        transport.SimulateRemoteClose();

        // Enqueue greeting + refresh for the reconnect.
        transport.EnqueueLine(Greeting);
        transport.EnqueueResponse("PWR OFF");
        transport.EnqueueResponse("VOL 00");
        transport.EnqueueResponse("MUTE OFF");

        // Give time for reconnect.
        await Task.Delay(500);

        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task Dispose_DisposesTransport()
    {
        var transport = new FakeTransport();
        transport.EnqueueLine(Greeting);
        transport.EnqueueResponse("PWR OFF");
        transport.EnqueueResponse("VOL 00");
        transport.EnqueueResponse("MUTE OFF");

        var client = new Onx100Client(transport);
        await client.ConnectAsync();
        await client.DisposeAsync();

        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task Command_AfterDispose_ThrowsObjectDisposed()
    {
        var transport = new FakeTransport();
        var client = new Onx100Client(transport);
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.QueryPowerAsync());
    }

    [Fact]
    public async Task StateChanged_FiredOnConnect()
    {
        var transport = new FakeTransport();
        transport.EnqueueLine(Greeting);
        transport.EnqueueResponse("PWR OFF");
        transport.EnqueueResponse("VOL 00");
        transport.EnqueueResponse("MUTE OFF");

        await using var client = new Onx100Client(transport);

        var states = new List<OnxDeviceState>();
        client.StateChanged += (_, e) => states.Add(e.State);

        await client.ConnectAsync();

        // At minimum: connect state, then RefreshState updates for PWR, VOL, MUTE.
        Assert.True(states.Count >= 1);
        Assert.True(states[0].IsConnected);
        Assert.Equal("ONX-100", states[0].Model);
    }

    /// <summary>
    /// Creates a connected client with the standard greeting and RefreshState
    /// responses already enqueued. The device reports power OFF so RefreshState
    /// queries PWR, VOL, and MUTE (no IN query).
    /// </summary>
    private static async Task<(Onx100Client Client, FakeTransport Transport)> CreateConnectedClientAsync()
    {
        var transport = new FakeTransport();
        transport.EnqueueLine(Greeting);
        transport.EnqueueResponse("PWR OFF");
        transport.EnqueueResponse("VOL 00");
        transport.EnqueueResponse("MUTE OFF");

        var client = new Onx100Client(transport);
        await client.ConnectAsync();
        return (client, transport);
    }

    /// <summary>
    /// In-memory <see cref="IOnxTransport"/> for unit-testing <see cref="Onx100Client"/>
    /// without real sockets.
    /// <para>
    /// <see cref="EnqueueLine"/> writes directly to the read channel (for greetings
    /// and unsolicited events). <see cref="EnqueueResponse"/> queues a line that is
    /// only released into the read channel when <see cref="SendCommandAsync"/> is
    /// called, ensuring the response arrives after <c>_pendingResponse</c> is set.
    /// </para>
    /// </summary>
    private sealed class FakeTransport : IOnxTransport
    {
        private readonly Channel<string?> _lines = Channel.CreateUnbounded<string?>();
        private readonly Queue<string?> _responseQueue = new();
        private readonly List<string> _sentCommands = [];

        public bool IsConnected { get; private set; }
        public bool Disposed { get; private set; }
        public int ConnectCount { get; private set; }
        public IReadOnlyList<string> SentCommands => _sentCommands;
        public string? LastCommand => _sentCommands.Count > 0 ? _sentCommands[^1] : null;

        /// <summary>
        /// Writes a line directly into the read channel. Use for greetings
        /// (read before the reader loop starts) and unsolicited events.
        /// </summary>
        public void EnqueueLine(string? line) => _lines.Writer.TryWrite(line);

        /// <summary>
        /// Queues a response that will be released into the read channel on the
        /// next <see cref="SendCommandAsync"/> call. Use for command responses.
        /// </summary>
        public void EnqueueResponse(string? line) => _responseQueue.Enqueue(line);

        public void SimulateRemoteClose()
        {
            IsConnected = false;
            _lines.Writer.TryWrite(null);
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            ConnectCount++;
            return Task.CompletedTask;
        }

        public Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new OnxNotConnectedException();

            _sentCommands.Add(command);

            if (_responseQueue.TryDequeue(out var response))
                _lines.Writer.TryWrite(response);

            return Task.CompletedTask;
        }

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            return await _lines.Reader.ReadAsync(cancellationToken);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            IsConnected = false;
            _lines.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
