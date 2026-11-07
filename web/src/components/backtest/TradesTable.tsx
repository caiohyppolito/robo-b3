import type { Trade } from "../../api";
import { formatBrl, formatDate, formatNumber, formatSignedPercent } from "../../format";

export function TradesTable({ trades }: { trades: Trade[] }) {
  if (trades.length === 0) return <p className="muted">Nenhuma operação no período.</p>;

  return (
    <div className="table-wrap scroll-y">
      <table>
        <thead>
          <tr>
            <th>Lado</th>
            <th>Entrada</th>
            <th>Saída</th>
            <th className="num">Qtd.</th>
            <th className="num">Preço entrada</th>
            <th className="num">Preço saída</th>
            <th className="num">Resultado</th>
            <th className="num">%</th>
            <th>Motivo</th>
          </tr>
        </thead>
        <tbody>
          {[...trades].reverse().map((t, i) => (
            <tr key={`${t.entryDate}-${i}`}>
              <td>{t.direction === "Long" ? "Compra" : "Venda"}</td>
              <td>{formatDate(t.entryDate)}</td>
              <td>{formatDate(t.exitDate)}</td>
              <td className="num">{t.quantity}</td>
              <td className="num">{formatNumber(t.entryPrice)}</td>
              <td className="num">{formatNumber(t.exitPrice)}</td>
              <td className={`num ${t.netProfit >= 0 ? "up" : "down"}`}>{formatBrl(t.netProfit)}</td>
              <td className={`num ${t.returnPercent >= 0 ? "up" : "down"}`}>{formatSignedPercent(t.returnPercent)}</td>
              <td className="muted small">{t.exitReason}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
