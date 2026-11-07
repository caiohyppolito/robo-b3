import { useCallback, useEffect, useMemo, useState } from "react";
import {
  backtestApi,
  overviewApi,
  paperApi,
  settingsApi,
  MODE_LABEL,
  type Assignment,
  type Decision,
  type ExecutionStatus,
  type MarketWatch,
  type PaperAccount,
  type PaperSettings,
  type Status,
  type StrategyDef,
} from "../../api";
import { formatBrl, formatNumber, formatPercent } from "../../format";
import { DecisionList } from "./DecisionList";
import { Sparkline } from "./Sparkline";

const REFRESH_MS = 15_000;
const brtTime = new Intl.DateTimeFormat("pt-BR", { hour: "2-digit", minute: "2-digit", timeZone: "America/Sao_Paulo", hourCycle: "h23" });

type Tone = "good" | "warn" | "bad" | "neutral";

interface Lock {
  name: string;
  detail: string;
  state: string;
  tone: Tone;
}

interface Alert {
  level: "Crítico" | "Atenção";
  title: string;
  detail: string;
}

interface Props {
  status: Status | null;
  execution: ExecutionStatus | null;
  onNavigate: (page: "decisoes" | "risco" | "operacao") => void;
}

const signed = (v: number) => `${v > 0 ? "+" : ""}${formatBrl(v)}`;

export function OverviewPage({ status, execution, onNavigate }: Props) {
  const [watch, setWatch] = useState<MarketWatch | null>(null);
  const [account, setAccount] = useState<PaperAccount | null>(null);
  const [decisions, setDecisions] = useState<Decision[]>([]);
  const [assignments, setAssignments] = useState<Assignment[]>([]);
  const [settings, setSettings] = useState<PaperSettings | null>(null);
  const [strategies, setStrategies] = useState<StrategyDef[]>([]);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      const [w, a, d, s] = await Promise.all([overviewApi.watch(), paperApi.account(), overviewApi.decisions(200), paperApi.assignments()]);
      setWatch(w);
      setAccount(a);
      setDecisions(d);
      setAssignments(s);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao atualizar");
    }
  }, []);

  useEffect(() => {
    settingsApi.paper().then(setSettings).catch(() => undefined);
    backtestApi.strategies().then(setStrategies).catch(() => undefined);
    void refresh();
    const timer = window.setInterval(() => void refresh(), REFRESH_MS);
    return () => window.clearInterval(timer);
  }, [refresh]);

  const strategyName = (key: string) => strategies.find((s) => s.key === key)?.name ?? key;
  const enabled = assignments.filter((a) => a.isEnabled);

  const last24h = useMemo(() => {
    const since = Date.now() - 24 * 3600_000;
    const recent = decisions.filter((d) => new Date(d.time).getTime() >= since);
    return {
      total: recent.length,
      entered: recent.filter((d) => d.outcome === "Entered").length,
      blocked: recent.filter((d) => d.outcome === "Blocked" || d.outcome === "Rejected").length,
      vetoed: recent.filter((d) => d.outcome === "Vetoed").length,
      failed: recent.filter((d) => d.outcome === "Failed").length,
    };
  }, [decisions]);

  const nowBrt = brtTime.format(new Date());
  const inWindow = settings ? nowBrt >= settings.noEntriesBefore.slice(0, 5) && nowBrt <= settings.noEntriesAfter.slice(0, 5) : null;
  const marketOpen = watch?.items.some((i) => i.isFresh) ?? false;

  const locks: Lock[] = [];
  const alerts: Alert[] = [];
  if (status && execution) {
    const t = status.trading;
    const p = status.portfolio;
    const l = status.limits;
    locks.push({
      name: "Robô",
      detail: t.isHalted ? t.haltReason ?? "travado" : "novas entradas permitidas",
      state: t.isHalted ? "travado" : "liberado",
      tone: t.isHalted ? "bad" : "good",
    });
    locks.push({
      name: "Modo de execução",
      detail: execution.mode === "Paper" ? "nada vai para a corretora" : "ordens vão para o MetaTrader 5",
      state: MODE_LABEL[execution.mode],
      tone: execution.mode === "Mt5Real" ? "bad" : execution.mode === "Mt5Demo" ? "warn" : "neutral",
    });
    locks.push({
      name: "MetaTrader 5",
      detail: execution.feed.connected ? `${execution.feed.server ?? ""} · conta ${execution.feed.login ?? "?"}` : execution.feed.message ?? "",
      state: execution.feed.connected ? (execution.feed.isDemo === false ? "conta real" : "conectado") : "desconectado",
      tone: execution.feed.connected ? "good" : "bad",
    });
    if (settings) {
      locks.push({
        name: "Horário de entradas",
        detail: `${settings.noEntriesBefore.slice(0, 5)}–${settings.noEntriesAfter.slice(0, 5)} · day trade zera às ${settings.dayTradeFlattenAt.slice(0, 5)}`,
        state: inWindow ? "dentro" : "fora",
        tone: inWindow ? "good" : "neutral",
      });
    }
    const dayUse = l.maxDailyLossPercent ? p.dailyLossPercent / l.maxDailyLossPercent : 0;
    locks.push({
      name: "Perda do dia",
      detail: `${formatPercent(p.dailyLossPercent)} de ${formatPercent(l.maxDailyLossPercent)} (${formatBrl((p.equity * l.maxDailyLossPercent) / 100)})`,
      state: dayUse >= 1 ? "estourada" : dayUse >= 0.5 ? "atenção" : "livre",
      tone: dayUse >= 1 ? "bad" : dayUse >= 0.5 ? "warn" : "good",
    });
    const ddUse = l.maxDrawdownPercent ? p.drawdownPercent / l.maxDrawdownPercent : 0;
    locks.push({
      name: "Drawdown",
      detail: `${formatPercent(p.drawdownPercent)} de ${formatPercent(l.maxDrawdownPercent)}`,
      state: ddUse >= 1 ? "estourado" : ddUse >= 0.5 ? "atenção" : "livre",
      tone: ddUse >= 1 ? "bad" : ddUse >= 0.5 ? "warn" : "good",
    });
    const contracts = account?.positions.filter((x) => x.contract.startsWith("WIN") || x.contract.startsWith("WDO")).reduce((s, x) => s + x.quantity, 0) ?? 0;
    locks.push({
      name: "Contratos futuros",
      detail: `${contracts} de ${l.maxFuturesContracts} em aberto`,
      state: contracts >= l.maxFuturesContracts ? "no teto" : "livre",
      tone: contracts >= l.maxFuturesContracts ? "warn" : "good",
    });

    if (t.isHalted && t.changedBy !== "sistema") alerts.push({ level: "Crítico", title: "Robô travado", detail: t.haltReason ?? "" });
    if (!execution.feed.connected) alerts.push({ level: "Crítico", title: "MetaTrader 5 desconectado", detail: execution.feed.message ?? "" });
    if (execution.mode === "Mt5Real") alerts.push({ level: "Atenção", title: "Conta real", detail: "As ordens usam dinheiro de verdade." });
    if (last24h.failed > 0) alerts.push({ level: "Crítico", title: "Ordens que falharam", detail: `${last24h.failed} nas últimas 24 h. Confira no MT5.` });
    if (dayUse >= 0.5 && dayUse < 1) alerts.push({ level: "Atenção", title: "Perda do dia acima da metade do limite", detail: `${formatPercent(p.dailyLossPercent)} de ${formatPercent(l.maxDailyLossPercent)}` });
    if (account && account.initialCapital >= 500_000) {
      alerts.push({
        level: "Atenção",
        title: "Patrimônio de referência alto",
        detail: `O livro deste modo usa ${formatBrl(account.initialCapital)}; os limites percentuais crescem junto. O teto de contratos é o que segura o tamanho.`,
      });
    }
    if (enabled.length === 0) alerts.push({ level: "Atenção", title: "Nenhuma alocação ligada", detail: "O robô não vai operar. Ligue uma na aba Operação." });
  }

  const summary = !status || !execution
    ? "Carregando…"
    : [
        status.trading.isHalted ? "Robô travado." : "Robô liberado.",
        `Modo ${MODE_LABEL[execution.mode]}.`,
        `${enabled.length} ${enabled.length === 1 ? "alocação ligada" : "alocações ligadas"}.`,
        marketOpen ? "Mercado com cotações ao vivo." : "Sem cotação recente: mercado fechado ou MT5 parado.",
      ].join(" ");

  const exposure = account?.positions.reduce((s, x) => s + Math.abs(x.unrealizedProfit), 0) ?? 0;

  return (
    <>
      <div className="page-head">
        <div>
          <p>{summary}</p>
        </div>
        <div className="actions">
          <button type="button" onClick={() => onNavigate("risco")}>
            Risco e travas
          </button>{" "}
          <button type="button" className="primary" onClick={() => onNavigate("decisoes")}>
            Ver decisões
          </button>
        </div>
      </div>
      {error && <p className="error banner">{error}</p>}

      <section className="tiles">
        <article className="card tile">
          <span className="muted small">Patrimônio · {execution ? MODE_LABEL[execution.mode] : "…"}</span>
          <strong className="tile-value">{account ? formatBrl(account.equity) : "—"}</strong>
          {account && (
            <span className={`small ${account.totalProfit >= 0 ? "up" : "down"}`}>
              {signed(account.totalProfit)} desde o início
            </span>
          )}
        </article>
        <article className="card tile">
          <span className="muted small">Resultado do dia</span>
          <strong className={`tile-value ${account ? (account.dayProfit > 0 ? "up" : account.dayProfit < 0 ? "down" : "") : ""}`}>
            {account ? signed(account.dayProfit) : "—"}
          </strong>
          {account && <span className="muted small">custos {formatBrl(account.costsPaid)}</span>}
        </article>
        <article className="card tile">
          <span className="muted small">Posições abertas</span>
          <strong className="tile-value">{account?.positions.length ?? "—"}</strong>
          <span className="muted small">{account?.positions.length ? `resultado aberto ${signed(account.positions.reduce((s, x) => s + x.unrealizedProfit, 0))}` : exposure === 0 ? "nenhum risco aberto" : ""}</span>
        </article>
        <article className="card tile">
          <span className="muted small">Decisões nas últimas 24 h</span>
          <strong className="tile-value">{last24h.total}</strong>
          <span className="small">
            <span className="up">{last24h.entered} entradas</span> · <span className="warn-text">{last24h.blocked} bloqueadas</span> ·{" "}
            <span className="warn-text">{last24h.vetoed} vetadas</span>
          </span>
        </article>
      </section>

      <section className="card">
        <div className="card-head">
          <h2>Mercado</h2>
          <span className="muted small">
            {watch && !watch.connected ? watch.message : "5 minutos do último pregão · ponto verde = cotação ao vivo"}
          </span>
        </div>
        {!watch ? (
          <p className="muted">Carregando…</p>
        ) : watch.items.length === 0 ? (
          <p className="muted small">Sem cotações: conecte o MetaTrader 5.</p>
        ) : (
          <div className="watch">
            {watch.items.map((item) => (
              <div key={item.symbol} className="watch-row">
                <div className="watch-name">
                  <strong>{item.contract}</strong>
                  <span className="muted small">{item.name}</span>
                </div>
                <Sparkline values={item.spark} label={`${item.contract}: barras de 5 minutos do último pregão`} />
                <div className="watch-price">
                  <strong className="mono">{item.last !== null ? formatNumber(item.last) : "—"}</strong>
                  <span className={`small mono ${item.changePercent === null ? "muted" : item.changePercent >= 0 ? "up" : "down"}`}>
                    {item.changePercent === null ? "—" : `${item.changePercent > 0 ? "+" : ""}${formatPercent(item.changePercent)}`}
                  </span>
                </div>
                <span className={`live ${item.isFresh ? "on" : ""}`} title={item.error ?? (item.isFresh ? "cotação ao vivo" : "sem cotação recente")}>
                  <span className="dot" aria-hidden="true" />
                  <span className="sr-only">{item.error ? "erro" : item.isFresh ? "ao vivo" : "parado"}</span>
                </span>
              </div>
            ))}
          </div>
        )}
      </section>

      <div className="overview-grid">
        <section className="card">
          <div className="card-head">
            <h2>Últimas decisões</h2>
            <button type="button" className="link" onClick={() => onNavigate("decisoes")}>
              Todas
            </button>
          </div>
          <DecisionList decisions={decisions.filter((d) => d.outcome !== "NoSignal" && d.outcome !== "Held").slice(0, 6)} strategyName={strategyName} />
          {decisions.length > 0 && <p className="muted small">"Sem sinal" e "manteve" ficam só na página de decisões.</p>}
        </section>

        <div className="stack">
          <section className="card">
            <h2>Alertas</h2>
            {alerts.length === 0 ? (
              <p className="muted small">Nenhum alerta.</p>
            ) : (
              <ul className="alerts">
                {alerts.map((a, i) => (
                  <li key={i}>
                    <span className={`chip tone ${a.level === "Crítico" ? "bad" : "warn"}`}>{a.level}</span>
                    <div>
                      <strong>{a.title}</strong>
                      <span className="muted small block">{a.detail}</span>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="card">
            <div className="card-head">
              <h2>Travas</h2>
              <button type="button" className="link" onClick={() => onNavigate("risco")}>
                Ajustar
              </button>
            </div>
            <ul className="locks">
              {locks.map((lock) => (
                <li key={lock.name}>
                  <div>
                    <strong>{lock.name}</strong>
                    <span className="muted small block">{lock.detail}</span>
                  </div>
                  <span className={`chip tone ${lock.tone}`}>{lock.state}</span>
                </li>
              ))}
            </ul>
          </section>
        </div>
      </div>
    </>
  );
}
