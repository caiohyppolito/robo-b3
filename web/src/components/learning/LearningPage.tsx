import { useCallback, useEffect, useMemo, useState } from "react";
import { backtestApi, learningApi, type JournalEntry, type MetaModel, type StrategyDef } from "../../api";
import { formatDate, formatDateTime, formatNumber, formatPercent, formatSignedPercent, formatBrl } from "../../format";
import { ImportanceChart } from "./ImportanceChart";

const STATUS: Record<MetaModel["status"], { icon: string; label: string; className: string }> = {
  Active: { icon: "●", label: "Ativo", className: "status-active" },
  Rejected: { icon: "✕", label: "Rejeitado", className: "status-rejected" },
  Retired: { icon: "◌", label: "Aposentado", className: "status-retired" },
};

function StatusBadge({ status }: { status: MetaModel["status"] }) {
  const s = STATUS[status];
  return (
    <span className={`status ${s.className}`}>
      <span aria-hidden="true">{s.icon}</span> {s.label}
    </span>
  );
}

export function LearningPage() {
  const [strategies, setStrategies] = useState<StrategyDef[]>([]);
  const [models, setModels] = useState<MetaModel[]>([]);
  const [journal, setJournal] = useState<JournalEntry[]>([]);
  const [analystReady, setAnalystReady] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    const [s, m, j, a] = await Promise.all([
      backtestApi.strategies(),
      learningApi.models(),
      learningApi.journal(),
      learningApi.analyst(),
    ]);
    setStrategies(s.filter((x) => x.key !== "buy-and-hold"));
    setModels(m);
    setJournal(j);
    setAnalystReady(a.isConfigured);
    setSelectedId((current) => current ?? m.find((x) => x.status === "Active")?.id ?? m[0]?.id ?? null);
  }, []);

  useEffect(() => {
    load().catch((e) => setError(e instanceof Error ? e.message : "Falha ao carregar"));
  }, [load]);

  const strategyName = (key: string) => strategies.find((s) => s.key === key)?.name ?? key;
  const selected = useMemo(() => models.find((m) => m.id === selectedId) ?? null, [models, selectedId]);

  async function run(label: string, action: () => Promise<MetaModel>) {
    setBusy(label);
    setError(null);
    try {
      const model = await action();
      await load();
      setSelectedId(model.id);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha");
    } finally {
      setBusy(null);
    }
  }

  return (
    <div className="stack">
      <section className="card">
        <h2>Como a IA funciona</h2>
        <p className="small">
          Para cada estratégia, um modelo de machine learning aprende, com as operações passadas, em que contexto de mercado os sinais
          costumam dar errado, e <strong>veta</strong> esses sinais. Ele <strong>nunca cria operações nem impede saídas</strong>: no pior
          caso, o robô opera menos. Um modelo novo só entra em uso se for melhor no período que ele não viu no treino; se o modelo atual
          deixar de ajudar, ele é aposentado e a estratégia volta a operar sem filtro.
        </p>
      </section>

      {error && <p className="error banner">{error}</p>}

      <section className="grid">
        {strategies.map((s) => {
          const active = models.find((m) => m.strategy === s.key && m.status === "Active");
          const last = models.find((m) => m.strategy === s.key);
          return (
            <article key={s.key} className="card">
              <h2>{s.name}</h2>
              {active ? (
                <>
                  <StatusBadge status="Active" />
                  <p className="small">
                    Desde {formatDateTime(active.activatedAt ?? active.createdAt)}. Sharpe fora da amostra {formatNumber(active.unfilteredSharpe)} →{" "}
                    <strong>{formatNumber(active.filteredSharpe)}</strong>.
                  </p>
                </>
              ) : (
                <p className="small muted">Sem modelo ativo: o robô opera esta estratégia sem filtro.</p>
              )}
              {last && <p className="muted small">Último treino: {formatDateTime(last.createdAt)}</p>}
              <button type="button" onClick={() => void run(s.key, () => learningApi.train(s.key))} disabled={busy !== null}>
                {busy === s.key ? "Treinando…" : "Treinar agora"}
              </button>
            </article>
          );
        })}
      </section>

      {selected && (
        <section className="card stack">
          <h2>
            Modelo de {strategyName(selected.strategy)} · {formatDateTime(selected.createdAt)}
          </h2>
          <div>
            <StatusBadge status={selected.status} />
            <p className="callout">{selected.decision}</p>
          </div>
          <div className="metrics">
            <Metric label="AUC (fora da amostra)" value={formatNumber(selected.auc)} hint="0,5 = acaso · 1 = perfeito" />
            <Metric label="Acerto sem → com filtro" value={`${formatPercent(selected.baseWinRatePercent)} → ${formatPercent(selected.filteredWinRatePercent)}`} />
            <Metric
              label="Retorno médio por operação"
              value={`${formatSignedPercent(selected.baseAverageReturnPercent)} → ${formatSignedPercent(selected.filteredAverageReturnPercent)}`}
            />
            <Metric label="Sinais mantidos" value={formatPercent(selected.keptPercent)} hint={`limiar ${formatNumber(selected.threshold)}`} />
            <Metric
              label="Sharpe sem → com filtro"
              value={`${formatNumber(selected.unfilteredSharpe)} → ${formatNumber(selected.filteredSharpe)}`}
              hint={selected.championSharpe !== null ? `modelo anterior ${formatNumber(selected.championSharpe)}` : undefined}
            />
            <Metric
              label="Operações"
              value={`${selected.trainingExamples} + ${selected.validationExamples}`}
              hint={`treino até ${formatDate(selected.splitDate)} · validação até ${formatDate(selected.validationTo)}`}
            />
          </div>

          {Object.keys(selected.featureImportance).length > 0 && (
            <div>
              <h2>O que o modelo mais usa para decidir</h2>
              <p className="muted small">Queda da AUC ao embaralhar cada variável na validação. Valores negativos indicam variáveis que só atrapalham.</p>
              <ImportanceChart importance={selected.featureImportance} />
            </div>
          )}

          <div>
            <h2>Análise do Claude</h2>
            {selected.analysis ? (
              <div className="analysis">{selected.analysis}</div>
            ) : analystReady ? (
              <button type="button" onClick={() => void run("analysis", () => learningApi.analyze(selected.id))} disabled={busy !== null}>
                {busy === "analysis" ? "Analisando…" : "Pedir análise ao Claude"}
              </button>
            ) : (
              <p className="muted small">
                Opcional. Para ativar, grave sua chave da Anthropic: <code>dotnet user-secrets set "Anthropic:ApiKey" "…" --project src/RoboB3.Api</code>
              </p>
            )}
          </div>
        </section>
      )}

      <section className="card">
        <h2>Histórico de modelos</h2>
        {models.length === 0 ? (
          <p className="muted">Nenhum modelo treinado ainda. O worker treina automaticamente a cada 24 h, ou use "Treinar agora".</p>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Data</th>
                  <th>Estratégia</th>
                  <th>Situação</th>
                  <th className="num">AUC</th>
                  <th className="num">Acerto</th>
                  <th className="num">Sharpe</th>
                  <th>Motivo</th>
                </tr>
              </thead>
              <tbody>
                {models.map((m) => (
                  <tr key={m.id} className={m.id === selectedId ? "selected" : ""} onClick={() => setSelectedId(m.id)} style={{ cursor: "pointer" }}>
                    <td className="small">{formatDateTime(m.createdAt)}</td>
                    <td>{strategyName(m.strategy)}</td>
                    <td>
                      <StatusBadge status={m.status} />
                    </td>
                    <td className="num">{formatNumber(m.auc)}</td>
                    <td className="num">
                      {formatPercent(m.baseWinRatePercent)} → {formatPercent(m.filteredWinRatePercent)}
                    </td>
                    <td className="num">
                      {formatNumber(m.unfilteredSharpe)} → {formatNumber(m.filteredSharpe)}
                    </td>
                    <td className="muted small">{m.decision}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <h2>Diário de operações do robô</h2>
        {journal.length === 0 ? (
          <p className="muted small">
            Vazio por enquanto: o diário recebe as operações do paper trading (Fase 4) e da conta real (Fase 5), que passam a pesar mais no
            treino do que o histórico simulado.
          </p>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Origem</th>
                  <th>Ativo</th>
                  <th>Estratégia</th>
                  <th>Entrada</th>
                  <th>Saída</th>
                  <th className="num">Resultado</th>
                  <th className="num">Chance estimada</th>
                </tr>
              </thead>
              <tbody>
                {journal.map((j) => (
                  <tr key={j.id}>
                    <td>{j.source === "Paper" ? "Simulado" : "Real"}</td>
                    <td>{j.symbol}</td>
                    <td>{strategyName(j.strategy)}</td>
                    <td>{formatDate(j.entryDate)}</td>
                    <td>{formatDate(j.exitDate)}</td>
                    <td className={`num ${j.netProfit >= 0 ? "up" : "down"}`}>{formatBrl(j.netProfit)}</td>
                    <td className="num">{j.modelProbability === null ? "—" : formatPercent(j.modelProbability * 100)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

function Metric({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="metric">
      <span className="muted small">{label}</span>
      <strong>{value}</strong>
      {hint && <span className="muted small">{hint}</span>}
    </div>
  );
}
