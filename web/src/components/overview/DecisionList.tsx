import type { Decision } from "../../api";
import { formatDateTime, formatNumber, formatPercent } from "../../format";
import { OUTCOME } from "./outcomes";

const SIGNAL: Record<Decision["signal"], string> = { Long: "▲ comprado", Short: "▼ vendido", Flat: "fora" };

export function DecisionList({ decisions, strategyName }: { decisions: Decision[]; strategyName: (key: string) => string }) {
  if (decisions.length === 0) {
    return <p className="muted small">Nenhuma decisão registrada ainda. Elas aparecem quando uma alocação ligada avalia uma barra fechada.</p>;
  }

  return (
    <ul className="decisions">
      {decisions.map((d) => {
        const outcome = OUTCOME[d.outcome];
        return (
          <li key={d.id} className={`decision tone-${outcome.tone}`}>
            <div className="decision-head">
              <strong>{d.contract}</strong>
              <span className={`signal ${d.signal.toLowerCase()}`}>{SIGNAL[d.signal]}</span>
              <span className="chip">{strategyName(d.strategy)}</span>
              <span className={`chip tone ${outcome.tone}`}>{outcome.label}</span>
              {d.probability !== null && <span className="chip">IA {formatPercent(d.probability * 100)}</span>}
              <span className="muted small decision-time">barra {formatDateTime(d.barTime)}</span>
            </div>
            <p>{d.detail}</p>
            {(d.price !== null || d.sizing) && (
              <p className="muted small mono">
                {d.price !== null && `${d.quantity} a ${formatNumber(d.price)}${d.stop !== null ? ` · stop ${formatNumber(d.stop)}` : ""}`}
                {d.price !== null && d.sizing && " · "}
                {d.sizing}
              </p>
            )}
          </li>
        );
      })}
    </ul>
  );
}
