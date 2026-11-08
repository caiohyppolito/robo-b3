import { useEffect, useState } from "react";
import { learningApi, type DriftLevel, type DriftReport, type MetaModel, type VetoReview } from "../../api";
import { formatDate, formatNumber, formatSignedPercent } from "../../format";

const DRIFT: Record<DriftLevel, { label: string; tone: string }> = {
  InsufficientData: { label: "poucas decisões para medir", tone: "neutral" },
  Stable: { label: "estável", tone: "good" },
  Moderate: { label: "mudança moderada", tone: "warn" },
  Strong: { label: "mudança forte: considere retreinar", tone: "bad" },
};

/** Calibração e mudança de regime do modelo escolhido. */
export function ModelHealth({ model, onReactivate, busy }: { model: MetaModel; onReactivate: () => void; busy: boolean }) {
  const [drift, setDrift] = useState<DriftReport | null>(null);
  useEffect(() => {
    setDrift(null);
    learningApi.drift(model.id).then(setDrift).catch(() => setDrift(null));
  }, [model.id]);

  const c = model.calibration;
  return (
    <div className="columns">
      <div>
        <h2>Calibração (fora da amostra)</h2>
        {c && c.examples > 0 ? (
          <>
            <p className="muted small" style={{ marginTop: 0 }}>
              Brier {formatNumber(c.brier, 3)} contra {formatNumber(c.baseRateBrier, 3)} de chutar sempre a taxa de acerto
              {c.beatsBaseRate ? ": prevê melhor que a taxa-base." : ": não prevê melhor que a taxa-base."} Erro de calibração (ECE){" "}
              {formatNumber(c.expectedCalibrationError * 100, 1)} p.p.
            </p>
            <table>
              <thead>
                <tr>
                  <th>Faixa prevista</th>
                  <th className="num">Operações</th>
                  <th className="num">Previsto</th>
                  <th className="num">Acertou</th>
                </tr>
              </thead>
              <tbody>
                {c.bins.map((b) => (
                  <tr key={b.from}>
                    <td className="mono">{formatNumber(b.from * 100, 0)}–{formatNumber(b.to * 100, 0)}%</td>
                    <td className="num">{b.count}</td>
                    <td className="num">{b.count ? `${formatNumber(b.meanPredicted * 100, 0)}%` : "—"}</td>
                    <td className="num">{b.count ? `${formatNumber(b.observedWinRate * 100, 0)}%` : "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        ) : (
          <p className="muted small">Modelo treinado antes da calibração existir: retreine para ver.</p>
        )}
      </div>
      <div>
        <h2>Mudança de regime</h2>
        {drift ? (
          <>
            <p style={{ marginTop: 0 }}>
              <span className={`chip tone ${DRIFT[drift.level].tone}`}>{DRIFT[drift.level].label}</span>{" "}
              <span className="muted small">{drift.recentSamples} decisões recentes em barras diárias</span>
            </p>
            {drift.features.length > 0 && (
              <ul className="locks">
                {drift.features.slice(0, 5).map((f) => (
                  <li key={f.name}>
                    <span className="mono small">{f.name}</span>
                    <span className={`chip tone ${DRIFT[f.level].tone}`}>PSI {formatNumber(f.psi, 2)}</span>
                  </li>
                ))}
              </ul>
            )}
            <p className="muted small">Só alerta: o robô nunca troca nem desliga o modelo sozinho.</p>
          </>
        ) : (
          <p className="muted small">{model.calibration ? "Calculando…" : "Sem linha de base: retreine o modelo."}</p>
        )}
        {model.status === "Retired" && model.activatedAt && (
          <button type="button" onClick={onReactivate} disabled={busy}>
            {busy ? "Reativando…" : "Reativar este modelo (rollback)"}
          </button>
        )}
      </div>
    </div>
  );
}

/** Entradas vetadas e o que teria acontecido com elas (simulação, não aconteceram). */
export function VetoCard() {
  const [review, setReview] = useState<VetoReview | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    learningApi.vetoes().then(setReview).catch((e: unknown) => setError(e instanceof Error ? e.message : "Falha"));
  }, []);

  return (
    <section className="card">
      <div className="card-head">
        <h2>Vetos da IA: acertou?</h2>
        <span className="chip tone warn">simulação</span>
      </div>
      {error && <p className="error">{error}</p>}
      {!review ? (
        <p className="muted">Carregando…</p>
      ) : review.vetoes === 0 ? (
        <p className="muted" style={{ margin: 0 }}>Nenhuma entrada vetada ainda. Os vetos aparecem quando uma alocação diária usa um modelo ativo.</p>
      ) : (
        <>
          <div className="metrics">
            <div className="metric kpi"><span className="label">Vetos</span><strong>{review.vetoes}</strong><span className="sub">{review.evaluated} já com desfecho</span></div>
            <div className="metric kpi"><span className="label">Vetos certos</span><strong>{review.hitRatePercent != null ? `${formatNumber(review.hitRatePercent, 0)}%` : "—"}</strong><span className="sub">{review.wouldHaveLost} teriam perdido</span></div>
            <div className="metric kpi"><span className="label">Perda evitada</span><strong className="up">{formatNumber(review.avoidedLossPercent, 2)}%</strong><span className="sub">soma dos retornos</span></div>
            <div className="metric kpi"><span className="label">Ganho perdido</span><strong className="down">{formatNumber(review.missedGainPercent, 2)}%</strong><span className="sub">{review.wouldHaveWon} teriam ganhado</span></div>
          </div>
          <div className="table-wrap" style={{ marginTop: "1rem" }}>
            <table className="nowrap">
              <thead>
                <tr><th>Vetado em</th><th>Ativo</th><th>Estratégia</th><th>Lado</th><th className="num">Chance</th><th>Desfecho</th><th className="num">Retorno</th></tr>
              </thead>
              <tbody>
                {review.items.map((v) => (
                  <tr key={v.decisionId}>
                    <td className="mono">{formatDate(v.time)}</td>
                    <td className="mono">{v.symbol}</td>
                    <td>{v.strategy}</td>
                    <td>{v.direction}</td>
                    <td className="num">{v.probability != null ? `${formatNumber(v.probability * 100, 0)}%` : "—"}</td>
                    <td>{v.status}{v.exitReason ? ` (${v.exitReason})` : ""}</td>
                    <td className={`num ${v.returnPercent == null ? "" : v.returnPercent > 0 ? "down" : "up"}`}>{v.returnPercent != null ? formatSignedPercent(v.returnPercent) : "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="muted small" style={{ marginBottom: 0 }}>
            Cada veto é refeito no histórico com a mesma estratégia, parâmetros, custos e execução na abertura seguinte. Retorno positivo em vermelho: o veto
            custou dinheiro. É aproximação: o robô real poderia estar em outra situação.
          </p>
        </>
      )}
    </section>
  );
}
