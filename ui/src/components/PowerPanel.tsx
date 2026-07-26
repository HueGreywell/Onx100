const POWER_COLOR: Record<string, string> = {
  Off: "#888",
  Warm: "#f0ad4e",
  On: "#5cb85c",
  Cool: "#5bc0de",
};

interface PowerPanelProps {
  power: string | null;
  isConnected: boolean;
  loading: boolean;
  onPowerOn: () => void;
  onPowerOff: () => void;
}

export function PowerPanel({
  power,
  isConnected,
  loading,
  onPowerOn,
  onPowerOff,
}: PowerPanelProps) {
  return (
    <section className="panel">
      <h2>Power</h2>
      <div className="power-status">
        <span
          className="power-dot"
          style={{
            backgroundColor: power ? POWER_COLOR[power] : "#888",
          }}
        />
        <span>{power ?? "Unknown"}</span>
      </div>
      <div className="button-row">
        <button
          onClick={onPowerOn}
          disabled={!isConnected || loading}
          className="btn-primary"
        >
          Power On
        </button>
        <button
          onClick={onPowerOff}
          disabled={!isConnected || loading}
          className="btn-secondary"
        >
          Power Off
        </button>
      </div>
    </section>
  );
}
