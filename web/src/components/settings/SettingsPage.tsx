import { useEffect, useState } from "react";
import { marketApi, settingsApi, type CdiInfo, type ExecutionStatus, type PaperSettings, type Status } from "../../api";
import { formatDate, formatNumber } from "../../format";
import { ExecutionModeCard } from "../operation/ExecutionModeCard";
import { RiskLimitsForm } from "../RiskLimitsForm";
import type { PageId } from "../shell/navigation";

type Tab = "risco" | "execucao" | "dados" | "notificacoes" | "robo";
const TABS: { id: Tab; label: string }[] = [
  { id: "risco", label: "Risco" },
  { id: "execucao", label: "Execução" },
  { id: "dados", label: "Dados e CDI" },
  { id: "notificacoes", label: "Notificações" },
  { id: "robo", label: "Robô" },
];

interface Props {
  status: Status | null;
  execution: ExecutionStatus | null;
  onChanged: () => void;
  onNavigate: (page: PageId) => void;
}

function CdiCard() {
  const [cdi, setCdi] = useState<CdiInfo | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const load = () => marketApi.cdi().then(setCdi).catch(() => setCdi(null));
  useEffect(() => void load(), []);

  const sync = async () => {
    setBusy(true);
    setMessage(null);
    try {
      const r = await marketApi.syncCdi();
      setMessage(r.saved > 0 ? `${r.saved} dias novos gravados.` : "Já estava em dia.");
      await load();
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Banco Central indisponível agora.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="card">
      <div className="card-head">
        <h2>CDI do Banco Central</h2>
        <button type="button" className="small" style={{ margin: 0 }} disabled={busy} onClick={() => void sync()}>
          {busy ? "Atualizando…" : "Atualizar agora"}
        </button>
      </div>
      {cdi?.date ? (
        <div className="metrics">
          <div className="metric kpi">
            <span className="label">Último dia</span>
            <strong>{formatDate(cdi.date)}</strong>
            <span className="sub">{cdi.source}</span>
          </div>
          <div className="metric kpi">
            <span className="label">Taxa do dia</span>
            <strong>{formatNumber(cdi.dailyPercent ?? 0, 6)}%</strong>
            <span className="sub">ao dia útil</span>
          </div>
          <div className="metric kpi">
            <span className="label">Equivalente anual</span>
            <strong>{formatNumber(cdi.annualizedPercent ?? 0, 2)}%</strong>
            <span className="sub">252 dias úteis</span>
          </div>
        </div>
      ) : (
        <p className="muted" style={{ margin: 0 }}>
          A série ainda não foi baixada: o backtest usa a taxa fixa configurada. O worker de mercado baixa sozinho; ou clique em atualizar.
        </p>
      )}
      {message && <p className="muted small" style={{ marginBottom: 0 }}>{message}</p>}
    </section>
  );
}

function RobotCard() {
  const [paper, setPaper] = useState<PaperSettings | null>(null);
  useEffect(() => {
    settingsApi.paper().then(setPaper).catch(() => setPaper(null));
  }, []);
  if (!paper) return <p className="muted">Carregando…</p>;
  const rows: [string, string, string][] = [
    ["Risco por operação", `${formatNumber(paper.riskPerTradePercent)}% do patrimônio`, "Quanto se aceita perder se o stop for atingido. No MT5, o patrimônio é o menor entre o livro do robô e o saldo da conta."],
    ["Sem entradas antes de", paper.noEntriesBefore, "Abertura do pregão costuma ter spread e leilão."],
    ["Sem entradas depois de", paper.noEntriesAfter, "Perto do fim do pregão, só saídas."],
    ["Zerar day trade às", paper.dayTradeFlattenAt, "Alocações de day trade são encerradas nesse horário."],
    ["Cotação velha demais", `${paper.maxQuoteAgeSeconds} s`, "Com cotação mais antiga que isso, o robô não opera."],
    ["Barras carregadas", String(paper.barsToLoad), "Histórico usado a cada ciclo."],
  ];
  return (
    <section className="card">
      <div className="card-head">
        <h2>Regras do ciclo</h2>
        <span className="muted small">definidas em appsettings (PaperTrading)</span>
      </div>
      {rows.map(([label, value, hint]) => (
        <div key={label} className="setting-row">
          <div>
            <strong>{label}</strong>
            <span>{hint}</span>
          </div>
          <span className="mono" style={{ color: "var(--text)", fontSize: "0.95rem" }}>{value}</span>
        </div>
      ))}
    </section>
  );
}

function NotificationsCard() {
  return (
    <section className="card">
      <div className="card-head">
        <h2>Telegram</h2>
        <span className="chip">opcional · desligado por padrão</span>
      </div>
      <p style={{ marginTop: 0 }}>
        Avisos de entradas, saídas, recusas, travas e o resumo do dia. Pelo Telegram só dá para consultar e <strong>travar</strong> o robô (com código de
        confirmação de uso único): destravar, mudar limites e mandar ordens continuam só aqui no painel.
      </p>
      <p className="muted small">O token do bot fica em user-secrets dos workers, nunca no código nem aqui na tela:</p>
      <pre className="analysis mono small" style={{ margin: 0 }}>{`cd src/RoboB3.Workers
dotnet user-secrets set "Telegram:BotToken" "<token do BotFather>"
dotnet user-secrets set "Telegram:Enabled" "true"
dotnet user-secrets set "Telegram:AllowedChatIds:0" "<chat.id>"
dotnet user-secrets set "Telegram:AllowedUserIds:0" "<from.id>"`}</pre>
      <p className="muted small" style={{ marginBottom: 0 }}>Passo a passo completo em docs/telegram.md.</p>
    </section>
  );
}

export function SettingsPage({ status, execution, onChanged, onNavigate }: Props) {
  const [tab, setTab] = useState<Tab>("risco");

  return (
    <>
      <nav className="tabs" aria-label="Seções das configurações">
        {TABS.map((t) => (
          <button key={t.id} type="button" className={tab === t.id ? "active" : ""} aria-current={tab === t.id ? "page" : undefined} onClick={() => setTab(t.id)}>
            {t.label}
          </button>
        ))}
      </nav>

      {tab === "risco" && (status ? <RiskLimitsForm limits={status.limits} onSaved={onChanged} /> : <p className="muted">Carregando…</p>)}
      {tab === "execucao" && (execution ? <ExecutionModeCard status={execution} onChanged={onChanged} /> : <p className="muted">Carregando…</p>)}
      {tab === "dados" && (
        <>
          <CdiCard />
          <section className="card">
            <div className="card-head">
              <h2>Dados de mercado</h2>
              <button type="button" className="small link" onClick={() => onNavigate("ativos")}>Ver ativos</button>
            </div>
            <div className="setting-row">
              <div>
                <strong>Histórico diário</strong>
                <span>Baixado pelo worker de mercado e gravado no SQL Server; candles diários à meia-noite UTC do pregão.</span>
              </div>
            </div>
            <div className="setting-row">
              <div>
                <strong>Intradiário e cotações</strong>
                <span>Lidos do MetaTrader 5 pelo conector local (127.0.0.1), com token.</span>
              </div>
              <span className={`chip tone ${execution?.feed.connected ? "good" : "bad"}`}>{execution?.feed.connected ? "conectado" : "desconectado"}</span>
            </div>
          </section>
        </>
      )}
      {tab === "notificacoes" && <NotificationsCard />}
      {tab === "robo" && <RobotCard />}
    </>
  );
}
