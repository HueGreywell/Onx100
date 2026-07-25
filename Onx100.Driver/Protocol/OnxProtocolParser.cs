using System.Globalization;

namespace Onx100Driver.Protocol;

public static class OnxProtocolParser
{
    public static OnxMessage Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var span = line.AsSpan();

        var firstSpace = span.IndexOf(' ');
        if (firstSpace < 0) return ParseSingleWord(span);

        var command = span[..firstSpace];
        var rest = span[(firstSpace + 1)..];

        return command switch
        {
            "*HELLO" => ParseHello(rest),
            "IN" => ParseInput(rest),
            "VOL" => ParseVolume(rest),
            "MUTE" => ParseMute(rest),
            "ERR" => ParseError(rest),
            "EVT" => ParseEvent(rest),
            "PWR" => new OnxPowerMessage(ParsePowerState(rest)),
            _ => throw new OnxProtocolParseException($"Unknown command prefix '{command}' in line: '{line}'")
        };
    }

    private static OnxMessage ParseSingleWord(ReadOnlySpan<char> span)
    {
        return span switch
        {
            "OK" => new OnxOkMessage(),
            "*BUSY" => new OnxBusyMessage(),
            "BYE" => new OnxByeMessage(),
            _ => throw new OnxProtocolParseException($"Unrecognized single-word response: '{span}'")
        };
    }

    private static OnxHelloMessage ParseHello(ReadOnlySpan<char> rest)
    {
        var fwMarker = rest.IndexOf(" FW:");
        if (fwMarker < 0) throw new OnxProtocolParseException($"Invalid HELLO format: '{rest}'");

        var model = rest[..fwMarker].ToString();
        var firmware = rest[(fwMarker + 4)..].ToString();
        return new OnxHelloMessage(model, firmware);
    }

    private static OnxInputMessage ParseInput(ReadOnlySpan<char> rest)
    {
        if (!int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new OnxProtocolParseException($"Invalid input value: '{rest}'");

        if (value is < 1 or > 4)
            throw new OnxProtocolParseException($"Invalid input value: '{rest}'");

        return new OnxInputMessage(value);
    }

    private static OnxVolumeMessage ParseVolume(ReadOnlySpan<char> rest)
    {
        if (!int.TryParse(rest, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            throw new OnxProtocolParseException($"Invalid volume value: '{rest}'");

        if (value is < 0 or > 100)
            throw new OnxProtocolParseException($"Invalid volume value: '{rest}'");

        return new OnxVolumeMessage(value);
    }

    private static OnxMuteMessage ParseMute(ReadOnlySpan<char> rest)
    {
        return rest switch
        {
            "ON" => new OnxMuteMessage(true),
            "OFF" => new OnxMuteMessage(false),
            _ => throw new OnxProtocolParseException($"Invalid mute value: '{rest}'")
        };
    }

    private static OnxErrorMessage ParseError(ReadOnlySpan<char> rest)
    {
        if (!int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var code))
            throw new OnxProtocolParseException($"Invalid error code: '{rest}'");

        if (code is < 1 or > 3)
            throw new OnxProtocolParseException($"Invalid error code: '{rest}'");

        return new OnxErrorMessage(code);
    }

    private static OnxMessage ParseEvent(ReadOnlySpan<char> rest)
    {
        if (rest.StartsWith("PWR ")) return new OnxPowerEventMessage(ParsePowerState(rest[4..]));

        if (rest.StartsWith("SIGNAL ")) return ParseSignalEvent(rest[7..]);

        throw new OnxProtocolParseException($"Unknown event type: '{rest}'");
    }

    private static OnxPowerState ParsePowerState(ReadOnlySpan<char> value)
    {
        return value switch
        {
            "OFF" => OnxPowerState.Off,
            "WARM" => OnxPowerState.Warm,
            "ON" => OnxPowerState.On,
            "COOL" => OnxPowerState.Cool,
            _ => throw new OnxProtocolParseException($"Invalid power state: '{value}'")
        };
    }

    private static OnxSignalEventMessage ParseSignalEvent(ReadOnlySpan<char> rest)
    {
        var space = rest.IndexOf(' ');
        if (space < 0) throw new OnxProtocolParseException($"Invalid signal event (missing status): '{rest}'");

        if (!int.TryParse(rest[..space], NumberStyles.None, CultureInfo.InvariantCulture, out var signal))
            throw new OnxProtocolParseException($"Invalid signal number: '{rest[..space]}'");

        var status = rest[(space + 1)..] switch
        {
            "OK" => OnxSignalStatus.Ok,
            "LOST" => OnxSignalStatus.Lost,
            _ => throw new OnxProtocolParseException($"Invalid signal status: '{rest[(space + 1)..]}'")
        };

        return new OnxSignalEventMessage(signal, status);
    }
}
