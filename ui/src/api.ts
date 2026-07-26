import type { DeviceState } from "./types";

async function post<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  const data = await res.json();
  if (!res.ok) throw new Error(data.error ?? `Request failed: ${res.status}`);
  return data as T;
}

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path);
  const data = await res.json();
  if (!res.ok) throw new Error(data.error ?? `Request failed: ${res.status}`);
  return data as T;
}

export const api = {
  getState: () => get<DeviceState>("/api/state"),
  connect: (host: string, port: number) =>
    post<DeviceState>("/api/connect", { host, port }),
  disconnect: () => post<DeviceState>("/api/disconnect"),
  powerOn: () => post<DeviceState>("/api/power/on"),
  powerOff: () => post<DeviceState>("/api/power/off"),
  selectInput: (id: number) => post<DeviceState>(`/api/input/${id}`),
  setVolume: (level: number) => post<DeviceState>(`/api/volume/${level}`),
  setMute: (enabled: boolean) => post<DeviceState>(`/api/mute/${enabled}`),
};
