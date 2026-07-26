interface ConnectionPanelProps {
  host: string;
  port: number;
  isConnected: boolean;
  loading: boolean;
  model: string | null;
  firmware: string | null;
  onHostChange: (host: string) => void;
  onPortChange: (port: number) => void;
  onConnect: () => void;
  onDisconnect: () => void;
}

export function ConnectionPanel({
  host,
  port,
  isConnected,
  loading,
  model,
  firmware,
  onHostChange,
  onPortChange,
  onConnect,
  onDisconnect,
}: ConnectionPanelProps) {
  return (
    <section className="panel">
      <h2>Connection</h2>
      <div className="field-row">
        <label>Host</label>
        <input
          type="text"
          value={host}
          onChange={(e) => onHostChange(e.target.value)}
          disabled={isConnected}
        />
      </div>
      <div className="field-row">
        <label>Port</label>
        <input
          type="number"
          value={port}
          onChange={(e) => onPortChange(Number(e.target.value))}
          disabled={isConnected}
        />
      </div>
      <div className="button-row">
        <button
          onClick={isConnected ? onDisconnect : onConnect}
          disabled={loading}
          className={isConnected ? "btn-danger" : "btn-primary"}
        >
          {isConnected ? "Disconnect" : "Connect"}
        </button>
      </div>
      <div className={`device-info ${model ? "visible" : ""}`}>
        {model} &mdash; FW {firmware}
      </div>
    </section>
  );
}
