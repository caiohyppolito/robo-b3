import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  api,
  backtestApi,
  executionApi,
  MODE_LABEL,
  paperApi,
  type ExecutionStatus,
  type Asset,
  type Assignment,
  type CycleReport,
  type FeedStatus,
  type PaperAccount,
  type StrategyDef,
  type Timeframe,
} from "../../api";
import { formatBrl, formatDateTime, formatNumber, formatPercent } from "../../format";
import { ExecutionModeCard } from "./ExecutionModeCard";
import { OrdersTable } from "./OrdersTable";

const REFRESH_MS = 10_000;
const TIMEFRAMES: { value: Timeframe; label: string }[] = [
  { value: "M1", label: "1 minuto" },
  { value: "M5", label: "5 minutos" },
  { value: "M15", label: "15 minutos" },
  { value: "H1", label: "1 hora" },
  { value: "D1", label: "Diário" },
];

const signed = (v: number) => `${v > 0 ? "+" : ""}${formatBrl(v)}`;
const tone = (v: number) => (v > 0 ? "up" : v < 0 ? "down" : "");

export function OperationPage() {
  const [feed, setFeed] = useState<FeedStatus | null>(null);
  const [execution, setExecution] = useState<ExecutionStatus | null>(null);
  const [account, setAccount] = useState<PaperAccount | null>(null);
  const [assignments, setAssignments] = useState<Assignment[]>([]);
  const [strategies, setStrategies] = useState<StrategyDef[]>([]);
  const [assets, setAssets] = useState<Asset[]>([]);
  const [report, setReport] = useState<CycleReport | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      const [f, a, s, e] = await Promise.all([paperApi.feed(), paperApi.account(), paperApi.assignments(), executionApi.get()]);
      setFeed(f);
      setExecution(e);
      setAccount(a);
      setAssignments(s);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao carregar");
    }
  }, []);

  useEffect(() => {
    Promise.all([backtestApi.strategies(), api.assets()])
      .then(([s, a]) => {
        setStrategies(s);
        setAssets(a.filter((x) => x.assetClass !== "Index"));
      })
      .catch(() => undefined);
    void refresh();
    const timer = window.setInterval(() => void refresh(), REFRESH_MS);
    return () => window.clearInterval(timer);
  }, [refresh]);

  async function act(label: string, action: () => Promise<unknown>) {
    setBusy(label);
    setError(null);
    try {
      const result = await action();
      if (result && typeof result === "object" && "messages" in result) setReport(result as CycleReport);
      await refresh();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha");
    } finally {
      setBusy(null);
    }
  }

  const strategyName = (key: string) => strategies.find((s) => s.key === key)?.name ?? key;

  return (
    <div className="stack">
      {execution?.mode === "Mt5Real" ? (
        <p className="banner danger-banner">
          <strong>CONTA REAL:</strong> as ordens do robô são enviadas à sua conta na corretora, com dinheiro de verdade. O stop de cada
          posição também fica registrado na corretora. Use o KILL SWITCH no Painel para travar novas entradas.
        </p>
      ) : execution?.mode === "Mt5Demo" ? (
        <p className="callout">
          <strong>MT5 conta demo:</strong> as ordens são enviadas de verdade ao MetaTrader 5, numa conta de demonstração (dinheiro
          fictício). O robô confere a cada ciclo se as posições dele batem com as da corretora.
        </p>
      ) : (
        <p className="callout">
          <strong>Operação simulada:</strong> as estratégias operam com preços reais do MetaTrader 5, mas as ordens são executadas só
          aqui dentro. Nada é enviado à corretora. As operações encerradas alimentam o diário da IA.
        </p>
      )}
      {error && <p className="error banner">{error}</p>}

      <section className="grid">
        <article className={`card ${feed && !feed.connected ? "state halted" : ""}`}>
          <h2>MetaTrader 5</h2>
          {feed ? (
            <>
              <span className={`status ${feed.connected ? "status-active" : "status-rejected"}`}>
                <span aria-hidden="true">{feed.connected ? "●" : "✕"}</span> {feed.connected ? "Conectado" : "Desconectado"}
              </span>
              {feed.connected ? (
                <p className="small">
                  {feed.mode === "fake" ? (
                    <strong>Modo de simulação: preços sintéticos, não use para avaliar estratégias.</strong>
                  ) : (
                    <>
                      {feed.company} · {feed.server}
                      {feed.login ? ` · conta ${feed.login}` : ""}
                      {feed.isDemo === false && " · conta real (somente leitura de preços)"}
                    </>
                  )}
                </p>
              ) : (
                <p className="small muted">{feed.message}</p>
              )}
            </>
          ) : (
            <p className="muted">Verificando…</p>
          )}
        </article>
        <article className="card">
          <h2>Patrimônio ({execution ? MODE_LABEL[execution.mode] : "…"})</h2>
          <p className="big">{account ? formatBrl(account.equity) : "—"}</p>
          {account && (
            <p className={`small ${tone(account.totalProfit)}`}>
              {signed(account.totalProfit)} desde o início ({formatPercent((account.totalProfit / account.initialCapital) * 100)})
            </p>
          )}
        </article>
        <article className="card">
          <h2>Resultado do dia</h2>
          <p className={`big ${account ? tone(account.dayProfit) : ""}`}>{account ? signed(account.dayProfit) : "—"}</p>
          {account && <p className="muted small">custos acumulados {formatBrl(account.costsPaid)}</p>}
        </article>
        <article className="card">
          <h2>Ciclo</h2>
          <p className="muted small">O worker roda a cada 30 s. Para testar agora:</p>
          <button type="button" onClick={() => void act("cycle", paperApi.cycle)} disabled={busy !== null}>
            {busy === "cycle" ? "Rodando…" : "Rodar ciclo agora"}
          </button>
        </article>
      </section>

      {execution && <ExecutionModeCard status={execution} onChanged={() => void refresh()} />}

      {report && (
        <section className="card">
          <h2>Último ciclo manual</h2>
          <p className="small">
            {report.opened} entradas, {report.closed} saídas.
          </p>
          {report.messages.length > 0 && (
            <ul className="small">
              {report.messages.map((m, i) => (
                <li key={i}>{m}</li>
              ))}
            </ul>
          )}
        </section>
      )}

      <section className="card">
        <h2>Posições abertas</h2>
        {!account || account.positions.length === 0 ? (
          <p className="muted">Nenhuma posição aberta.</p>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Contrato</th>
                  <th>Estratégia</th>
                  <th>Lado</th>
                  <th className="num">Qtd.</th>
                  <th className="num">Entrada</th>
                  <th className="num">Stop</th>
                  <th className="num">Atual</th>
                  <th className="num">Resultado</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {account.positions.map((p) => (
                  <tr key={p.id}>
                    <td>
                      <strong>{p.contract}</strong>
                      <span className="muted small block">
                        desde {formatDateTime(p.entryTime)}
                        {p.brokerTicket !== null && ` · ticket ${p.brokerTicket}`}
                      </span>
                    </td>
                    <td>{strategyName(p.strategy)}</td>
                    <td>{p.direction === "Long" ? "Compra" : "Venda"}</td>
                    <td className="num">{p.quantity}</td>
                    <td className="num">{formatNumber(p.entryPrice)}</td>
                    <td className="num">{p.stop === null ? "—" : formatNumber(p.stop)}</td>
                    <td className="num">{formatNumber(p.markPrice)}</td>
                    <td className={`num ${tone(p.unrealizedProfit)}`}>{signed(p.unrealizedProfit)}</td>
                    <td>
                      <button
                        type="button"
                        className="link"
                        disabled={busy !== null}
                        onClick={() => {
                          if (window.confirm(`Encerrar ${p.contract} a mercado?`)) void act(p.id, () => paperApi.closePosition(p.id));
                        }}
                      >
                        Encerrar
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card stack">
        <h2>Estratégias em operação</h2>
        {assignments.length === 0 ? (
          <p className="muted">Nenhuma alocação. Adicione uma abaixo; ela nasce desligada.</p>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Ativo</th>
                  <th>Estratégia</th>
                  <th>Tempo gráfico</th>
                  <th>Opções</th>
                  <th>Última barra avaliada</th>
                  <th>Situação</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {assignments.map((a) => (
                  <tr key={a.id}>
                    <td>
                      <strong>{a.symbol}</strong>
                    </td>
                    <td>
                      {strategyName(a.strategy)}
                      <span className="muted small block">
                        {Object.entries(a.parameters)
                          .map(([k, v]) => `${k}=${v}`)
                          .join(" · ")}
                      </span>
                    </td>
                    <td>{TIMEFRAMES.find((t) => t.value === a.timeframe)?.label}</td>
                    <td className="small">
                      {[a.flattenAtDayEnd && "day trade", a.useMetaModel && "filtro da IA"].filter(Boolean).join(", ") || "—"}
                    </td>
                    <td className="small muted">{a.lastProcessedBar ? formatDateTime(a.lastProcessedBar) : "—"}</td>
                    <td>
                      <span className={`status ${a.isEnabled ? "status-active" : "status-retired"}`}>
                        <span aria-hidden="true">{a.isEnabled ? "●" : "◌"}</span> {a.isEnabled ? "Ligada" : "Desligada"}
                      </span>
                    </td>
                    <td className="row-actions">
                      <button type="button" className="link" disabled={busy !== null} onClick={() => void act(a.id, () => paperApi.setEnabled(a.id, !a.isEnabled))}>
                        {a.isEnabled ? "Desligar" : "Ligar"}
                      </button>
                      <button
                        type="button"
                        className="link"
                        disabled={busy !== null}
                        onClick={() => {
                          if (window.confirm("Remover esta alocação?")) void act(a.id, () => paperApi.deleteAssignment(a.id));
                        }}
                      >
                        Remover
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <AssignmentForm strategies={strategies} assets={assets} busy={busy !== null} onSubmit={(body) => act("create", () => paperApi.createAssignment(body))} />
      </section>

      <section className="card">
        <h2>Ordens enviadas</h2>
        <OrdersTable refreshKey={account?.fills.length ?? 0} />
      </section>

      <section className="card">
        <h2>Execuções</h2>
        {!account || account.fills.length === 0 ? (
          <p className="muted">Nenhuma execução ainda.</p>
        ) : (
          <div className="table-wrap scroll-y">
            <table>
              <thead>
                <tr>
                  <th>Horário</th>
                  <th>Contrato</th>
                  <th>Lado</th>
                  <th className="num">Qtd.</th>
                  <th className="num">Preço</th>
                  <th className="num">Resultado</th>
                  <th>Motivo</th>
                </tr>
              </thead>
              <tbody>
                {account.fills.map((f, i) => (
                  <tr key={`${f.time}-${i}`}>
                    <td className="small">{formatDateTime(f.time)}</td>
                    <td>{f.symbol}</td>
                    <td>{f.side === "Buy" ? "Compra" : "Venda"}</td>
                    <td className="num">{f.quantity}</td>
                    <td className="num">{formatNumber(f.price)}</td>
                    <td className={`num ${f.realizedProfit === null ? "" : tone(f.realizedProfit)}`}>
                      {f.realizedProfit === null ? "—" : signed(f.realizedProfit)}
                    </td>
                    <td className="muted small">{f.reason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="card">
        <h2>Reiniciar o livro do modo atual</h2>
        <p className="muted small">
          Apaga o registro interno de posições e execuções deste modo e recomeça do capital inicial (no MT5, do patrimônio da conta). Não
          mexe na corretora. Exige o robô travado e nenhuma posição aberta. O diário da IA é mantido.
        </p>
        <button
          type="button"
          disabled={busy !== null}
          onClick={() => {
            if (window.confirm("Reiniciar o livro do modo atual?")) void act("reset", paperApi.reset);
          }}
        >
          Reiniciar conta
        </button>
      </section>
    </div>
  );
}

function AssignmentForm({
  strategies,
  assets,
  busy,
  onSubmit,
}: {
  strategies: StrategyDef[];
  assets: Asset[];
  busy: boolean;
  onSubmit: (body: { symbol: string; strategy: string; parameters: Record<string, number>; timeframe: Timeframe; useMetaModel: boolean; flattenAtDayEnd: boolean }) => Promise<void>;
}) {
  const [symbol, setSymbol] = useState("WIN");
  const [strategy, setStrategy] = useState("donchian-breakout");
  const [timeframe, setTimeframe] = useState<Timeframe>("M5");
  const [useMetaModel, setUseMetaModel] = useState(false);
  const [flattenAtDayEnd, setFlattenAtDayEnd] = useState(true);

  function submit(event: FormEvent) {
    event.preventDefault();
    const definition = strategies.find((s) => s.key === strategy);
    const parameters = Object.fromEntries((definition?.parameters ?? []).map((p) => [p.name, p.default]));
    void onSubmit({ symbol, strategy, parameters, timeframe, useMetaModel: timeframe === "D1" && useMetaModel, flattenAtDayEnd: timeframe !== "D1" && flattenAtDayEnd });
  }

  return (
    <form onSubmit={submit}>
      <h2>Nova alocação</h2>
      <div className="form-grid">
        <label>
          Ativo
          <select value={symbol} onChange={(e) => setSymbol(e.target.value)}>
            {assets.map((a) => (
              <option key={a.symbol} value={a.symbol}>
                {a.symbol} · {a.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          Estratégia
          <select value={strategy} onChange={(e) => setStrategy(e.target.value)}>
            {strategies.map((s) => (
              <option key={s.key} value={s.key}>
                {s.name}
              </option>
            ))}
          </select>
        </label>
        <label>
          Tempo gráfico
          <select value={timeframe} onChange={(e) => setTimeframe(e.target.value as Timeframe)}>
            {TIMEFRAMES.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
        </label>
        <label className="checkbox">
          <input type="checkbox" checked={flattenAtDayEnd && timeframe !== "D1"} disabled={timeframe === "D1"} onChange={(e) => setFlattenAtDayEnd(e.target.checked)} />
          Day trade (zera no fim do pregão)
        </label>
        <label className="checkbox">
          <input type="checkbox" checked={useMetaModel && timeframe === "D1"} disabled={timeframe !== "D1"} onChange={(e) => setUseMetaModel(e.target.checked)} />
          Usar o filtro da IA (se houver modelo ativo; só em barras diárias, que é como ele é treinado)
        </label>
      </div>
      <p className="muted small">Parâmetros padrão da estratégia; ajuste depois de validar no backtest.</p>
      <button type="submit" className="primary" disabled={busy}>
        Adicionar (desligada)
      </button>
    </form>
  );
}
