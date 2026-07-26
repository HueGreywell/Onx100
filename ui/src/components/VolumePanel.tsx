interface VolumePanelProps {
  volume: number;
  isMuted: boolean | null;
  isConnected: boolean;
  loading: boolean;
  onVolumeChange: (value: number) => void;
  onVolumeCommit: () => void;
  onToggleMute: () => void;
}

export function VolumePanel({
  volume,
  isMuted,
  isConnected,
  loading,
  onVolumeChange,
  onVolumeCommit,
  onToggleMute,
}: VolumePanelProps) {
  return (
    <section className="panel">
      <h2>Volume</h2>
      <div className="volume-control">
        <input
          type="range"
          min={0}
          max={100}
          value={volume}
          onChange={(e) => onVolumeChange(Number(e.target.value))}
          onMouseUp={onVolumeCommit}
          onTouchEnd={onVolumeCommit}
          disabled={!isConnected || loading}
        />
        <span className="volume-value">{volume}</span>
      </div>
      <div className="button-row">
        <button
          className={`mute-btn ${isMuted ? "muted" : ""}`}
          onClick={onToggleMute}
          disabled={!isConnected || loading}
        >
          {isMuted ? "Unmute" : "Mute"}
        </button>
      </div>
    </section>
  );
}
