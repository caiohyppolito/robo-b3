import type { ComparisonResult, StrategyDef } from "../../api";
import { formatDate, formatNumber, formatPercent, formatSignedPercent } from "../../format";

const paramsText = (p: Record<string, number>) =>
  Object.entries(p)
    .map(([k, v]) => `${k}=${v}`)
    .join(" · ") || "—";

export function ComparisonView({ result, strategies }: { result: ComparisonResult; strategies: StrategyDef[] }) {
  const name = (key: string) => strategies.find((s) => s.key === key)?.name ?? key;
  const best = result.ranking[0];

  return (
    <div className="stack">
      <p className="muted small">
        Parâmetros escolhidos de {formatDate(result.inSampleFrom)} a {formatDate(result.outOfSampleFrom)} (dentro da amostra) e
        avaliados de {formatDate(result.outOfSampleFrom)} a {formatDate(result.outOfSampleTo)}, período que a otimização não viu.
      </p>
      {best && (
        <p className="callout">
          Melhor fora da amostra: <strong>{best.strategyName}</strong>, Sharpe médio {formatNumber(best.averageOutOfSampleSharpe)}.
          {best.beatsBuyAndHoldPercent < 50 && " Atenção: ela perdeu para o comprar e segurar na maioria dos ativos."}
          {best.averageOutOfSampleSharpe <= 0 && " Nenhuma estratégia superou o CDI ajustado ao risco: não opere com dinheiro real ainda."}
        </p>
      )}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>#</th>
              <th>Estratégia</th>
              <th className="num">Sharpe médio</th>
              <th className="num">Retorno médio</th>
              <th className="num">Pior drawdown</th>
              <th className="num">Vence comprar e segurar</th>
              <th className="num">Vence CDI</th>
            </tr>
          </thead>
          <tbody>
            {result.ranking.map((r, i) => (
              <tr key={r.strategyKey}>
                <td>{i + 1}</td>
                <td>
                  <strong>{r.strategyName}</strong>
                  <span className="muted small block">{r.assets} ativos</span>
                </td>
                <td className="num">{formatNumber(r.averageOutOfSampleSharpe)}</td>
                <td className={`num ${r.averageOutOfSampleReturnPercent >= 0 ? "up" : "down"}`}>
                  {formatSignedPercent(r.averageOutOfSampleReturnPercent)}
                </td>
                <td className="num">{formatPercent(r.worstOutOfSampleDrawdownPercent)}</td>
                <td className="num">{r.strategyKey === "buy-and-hold" ? "—" : formatPercent(r.beatsBuyAndHoldPercent)}</td>
                <td className="num">{formatPercent(r.beatsRiskFreePercent)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <details>
        <summary>Detalhe por ativo ({result.rows.length})</summary>
        <div className="table-wrap scroll-y">
          <table>
            <thead>
              <tr>
                <th>Ativo</th>
                <th>Estratégia</th>
                <th>Parâmetros escolhidos</th>
                <th className="num">Sharpe dentro</th>
                <th className="num">Sharpe fora</th>
                <th className="num">Retorno fora</th>
                <th className="num">Comprar e segurar</th>
                <th className="num">Drawdown</th>
                <th className="num">Operações</th>
              </tr>
            </thead>
            <tbody>
              {result.rows.map((r) => (
                <tr key={`${r.symbol}-${r.strategyKey}`}>
                  <td>{r.symbol}</td>
                  <td>{name(r.strategyKey)}</td>
                  <td className="muted small">{paramsText(r.parameters)}</td>
                  <td className="num">{formatNumber(r.inSampleSharpe)}</td>
                  <td className="num">{formatNumber(r.outOfSampleSharpe)}</td>
                  <td className={`num ${r.outOfSampleReturnPercent >= 0 ? "up" : "down"}`}>{formatSignedPercent(r.outOfSampleReturnPercent)}</td>
                  <td className="num">{formatSignedPercent(r.outOfSampleBenchmarkReturnPercent)}</td>
                  <td className="num">{formatPercent(r.outOfSampleMaxDrawdownPercent)}</td>
                  <td className="num">{r.outOfSampleTrades}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
      {result.skipped.length > 0 && (
        <details>
          <summary>Ignorados ({result.skipped.length})</summary>
          <ul className="muted small">
            {result.skipped.map((s) => (
              <li key={s}>{s}</li>
            ))}
          </ul>
        </details>
      )}
    </div>
  );
}
