import { useCallback, useEffect, useState } from "react";
import { api, executionApi, session, sessionUser, setUnauthorizedHandler, type ExecutionStatus, type Status } from "./api";
import { AssetsTable } from "./components/AssetsTable";
import { AuditList } from "./components/AuditList";
import { BacktestPage } from "./components/backtest/BacktestPage";
import { ChartPage } from "./components/chart/ChartPage";
import { ScannerPage } from "./components/scanner/ScannerPage";
import { PortfolioPage } from "./components/portfolio/PortfolioPage";
import { EmergencyCard } from "./components/EmergencyCard";
import { HistoryPage } from "./components/history/HistoryPage";
import { LearningPage } from "./components/learning/LearningPage";
import { Login } from "./components/Login";
import { OperationPage } from "./components/operation/OperationPage";
import { DecisionsPage } from "./components/overview/DecisionsPage";
import { OverviewPage } from "./components/overview/OverviewPage";
import { RiskLimitsForm } from "./components/RiskLimitsForm";
import { SettingsPage } from "./components/settings/SettingsPage";
import { StatusPanel } from "./components/StatusPanel";
import { pageFromHash, pageInfo, type PageId } from "./components/shell/navigation";
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
    window.scrollTo({ top: 0 });
  };

  const logout = () => {
    session.token = null;
    setLoggedIn(false);
  };

  if (!loggedIn) return <Login onLoggedIn={() => setLoggedIn(true)} />;

  const info = pageInfo(page);

  return (
    <div className="shell">
      <Sidebar page={page} open={menuOpen} mode={execution?.mode} user={sessionUser()} onNavigate={navigate} onLogout={logout} />
      {menuOpen && <div className="scrim" onClick={() => setMenuOpen(false)} aria-hidden="true" />}
      <div className="content">
        <TopBar
          status={status}
          execution={execution}
          theme={theme}
          onTheme={setTheme}
          onMenu={() => setMenuOpen((open) => !open)}
          onRisk={() => navigate("risco")}
          onExecution={() => navigate("operacao")}
        />
        <main>
          <header className="page-title">
            <span className="eyebrow">{info.group}</span>
            <h1>{info.label}</h1>
            <p className="muted" style={{ margin: "0.2rem 0 0", maxWidth: 760 }}>{info.description}</p>
          </header>
          {error && <p className="banner error">{error}</p>}
          {page === "situacao" && <OverviewPage status={status} execution={execution} onNavigate={navigate} />}
          {page === "grafico" && <ChartPage />}
          {page === "operacao" && <OperationPage />}
          {page === "decisoes" && <DecisionsPage />}
          {page === "backtest" && <BacktestPage />}
          {page === "ia" && <LearningPage />}
          {page === "varredura" && <ScannerPage />}
          {page === "carteira" && <PortfolioPage />}
          {page === "historico" && <HistoryPage />}
          {page === "risco" &&
            (status ? (
              <>
                <StatusPanel status={status} onChange={refresh} />
                <RiskLimitsForm limits={status.limits} onSaved={refresh} />
                <EmergencyCard onDone={refresh} />
              </>
            ) : (
              <p className="muted">Carregando…</p>
            ))}
          {page === "auditoria" && (
            <section className="card">
              <AuditList refreshKey={refreshKey} count={100} />
            </section>
          )}
          {page === "config" && <SettingsPage status={status} execution={execution} onChanged={refresh} onNavigate={navigate} />}
          {page === "ativos" && <AssetsTable />}
        </main>
      </div>
    </div>
  );
}
