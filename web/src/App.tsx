import { useCallback, useEffect, useState } from "react";
import { api, executionApi, session, setUnauthorizedHandler, type ExecutionStatus, type Status } from "./api";
import { AssetsTable } from "./components/AssetsTable";
import { AuditList } from "./components/AuditList";
import { BacktestPage } from "./components/backtest/BacktestPage";
import { LearningPage } from "./components/learning/LearningPage";
import { Login } from "./components/Login";
import { OperationPage } from "./components/operation/OperationPage";
import { DecisionsPage } from "./components/overview/DecisionsPage";
import { OverviewPage } from "./components/overview/OverviewPage";
import { RiskLimitsForm } from "./components/RiskLimitsForm";
import { StatusPanel } from "./components/StatusPanel";
import { pageFromHash, PAGE_TITLE, type PageId } from "./components/shell/navigation";
import { Sidebar } from "./components/shell/Sidebar";
import { applyTheme, loadTheme, type ThemeChoice } from "./components/shell/theme";
import { TopBar } from "./components/shell/TopBar";

const REFRESH_MS = 10_000;

export function App() {
  const [loggedIn, setLoggedIn] = useState(() => session.token !== null);
  const [status, setStatus] = useState<Status | null>(null);
  const [execution, setExecution] = useState<ExecutionStatus | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const [page, setPage] = useState<PageId>(pageFromHash);
  const [menuOpen, setMenuOpen] = useState(false);
  const [theme, setTheme] = useState<ThemeChoice>(loadTheme);

  useEffect(() => setUnauthorizedHandler(() => setLoggedIn(false)), []);
  useEffect(() => applyTheme(theme), [theme]);
  useEffect(() => {
    const onHash = () => setPage(pageFromHash());
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  const refresh = useCallback(async () => {
    try {
      const [s, e] = await Promise.all([api.status(), executionApi.get()]);
      setStatus(s);
      setExecution(e);
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

  const navigate = (next: PageId) => {
    window.location.hash = `/${next}`;
    setPage(next);
    setMenuOpen(false);
  };

  const logout = () => {
    session.token = null;
    setLoggedIn(false);
  };

  if (!loggedIn) return <Login onLoggedIn={() => setLoggedIn(true)} />;

  return (
    <div className="shell">
      <Sidebar page={page} open={menuOpen} onNavigate={navigate} onLogout={logout} />
      {menuOpen && <div className="scrim" onClick={() => setMenuOpen(false)} aria-hidden="true" />}
      <div className="content">
        <TopBar
          title={PAGE_TITLE[page]}
          status={status}
          execution={execution}
          theme={theme}
          onTheme={setTheme}
          onMenu={() => setMenuOpen((open) => !open)}
          onRisk={() => navigate("risco")}
        />
        <main>
          {error && <p className="error banner">{error}</p>}
          {page === "situacao" && <OverviewPage status={status} execution={execution} onNavigate={navigate} />}
          {page === "operacao" && <OperationPage />}
          {page === "decisoes" && <DecisionsPage />}
          {page === "backtest" && <BacktestPage />}
          {page === "ia" && <LearningPage />}
          {page === "risco" &&
            (status ? (
              <>
                <div className="page-head">
                  <div>
                              <p>Kill switch, perda do dia e drawdown do modo de execução atual, e os limites que o robô respeita em toda entrada.</p>
                  </div>
                </div>
                <StatusPanel status={status} onChange={refresh} />
                <RiskLimitsForm limits={status.limits} onSaved={refresh} />
              </>
            ) : (
              <p className="muted">Carregando…</p>
            ))}
          {page === "auditoria" && (
            <>
              <div className="page-head">
                <div>
                          <p>Logins, kill switch, limites, modo de execução, ordens e decisões da IA.</p>
                </div>
              </div>
              <AuditList refreshKey={refreshKey} count={100} />
            </>
          )}
          {page === "ativos" && <AssetsTable />}
        </main>
      </div>
    </div>
  );
}
