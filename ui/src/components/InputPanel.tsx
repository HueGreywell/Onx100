interface InputPanelProps {
  selectedInput: number | null;
  signals: Record<string, string>;
  isConnected: boolean;
  loading: boolean;
  onSelectInput: (id: number) => void;
}

export function InputPanel({
  selectedInput,
  signals,
  isConnected,
  loading,
  onSelectInput,
}: InputPanelProps) {
  return (
    <section className="panel">
      <h2>Input Select</h2>
      <div className="input-grid">
        {[1, 2, 3, 4].map((i) => (
          <button
            key={i}
            className={`input-btn ${selectedInput === i ? "active" : ""}`}
            onClick={() => onSelectInput(i)}
            disabled={!isConnected || loading}
          >
            <span className="input-number">{i}</span>
            <span className="input-signal">
              {signals[i] === "Ok"
                ? "Signal OK"
                : signals[i] === "Lost"
                ? "No Signal"
                : ""}
            </span>
          </button>
        ))}
      </div>
    </section>
  );
}
