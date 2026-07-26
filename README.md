# ONX-100 Driver

C#/.NET driver for the ONX-100 AV presentation switcher. Provides a clean async API, automatic reconnection, and real-time state tracking over TCP.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) (only for the web panel)

## Project Structure

```
Onx100.Driver/          Class library — the driver itself
Onx100.Driver.Tests/    xUnit tests (protocol parser, transport, client)
Onx100.Demo/            Interactive CLI that exercises the driver
Onx100.Api/             ASP.NET Core API + SignalR hub (bridges driver to web)
ui/                     React panel controlling the device through the API
```

## Quick Start

Start the simulator in one terminal:

```sh
cd simulator
dotnet OnxSimulator.dll
```

### Run the CLI demo

```sh
cp Onx100.Demo/.env.example Onx100.Demo/.env   # adjust host/port if needed
dotnet run --project Onx100.Demo
```

Type `help` for available commands (`on`, `off`, `in 2`, `vol 50`, `mute on`, etc.).

### Run the tests

```sh
dotnet test
```

### Run the web panel

Terminal 1 — API server:

```sh
cp Onx100.Api/.env.example Onx100.Api/.env
dotnet run --project Onx100.Api
```

Terminal 2 — React dev server:

```sh
cd ui
npm install
npm run dev
```

Open `http://localhost:5173`. The Vite dev server proxies `/api` and `/hub` to the API on port 5234.

## Driver API

```csharp
await using var client = new Onx100Client("localhost", 4999);
await client.ConnectAsync();

await client.PowerOnAsync();
await client.WaitForPowerStateAsync(OnxPowerState.On);

await client.SelectInputAsync(2);
await client.SetVolumeAsync(75);
await client.SetMuteAsync(true);

// Query individual properties
var power = await client.QueryPowerAsync();
var volume = await client.QueryVolumeAsync();

// Or refresh everything at once
var state = await client.QueryAllAsync();

// React to changes
client.StateChanged += (_, e) => Console.WriteLine(e.State);
client.SignalChanged += (_, e) => Console.WriteLine($"Input {e.Signal}: {e.Status}");
client.Disconnected += (_, _) => Console.WriteLine("Lost connection");
```

### Configuration

| Parameter | Default | Description |
|---|---|---|
| `commandTimeout` | 5s | How long to wait for a device response |
| `autoReconnect` | `true` (TCP constructor) | Reconnect automatically on connection loss |
| `reconnectDelay` | 5s | Initial delay between reconnect attempts (doubles up to 2 min) |
| `maxReconnectAttempts` | unlimited | Give up after N failed attempts |

### Testability

The driver accepts an `IOnxTransport` interface internally, so protocol logic is tested without a live socket. The test suite uses an in-memory transport to verify parsing, command sequencing, error mapping, and reconnection behavior.

## Protocol Documentation

See [PROTOCOL.md](PROTOCOL.md) for the reverse-engineered protocol reference, including behaviors that differ from the vendor excerpt.
