using Onx100Driver.Protocol;
using Onx100Driver.Transport;

namespace Onx100Driver;

public sealed class Onx100Client : IAsyncDisposable
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(5);

    private readonly IOnxTransport _transport;
    private readonly TimeSpan _commandTimeout;
    private readonly bool _autoReconnect;
    private readonly TimeSpan _reconnectDelay;
    private readonly int? _maxReconnectAttempts;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly object _pendingLock = new();

    private OnxDeviceState _state = OnxDeviceState.Disconnected;
    private CancellationTokenSource? _connectionCts;
    private Task? _readerTask;
    private Task? _reconnectTask;
    private CancellationTokenSource? _reconnectCts;
    private TaskCompletionSource<OnxMessage>? _pendingResponse;
    private bool _disposed;

    /// <summary>
    /// Creates a client that connects to the ONX-100 over TCP.
    /// </summary>
    public Onx100Client(
        string host,
        int port,
        TimeSpan? commandTimeout = null,
        bool autoReconnect = true,
        TimeSpan? reconnectDelay = null,
        int? maxReconnectAttempts = null)
        : this(
            new OnxTcpTransport(host, port),
            commandTimeout,
            autoReconnect,
            reconnectDelay,
            maxReconnectAttempts)
    {
    }

    /// <summary>
    /// Creates a client with the specified transport implementation.
    /// </summary>
    internal Onx100Client(
        IOnxTransport transport,
        TimeSpan? commandTimeout = null,
        bool autoReconnect = false,
        TimeSpan? reconnectDelay = null,
        int? maxReconnectAttempts = null)
    {
        _transport = transport;
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
        _autoReconnect = autoReconnect;
        _reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(5);
        _maxReconnectAttempts = maxReconnectAttempts;

        if (_commandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(commandTimeout), "Command timeout must be positive.");

        if (_maxReconnectAttempts is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxReconnectAttempts), "Max reconnect attempts must be positive.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;
        _connectionCts?.Cancel();
        _reconnectCts?.Cancel();
        FailPending(new ObjectDisposedException(nameof(Onx100Client)));

        if (_reconnectTask is not null)
            await _reconnectTask.ConfigureAwait(false);

        await _transport.DisposeAsync().ConfigureAwait(false);
        _commandLock.Dispose();
        _connectionCts?.Dispose();
        _reconnectCts?.Dispose();
    }

    /// <summary>Raised whenever <see cref="CurrentState"/> changes.</summary>
    public event EventHandler<OnxStateChangedEventArgs>? StateChanged;

    /// <summary>Raised on unsolicited EVT SIGNAL events from the device.</summary>
    public event EventHandler<OnxSignalChangedEventArgs>? SignalChanged;

    /// <summary>Raised when the connection is lost (after cleanup).</summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Raised when automatic reconnection has exhausted
    /// <c>maxReconnectAttempts</c> and will no longer retry.
    /// Not raised when <c>maxReconnectAttempts</c> is null (unlimited).
    /// </summary>
    public event EventHandler<OnxReconnectFailedEventArgs>? ReconnectionFailed;

    /// <summary>Current device state snapshot. Thread-safe.</summary>
    public OnxDeviceState CurrentState
    {
        get { lock (_stateLock) { return _state; } }
    }

    /// <summary>Whether the underlying transport is connected.</summary>
    public bool IsConnected => _transport.IsConnected;

    /// <summary>
    /// Opens the TCP connection, consumes the device greeting, and starts
    /// the background reader loop.
    /// </summary>
    /// <exception cref="OnxTransportException">
    /// The device sent *BUSY (another client is connected) or the greeting
    /// was not recognized.
    /// </exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_transport.IsConnected) return;

        await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Consume the initial greeting before accepting commands.
            var greetingLine = await _transport.ReadLineAsync(cancellationToken)
                .WaitAsync(_commandTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (greetingLine is null)
                throw new OnxConnectionClosedException("The device closed the connection before sending a greeting.");

            var greeting = OnxProtocolParser.Parse(greetingLine);

            if (greeting is OnxBusyMessage)
                throw new OnxConnectionTakenException();

            if (greeting is not OnxHelloMessage hello)
                throw new OnxUnexpectedResponseException($"Unexpected greeting: '{greetingLine}'");

            _connectionCts = new CancellationTokenSource();
            UpdateState(CurrentState with
            {
                IsConnected = true,
                Model = hello.Model,
                Firmware = hello.Firmware
            });

            _readerTask = ReaderLoopAsync(_connectionCts.Token);

            await RefreshState().ConfigureAwait(false);
        }
        catch
        {
            await _transport.DisconnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Cleanly disconnects from the device.</summary>
    public async Task DisconnectAsync()
    {
        ThrowIfDisposed();
        _connectionCts?.Cancel();
        FailPending(new OperationCanceledException("Disconnected by the caller."));
        await _transport.DisconnectAsync().ConfigureAwait(false);
        SetDisconnected();
    }

    /// <summary>Queries the current power state.</summary>
    public async Task<OnxPowerState> QueryPowerAsync(CancellationToken ct = default)
    {
        var msg = await ExecuteAsync("PWR ?", ct).ConfigureAwait(false);
        var power = Require<OnxPowerMessage>(msg, "PWR ?");
        UpdateState(CurrentState with { Power = power.State });
        return power.State;
    }

    /// <summary>
    /// Sends PWR ON. The device transitions Off → Warm → On asynchronously.
    /// Subscribe to <see cref="StateChanged"/> or call
    /// <see cref="WaitForPowerStateAsync"/> to detect the stable On state.
    /// </summary>
    public Task PowerOnAsync(CancellationToken ct = default) =>
        SetPowerAsync("PWR ON", OnxPowerState.Warm, ct);

    /// <summary>
    /// Sends PWR OFF. The device transitions On → Cool → Off asynchronously.
    /// </summary>
    public Task PowerOffAsync(CancellationToken ct = default) =>
        SetPowerAsync("PWR OFF", OnxPowerState.Cool, ct);

    /// <summary>
    /// Waits until the device reaches the specified power state.
    /// Returns immediately if already in that state.
    /// </summary>
    public Task WaitForPowerStateAsync(OnxPowerState target, CancellationToken ct = default)
    {
        if (CurrentState.Power == target) return Task.CompletedTask;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStateChanged(object? sender, OnxStateChangedEventArgs e)
        {
            if (e.State.Power == target)
                tcs.TrySetResult();
        }

        void OnDisconnected(object? sender, EventArgs e)
        {
            tcs.TrySetException(new OperationCanceledException("Disconnected while waiting for power state."));
        }

        StateChanged += OnStateChanged;
        Disconnected += OnDisconnected;

        // Check again after subscribing to close the race window.
        if (CurrentState.Power == target)
            tcs.TrySetResult();

        return tcs.Task.ContinueWith(t =>
        {
            StateChanged -= OnStateChanged;
            Disconnected -= OnDisconnected;
            t.GetAwaiter().GetResult();
        }, ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task SetPowerAsync(string command, OnxPowerState transition, CancellationToken ct)
    {
        await ExpectOkAsync(command, ct).ConfigureAwait(false);

        var current = CurrentState.Power;
        var needsUpdate = transition == OnxPowerState.Warm
            ? current != OnxPowerState.On
            : current != OnxPowerState.Off;

        if (needsUpdate) UpdateState(CurrentState with { Power = transition });
    }

    /// <summary>
    /// Selects an input (1-4). Only available when fully powered on.
    /// </summary>
    public async Task SelectInputAsync(int input, CancellationToken ct = default)
    {
        if (input is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(input), "The ONX-100 input must be between 1 and 4.");

        await ExpectOkAsync($"IN {input}", ct).ConfigureAwait(false);
        UpdateState(CurrentState with { SelectedInput = input });
    }

    /// <summary>Queries the currently selected input.</summary>
    public async Task<int> QueryInputAsync(CancellationToken ct = default)
    {
        var msg = await ExecuteAsync("IN ?", ct).ConfigureAwait(false);
        var input = Require<OnxInputMessage>(msg, "IN ?").Input;
        UpdateState(CurrentState with { SelectedInput = input });
        return input;
    }

    /// <summary>Sets the volume (0-100 decimal).</summary>
    public async Task SetVolumeAsync(int volume, CancellationToken ct = default)
    {
        if (volume is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(volume), "The ONX-100 volume must be between 0 and 100.");

        await ExpectOkAsync($"VOL {volume}", ct).ConfigureAwait(false);
        UpdateState(CurrentState with { Volume = volume });
    }

    /// <summary>Queries the current volume.</summary>
    public async Task<int> QueryVolumeAsync(CancellationToken ct = default)
    {
        var msg = await ExecuteAsync("VOL ?", ct).ConfigureAwait(false);
        var volume = Require<OnxVolumeMessage>(msg, "VOL ?").Volume;
        UpdateState(CurrentState with { Volume = volume });
        return volume;
    }

    /// <summary>Sets or clears mute.</summary>
    public async Task SetMuteAsync(bool muted, CancellationToken ct = default)
    {
        var command = muted ? "MUTE ON" : "MUTE OFF";
        await ExpectOkAsync(command, ct).ConfigureAwait(false);
        UpdateState(CurrentState with { IsMuted = muted });
    }

    /// <summary>Queries the current mute state.</summary>
    public async Task<bool> QueryMuteAsync(CancellationToken ct = default)
    {
        var msg = await ExecuteAsync("MUTE ?", ct).ConfigureAwait(false);
        var muted = Require<OnxMuteMessage>(msg, "MUTE ?").IsMuted;
        UpdateState(CurrentState with { IsMuted = muted });
        return muted;
    }

    private async Task ExpectOkAsync(string command, CancellationToken ct)
    {
        var msg = await ExecuteAsync(command, ct).ConfigureAwait(false);
        Require<OnxOkMessage>(msg, command);
    }

    /// <summary>
    /// Sends a command and waits for the corresponding response.
    /// Reconnects transparently if the connection was dropped.
    /// </summary>
    private async Task<OnxMessage> ExecuteAsync(string command, CancellationToken ct)
    {
        ThrowIfDisposed();

        await _commandLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_transport.IsConnected)
            {
                if (!_autoReconnect) throw new OnxNotConnectedException();

                await ConnectAsync(ct).ConfigureAwait(false);
            }

            var pending = new TaskCompletionSource<OnxMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_pendingLock)
            {
                _pendingResponse = pending;
            }

            try
            {
                await _transport.SendCommandAsync(command, ct).ConfigureAwait(false);
                return await pending.Task.WaitAsync(_commandTimeout, ct).ConfigureAwait(false);
            }
            finally
            {
                lock (_pendingLock)
                {
                    if (ReferenceEquals(_pendingResponse, pending)) _pendingResponse = null;
                }
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Reads incoming lines from the transport, dispatching unsolicited events
    /// and completing pending command responses.
    /// </summary>
    private async Task ReaderLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _transport.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;

                OnxMessage message;
                try
                {
                    message = OnxProtocolParser.Parse(line);
                }
                catch (OnxProtocolParseException)
                {
                    continue;
                }

                switch (message)
                {
                    case OnxPowerEventMessage powerEvt:
                        UpdateState(CurrentState with { Power = powerEvt.State });
                        if (powerEvt.State == OnxPowerState.On)
                        {
                            _ = RefreshState();
                        }
                        continue;

                    case OnxSignalEventMessage signalEvt:
                        var signals = new Dictionary<int, OnxSignalStatus>(CurrentState.Signals)
                        {
                            [signalEvt.Signal] = signalEvt.Status
                        };
                        UpdateState(CurrentState with { Signals = signals });
                        SignalChanged?.Invoke(this,
                            new OnxSignalChangedEventArgs(signalEvt.Signal, signalEvt.Status));
                        continue;

                    case OnxByeMessage:
                        break;

                    default:
                        lock (_pendingLock)
                        {
                            _pendingResponse?.TrySetResult(message);
                        }
                        continue;
                }

                break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            FailPending(ex);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                FailPending(new OnxConnectionClosedException("The ONX-100 connection was closed by the device."));
                await _transport.DisconnectAsync().ConfigureAwait(false);
                SetDisconnected();

                if (_autoReconnect && !_disposed)
                {
                    _reconnectCts = new CancellationTokenSource();
                    _reconnectTask = ReconnectAsync(_reconnectCts.Token);
                }
            }
        }
    }

    /// <summary>
    /// Queries all device state available in the current power state.
    /// PWR, VOL, and MUTE queries work in all states; IN only works when fully ON.
    /// </summary>
    private async Task RefreshState()
    {
        try
        {
            var power = await QueryPowerAsync().ConfigureAwait(false);
            await QueryVolumeAsync().ConfigureAwait(false);
            await QueryMuteAsync().ConfigureAwait(false);
            if (power == OnxPowerState.On) await QueryInputAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort; state will update on subsequent queries.
        }
    }

    /// <summary>
    /// Automatic reconnection loop with exponential backoff.
    /// Retries up to <see cref="_maxReconnectAttempts"/> times (unlimited when null).
    /// </summary>
    private async Task ReconnectAsync(CancellationToken ct)
    {
        var delay = _reconnectDelay;
        var maxDelay = TimeSpan.FromMinutes(2);
        var attempt = 0;
        Exception? lastException = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);

                if (_transport.IsConnected) return;

                await ConnectAsync(ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                attempt++;

                if (_maxReconnectAttempts.HasValue && attempt >= _maxReconnectAttempts.Value)
                {
                    ReconnectionFailed?.Invoke(this, new OnxReconnectFailedEventArgs(attempt, ex));
                    return;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, maxDelay.Ticks));
            }
        }
    }

    private void UpdateState(OnxDeviceState newState)
    {
        lock (_stateLock)
        {
            _state = newState;
        }

        StateChanged?.Invoke(this, new OnxStateChangedEventArgs(newState));
    }

    private void SetDisconnected()
    {
        var wasConnected = CurrentState.IsConnected;
        UpdateState(CurrentState with { IsConnected = false });

        if (wasConnected) Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void FailPending(Exception exception)
    {
        lock (_pendingLock)
        {
            _pendingResponse?.TrySetException(exception);
        }
    }

    private static T Require<T>(OnxMessage message, string command) where T : OnxMessage
    {
        if (message is OnxErrorMessage error)
        {
            throw error.Code switch
            {
                1 => new OnxUnknownCommandException(command),
                2 => new OnxInvalidParameterException(command),
                3 => new OnxUnavailableException(command),
                _ => new OnxCommandException(command, $"ONX-100 command '{command}' failed with ERR {error.Code:00}."),
            };
        }

        return message as T ?? throw new OnxUnexpectedResponseException($"Unexpected response to '{command}': {message.GetType().Name}");
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
