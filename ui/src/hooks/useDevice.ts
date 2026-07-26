import { useCallback, useEffect, useState } from "react";
import {
  HubConnectionBuilder,
  HubConnectionState,
} from "@microsoft/signalr";
import { api } from "../api";
import type { DeviceEvent, DeviceState } from "../types";

const EMPTY_STATE: DeviceState = {
  isConnected: false,
  model: null,
  firmware: null,
  power: null,
  selectedInput: null,
  volume: null,
  isMuted: null,
  signals: {},
};

export interface LogEntry {
  time: string;
  message: string;
  type: "info" | "error" | "event";
}

export function useDevice() {
  const [state, setState] = useState<DeviceState>(EMPTY_STATE);
  const [host, setHost] = useState("localhost");
  const [port, setPort] = useState(4999);
  const [log, setLog] = useState<LogEntry[]>([]);
  const [loading, setLoading] = useState<string | null>(null);
  const [volumeInput, setVolumeInput] = useState(50);

  const addLog = useCallback(
    (message: string, type: LogEntry["type"] = "info") => {
      const time = new Date().toLocaleTimeString();
      setLog((prev) => [...prev.slice(-99), { time, message, type }]);
    },
    []
  );

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/hub")
      .withAutomaticReconnect()
      .build();

    connection.on("DeviceEvent", (evt: DeviceEvent) => {
      setState(evt.state);
      if (evt.message) {
        addLog(evt.message, "event");
      }
      if (evt.type === "disconnected") {
        addLog("Device disconnected", "event");
      }
    });

    connection.start().catch(() => {});

    return () => {
      if (connection.state !== HubConnectionState.Disconnected) {
        connection.stop();
      }
    };
  }, [addLog]);

  useEffect(() => {
    if (state.volume !== null) setVolumeInput(state.volume);
  }, [state.volume]);

  useEffect(() => {
    api.getState().then(setState).catch(() => {});
  }, []);

  async function exec(label: string, fn: () => Promise<unknown>) {
    setLoading(label);
    try {
      await fn();
      addLog(`${label}: OK`);
    } catch (err) {
      addLog(`${label}: ${(err as Error).message}`, "error");
    } finally {
      setLoading(null);
    }
  }

  const handleConnect = () => exec("Connect", () => api.connect(host, port));
  const handleDisconnect = () => exec("Disconnect", () => api.disconnect());

  return {
    state,
    host,
    setHost,
    port,
    setPort,
    log,
    setLog,
    loading,
    volumeInput,
    setVolumeInput,
    exec,
    handleConnect,
    handleDisconnect,
  };
}
