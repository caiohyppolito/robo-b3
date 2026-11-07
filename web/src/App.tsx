import { useCallback, useEffect, useState } from "react";
import { api, session, setUnauthorizedHandler, type Status } from "./api";
import { AssetsTable } from "./components/AssetsTable";
import { AuditList } from "./components/AuditList";
import { BacktestPage } from "./components/backtest/BacktestPage";
import { LearningPage } from "./components/learning/LearningPage";
import { Login } from "./components/Login";
import { RiskLimitsForm } from "./components/RiskLimitsForm";
import { StatusPanel } from "./components/StatusPanel";

const REFRESH_MS = 10_000;

export function App() {
  const [loggedIn, setLoggedIn] = useState(() => session.token !== null);
  const [status, setStatus] = useState<Status | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [page, setPage] = useState<"painel" | "backtest" | "ia">("painel");

  useEffect(() => setUnauthorizedHandler(() => setLoggedIn(false)), []);

  const refresh = useCallback(async () => {
    try {
      setStatus(await api.status());
      setError(null);
      setRefreshKey((k) => k + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : "API indisponível");
    }
  }, []);

  useEffect(() => {
    if (!loggedIn) return;
    void refresh();
    const timer = window.setInterval(() => void refresh(), REFRESH_MS);
    return () => window.clearInterval(timer);
  }, [loggedIn, refresh]);

  if (!loggedIn) return <Login onLoggedIn={() => setLoggedIn(true)} />;

  return (
    <div className="app">
      <header>
        <h1>Robô B3</h1>
        <nav className="tabs" aria-label="Seções">
          <button className={page === "painel" ? "active" : ""} onClick={() => setPage("painel")}>
            Painel
          </button>
          <button className={page === "backtest" ? "active" : ""} onClick={() => setPage("backtest")}>
            Backtest
          </button>
          <button className={page === "ia" ? "active" : ""} onClick={() => setPage("ia")}>
            IA
          </button>
        </nav>
        <span className="badge">carteira simulada</span>
        <button
          className="link"
          onClick={() => {
            session.token = null;
            setLoggedIn(false);
          }}
        >
          Sair
        </button>
      </header>
      {error && <p className="error banner">{error}</p>}
      {page === "backtest" ? (
        <main>
          <BacktestPage />
        </main>
      ) : page === "ia" ? (
        <main>
          <LearningPage />
        </main>
      ) : status ? (
        <main>
          <StatusPanel status={status} onChange={refresh} />
          <div className="columns">
            <RiskLimitsForm limits={status.limits} onSaved={refresh} />
            <AuditList refreshKey={refreshKey} />
          </div>
          <AssetsTable />
        </main>
      ) : (
        !error && <p className="muted center">Carregando…</p>
      )}
    </div>
  );
}
