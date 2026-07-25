using Onx100Driver.Protocol;

namespace Onx100.Driver.Tests.Protocol;

public class OnxProtocolParserTests
{
    [Fact]
    public void Parse_Ok_ReturnsOkMessage()
    {
        var result = OnxProtocolParser.Parse("OK");
        Assert.IsType<OnxOkMessage>(result);
    }

    [Fact]
    public void Parse_Busy_ReturnsBusyMessage()
    {
        var result = OnxProtocolParser.Parse("*BUSY");
        Assert.IsType<OnxBusyMessage>(result);
    }

    [Fact]
    public void Parse_Bye_ReturnsByeMessage()
    {
        var result = OnxProtocolParser.Parse("BYE");
        Assert.IsType<OnxByeMessage>(result);
    }

    [Fact]
    public void Parse_UnknownSingleWord_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("BOGUS"));
    }

    [Fact]
    public void Parse_Hello_ReturnsModelAndFirmware()
    {
        var result = OnxProtocolParser.Parse("*HELLO ONX-100 FW:2.13");
        var hello = Assert.IsType<OnxHelloMessage>(result);
        Assert.Equal("ONX-100", hello.Model);
        Assert.Equal("2.13", hello.Firmware);
    }

    [Fact]
    public void Parse_Hello_MissingFirmwareMarker_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("*HELLO ONX-100"));
    }

    [Theory]
    [InlineData("PWR OFF", OnxPowerState.Off)]
    [InlineData("PWR WARM", OnxPowerState.Warm)]
    [InlineData("PWR ON", OnxPowerState.On)]
    [InlineData("PWR COOL", OnxPowerState.Cool)]
    public void Parse_PowerState_ReturnsCorrectState(string line, OnxPowerState expected)
    {
        var result = OnxProtocolParser.Parse(line);
        var pwr = Assert.IsType<OnxPowerMessage>(result);
        Assert.Equal(expected, pwr.State);
    }

    [Fact]
    public void Parse_PowerState_Invalid_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("PWR MAYBE"));
    }

    [Theory]
    [InlineData("IN 1", 1)]
    [InlineData("IN 2", 2)]
    [InlineData("IN 3", 3)]
    [InlineData("IN 4", 4)]
    public void Parse_Input_ReturnsCorrectValue(string line, int expected)
    {
        var result = OnxProtocolParser.Parse(line);
        var input = Assert.IsType<OnxInputMessage>(result);
        Assert.Equal(expected, input.Input);
    }

    [Theory]
    [InlineData("IN 0")]
    [InlineData("IN 5")]
    [InlineData("IN -1")]
    [InlineData("IN 99")]
    public void Parse_Input_OutOfRange_Throws(string line)
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse(line));
    }

    [Fact]
    public void Parse_Input_NonNumeric_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("IN abc"));
    }

    [Theory]
    [InlineData("VOL 00", 0)]
    [InlineData("VOL 0A", 10)]
    [InlineData("VOL 40", 64)]
    [InlineData("VOL 64", 100)]
    [InlineData("VOL 32", 50)]
    public void Parse_Volume_ParsesHexCorrectly(string line, int expected)
    {
        var result = OnxProtocolParser.Parse(line);
        var vol = Assert.IsType<OnxVolumeMessage>(result);
        Assert.Equal(expected, vol.Volume);
    }

    [Fact]
    public void Parse_Volume_OverMax_Throws()
    {
        // 0x65 = 101, out of 0-100 range
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("VOL 65"));
    }

    [Fact]
    public void Parse_Volume_InvalidHex_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("VOL XY"));
    }

    [Fact]
    public void Parse_MuteOn_ReturnsIsMutedTrue()
    {
        var result = OnxProtocolParser.Parse("MUTE ON");
        var mute = Assert.IsType<OnxMuteMessage>(result);
        Assert.True(mute.IsMuted);
    }

    [Fact]
    public void Parse_MuteOff_ReturnsIsMutedFalse()
    {
        var result = OnxProtocolParser.Parse("MUTE OFF");
        var mute = Assert.IsType<OnxMuteMessage>(result);
        Assert.False(mute.IsMuted);
    }

    [Fact]
    public void Parse_Mute_InvalidValue_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("MUTE MAYBE"));
    }

    [Theory]
    [InlineData("ERR 01", 1)]
    [InlineData("ERR 02", 2)]
    [InlineData("ERR 03", 3)]
    public void Parse_Error_ReturnsCorrectCode(string line, int expected)
    {
        var result = OnxProtocolParser.Parse(line);
        var err = Assert.IsType<OnxErrorMessage>(result);
        Assert.Equal(expected, err.Code);
    }

    [Theory]
    [InlineData("ERR 00")]
    [InlineData("ERR 04")]
    [InlineData("ERR 99")]
    public void Parse_Error_OutOfRange_Throws(string line)
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse(line));
    }

    [Fact]
    public void Parse_Error_NonNumeric_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("ERR abc"));
    }

    [Theory]
    [InlineData("EVT PWR ON", OnxPowerState.On)]
    [InlineData("EVT PWR OFF", OnxPowerState.Off)]
    public void Parse_PowerEvent_ReturnsCorrectState(string line, OnxPowerState expected)
    {
        var result = OnxProtocolParser.Parse(line);
        var evt = Assert.IsType<OnxPowerEventMessage>(result);
        Assert.Equal(expected, evt.State);
    }

    [Fact]
    public void Parse_PowerEvent_WarmState_Parses()
    {
        var result = OnxProtocolParser.Parse("EVT PWR WARM");
        var evt = Assert.IsType<OnxPowerEventMessage>(result);
        Assert.Equal(OnxPowerState.Warm, evt.State);
    }

    [Theory]
    [InlineData("EVT SIGNAL 2 OK", 2, OnxSignalStatus.Ok)]
    [InlineData("EVT SIGNAL 4 LOST", 4, OnxSignalStatus.Lost)]
    [InlineData("EVT SIGNAL 1 OK", 1, OnxSignalStatus.Ok)]
    [InlineData("EVT SIGNAL 3 LOST", 3, OnxSignalStatus.Lost)]
    public void Parse_SignalEvent_ReturnsCorrectValues(string line, int signal, OnxSignalStatus status)
    {
        var result = OnxProtocolParser.Parse(line);
        var evt = Assert.IsType<OnxSignalEventMessage>(result);
        Assert.Equal(signal, evt.Signal);
        Assert.Equal(status, evt.Status);
    }

    [Fact]
    public void Parse_SignalEvent_MissingStatus_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("EVT SIGNAL 2"));
    }

    [Fact]
    public void Parse_SignalEvent_InvalidStatus_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("EVT SIGNAL 2 BAD"));
    }

    [Fact]
    public void Parse_SignalEvent_NonNumericSignal_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("EVT SIGNAL abc OK"));
    }

    [Fact]
    public void Parse_UnknownEvent_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("EVT UNKNOWN 1"));
    }

    [Fact]
    public void Parse_UnknownPrefix_Throws()
    {
        Assert.Throws<OnxProtocolParseException>(() => OnxProtocolParser.Parse("BOGUS data"));
    }

    [Fact]
    public void Parse_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => OnxProtocolParser.Parse(null!));
    }
}
