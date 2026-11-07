import { useEffect, useState } from "react";
import { MODE_LABEL, type ExecutionStatus, type Status } from "../../api";
import { Icon } from "./Icon";
import { nextTheme, type ThemeChoice } from "./theme";

const clock = new Intl.DateTimeFormat("pt-BR", { hour: "2-digit", minute: "2-digit", second: "2-digit", timeZone: "America/Sao_Paulo" });
const THEME_LABEL: Record<ThemeChoice, string> = { auto: "Tema automático", light: "Tema claro", dark: "Tema escuro" };

interface Props {
  title: string;
  status: Status | null;
  execution: ExecutionStatus | null;
  theme: ThemeChoice;
  onTheme: (theme: ThemeChoice) => void;
  onMenu: () => void;
  onRisk: () => void;
}

/** Barra de status sempre visível: modo de execução, robô, MetaTrader 5 e relógio de Brasília. */
export function TopBar({ title, status, execution, theme, onTheme, onMenu, onRisk }: Props) {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  const halted = status?.trading.isHalted;
  const feed = execution?.feed;
  const mode = execution?.mode;

  return (
    <header className="topbar">
      <button type="button" className="icon-button menu-button" aria-label="Abrir menu" onClick={onMenu}>
        <Icon name="menu" />
      </button>
      <h1>{title}</h1>
      {mode && (
        <span className={`pill ${mode === "Mt5Real" ? "real" : mode === "Mt5Demo" ? "warn" : ""}`} title="Modo de execução">
          {MODE_LABEL[mode]}
        </span>
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
