import { useEffect, useMemo, useRef, useState, type PointerEvent } from "react";
import type { EquityPoint } from "../../api";
import { formatBrl, formatCompactBrl, formatDate, formatMonthYear } from "../../format";

type SeriesKey = "equity" | "benchmark" | "riskFree";

const SERIES: { key: SeriesKey; label: string; color: string }[] = [
  { key: "equity", label: "Estratégia", color: "var(--series-1)" },
  { key: "benchmark", label: "Comprar e segurar", color: "var(--series-2)" },
  { key: "riskFree", label: "CDI", color: "var(--series-3)" },
];

const HEIGHT = 300;
const MARGIN = { top: 12, right: 132, bottom: 28, left: 64 };

function niceTicks(min: number, max: number, count: number): number[] {
  const span = max - min || 1;
  const rough = span / count;
  const power = 10 ** Math.floor(Math.log10(rough));
  const step = [1, 2, 2.5, 5, 10].map((m) => m * power).find((s) => s >= rough) ?? rough;
  // Estende até o múltiplo seguinte, para o maior valor ficar sob uma linha de grade rotulada.
  const ticks: number[] = [];
  const end = Math.ceil(max / step) * step;
  for (let v = Math.floor(min / step) * step; v <= end + step * 1e-9; v += step) ticks.push(v);
  return ticks;
}

/** Curva de patrimônio: estratégia x comprar e segurar x CDI, com cursor e rótulos diretos. */
export function EquityChart({ points }: { points: EquityPoint[] }) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(720);
  const [hover, setHover] = useState<number | null>(null);

  useEffect(() => {
    const element = wrapRef.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => entry && setWidth(Math.max(320, entry.contentRect.width)));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const narrow = width < 560;
  const margin = narrow ? { ...MARGIN, right: 16 } : MARGIN;
  const plotW = width - margin.left - margin.right;
  const plotH = HEIGHT - margin.top - margin.bottom;

  const geometry = useMemo(() => {
    const values = points.flatMap((p) => [p.equity, p.benchmark, p.riskFree]);
    const yTicks = niceTicks(Math.min(...values), Math.max(...values), 5);
    const yMin = yTicks[0] ?? Math.min(...values);
    const yMax = yTicks.at(-1) ?? Math.max(...values);
    const x = (i: number) => (points.length <= 1 ? 0 : (i / (points.length - 1)) * plotW);
    const y = (v: number) => plotH - ((v - yMin) / (yMax - yMin || 1)) * plotH;
    const paths = Object.fromEntries(
      SERIES.map((s) => [s.key, points.map((p, i) => `${i === 0 ? "M" : "L"}${x(i).toFixed(1)},${y(p[s.key]).toFixed(1)}`).join("")]),
    ) as Record<SeriesKey, string>;

    const tickCount = Math.max(2, Math.min(6, Math.floor(plotW / 110)));
    const xTicks = Array.from({ length: tickCount }, (_, k) => Math.round((k / (tickCount - 1)) * (points.length - 1)));

    // Rótulos diretos no fim das linhas, afastados para não colidirem.
    const last = points.at(-1);
    const labels = last
      ? SERIES.map((s) => ({ ...s, y: y(last[s.key]), value: last[s.key] })).sort((a, b) => a.y - b.y)
      : [];
    for (let k = 1; k < labels.length; k++) {
      const prev = labels[k - 1]!;
      const cur = labels[k]!;
      if (cur.y - prev.y < 16) cur.y = prev.y + 16;
    }

    return { x, y, yTicks, xTicks, paths, labels };
  }, [points, plotW, plotH]);

  if (points.length < 2) return <p className="muted">Dados insuficientes para o gráfico.</p>;

  function onMove(event: PointerEvent<SVGRectElement>) {
    const rect = event.currentTarget.getBoundingClientRect();
    const ratio = (event.clientX - rect.left) / rect.width;
    setHover(Math.max(0, Math.min(points.length - 1, Math.round(ratio * (points.length - 1)))));
  }

  const hovered = hover === null ? null : points[hover];
  const hoverX = hover === null ? 0 : geometry.x(hover);

  return (
    <div className="chart" ref={wrapRef}>
      <ul className="legend" aria-label="Legenda">
        {SERIES.map((s) => (
          <li key={s.key}>
            <span className="key-line" style={{ background: s.color }} />
            {s.label}
          </li>
        ))}
      </ul>
      <svg width={width} height={HEIGHT} role="img" aria-label="Curva de patrimônio da estratégia, do comprar e segurar e do CDI">
        <g transform={`translate(${margin.left},${margin.top})`}>
          {geometry.yTicks.map((t) => (
            <g key={t}>
              <line className="grid" x1={0} x2={plotW} y1={geometry.y(t)} y2={geometry.y(t)} />
              <text className="axis" x={-8} y={geometry.y(t)} dy="0.32em" textAnchor="end">
                {formatCompactBrl(t)}
              </text>
            </g>
          ))}
          {geometry.xTicks.map((i, k) => (
            <text
              key={i}
              className="axis"
              x={geometry.x(i)}
              y={plotH + 18}
              textAnchor={k === 0 ? "start" : k === geometry.xTicks.length - 1 ? "end" : "middle"}
            >
              {formatMonthYear(points[i]!.date)}
            </text>
          ))}
          {[...SERIES].reverse().map((s) => (
            <path key={s.key} d={geometry.paths[s.key]} fill="none" stroke={s.color} strokeWidth={2} strokeLinejoin="round" />
          ))}
          {!narrow &&
            geometry.labels.map((l) => (
              <g key={l.key} transform={`translate(${plotW + 8},${l.y})`}>
                <circle r={4} cx={4} fill={l.color} stroke="var(--surface)" strokeWidth={2} />
                <text className="direct-label" x={14} dy="0.32em">
                  {l.label}
                </text>
              </g>
            ))}
          {hovered && (
            <g pointerEvents="none">
              <line className="crosshair" x1={hoverX} x2={hoverX} y1={0} y2={plotH} />
              {SERIES.map((s) => (
                <circle key={s.key} cx={hoverX} cy={geometry.y(hovered[s.key])} r={4} fill={s.color} stroke="var(--surface)" strokeWidth={2} />
              ))}
            </g>
          )}
          <rect
            width={plotW}
            height={plotH}
            fill="transparent"
            onPointerMove={onMove}
            onPointerLeave={() => setHover(null)}
          />
        </g>
      </svg>
      {hovered && (
        <div
          className="tooltip"
          style={
            // Fica do lado oposto ao cursor para não cobrir a linha que está sendo lida.
            hoverX > plotW / 2
              ? { right: width - (margin.left + hoverX) + 12, top: margin.top + 28 }
              : { left: margin.left + hoverX + 12, top: margin.top + 28 }
          }
        >
          <div className="muted small">{formatDate(hovered.date)}</div>
          {SERIES.map((s) => (
            <div key={s.key} className="tooltip-row">
              <span className="key-line" style={{ background: s.color }} />
              <strong>{formatBrl(hovered[s.key])}</strong>
              <span className="muted small">{s.label}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
