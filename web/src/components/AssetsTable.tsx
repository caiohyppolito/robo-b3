import { useEffect, useState } from "react";
import { api, type Asset, type Candle } from "../api";
import { formatNumber, formatPercent } from "../format";

const classLabel: Record<Asset["assetClass"], string> = {
  Stock: "Ação",
  Etf: "ETF",
  Index: "Índice",
  Future: "Futuro",
};

interface Row {
  asset: Asset;
  last?: Candle;
  previous?: Candle;
}

export function AssetsTable() {
  const [rows, setRows] = useState<Row[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const assets = await api.assets();
        const result = await Promise.all(
          assets.map(async (asset) => {
            const candles = asset.dailyDataSymbol ? await api.candles(asset.symbol, 10) : [];
            return { asset, last: candles.at(-1), previous: candles.at(-2) };
          }),
        );
        if (!cancelled) setRows(result);
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : "Falha ao carregar ativos");
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <section className="card">
      <h2>Ativos acompanhados</h2>
      {error && <p className="error">{error}</p>}
      {!rows && !error && <p className="muted">Carregando…</p>}
      {rows && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Ativo</th>
                <th>Tipo</th>
                <th className="num">Último</th>
                <th className="num">Variação</th>
                <th>Pregão</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(({ asset, last, previous }) => {
                const change = last && previous ? ((last.close - previous.close) / previous.close) * 100 : null;
                return (
                  <tr key={asset.symbol}>
                    <td>
                      <strong>{asset.symbol}</strong>
                      <span className="muted small block">{asset.name}</span>
                    </td>
                    <td>{classLabel[asset.assetClass]}</td>
                    <td className="num">{last ? formatNumber(last.close) : "—"}</td>
                    <td className={`num ${change === null ? "" : change >= 0 ? "up" : "down"}`}>
                      {change === null ? "—" : `${change >= 0 ? "+" : ""}${formatPercent(change)}`}
                    </td>
                    <td className="muted small">
                      {last
                        ? new Date(last.openTime).toLocaleDateString("pt-BR", { timeZone: "UTC" })
                        : asset.dailyDataSymbol
                          ? "aguardando coleta"
                          : "via MT5 (Fase 4)"}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
