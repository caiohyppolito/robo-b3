import { useEffect, useState } from "react";
import { overviewApi, type ExecutionMode, type ExecutionStatus, type Status, type WatchItem } from "../../api";
import { formatNumber, formatSignedPercent } from "../../format";
import { Icon } from "./Icon";
import { nextTheme, type ThemeChoice } from "./theme";

const clock = new Intl.DateTimeFormat("pt-BR", { hour: "2-digit", minute: "2-digit", second: "2-digit", timeZone: "America/Sao_Paulo" });
const THEME_LABEL: Record<ThemeChoice, string> = { auto: "Tema automático", light: "Tema claro", dark: "Tema escuro" };
const MODES: { id: ExecutionMode; label: string }[] = [
  { id: "Paper", label: "Simulado" },
  { id: "Mt5Demo", label: "MT5 demo" },
  { id: "Mt5Real", label: "MT5 real" },
];
const SYMBOLS = ["WIN", "WDO"] as const;

interface Props {
  status: Status | null;
  execution: ExecutionStatus | null;
  theme: ThemeChoice;
  onTheme: (theme: ThemeChoice) => void;
  onMenu: () => void;
  onRisk: () => void;
  onExecution: () => void;
}

/** Topo sempre visível: cotação do mini índice ou dólar, modo de execução, robô, MetaTrader 5 e relógio de Brasília. */
export function TopBar({ status, execution, theme, onTheme, onMenu, onRisk, onExecution }: Props) {
  const [now, setNow] = useState(() => new Date());
  const [symbol, setSymbol] = useState<(typeof SYMBOLS)[number]>("WIN");
  const [watch, setWatch] = useState<WatchItem[]>([]);

  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    let alive = true;
    const load = () =>
      overviewApi
        .watch()
        .then((w) => alive && setWatch(w.items))
        .catch(() => alive && setWatch([]));
    void load();
    const timer = window.setInterval(load, 10_000);
    return () => {
      alive = false;
      window.clearInterval(timer);
    };
  }, []);

  const halted = status?.trading.isHalted;
  const feed = execution?.feed;
  const mode = execution?.mode;
  const quote = watch.find((w) => w.symbol === symbol);
  const change = quote?.changePercent ?? null;

  return (
    <header className="topbar">
      <button type="button" className="icon-button menu-button" aria-label="Abrir menu" onClick={onMenu}>
        <Icon name="menu" />
      </button>
      <div className="segmented" role="group" aria-label="Ativo da cotação">
        {SYMBOLS.map((s) => (
          <button key={s} type="button" className={s === symbol ? "active" : ""} aria-pressed={s === symbol} onClick={() => setSymbol(s)}>
            {s}
          </button>
        ))}
      </div>
      <div className="quote" aria-live="polite">
        {quote?.last != null ? (
          <>
            <span className="price">{formatNumber(quote.last, quote.last > 1000 ? 0 : 2)}</span>
            {change != null && <span className={`change ${change > 0 ? "up" : change < 0 ? "down" : "muted"}`}>{formatSignedPercent(change)}</span>}
            <span className="contract hide-mobile">{quote.contract}</span>
          </>
        ) : (
          <span className="muted small">sem cotação</span>
        )}
      </div>
      {mode && (
        <div className="segmented hide-mobile" role="group" aria-label="Modo de execução (troca na tela Operação)">
          {MODES.map((m) => (
            <button
              key={m.id}
              type="button"
              className={`${m.id === mode ? "active" : ""} ${m.id === "Mt5Real" ? "real" : ""}`}
              aria-pressed={m.id === mode}
              title="Trocar o modo na tela Operação"
              onClick={onExecution}
            >
              {m.label}
            </button>
          ))}
        </div>
      )}
      {status && (
        <button
          type="button"
          className={`pill ${halted ? "bad" : "good"}`}
          title={halted ? `Travado: ${status.trading.haltReason ?? ""}` : "Robô liberado para novas entradas"}
          onClick={onRisk}
        >
          <span className="dot" aria-hidden="true" />
          {halted ? "Robô travado" : "Robô liberado"}
        </button>
      )}
      {feed && (
        <span className={`pill hide-mobile ${feed.connected ? "good" : "bad"}`} title={feed.message ?? feed.server ?? ""}>
          <span className="dot" aria-hidden="true" />
          MT5 {feed.connected ? (feed.mode === "fake" ? "simulação" : feed.isDemo ? "demo" : feed.isDemo === false ? "REAL" : "") : "desconectado"}
        </span>
      )}
      <span className="pill mono hide-mobile" title="Horário de Brasília">
        {clock.format(now)} BRT
      </span>
      <button type="button" className="icon-button" aria-label={THEME_LABEL[theme]} title={THEME_LABEL[theme]} onClick={() => onTheme(nextTheme(theme))}>
        <Icon name={theme === "auto" ? "auto" : theme === "light" ? "sun" : "moon"} />
      </button>
    </header>
  );
}
