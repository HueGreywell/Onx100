import type { LogEntry } from "../hooks/useDevice";

interface EventLogProps {
  log: LogEntry[];
  onClear: () => void;
}

export function EventLog({ log, onClear }: EventLogProps) {
  return (
    <section className="panel log-panel">
      <div className="log-header">
        <h2>Event Log</h2>
        <button className="btn-small" onClick={onClear}>
          Clear
        </button>
      </div>
      <div className="log-container">
        {log.length === 0 && (
          <div className="log-empty">No events yet</div>
        )}
        {log.map((entry, i) => (
          <div key={i} className={`log-entry log-${entry.type}`}>
            <span className="log-time">{entry.time}</span>
            <span className="log-msg">{entry.message}</span>
          </div>
        ))}
      </div>
    </section>
  );
}
