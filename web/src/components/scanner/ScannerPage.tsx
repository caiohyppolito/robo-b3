import { useEffect, useState } from "react";
import { backtestApi, scanApi, type ScanItem, type ScanResult, type StrategyDef } from "../../api";
import { formatDate, formatNumber, formatSignedPercent } from "../../format";

const TONE: Record<ScanItem["verdict"], string> = {
  candidato: "good",
  observar: "warn",
  descartar: "bad",
  "ilíquido": "neutral",
  "sem dados": "neutral",
};

const yearsAgo = (years: number) => {
  const d = new Date();
  d.setFullYear(d.getFullYear() - years);
  return d.toISOString().slice(0, 10);
};

export function ScannerPage() {
  const [strategies, setStrategies] = useState<StrategyDef[]>([]);
  const [minVolume, setMinVolume] = useState(50);
  const [inSample, setInSample] = useState(70);
  const [from, setFrom] = useState(yearsAgo(5));
  const [result, setResult] = useState<ScanResult | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    backtestApi.strategies().then(setStrategies).catch(() => setStrategies([]));
  }, []);

  const name = (key: string | null) => (key ? (strategies.find((s) => s.key === key)?.name ?? key) : "—");

  async function act(label: string, action: () => Promise<void>) {
    setBusy(label);
    setError(null);
    setMessage(null);
    try {
      await action();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha");
    } finally {
      setBusy(null);
    }
  }

  const counts = (result?.items ?? []).reduce<Record<string, number>>((acc, i) => ({ ...acc, [i.verdict]: (acc[i.verdict] ?? 0) + 1 }), {});

  return (
    <>
      <section className="card">
        <div className="card-head">
          <h2>Universo e parâmetros</h2>
          <div className="actions">
            <button type="button" className="small" style={{ margin: 0 }} disabled={busy !== null}
              onClick={() => void act("universe", async () => {
                const r = await scanApi.universe(true);
                setMessage(`${r.changed} ativos ligados (universo de ${r.universe}). Baixe os dados para incluí-los na varredura.`);
              })}>
              {busy === "universe" ? "Ligando…" : "Ligar universo amplo"}
            </button>
            <button type="button" className="small" style={{ margin: 0 }} disabled={busy !== null}
              onClick={() => void act("ingest", async () => {
                const r = await scanApi.ingest();
                setMessage(`${r.assetsProcessed} ativos atualizados, ${r.candlesSaved} candles gravados${r.failures.length ? `; ${r.failures.length} falhas (códigos que mudaram ou sem dados)` : ""}.`);
              })}>
              {busy === "ingest" ? "Baixando… (pode levar minutos)" : "Baixar dados agora"}
            </button>
            <button type="button" className="primary small" style={{ margin: 0 }} disabled={busy !== null}
              onClick={() => void act("scan", async () => setResult(await scanApi.scan(minVolume, inSample, from)))}>
              {busy === "scan" ? "Varrendo…" : "▶ Rodar varredura"}
            </button>
          </div>
        </div>
        <div className="form-grid">
          <label>
            Volume mínimo (R$ milhões por dia)
            <input type="number" min={0} step={5} value={minVolume} onChange={(e) => setMinVolume(Number(e.target.value))} />
          </label>
          <label>
            Início do histórico
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label>
            Parte dentro da amostra (%)
            <input type="number" min={30} max={90} step={5} value={inSample} onChange={(e) => setInSample(Number(e.target.value))} />
          </label>
        </div>
        <p className="callout" style={{ margin: 0 }}>
          Para cada ativo líquido, todas as estratégias são otimizadas na primeira parte do histórico. A escolhida é a de melhor Sharpe <strong>dentro</strong> da
          amostra, e o veredito usa só o período <strong>fora</strong> dela, contra o CDI e o comprar e segurar. É triagem: candidato ainda precisa de período
          novo, teste de sensibilidade e conta demo.
        </p>
        {message && <p className="muted small" style={{ marginBottom: 0 }}>{message}</p>}
        {error && <p className="callout bad" style={{ marginBottom: 0 }}>{error}</p>}
      </section>

      {result && (
        <>
          <div className="metrics">
            {(["candidato", "observar", "descartar", "ilíquido", "sem dados"] as const).map((v) => (
              <div key={v} className="metric kpi">
                <span className="label" style={{ textTransform: "capitalize" }}>{v}</span>
                <strong>{counts[v] ?? 0}</strong>
              </div>
            ))}
            <div className="metric kpi">
              <span className="label">Simulações</span>
              <strong>{result.trialsEvaluated}</strong>
              <span className="sub">mais tentativas, mais chance de sorte</span>
            </div>
          </div>
          <section className="card">
            <div className="card-head">
              <h2>Ativos</h2>
              {result.outOfSampleFrom && result.outOfSampleTo && (
                <span className="muted small">fora da amostra: {formatDate(result.outOfSampleFrom)} a {formatDate(result.outOfSampleTo)}</span>
              )}
            </div>
            <div className="table-wrap">
              <table className="nowrap">
                <thead>
                  <tr>
                    <th>Ativo</th>
                    <th>Veredito</th>
                    <th className="num">Volume/dia</th>
                    <th>Estratégia (escolhida dentro)</th>
                    <th className="num">Sharpe fora</th>
                    <th className="num">Retorno fora</th>
                    <th className="num">Comprar e segurar</th>
                    <th className="num">CDI</th>
                    <th className="num">Drawdown</th>
                    <th className="num">Operações</th>
                    <th>Por quê</th>
                  </tr>
                </thead>
                <tbody>
                  {result.items.map((i) => (
                    <tr key={i.symbol}>
                      <td><strong className="mono">{i.symbol}</strong> <span className="muted small">{i.name}</span></td>
                      <td><span className={`chip tone ${TONE[i.verdict]}`}>{i.verdict}</span></td>
                      <td className="num">R$ {formatNumber(i.averageDailyVolumeMillions, 1)} mi</td>
                      <td>{name(i.strategy)}</td>
                      <td className="num">{i.outOfSampleSharpe != null ? formatNumber(i.outOfSampleSharpe, 2) : "—"}</td>
                      <td className={`num ${(i.outOfSampleReturnPercent ?? 0) > 0 ? "up" : (i.outOfSampleReturnPercent ?? 0) < 0 ? "down" : ""}`}>
                        {i.outOfSampleReturnPercent != null ? formatSignedPercent(i.outOfSampleReturnPercent) : "—"}
                      </td>
                      <td className="num">{i.buyAndHoldReturnPercent != null ? formatSignedPercent(i.buyAndHoldReturnPercent) : "—"}</td>
                      <td className="num">{i.riskFreeReturnPercent != null ? formatSignedPercent(i.riskFreeReturnPercent) : "—"}</td>
                      <td className="num">{i.outOfSampleMaxDrawdownPercent != null ? `${formatNumber(i.outOfSampleMaxDrawdownPercent, 1)}%` : "—"}</td>
                      <td className="num">{i.outOfSampleTrades ?? "—"}</td>
                      <td className="muted small" style={{ whiteSpace: "normal", minWidth: 260 }}>{i.reason}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}
    </>
  );
}
