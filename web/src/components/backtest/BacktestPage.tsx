import { useEffect, useMemo, useState, type FormEvent } from "react";
import {
  api,
  backtestApi,
  type Asset,
  type BacktestDefaults,
  type BacktestDetail,
  type BacktestSummary,
  type ComparisonResult,
  type Simulation,
  type StrategyDef,
  type Timeframe,
} from "../../api";
import { formatDate, formatNumber, formatSignedPercent } from "../../format";
import { ComparisonView } from "./ComparisonView";
import { EquityChart } from "./EquityChart";
import { MetricsGrid } from "./MetricsGrid";
import { TradesTable } from "./TradesTable";

const isoDaysAgo = (days: number) => new Date(Date.now() - days * 86_400_000).toISOString().slice(0, 10);

export function BacktestPage() {
  const [strategies, setStrategies] = useState<StrategyDef[]>([]);
  const [assets, setAssets] = useState<Asset[]>([]);
  const [defaults, setDefaults] = useState<BacktestDefaults | null>(null);
  const [history, setHistory] = useState<BacktestSummary[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [symbol, setSymbol] = useState("BOVA11");
  const [strategyKey, setStrategyKey] = useState("sma-cross");
  const [params, setParams] = useState<Record<string, number>>({});
  const [from, setFrom] = useState(isoDaysAgo(3 * 365));
  const [to, setTo] = useState(isoDaysAgo(0));
  const [simulation, setSimulation] = useState<Required<Simulation>>({
    initialCapital: 100_000,
    riskPerTradePercent: 1,
    maxPositionPercent: 100,
    riskFreeRatePercent: 10,
    applyTaxes: true,
  });

  const [useMetaModel, setUseMetaModel] = useState(false);
  const [timeframe, setTimeframe] = useState<Timeframe>("D1");
  const [flattenAtDayEnd, setFlattenAtDayEnd] = useState(false);
  const [running, setRunning] = useState<"run" | "compare" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<BacktestDetail | null>(null);
  const [comparison, setComparison] = useState<ComparisonResult | null>(null);
  const [view, setView] = useState<"resultado" | "operacoes" | "eventos">("resultado");
  useEffect(() => setView("resultado"), [result]);

  // Futuros (WIN/WDO) só têm barras intradiárias, vindas do MT5.
  const tradable = useMemo(() => assets.filter((a) => a.assetClass !== "Index"), [assets]);
  const strategy = strategies.find((s) => s.key === strategyKey);

  useEffect(() => {
    Promise.all([backtestApi.strategies(), api.assets(), backtestApi.defaults(), backtestApi.list()])
      .then(([s, a, d, h]) => {
        setStrategies(s);
        setAssets(a);
        setDefaults(d);
        setHistory(h);
        setSimulation((current) => ({ ...current, riskFreeRatePercent: d.riskFreeRatePercent }));
      })
      .catch((e) => setLoadError(e instanceof Error ? e.message : "Falha ao carregar"));
  }, []);

  useEffect(() => {
    if (strategy) setParams(Object.fromEntries(strategy.parameters.map((p) => [p.name, p.default])));
  }, [strategy]);

  async function runBacktest(event: FormEvent) {
    event.preventDefault();
    setRunning("run");
    setError(null);
    try {
      setResult(await backtestApi.run({
          symbol,
          strategy: strategyKey,
          parameters: params,
          from,
          to,
          simulation,
          useMetaModel,
          timeframe,
          flattenAtDayEnd: timeframe !== "D1" && flattenAtDayEnd,
        }));
      setHistory(await backtestApi.list());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha no backtest");
    } finally {
      setRunning(null);
    }
  }

  async function compare() {
    setRunning("compare");
    setError(null);
    try {
      setComparison(await backtestApi.compare({ from, to, simulation }));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha na comparação");
    } finally {
      setRunning(null);
    }
  }

  async function open(id: string) {
    setError(null);
    try {
      const detail = await backtestApi.get(id);
      setResult(detail);
      setSymbol(detail.summary.symbol);
      setStrategyKey(detail.summary.strategy);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao abrir backtest");
    }
  }

  const strategyName = (key: string) => strategies.find((s) => s.key === key)?.name ?? key;
  const setSim = <K extends keyof Simulation>(key: K, value: Required<Simulation>[K]) =>
    setSimulation((s) => ({ ...s, [key]: value }));

  if (loadError) return <p className="error banner">{loadError}</p>;

  return (
    <div className="stack">
      <form className="card" onSubmit={runBacktest}>
        <div className="card-head">
          <h2>Parâmetros</h2>
          <div className="actions">
            <button type="button" className="small" style={{ margin: 0 }} onClick={compare} disabled={running !== null}>
              {running === "compare" ? "Comparando…" : "Comparar estratégias"}
            </button>
            <button type="submit" className="primary small" style={{ margin: 0 }} disabled={running !== null}>
              {running === "run" ? "Rodando…" : "▶ Rodar backtest"}
            </button>
          </div>
        </div>
        <div className="form-grid">
          <label>
            Ativo
            <select value={symbol} onChange={(e) => setSymbol(e.target.value)}>
              {tradable.map((a) => (
                <option key={a.symbol} value={a.symbol}>
                  {a.symbol} · {a.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Estratégia
            <select value={strategyKey} onChange={(e) => setStrategyKey(e.target.value)}>
              {strategies.map((s) => (
                <option key={s.key} value={s.key}>
                  {s.name}
                </option>
              ))}
            </select>
            {strategy && <span className="muted small">{strategy.description}</span>}
          </label>
          <label>
            Tempo gráfico
            <select value={timeframe} onChange={(e) => setTimeframe(e.target.value as Timeframe)}>
              <option value="D1">Diário</option>
              <option value="H1">1 hora</option>
              <option value="M15">15 minutos</option>
              <option value="M5">5 minutos</option>
              <option value="M1">1 minuto</option>
            </select>
            {timeframe !== "D1" && <span className="muted small">Barras intradiárias vêm do MetaTrader 5.</span>}
          </label>
          <label>
            De
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} required />
          </label>
          <label>
            Até
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} required />
          </label>
          {strategy?.parameters.map((p) => (
            <label key={p.name}>
              {p.description}
              <input
                type="number"
                min={p.min}
                max={p.max}
                step="any"
                value={params[p.name] ?? p.default}
                onChange={(e) => setParams({ ...params, [p.name]: Number(e.target.value) })}
              />
            </label>
          ))}
        </div>

        {timeframe !== "D1" && (
          <label className="checkbox">
            <input type="checkbox" checked={flattenAtDayEnd} onChange={(e) => setFlattenAtDayEnd(e.target.checked)} />
            Day trade: zerar na última barra de cada pregão
          </label>
        )}
        <label className="checkbox">
          <input type="checkbox" checked={useMetaModel} onChange={(e) => setUseMetaModel(e.target.checked)} />
          Usar o filtro da IA (modelo ativo da estratégia, se houver)
        </label>

        <details>
          <summary>Simulação e custos</summary>
          <div className="form-grid">
            <label>
              Capital inicial (R$)
              <input type="number" min={1000} step={1000} value={simulation.initialCapital} onChange={(e) => setSim("initialCapital", Number(e.target.value))} />
            </label>
            <label>
              Risco por operação (%)
              <input type="number" min={0.1} max={100} step={0.1} value={simulation.riskPerTradePercent} onChange={(e) => setSim("riskPerTradePercent", Number(e.target.value))} />
              <span className="muted small">Perda aceita se o stop for atingido</span>
            </label>
            <label>
              Exposição máxima no ativo (%)
              <input type="number" min={1} max={100} step={1} value={simulation.maxPositionPercent} onChange={(e) => setSim("maxPositionPercent", Number(e.target.value))} />
            </label>
            <label>
              CDI anual (%)
              <input type="number" min={0} max={100} step={0.1} value={simulation.riskFreeRatePercent} onChange={(e) => setSim("riskFreeRatePercent", Number(e.target.value))} />
            </label>
            <label className="checkbox">
              <input type="checkbox" checked={simulation.applyTaxes} onChange={(e) => setSim("applyTaxes", e.target.checked)} />
              Descontar IR (15% swing, 20% day trade, isenção até R$ 20 mil em ações)
            </label>
          </div>
          {defaults && (
            <p className="muted small">
              Custos: corretagem R$ {formatNumber(defaults.brokeragePerOrder)}, taxas B3 {formatNumber(defaults.feesPercent)}%, slippage{" "}
              {formatNumber(defaults.slippagePercent)}%, futuros R$ {formatNumber(defaults.futuresFeePerContract)}/contrato (ajustáveis no appsettings).
            </p>
          )}
        </details>

        {error && <p className="callout bad">{error}</p>}
        <p className="callout" style={{ marginTop: "0.75rem" }}>
          O sinal sai no fechamento do candle e a ordem é executada na abertura do seguinte, com o mesmo gestor de risco do robô. Sem taxa de CDI
          informada, o caixa rende o CDI diário do Banco Central.
        </p>
      </form>

      {result && (
        <>
          <nav className="tabs" aria-label="Resultado do backtest">
            {(
              [
                ["resultado", "Resultado"],
                ["operacoes", `Operações (${result.trades.length})`],
                ["eventos", `Eventos (${result.events.length})`],
              ] as const
            ).map(([id, label]) => (
              <button key={id} type="button" className={view === id ? "active" : ""} onClick={() => setView(id)}>
                {label}
              </button>
            ))}
            <span className="muted small" style={{ marginLeft: "auto", alignSelf: "center" }}>
              {result.summary.symbol} · {strategyName(result.summary.strategy)} · {formatDate(result.summary.startDate)} a {formatDate(result.summary.endDate)}
              {result.summary.metaModelId && " · com filtro da IA"}
            </span>
          </nav>
          {view === "resultado" && (
            <>
              <MetricsGrid metrics={result.summary.metrics} />
              <section className="card">
                <div className="card-head">
                  <h2>Curva de capital</h2>
                  <span className="muted small">estratégia × comprar e segurar × CDI</span>
                </div>
                <EquityChart points={result.equity} />
              </section>
            </>
          )}
          {view === "operacoes" && (
            <section className="card">
              <TradesTable trades={result.trades} />
            </section>
          )}
          {view === "eventos" && (
            <section className="card">
              {result.events.length === 0 ? (
                <p className="muted" style={{ margin: 0 }}>Nenhum evento: os limites de risco não foram atingidos.</p>
              ) : (
                <ul className="audit">
                  {result.events.map((e, i) => (
                    <li key={i}>
                      <span className="mono muted">{formatDate(e.date)}</span> {e.message}
                    </li>
                  ))}
                </ul>
              )}
            </section>
          )}
        </>
      )}

      {comparison && (
        <section className="card">
          <div className="card-head">
            <h2>Comparação de estratégias</h2>
            <span className="muted small">parâmetros escolhidos dentro da amostra, avaliados fora dela</span>
          </div>
          <ComparisonView result={comparison} strategies={strategies} />
        </section>
      )}

      <section className="card">
        <h2>Backtests recentes</h2>
        {history.length === 0 ? (
          <p className="muted">Nenhum backtest ainda.</p>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Ativo</th>
                  <th>Estratégia</th>
                  <th>Período</th>
                  <th className="num">Retorno</th>
                  <th className="num">Comprar e segurar</th>
                  <th className="num">Sharpe</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {history.map((h) => (
                  <tr key={h.id}>
                    <td>{h.symbol}</td>
                    <td>{strategyName(h.strategy)}</td>
                    <td className="muted small">
                      {formatDate(h.startDate)} a {formatDate(h.endDate)}
                    </td>
                    <td className={`num ${h.metrics.totalReturnPercent >= 0 ? "up" : "down"}`}>{formatSignedPercent(h.metrics.totalReturnPercent)}</td>
                    <td className="num">{formatSignedPercent(h.metrics.benchmarkReturnPercent)}</td>
                    <td className="num">{formatNumber(h.metrics.sharpe)}</td>
                    <td>
                      <button type="button" className="link" onClick={() => void open(h.id)}>
                        Abrir
                      </button>
                    </td>
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
