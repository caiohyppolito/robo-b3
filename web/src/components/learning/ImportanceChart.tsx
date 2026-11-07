import { useState } from "react";

const LABELS: Record<string, string> = {
  retorno_1d: "Retorno 1 dia",
  retorno_5d: "Retorno 5 dias",
  retorno_20d: "Retorno 20 dias",
  volatilidade_20d: "Volatilidade 20 dias",
  atr_pct: "ATR (% do preço)",
  rsi_14: "RSI 14",
  dist_media_20: "Distância da média 20",
  dist_media_50: "Distância da média 50",
  dist_media_200: "Distância da média 200",
  inclinacao_media_50: "Inclinação da média 50",
  posicao_no_canal_20: "Posição no canal de 20 dias",
  volume_relativo: "Volume relativo",
  queda_desde_maxima_200: "Queda desde a máxima (200d)",
  direcao: "Direção (compra/venda)",
};

export const featureLabel = (name: string) => LABELS[name] ?? name;

const ROW = 24;
const number = new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 3, maximumFractionDigits: 3, signDisplay: "always" });
const LABEL_W = 210;
const LABELED = 3;

/**
 * Importância por permutação: quanto a AUC cai ao embaralhar cada variável.
 * Série única (sem legenda; o título nomeia). Valores negativos = variável que só atrapalha.
 */
export function ImportanceChart({ importance }: { importance: Record<string, number> }) {
  const [hover, setHover] = useState<string | null>(null);
  const entries = Object.entries(importance).sort((a, b) => b[1] - a[1]);
  if (entries.length === 0) return <p className="muted">Sem dados de importância.</p>;

  const max = Math.max(...entries.map(([, v]) => Math.abs(v)), 0.001);
  const hasNegative = entries.some(([, v]) => v < 0);
  const barArea = 100 - (hasNegative ? 20 : 0);
  const zero = hasNegative ? 20 : 0;

  return (
    <div className="importance" role="list" aria-label="Importância das variáveis">
      {entries.map(([name, value], index) => {
        const width = (Math.abs(value) / max) * barArea;
        const left = value >= 0 ? zero : zero - Math.min(zero, width);
        const label = `${featureLabel(name)}: ${number.format(value)} de AUC`;
        return (
          <div
            key={name}
            className="importance-row"
            role="listitem"
            aria-label={label}
            tabIndex={0}
            style={{ height: ROW }}
            onPointerEnter={() => setHover(name)}
            onPointerLeave={() => setHover(null)}
            onFocus={() => setHover(name)}
            onBlur={() => setHover(null)}
          >
            <span className="importance-label" style={{ width: LABEL_W }}>
              {featureLabel(name)}
            </span>
            <span className="importance-track">
              {hasNegative && <span className="importance-zero" style={{ left: `${zero}%` }} />}
              <span
                className={`importance-bar ${value < 0 ? "negative" : ""}`}
                style={{ left: `${left}%`, width: `${Math.min(width, value >= 0 ? 100 - zero : zero)}%` }}
              />
              {(index < LABELED || hover === name) && (
                <span className="importance-value" style={{ left: `calc(${value >= 0 ? zero + width : zero}% + 6px)` }}>
                  {number.format(value)}
                </span>
              )}
            </span>
          </div>
        );
      })}
    </div>
  );
}
