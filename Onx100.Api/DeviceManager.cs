using Onx100Driver;

namespace Onx100Api;

public sealed class DeviceManager : IAsyncDisposable
{
    private Onx100Client? _client;

    public Action<DeviceEventDto>? OnEvent { get; set; }

    public DeviceStateDto CurrentState => _client is { } client
        ? MapState(client.CurrentState)
        : DeviceStateDto.Disconnected;

    public async Task<DeviceStateDto> ConnectAsync(string host, int port)
    {
        if (_client is not null)
        {
            UnwireEvents(_client);
            await _client.DisposeAsync();
        }

        var client = new Onx100Client(host, port);
        WireEvents(client);
        await client.ConnectAsync();
        _client = client;

        return CurrentState;
    }

    public async Task<DeviceStateDto> DisconnectAsync()
    {
        if (_client is not null)
        {
            UnwireEvents(_client);
            await _client.DisposeAsync();
            _client = null;
        }

        return CurrentState;
    }

    public async Task<DeviceStateDto> PowerOnAsync()
    {
        await GetClient().PowerOnAsync();
        return CurrentState;
    }

    public async Task<DeviceStateDto> PowerOffAsync()
    {
        await GetClient().PowerOffAsync();
        return CurrentState;
    }

    public async Task<DeviceStateDto> SelectInputAsync(int input)
    {
        await GetClient().SelectInputAsync(input);
        return CurrentState;
    }

    public async Task<DeviceStateDto> SetVolumeAsync(int level)
    {
        await GetClient().SetVolumeAsync(level);
        return CurrentState;
    }

    public async Task<DeviceStateDto> SetMuteAsync(bool enabled)
    {
        await GetClient().SetMuteAsync(enabled);
        return CurrentState;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            UnwireEvents(_client);
            await _client.DisposeAsync();
            _client = null;
        }
    }

    private Onx100Client GetClient() =>
        _client ?? throw new InvalidOperationException("Not connected to the device.");

    private void WireEvents(Onx100Client client)
    {
        client.StateChanged += OnStateChanged;
        client.SignalChanged += OnSignalChanged;
        client.Disconnected += OnDisconnected;
        client.Reconnecting += OnReconnecting;
        client.ReconnectionFailed += OnReconnectionFailed;
    }

    private void UnwireEvents(Onx100Client client)
    {
        client.StateChanged -= OnStateChanged;
        client.SignalChanged -= OnSignalChanged;
        client.Disconnected -= OnDisconnected;
        client.Reconnecting -= OnReconnecting;
        client.ReconnectionFailed -= OnReconnectionFailed;
    }

    private void OnStateChanged(object? sender, OnxStateChangedEventArgs e) =>
        OnEvent?.Invoke(new DeviceEventDto("stateChanged", MapState(e.State), null));

    private void OnSignalChanged(object? sender, OnxSignalChangedEventArgs e) =>
        OnEvent?.Invoke(new DeviceEventDto("signalChanged", CurrentState, $"Input {e.Signal}: {e.Status}"));

    private void OnDisconnected(object? sender, EventArgs e) =>
        OnEvent?.Invoke(new DeviceEventDto("disconnected", DeviceStateDto.Disconnected, "Device disconnected."));

    private void OnReconnecting(object? sender, EventArgs e) =>
        OnEvent?.Invoke(new DeviceEventDto("reconnecting", DeviceStateDto.Disconnected, "Reconnecting to device..."));

    private void OnReconnectionFailed(object? sender, OnxReconnectFailedEventArgs e) =>
        OnEvent?.Invoke(new DeviceEventDto("reconnectionFailed", DeviceStateDto.Disconnected,
            $"Reconnection failed after {e.Attempts} attempts."));

    private static DeviceStateDto MapState(OnxDeviceState state)
    {
        var signals = new Dictionary<string, string>();
        foreach (var (key, value) in state.Signals)
            signals[key.ToString()] = value.ToString();

        return new DeviceStateDto(
            state.IsConnected,
            state.Model,
            state.Firmware,
            state.Power?.ToString(),
            state.SelectedInput,
            state.Volume,
            state.IsMuted,
            signals);
    }
}

