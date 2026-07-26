using dotenv.net;
using Onx100Driver;
using Onx100Driver.Transport;

DotEnv.Load();

var host = Environment.GetEnvironmentVariable("ONX_HOST") ?? "127.0.0.1";
var port = int.TryParse(Environment.GetEnvironmentVariable("ONX_PORT"), out var envPort) ? envPort : 4999;

using var exitCts = new CancellationTokenSource();
var isAtPrompt = false;

Console.CancelKeyPress += (_, _) => { };

await using var client = new Onx100Client(host, port);

client.StateChanged += (_, e) =>
{
    if (e.State.IsConnected)
    {
        WriteLog("State changed");
        PrintState(e.State);
        if (isAtPrompt) WritePrompt();
    }
};

client.SignalChanged += (_, e) =>
{
    WriteLog($"Signal {e.Signal} -> {e.Status}");
    if (isAtPrompt) WritePrompt();
};

client.Disconnected += (_, _) =>
{
    WriteLog("Disconnected");
    if (isAtPrompt) WritePrompt();
};

client.Reconnecting += (_, _) =>
{
    WriteLog("Reconnecting");
};

client.ReconnectionFailed += (_, e) =>
{
    WriteError($"Reconnection failed after {e.Attempts} attempts: {e.LastException.Message}");
    if (isAtPrompt) WritePrompt();
};

// Auto-connect on startup
try
{
    WriteLog($"Connecting to {host}:{port}");
    await client.ConnectAsync(exitCts.Token);
}
catch (OperationCanceledException)
{
    return;
}
catch (Exception ex)
{
    WriteError($"Connection failed: {ex.Message}");
}

PrintHelp();

while (!exitCts.IsCancellationRequested)
{
    WritePrompt();
    isAtPrompt = true;
    var line = Console.ReadLine();
    isAtPrompt = false;
    if (line is null) break;

    var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) continue;

    var command = parts[0].ToLowerInvariant();

    try
    {
        switch (command)
        {
            case "connect":
                if (!client.IsConnected)
                {
                    WriteLog($"Connecting to {host}:{port}");
                    await client.ConnectAsync();
                }
                break;

            case "disconnect":
                await client.DisconnectAsync();
                PrintState(client.CurrentState);
                break;

            case "on":
                await client.PowerOnAsync(exitCts.Token);
                break;

            case "off":
                await client.PowerOffAsync(exitCts.Token);
                break;

            case "in" when parts.Length > 1:
                await client.SelectInputAsync(int.Parse(parts[1]), exitCts.Token);
                break;

            case "in":
                var input = await client.QueryInputAsync(exitCts.Token);
                WriteInfo($"Input: {input}");
                break;

            case "vol" when parts.Length > 1:
                await client.SetVolumeAsync(int.Parse(parts[1]), exitCts.Token);
                PrintState(client.CurrentState);
                break;

            case "vol":
                var vol = await client.QueryVolumeAsync(exitCts.Token);
                WriteInfo($"Volume: {vol}");
                break;

            case "mute" when parts.Length > 1:
                var muted = parts[1].ToLowerInvariant() is "on" or "true" or "1";
                await client.SetMuteAsync(muted, exitCts.Token);
                break;

            case "mute":
                var m = await client.QueryMuteAsync(exitCts.Token);
                WriteInfo($"Mute: {(m ? "On" : "Off")}");
                break;

            case "pwr":
                var pwr = await client.QueryPowerAsync(exitCts.Token);
                WriteInfo($"Power: {pwr}");
                break;

            case "state":
                PrintState(client.CurrentState);
                break;

            case "clear":
                Console.Clear();
                break;

            case "help":
                PrintHelp();
                break;

            case "quit" or "exit":
                if (client.IsConnected)
                    await client.DisconnectAsync();
                return;

            default:
                WriteError($"Unknown command: {command}. Type 'help' for usage.");
                break;
        }
    }
    catch (OnxUnavailableException ex)
    {
        WriteError($"Unavailable: {ex.Message}");
    }
    catch (OnxConnectionTakenException)
    {
        WriteError("Device is busy (another client is connected).");
    }
    catch (OnxNotConnectedException)
    {
        WriteError("Not connected. Use 'connect' first.");
    }
    catch (OnxCommandException ex)
    {
        WriteError($"Command error: {ex.Message}");
    }
    catch (OnxTransportException ex)
    {
        WriteError($"Transport error: {ex.Message}");
    }
    catch (TimeoutException)
    {
        WriteError("Command timed out.");
    }
    catch (ArgumentOutOfRangeException ex)
    {
        WriteError(ex.Message);
    }
    catch (FormatException)
    {
        WriteError("Invalid number format.");
    }
    catch (OperationCanceledException)
    {
        break;
    }
}

void PrintState(OnxDeviceState s)
{
    if (!s.IsConnected)
    {
        Console.WriteLine("  Disconnected");
        return;
    }

    Console.ForegroundColor = ConsoleColor.Cyan;

    var model = s.Model ?? "?";
    var fw = s.Firmware ?? "?";
    var power = s.Power?.ToString() ?? "?";
    var inp = s.SelectedInput?.ToString() ?? "-";
    var vol = s.Volume?.ToString() ?? "-";
    var mute = s.IsMuted switch { true => "On", false => "Off", null => "-" };

    Console.WriteLine($"  {model} v{fw} | Power: {power} | Vol: {vol} | Mute: {mute} | Input: {inp}");

    if (s.Signals.Count > 0)
    {
        var sigs = string.Join("  ", s.Signals.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        Console.WriteLine($"  Signals: {sigs}");
    }

    Console.ResetColor();
}

void WriteLog(string message)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  [event] {message}");
    Console.ResetColor();
}

void WriteError(string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  {message}");
    Console.ResetColor();
}

void WriteInfo(string message)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  {message}");
    Console.ResetColor();
}

void WritePrompt()
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("onx> ");
    Console.ResetColor();
}

void PrintHelp()
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine("  ONX-100 Demo CLI");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("  ────────────────────────────────────────");
    Console.ResetColor();
    Console.WriteLine("  connect                 Connect to device");
    Console.WriteLine("  disconnect              Disconnect from device");
    Console.WriteLine("  on                      Power on");
    Console.WriteLine("  off                     Power off");
    Console.WriteLine("  pwr                     Query power state");
    Console.WriteLine("  in <1-4>                Select input");
    Console.WriteLine("  in                      Query current input");
    Console.WriteLine("  vol <0-100>             Set volume");
    Console.WriteLine("  vol                     Query current volume");
    Console.WriteLine("  mute <on|off>           Set mute");
    Console.WriteLine("  mute                    Query mute state");
    Console.WriteLine("  state                   Print full device state");
    Console.WriteLine("  clear                   Clear the screen");
    Console.WriteLine("  help                    Show this help");
    Console.WriteLine("  quit                    Disconnect and exit");
    Console.WriteLine();
}
