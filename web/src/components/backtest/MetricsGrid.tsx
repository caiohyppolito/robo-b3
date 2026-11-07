import type { Metrics } from "../../api";
import { formatBrl, formatNumber, formatPercent, formatSignedPercent } from "../../format";

interface Tile {
  label: string;
  value: string;
  hint?: string;
  tone?: "up" | "down";
}

const tone = (v: number): "up" | "down" | undefined => (v > 0 ? "up" : v < 0 ? "down" : undefined);

export function MetricsGrid({ metrics: m }: { metrics: Metrics }) {
  const tiles: Tile[] = [
    { label: "Retorno total", value: formatSignedPercent(m.totalReturnPercent), tone: tone(m.totalReturnPercent), hint: `${formatBrl(m.finalEquity)} no final` },
    { label: "Comprar e segurar", value: formatSignedPercent(m.benchmarkReturnPercent), hint: `drawdown ${formatPercent(m.benchmarkMaxDrawdownPercent)}` },
    { label: "CDI no período", value: formatSignedPercent(m.riskFreeReturnPercent) },
    { label: "Retorno anual", value: formatSignedPercent(m.cagrPercent), tone: tone(m.cagrPercent) },
    { label: "Drawdown máximo", value: formatPercent(m.maxDrawdownPercent), hint: "maior queda a partir do pico" },
    { label: "Sharpe", value: formatNumber(m.sharpe), hint: "retorno acima do CDI por unidade de risco" },
    { label: "Sortino", value: formatNumber(m.sortino ?? 0), hint: "como o Sharpe, mas só pune as quedas" },
    { label: "Calmar", value: formatNumber(m.calmar ?? 0), hint: "retorno anual ÷ drawdown máximo" },
    { label: "CVaR 95%", value: formatPercent(m.cVaR95Percent ?? 0), hint: "perda média nos 5% piores dias" },
    { label: "Operações", value: String(m.trades), hint: `${formatPercent(m.winRatePercent)} vencedoras` },
    {
      label: "Resultado por operação",
      value: formatBrl(m.expectancyPerTrade ?? 0),
      tone: tone(m.expectancyPerTrade ?? 0),
      hint: m.trades > 1 ? `± ${formatBrl(m.expectancyMargin95 ?? 0)} (95%, ${m.trades} operações)` : "amostra pequena demais",
    },
    { label: "Giro anual", value: `${formatNumber(m.turnoverPerYear ?? 0)}×`, hint: "volume negociado ÷ capital, por ano" },
    { label: "Fator de lucro", value: formatNumber(m.profitFactor), hint: "ganhos ÷ perdas" },
    { label: "Tempo posicionado", value: formatPercent(m.exposurePercent) },
    { label: "Custos + IR", value: formatBrl(m.costsPaid + m.taxesPaid), hint: `IR ${formatBrl(m.taxesPaid)}` },
    { label: "Rendimento do caixa", value: formatBrl(m.interestEarned), hint: "CDI sobre o dinheiro parado" },
  ];

  if ((m.ambiguousTrades ?? 0) > 0) {
    tiles.push({
      label: "Barras ambíguas",
      value: String(m.ambiguousTrades),
      hint: "stop e alvo na mesma barra: contadas como stop (pior caso)",
    });
  }

  if (m.signalsFiltered > 0) {
    tiles.push({ label: "Sinais vetados pela IA", value: String(m.signalsFiltered), hint: "entradas que a IA julgou ruins" });
  }

  return (
    <div className="metrics">
      {tiles.map((t) => (
        <div key={t.label} className="metric">
          <span className="muted small">{t.label}</span>
          <strong className={t.tone}>{t.value}</strong>
          {t.hint && <span className="muted small">{t.hint}</span>}
        </div>
      ))}
    </div>
  );
}
