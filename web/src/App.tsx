import { useCallback, useEffect, useState } from "react";
import { api, session, setUnauthorizedHandler, type Status } from "./api";
import { AssetsTable } from "./components/AssetsTable";
import { AuditList } from "./components/AuditList";
import { Login } from "./components/Login";
import { RiskLimitsForm } from "./components/RiskLimitsForm";
import { StatusPanel } from "./components/StatusPanel";

const REFRESH_MS = 10_000;

export function App() {
  const [loggedIn, setLoggedIn] = useState(() => session.token !== null);
  const [status, setStatus] = useState<Status | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);

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
        <span className="badge">Fase 1 · carteira simulada</span>
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
      {status ? (
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
