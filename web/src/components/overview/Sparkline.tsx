/** Mini gráfico de linha (série única, sem eixos): a cor indica se o preço subiu ou caiu no período. */
export function Sparkline({ values, width = 120, height = 36, label }: { values: number[]; width?: number; height?: number; label: string }) {
  if (values.length < 2) return <span className="spark-empty muted small">sem série</span>;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = max - min || 1;
  const pad = 3;
  const points = values.map((v, i) => [
    (i / (values.length - 1)) * width,
    pad + (1 - (v - min) / span) * (height - pad * 2),
  ]);
  const line = points.map(([x, y], i) => `${i === 0 ? "M" : "L"}${x!.toFixed(1)},${y!.toFixed(1)}`).join("");
  const area = `${line}L${width},${height}L0,${height}Z`;
  const up = values[values.length - 1]! >= values[0]!;
  const [lx, ly] = points[points.length - 1]!;
  return (
    <svg className={`spark ${up ? "up" : "down"}`} width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label={label}>
      <path d={area} className="spark-area" />
      <path d={line} className="spark-line" fill="none" strokeWidth={1.6} strokeLinejoin="round" />
      <circle cx={lx} cy={ly} r={2.5} className="spark-end" />
    </svg>
  );
}
