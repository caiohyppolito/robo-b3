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

  const [running, setRunning] = useState<"run" | "compare" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<BacktestDetail | null>(null);
  const [comparison, setComparison] = useState<ComparisonResult | null>(null);

  const tradable = useMemo(() => assets.filter((a) => a.dailyDataSymbol && a.assetClass !== "Index"), [assets]);
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
      setResult(await backtestApi.run({ symbol, strategy: strategyKey, parameters: params, from, to, simulation }));
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
        <h2>Backtest</h2>
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

        {error && <p className="error">{error}</p>}
        <div className="actions">
          <button type="submit" className="primary" disabled={running !== null}>
            {running === "run" ? "Rodando…" : "Rodar backtest"}
          </button>
          <button type="button" onClick={compare} disabled={running !== null}>
            {running === "compare" ? "Comparando… (pode levar alguns segundos)" : "Comparar todas as estratégias"}
          </button>
        </div>
      </form>

      {result && (
        <section className="card stack">
          <h2>
            {result.summary.symbol} · {strategyName(result.summary.strategy)} · {formatDate(result.summary.startDate)} a{" "}
            {formatDate(result.summary.endDate)}
          </h2>
          <MetricsGrid metrics={result.summary.metrics} />
          <EquityChart points={result.equity} />
          {result.events.length > 0 && (
            <details>
              <summary>Eventos ({result.events.length})</summary>
              <ul className="muted small">
                {result.events.map((e, i) => (
                  <li key={i}>
                    {formatDate(e.date)}: {e.message}
                  </li>
                ))}
              </ul>
            </details>
          )}
          <h2>Operações</h2>
          <TradesTable trades={result.trades} />
        </section>
      )}

      {comparison && (
        <section className="card">
          <h2>Comparação de estratégias (fora da amostra)</h2>
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
