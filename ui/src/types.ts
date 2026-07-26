export interface DeviceState {
  isConnected: boolean;
  model: string | null;
  firmware: string | null;
  power: "Off" | "Warm" | "On" | "Cool" | null;
  selectedInput: number | null;
  volume: number | null;
  isMuted: boolean | null;
  signals: Record<string, string>;
}

export interface DeviceEvent {
  type: "stateChanged" | "signalChanged" | "disconnected" | "reconnecting" | "reconnectionFailed";
  state: DeviceState;
  message: string | null;
}
