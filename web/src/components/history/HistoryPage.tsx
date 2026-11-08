import { useEffect, useMemo, useState } from "react";
import { historyApi, MODE_LABEL, type ExecutionMode, type TradeHistory } from "../../api";
import { formatBrl, formatNumber, formatSignedBrl } from "../../format";

const time = new Intl.DateTimeFormat("pt-BR", { hour: "2-digit", minute: "2-digit", second: "2-digit", timeZone: "America/Sao_Paulo" });
const date = new Intl.DateTimeFormat("pt-BR", { day: "2-digit", month: "2-digit", year: "numeric", timeZone: "America/Sao_Paulo" });
const MODE_TONE: Record<ExecutionMode, string> = { Paper: "neutral", Mt5Demo: "warn", Mt5Real: "bad" };

type ResultFilter = "all" | "wins" | "losses";

export function HistoryPage() {
  const [mode, setMode] = useState<ExecutionMode | "">("");
  const [symbol, setSymbol] = useState("");
  const [result, setResult] = useState<ResultFilter>("all");
  const [data, setData] = useState<TradeHistory | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    historyApi
      .trades(mode || undefined, symbol || undefined)
      .then((d) => alive && (setData(d), setError(null)))
      .catch((e: unknown) => alive && setError(e instanceof Error ? e.message : "Falha ao carregar o histórico"));
    return () => {
      alive = false;
    };
  }, [mode, symbol]);

  const trades = useMemo(
    () => (data?.trades ?? []).filter((t) => (result === "wins" ? t.netProfit > 0 : result === "losses" ? t.netProfit <= 0 : true)),
    [data, result],
  );
  const symbols = useMemo(() => [...new Set((data?.trades ?? []).map((t) => t.symbol.replace(/[FGHJKMNQUVXZ]\d\d$/, "")))].sort(), [data]);

  const net = trades.reduce((s, t) => s + t.netProfit, 0);
  const wins = trades.filter((t) => t.netProfit > 0).length;
  const gains = trades.filter((t) => t.netProfit > 0).reduce((s, t) => s + t.netProfit, 0);
  const losses = -trades.filter((t) => t.netProfit < 0).reduce((s, t) => s + t.netProfit, 0);

  return (
    <>
      <section className="card">
        <div className="filters">
          <label>
            Ativo
            <select value={symbol} onChange={(e) => setSymbol(e.target.value)}>
              <option value="">Todos</option>
              {symbols.map((s) => (
                <option key={s} value={s}>{s}</option>
              ))}
            </select>
          </label>
          <label>
            Modo
            <select value={mode} onChange={(e) => setMode(e.target.value as ExecutionMode | "")}>
              <option value="">Todos</option>
              {(Object.keys(MODE_LABEL) as ExecutionMode[]).map((m) => (
                <option key={m} value={m}>{MODE_LABEL[m]}</option>
              ))}
            </select>
          </label>
          <label>
            Resultado
            <select value={result} onChange={(e) => setResult(e.target.value as ResultFilter)}>
              <option value="all">Todos</option>
              <option value="wins">Só ganhos</option>
              <option value="losses">Só perdas</option>
            </select>
          </label>
        </div>
      </section>

      {error && <p className="callout bad">{error}</p>}

      <div className="metrics">
        <div className="metric kpi">
          <span className="label">Operações</span>
          <strong>{trades.length}</strong>
          <span className="sub">{wins} com lucro</span>
        </div>
        <div className="metric kpi">
          <span className="label">Resultado líquido</span>
          <strong className={net > 0 ? "up" : net < 0 ? "down" : ""}>{formatSignedBrl(net)}</strong>
          <span className="sub">após custos</span>
        </div>
        <div className="metric kpi">
          <span className="label">Taxa de acerto</span>
          <strong>{trades.length ? `${formatNumber((100 * wins) / trades.length, 1)}%` : "—"}</strong>
          <span className="sub">{trades.length < 30 ? "amostra pequena: pouco conclusivo" : `${trades.length} operações`}</span>
        </div>
        <div className="metric kpi">
          <span className="label">Fator de lucro</span>
          <strong>{losses > 0 ? formatNumber(gains / losses, 2) : "—"}</strong>
          <span className="sub">ganhos ÷ perdas</span>
        </div>
        <div className="metric kpi">
          <span className="label">Média por operação</span>
          <strong className={net > 0 ? "up" : net < 0 ? "down" : ""}>{trades.length ? formatSignedBrl(net / trades.length) : "—"}</strong>
          <span className="sub">líquida</span>
        </div>
      </div>

      <section className="card">
        <div className="card-head">
          <h2>Operações encerradas</h2>
          <span className="muted small">horários de Brasília</span>
        </div>
        {trades.length === 0 ? (
          <p className="muted" style={{ margin: 0 }}>{data ? "Nenhuma operação encerrada com esses filtros." : "Carregando…"}</p>
        ) : (
          <div className="table-wrap">
            <table className="nowrap">
              <thead>
                <tr>
                  <th>Data</th>
                  <th>Ativo</th>
                  <th>Modo</th>
                  <th>Estratégia</th>
                  <th>Lado</th>
                  <th className="num">Qtd.</th>
                  <th className="num">Entrada</th>
                  <th className="num">Saída</th>
                  <th className="num">Preço entrada</th>
                  <th className="num">Preço saída</th>
                  <th>Saída por</th>
                  <th className="num">Custos</th>
                  <th className="num">Resultado</th>
                </tr>
              </thead>
              <tbody>
                {trades.map((t) => {
                  const digits = t.exitPrice > 1000 ? 0 : 2;
                  return (
                    <tr key={t.positionId}>
                      <td className="mono">{date.format(new Date(t.exitTime))}</td>
                      <td className="mono">{t.symbol}</td>
                      <td><span className={`chip tone ${MODE_TONE[t.mode]}`}>{MODE_LABEL[t.mode]}</span></td>
                      <td>{t.strategy ?? <span className="muted">—</span>}</td>
                      <td className={t.direction === "Compra" ? "up" : "down"}>{t.direction}</td>
                      <td className="num">{t.quantity}</td>
                      <td className="num">{t.entryTime ? time.format(new Date(t.entryTime)) : "—"}</td>
                      <td className="num">{time.format(new Date(t.exitTime))}</td>
                      <td className="num">{t.entryPrice != null ? formatNumber(t.entryPrice, digits) : "—"}</td>
                      <td className="num">{formatNumber(t.exitPrice, digits)}</td>
                      <td>{t.exitReason}</td>
                      <td className="num muted">{formatBrl(t.costs)}</td>
                      <td className={`num ${t.netProfit > 0 ? "up" : t.netProfit < 0 ? "down" : ""}`}>{formatSignedBrl(t.netProfit)}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}
