import { useEffect, useState } from "react";
import { ordersApi, type OrderIntent } from "../../api";
import { formatDateTime, formatNumber } from "../../format";

const STATUS: Record<OrderIntent["status"], { label: string; tone: "good" | "bad" | "warn" | "neutral" }> = {
  Filled: { label: "executada", tone: "good" },
  Recovered: { label: "recuperada", tone: "warn" },
  Rejected: { label: "recusada", tone: "bad" },
  Unconfirmed: { label: "não confirmada", tone: "bad" },
  Pending: { label: "pendente", tone: "warn" },
};

/** Ordens do modo atual com a qualidade da execução: slippage contra o preço de referência e latência. */
export function OrdersTable({ refreshKey }: { refreshKey: number }) {
  const [orders, setOrders] = useState<OrderIntent[]>([]);
  useEffect(() => {
    ordersApi.list().then(setOrders).catch(() => setOrders([]));
  }, [refreshKey]);

  if (orders.length === 0) return <p className="muted small">Nenhuma ordem neste modo.</p>;

  return (
    <div className="table-wrap scroll-y">
      <table>
        <thead>
          <tr>
            <th>Horário</th>
            <th>Ordem</th>
            <th className="num">Qtd.</th>
            <th className="num">Referência</th>
            <th className="num">Executado</th>
            <th className="num">Slippage</th>
            <th className="num">Latência</th>
            <th>Situação</th>
          </tr>
        </thead>
        <tbody>
          {orders.map((o) => (
            <tr key={o.clientOrderId} title={`${o.clientOrderId}${o.message ? ` · ${o.message}` : ""}`}>
              <td className="small">{formatDateTime(o.createdAt)}</td>
              <td>
                <strong>{o.symbol}</strong> {o.side === "Buy" ? "compra" : "venda"}
                <span className="muted small block">
                  {o.purpose === "Entry" ? "entrada" : "saída"} · {o.reason}
                  {o.brokerTicket !== null && ` · ticket ${o.brokerTicket}`}
                </span>
              </td>
              <td className="num">{o.fillQuantity ?? o.quantity}</td>
              <td className="num">{formatNumber(o.referencePrice)}</td>
              <td className="num">{o.fillPrice !== null ? formatNumber(o.fillPrice) : "—"}</td>
              <td className={`num ${o.slippage === null ? "" : o.slippage > 0 ? "down" : "up"}`}>
                {o.slippage === null ? "—" : `${o.slippage > 0 ? "+" : ""}${formatNumber(o.slippage)}`}
              </td>
              <td className="num">{o.latencyMs === null ? "—" : `${o.latencyMs} ms`}</td>
              <td>
                <span className={`chip tone ${STATUS[o.status].tone}`}>{STATUS[o.status].label}</span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
