import { useEffect, useMemo, useState } from "react";
import { api, portfolioApi, type Asset, type PortfolioResponse, type PortfolioWeighting } from "../../api";
import { formatBrl, formatDate, formatNumber, formatPercent, formatSignedPercent } from "../../format";
import { EquityChart } from "../backtest/EquityChart";

const yearsAgo = (years: number) => {
  const d = new Date();
  d.setFullYear(d.getFullYear() - years);
  return d.toISOString().slice(0, 10);
};

export function PortfolioPage() {
  const [assets, setAssets] = useState<Asset[]>([]);
  const [selected, setSelected] = useState<string[]>([]);
  const [weighting, setWeighting] = useState<PortfolioWeighting>("InverseVolatility");
  const [cap, setCap] = useState(20);
  const [trend, setTrend] = useState(false);
  const [fractional, setFractional] = useState(true);
  const [capital, setCapital] = useState(100_000);
  const [from, setFrom] = useState(yearsAgo(3));
  const [result, setResult] = useState<PortfolioResponse | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .assets()
      .then((all) => {
        const eligible = all.filter((a) => a.isActive && (a.assetClass === "Stock" || a.assetClass === "Etf"));
        setAssets(eligible);
        setSelected(eligible.filter((a) => a.assetClass === "Stock").map((a) => a.symbol));
      })
      .catch(() => setAssets([]));
  }, []);

  const toggle = (symbol: string) => setSelected((s) => (s.includes(symbol) ? s.filter((x) => x !== symbol) : [...s, symbol]));

  async function run() {
    setBusy(true);
    setError(null);
    try {
      setResult(await portfolioApi.run({ symbols: selected, from, weighting, maxWeightPercent: cap, trendFilter: trend, initialCapital: capital, fractional }));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha");
    } finally {
      setBusy(false);
    }
  }

  const m = result?.result.metrics;
  const last = result?.result.rebalances.at(-1);
  const weights = useMemo(
    () => Object.entries(result?.result.finalWeights ?? {}).sort((a, b) => b[1] - a[1]),
    [result],
  );

  return (
    <>
      <section className="card">
        <div className="card-head">
          <h2>Carteira</h2>
          <button type="button" className="primary small" style={{ margin: 0 }} disabled={busy || selected.length === 0} onClick={() => void run()}>
            {busy ? "Simulando…" : "▶ Simular carteira"}
          </button>
        </div>
        <div className="form-grid">
          <label>
            Pesos
            <select value={weighting} onChange={(e) => setWeighting(e.target.value as PortfolioWeighting)}>
              <option value="InverseVolatility">Inverso da volatilidade (paridade de risco)</option>
              <option value="Equal">Iguais</option>
            </select>
          </label>
          <label>
            Teto por ativo (%)
            <input type="number" min={1} max={100} step={1} value={cap} onChange={(e) => setCap(Number(e.target.value))} />
          </label>
          <label>
            Capital inicial (R$)
            <input type="number" min={1000} step={1000} value={capital} onChange={(e) => setCapital(Number(e.target.value))} />
          </label>
          <label>
            Início
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label className="checkbox">
            <input type="checkbox" checked={trend} onChange={(e) => setTrend(e.target.checked)} />
            Filtro de tendência: ativo abaixo da média de 200 dias fica fora (a fatia dele vai para o caixa)
          </label>
          <label className="checkbox">
            <input type="checkbox" checked={fractional} onChange={(e) => setFractional(e.target.checked)} />
            Mercado fracionário (ações de 1 em 1, como PETR4F); desligado, só lotes de 100
          </label>
        </div>
        <p className="muted small" style={{ margin: "0 0 0.4rem" }}>Ativos ({selected.length} de {assets.length}) · só ações e ETFs ligados com histórico</p>
        <div style={{ display: "flex", flexWrap: "wrap", gap: "0.4rem" }}>
          {assets.map((a) => (
            <button key={a.symbol} type="button" className={`small ${selected.includes(a.symbol) ? "primary" : "ghost"}`} style={{ margin: 0 }} onClick={() => toggle(a.symbol)} title={a.name}>
              {a.symbol}
            </button>
          ))}
        </div>
        <p className="callout" style={{ margin: "1rem 0 0" }}>
          Rebalanceia no primeiro pregão de cada mês: pesos com dados até o fechamento anterior, ordens na abertura com slippage, corretagem, taxas e lotes. O
          que passa do teto fica no caixa rendendo CDI. Resultado antes do IR.
        </p>
        {error && <p className="callout bad" style={{ marginBottom: 0 }}>{error}</p>}
      </section>

      {result && m && (
        <>
          <div className="metrics">
            <div className="metric kpi"><span className="label">Retorno da carteira</span><strong className={m.totalReturnPercent >= 0 ? "up" : "down"}>{formatSignedPercent(m.totalReturnPercent)}</strong><span className="sub">{formatBrl(m.finalEquity)} no final</span></div>
            <div className="metric kpi"><span className="label">Cesta comprada e segurada</span><strong>{formatSignedPercent(m.benchmarkReturnPercent)}</strong><span className="sub">drawdown {formatPercent(m.benchmarkMaxDrawdownPercent)}</span></div>
            <div className="metric kpi"><span className="label">CDI no período</span><strong>{formatSignedPercent(m.riskFreeReturnPercent)}</strong></div>
            <div className="metric kpi"><span className="label">Drawdown máximo</span><strong>{formatPercent(m.maxDrawdownPercent)}</strong><span className="sub">volatilidade {formatPercent(m.volatilityPercent)}</span></div>
            <div className="metric kpi"><span className="label">Sharpe · Sortino</span><strong>{formatNumber(m.sharpe)} · {formatNumber(m.sortino ?? 0)}</strong><span className="sub">acima do CDI</span></div>
            <div className="metric kpi"><span className="label">Investido em média</span><strong>{formatNumber(result.result.averageInvestedPercent, 1)}%</strong><span className="sub">o resto no caixa (CDI)</span></div>
            <div className="metric kpi"><span className="label">Custos</span><strong>{formatBrl(m.costsPaid)}</strong><span className="sub">{result.result.rebalances.length} rebalanceamentos</span></div>
            <div className="metric kpi"><span className="label">Rendimento do caixa</span><strong>{formatBrl(m.interestEarned)}</strong></div>
          </div>
          <section className="card">
            <div className="card-head">
              <h2>Curva de capital</h2>
              <span className="muted small">{formatDate(result.from)} a {formatDate(result.to)} · carteira × cesta comprada e segurada × CDI</span>
            </div>
            <EquityChart points={result.result.equity} />
          </section>
          <section className="card">
            <div className="card-head">
              <h2>Pesos no fim do período</h2>
              {last && <span className="muted small">último rebalanceamento {formatDate(last.date)} · caixa-alvo {formatNumber(last.cashWeight * 100, 1)}%</span>}
            </div>
            <ul className="locks">
              {weights.map(([symbol, w]) => (
                <li key={symbol}>
                  <span className="mono">{symbol}</span>
                  <span style={{ flex: 1, margin: "0 1rem" }}>
                    <span className="meter" style={{ display: "block", margin: 0 }}>
                      <span className="fill low" style={{ display: "block", width: `${Math.min(100, (w * 100 * 100) / Math.max(cap, 1))}%` }} />
                    </span>
                  </span>
                  <span className="mono">{formatNumber(w * 100, 1)}%</span>
                </li>
              ))}
            </ul>
            {last && last.notBought.length > 0 && (
              <p className="callout warn" style={{ marginBottom: 0 }}>
                Não couberam num lote no último rebalanceamento: {last.notBought.join(", ")}. Ligue o fracionário ou aumente o capital.
              </p>
            )}
            {result.skipped.length > 0 && <p className="muted small" style={{ marginBottom: 0 }}>Ignorados: {result.skipped.join(" · ")}</p>}
          </section>
        </>
      )}
    </>
  );
}
