import { useEffect, useState } from "react";
import { backtestApi, overviewApi, type Decision, type DecisionOutcome, type StrategyDef } from "../../api";
import { DecisionList } from "./DecisionList";
import { OUTCOME } from "./outcomes";

const FILTERS: { id: "todas" | "acoes" | DecisionOutcome; label: string }[] = [
  { id: "acoes", label: "Ações do robô" },
  { id: "todas", label: "Todas" },
  { id: "Blocked", label: OUTCOME.Blocked.label },
  { id: "Vetoed", label: OUTCOME.Vetoed.label },
  { id: "Failed", label: OUTCOME.Failed.label },
];

/** Registro completo do que o robô decidiu em cada barra fechada, com o motivo. */
export function DecisionsPage() {
  const [decisions, setDecisions] = useState<Decision[]>([]);
  const [strategies, setStrategies] = useState<StrategyDef[]>([]);
  const [filter, setFilter] = useState<(typeof FILTERS)[number]["id"]>("acoes");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    backtestApi.strategies().then(setStrategies).catch(() => undefined);
    const load = () => overviewApi.decisions(300).then(setDecisions).catch((e) => setError(e instanceof Error ? e.message : "Falha"));
    void load();
    const timer = window.setInterval(() => void load(), 15_000);
    return () => window.clearInterval(timer);
  }, []);

  const shown = decisions.filter((d) =>
    filter === "todas" ? true : filter === "acoes" ? d.outcome !== "NoSignal" && d.outcome !== "Held" : d.outcome === filter,
  );

  return (
    <>
      <div className="page-head">
        <div>
          <p>O que o robô decidiu em cada barra fechada das alocações ligadas, no modo de execução atual, e por quê.</p>
        </div>
      </div>
      {error && <p className="error banner">{error}</p>}
      <div className="segmented" role="tablist" aria-label="Filtro">
        {FILTERS.map((f) => (
          <button key={f.id} type="button" role="tab" aria-selected={filter === f.id} className={filter === f.id ? "active" : ""} onClick={() => setFilter(f.id)}>
            {f.label}
          </button>
        ))}
      </div>
      <section className="card">
        <DecisionList decisions={shown} strategyName={(k) => strategies.find((s) => s.key === k)?.name ?? k} />
      </section>
    </>
  );
}
