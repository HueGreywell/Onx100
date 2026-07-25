namespace Onx100Driver.Protocol;

/// <summary>
/// Base type for all parsed ONX-100 protocol messages.
/// Each concrete type represents a specific device response or event.
/// </summary>
public abstract record OnxMessage;

/// <summary>Connection greeting: *HELLO ONX-100 FW:x.xx</summary>
public sealed record OnxHelloMessage(string Model, string Firmware) : OnxMessage;

/// <summary>Device busy (another client is connected): *BUSY</summary>
public sealed record OnxBusyMessage : OnxMessage;

/// <summary>Command acknowledged successfully: OK</summary>
public sealed record OnxOkMessage : OnxMessage;

/// <summary>Device closing the connection: BYE</summary>
public sealed record OnxByeMessage : OnxMessage;

/// <summary>Power state query response: PWR OFF|WARM|ON|COOL</summary>
public sealed record OnxPowerMessage(OnxPowerState State) : OnxMessage;

/// <summary>Unsolicited power transition event: EVT PWR ON|OFF</summary>
public sealed record OnxPowerEventMessage(OnxPowerState State) : OnxMessage;

/// <summary>Input query response: IN 1-4</summary>
public sealed record OnxInputMessage(int Input) : OnxMessage;

/// <summary>Volume query response: VOL 0-100</summary>
public sealed record OnxVolumeMessage(int Volume) : OnxMessage;

/// <summary>Mute query response: MUTE ON|OFF</summary>
public sealed record OnxMuteMessage(bool IsMuted) : OnxMessage;

/// <summary>Error response: ERR 01|02|03</summary>
public sealed record OnxErrorMessage(int Code) : OnxMessage;

/// <summary>Unsolicited signal status event: EVT SIGNAL n OK|LOST</summary>
public sealed record OnxSignalEventMessage(int Signal, OnxSignalStatus Status) : OnxMessage;
