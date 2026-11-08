import { useEffect, useRef, useState } from "react";
import {
  CandlestickSeries,
  createChart,
  createSeriesMarkers,
  HistogramSeries,
  LineSeries,
  type IChartApi,
  type UTCTimestamp,
} from "lightweight-charts";
import { api, marketApi, type Asset, type ChartData, type Timeframe } from "../../api";
import { formatBrl, formatNumber, formatSignedPercent } from "../../format";

const TIMEFRAMES: Timeframe[] = ["M1", "M5", "M15", "H1", "D1"];
const TF_LABEL: Record<Timeframe, string> = { M1: "1m", M5: "5m", M15: "15m", H1: "1h", D1: "1D" };

const css = (name: string) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

/** Cores do gráfico a partir dos tokens do tema (claro ou escuro). */
function palette() {
  return {
    text: css("--muted"),
    grid: css("--border"),
    up: css("--success"),
    down: css("--danger"),
    sma20: "#1f5bf5",
    sma50: "#8792a9",
    vwap: "#c98a12",
  };
}

const t = (time: number) => time as UTCTimestamp;

function CandleChart({ data }: { data: ChartData }) {
  const box = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!box.current) return;
    const colors = palette();
    const chart: IChartApi = createChart(box.current, {
      autoSize: true,
      layout: { background: { color: "transparent" }, textColor: colors.text, fontFamily: "IBM Plex Mono, monospace", fontSize: 11, attributionLogo: true },
      grid: { vertLines: { color: colors.grid }, horzLines: { color: colors.grid } },
      rightPriceScale: { borderColor: colors.grid },
      timeScale: { borderColor: colors.grid, timeVisible: data.timeframe !== "D1", secondsVisible: false },
      crosshair: { mode: 1 },
      localization: { locale: "pt-BR" },
    });

    const decimals = data.reading && data.reading.last > 1000 ? 0 : 2;
    const priceFormat = { type: "price" as const, precision: decimals, minMove: decimals === 0 ? 1 : 0.01 };
    const candles = chart.addSeries(CandlestickSeries, {
      upColor: colors.up,
      downColor: colors.down,
      borderUpColor: colors.up,
      borderDownColor: colors.down,
      wickUpColor: colors.up,
      wickDownColor: colors.down,
      priceFormat,
    });
    candles.setData(data.candles.map((c) => ({ time: t(c.time), open: c.open, high: c.high, low: c.low, close: c.close })));

    const volume = chart.addSeries(HistogramSeries, { priceScaleId: "volume", priceFormat: { type: "volume" }, lastValueVisible: false, priceLineVisible: false });
    chart.priceScale("volume").applyOptions({ scaleMargins: { top: 0.82, bottom: 0 } });
    volume.setData(data.candles.map((c) => ({ time: t(c.time), value: c.volume, color: c.close >= c.open ? `${colors.up}55` : `${colors.down}55` })));

    const line = (points: { time: number; value: number }[], color: string, style = 0) => {
      if (points.length === 0) return;
      const s = chart.addSeries(LineSeries, { color, lineWidth: 2, lineStyle: style, priceLineVisible: false, lastValueVisible: false, crosshairMarkerVisible: false, priceFormat });
      s.setData(points.map((p) => ({ time: t(p.time), value: p.value })));
    };
    line(data.sma20, colors.sma20);
    line(data.sma50, colors.sma50);
    line(data.vwap, colors.vwap, 2);

    // Execuções do robô no gráfico: compra embaixo, venda em cima. Cada marcador cai no candle que contém o horário.
    const candleTimes = data.candles.map((c) => c.time);
    const snap = (time: number) => {
      let found = candleTimes[0] ?? time;
      for (const ct of candleTimes) {
        if (ct <= time) found = ct;
        else break;
      }
      return found;
    };
    if (data.markers.length > 0 && candleTimes.length > 0) {
      createSeriesMarkers(
        candles,
        data.markers.map((m) => ({
          time: t(snap(m.time)),
          position: m.side === "buy" ? ("belowBar" as const) : ("aboveBar" as const),
          shape: m.side === "buy" ? ("arrowUp" as const) : ("arrowDown" as const),
          color: m.side === "buy" ? colors.up : colors.down,
          text: m.result != null ? `${m.reason} ${formatBrl(m.result)}` : m.reason,
        })),
      );
    }

    chart.timeScale().fitContent();

    // Troca de tema: recolore eixos e grade sem recriar o gráfico.
    const recolor = () => {
      const c = palette();
      chart.applyOptions({ layout: { textColor: c.text }, grid: { vertLines: { color: c.grid }, horzLines: { color: c.grid } } });
    };
    const observer = new MutationObserver(recolor);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    media.addEventListener("change", recolor);

    return () => {
      observer.disconnect();
      media.removeEventListener("change", recolor);
      chart.remove();
    };
  }, [data]);

  return <div ref={box} className="candles" role="img" aria-label={`Gráfico de candles de ${data.contract}`} />;
}

const TREND_TONE = { alta: "good", baixa: "bad", lateral: "neutral", indefinida: "neutral" } as const;

export function ChartPage() {
  const [assets, setAssets] = useState<Asset[]>([]);
  const [symbol, setSymbol] = useState("WIN");
  const [timeframe, setTimeframe] = useState<Timeframe>("M5");
  const [data, setData] = useState<ChartData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    api.assets().then(setAssets).catch(() => setAssets([]));
  }, []);

  useEffect(() => {
    let alive = true;
    const load = (quiet: boolean) => {
      if (!quiet) setLoading(true);
      marketApi
        .chart(symbol, timeframe)
        .then((d) => {
          if (!alive) return;
          setData(d);
          setError(null);
        })
        .catch((e: unknown) => alive && setError(e instanceof Error ? e.message : "Falha ao carregar o gráfico"))
        .finally(() => alive && setLoading(false));
    };
    load(false);
    // Intradiário atualiza sozinho a cada 30 s; o diário não precisa.
    const timer = timeframe === "D1" ? undefined : window.setInterval(() => load(true), 30_000);
    return () => {
      alive = false;
      if (timer) window.clearInterval(timer);
    };
  }, [symbol, timeframe]);

  const reading = data?.reading;
  const decimals = reading && reading.last > 1000 ? 0 : 2;

  return (
    <>
      <div className="chart-toolbar">
        <label>
          Ativo
          <select value={symbol} onChange={(e) => setSymbol(e.target.value)}>
            {(assets.length ? assets : [{ symbol: "WIN", name: "Mini índice" } as Asset]).map((a) => (
              <option key={a.symbol} value={a.symbol}>
                {a.symbol} · {a.name}
              </option>
            ))}
          </select>
        </label>
        <div className="segmented" role="group" aria-label="Tempo gráfico" style={{ marginLeft: "auto", alignSelf: "flex-end" }}>
          {TIMEFRAMES.map((tf) => (
            <button key={tf} type="button" className={tf === timeframe ? "active" : ""} aria-pressed={tf === timeframe} onClick={() => setTimeframe(tf)}>
              {TF_LABEL[tf]}
            </button>
          ))}
        </div>
      </div>

      {error && <p className="callout bad">{error}</p>}

      <div className="chart-layout">
        <section className="card">
          <div className="card-head">
            <h2>
              {data?.contract ?? symbol}{" "}
              {reading && (
                <span className="mono muted" style={{ fontWeight: 500 }}>
                  {formatNumber(reading.last, decimals)}{" "}
                  <span className={reading.changePercent > 0 ? "up" : reading.changePercent < 0 ? "down" : ""}>{formatSignedPercent(reading.changePercent)}</span>
                </span>
              )}
            </h2>
            <div className="chart-legend">
              <span><i style={{ background: "#1f5bf5" }} />Média 20</span>
              <span><i style={{ background: "#8792a9" }} />Média 50</span>
              {timeframe !== "D1" && <span><i style={{ background: "#c98a12" }} />VWAP do dia</span>}
              <span>▲▼ execuções do robô</span>
            </div>
          </div>
          {data && data.candles.length > 0 ? (
            <CandleChart data={data} />
          ) : (
            <p className="muted center" style={{ padding: "4rem 0" }}>{loading ? "Carregando…" : "Sem candles para mostrar."}</p>
          )}
        </section>

        <aside className="stack">
          <section className="card">
            <div className="card-head">
              <h2>Leitura agora</h2>
              {reading && <span className={`chip tone ${TREND_TONE[reading.trend]}`}>Tendência {reading.trend}</span>}
            </div>
            {reading ? (
              <>
                <dl className="reading">
                  <div><dt>ADX 14</dt><dd>{reading.adx14 != null ? formatNumber(reading.adx14, 1) : "—"}</dd></div>
                  <div><dt>ATR 14</dt><dd>{reading.atr14 != null ? formatNumber(reading.atr14, decimals) : "—"}</dd></div>
                  <div><dt>Percentil do ATR</dt><dd>{reading.atrPercentile ?? "—"}</dd></div>
                  <div><dt>RSI 14</dt><dd>{reading.rsi14 != null ? formatNumber(reading.rsi14, 1) : "—"}</dd></div>
                  <div><dt>Volume / média</dt><dd>{reading.volumeRatio != null ? `${formatNumber(reading.volumeRatio, 2)}×` : "—"}</dd></div>
                  <div><dt>VWAP</dt><dd>{reading.vwap != null ? formatNumber(reading.vwap, decimals) : "—"}</dd></div>
                </dl>
                <p className="muted small" style={{ marginBottom: 0 }}>{reading.trendDetail}</p>
              </>
            ) : (
              <p className="muted">Sem leitura.</p>
            )}
          </section>
          <section className="card">
            <h2>Execuções no período</h2>
            {data && data.markers.length > 0 ? (
              <ul className="locks">
                {data.markers.slice(-8).reverse().map((m, i) => (
                  <li key={i}>
                    <span>
                      <span className={m.side === "buy" ? "signal long" : "signal short"}>{m.side === "buy" ? "COMPRA" : "VENDA"}</span>{" "}
                      <span className="mono">{formatNumber(m.price, decimals)}</span> <span className="muted small">{m.reason}</span>
                    </span>
                    {m.result != null && <span className={`mono ${m.result >= 0 ? "up" : "down"}`}>{formatBrl(m.result)}</span>}
                  </li>
                ))}
              </ul>
            ) : (
              <p className="muted small" style={{ margin: 0 }}>Nenhuma execução do robô neste trecho do gráfico.</p>
            )}
            {data && <p className="muted small" style={{ marginBottom: 0 }}>Fonte: {data.source}. Horários de Brasília.</p>}
          </section>
        </aside>
      </div>
    </>
  );
}
