import { api } from "./api";
import { useDevice } from "./hooks/useDevice";
import { ConnectionPanel } from "./components/ConnectionPanel";
import { PowerPanel } from "./components/PowerPanel";
import { InputPanel } from "./components/InputPanel";
import { VolumePanel } from "./components/VolumePanel";
import { EventLog } from "./components/EventLog";
import "./App.css";

function App() {
  const d = useDevice();
  const busy = d.loading !== null;

  return (
    <div className="app">
      <header>
        <h1>ONX-100</h1>
        <div className="connection-badge" data-connected={d.state.isConnected}>
          {d.state.isConnected ? "Connected" : "Disconnected"}
        </div>
      </header>

      <div className="panels">
        <ConnectionPanel
          host={d.host}
          port={d.port}
          isConnected={d.state.isConnected}
          loading={busy}
          model={d.state.model}
          firmware={d.state.firmware}
          onHostChange={d.setHost}
          onPortChange={d.setPort}
          onConnect={d.handleConnect}
          onDisconnect={d.handleDisconnect}
        />
        <PowerPanel
          power={d.state.power}
          isConnected={d.state.isConnected}
          loading={busy}
          onPowerOn={() => d.exec("Power On", api.powerOn)}
          onPowerOff={() => d.exec("Power Off", api.powerOff)}
        />
        <InputPanel
          selectedInput={d.state.selectedInput}
          signals={d.state.signals}
          isConnected={d.state.isConnected}
          loading={busy}
          onSelectInput={(i) =>
            d.exec(`Input ${i}`, () => api.selectInput(i))
          }
        />
        <VolumePanel
          volume={d.volumeInput}
          isMuted={d.state.isMuted}
          isConnected={d.state.isConnected}
          loading={busy}
          onVolumeChange={d.setVolumeInput}
          onVolumeCommit={() =>
            d.exec(`Volume ${d.volumeInput}`, () =>
              api.setVolume(d.volumeInput)
            )
          }
          onToggleMute={() =>
            d.exec(d.state.isMuted ? "Unmute" : "Mute", () =>
              api.setMute(!d.state.isMuted)
            )
          }
        />
      </div>

      <EventLog log={d.log} onClear={() => d.setLog([])} />

      <div className={`loading-bar ${d.loading ? "active" : ""}`} />
    </div>
  );
}

export default App;
