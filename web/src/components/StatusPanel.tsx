import { useState } from "react";
import { api, type Status } from "../api";
import { formatBrl, formatDateTime, formatPercent } from "../format";

interface Props {
  status: Status;
  onChange: () => void;
}

export function StatusPanel({ status, onChange }: Props) {
  const { trading, portfolio, limits } = status;
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      onChange();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha");
    } finally {
      setBusy(false);
    }
  }

  function halt() {
    const reason = window.prompt("Motivo para travar o robô:", "Travado manualmente pelo painel");
    if (reason?.trim()) void run(() => api.halt(reason.trim()));
  }

  function resume() {
    const ok = window.confirm(
      "Liberar o robô para operar?\n\nConfira os limites de risco antes. Esta ação fica registrada na auditoria.",
    );
    if (ok) void run(() => api.resume());
  }

  return (
    <section className="grid">
      <article className={`card state ${trading.isHalted ? "halted" : "running"}`}>
        <h2>Estado</h2>
        <p className="big">{trading.isHalted ? "TRAVADO" : "LIBERADO"}</p>
        {trading.haltReason && <p className="muted">{trading.haltReason}</p>}
        <p className="muted small">
          {trading.changedBy} · {formatDateTime(trading.changedAt)}
        </p>
        {trading.isHalted ? (
          <button onClick={resume} disabled={busy}>
            Liberar robô
          </button>
        ) : (
          <button className="danger" onClick={halt} disabled={busy}>
            KILL SWITCH
          </button>
        )}
        {error && <p className="error">{error}</p>}
      </article>

      <article className="card">
        <h2>Patrimônio</h2>
        <p className="big">{formatBrl(portfolio.equity)}</p>
        <p className="muted">{portfolio.openPositions} posições abertas</p>
      </article>

      <Meter label="Perda do dia" value={portfolio.dailyLossPercent} limit={limits.maxDailyLossPercent} />
      <Meter label="Drawdown" value={portfolio.drawdownPercent} limit={limits.maxDrawdownPercent} />
    </section>
  );
}

function Meter({ label, value, limit }: { label: string; value: number; limit: number }) {
  const used = Math.min(100, (value / limit) * 100);
  const level = used >= 80 ? "high" : used >= 50 ? "mid" : "low";
  return (
    <article className="card">
      <h2>{label}</h2>
      <p className="big">{formatPercent(value)}</p>
      <div className="meter" role="meter" aria-valuenow={value} aria-valuemin={0} aria-valuemax={limit} aria-label={label}>
        <div className={`fill ${level}`} style={{ width: `${used}%` }} />
      </div>
      <p className="muted small">limite {formatPercent(limit)}</p>
    </article>
  );
}
