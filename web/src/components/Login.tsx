import { useState, type FormEvent } from "react";
import { api, session } from "../api";

/** Fundo do lado escuro: uma série de candles desenhada uma vez, só decorativa. */
function Backdrop() {
  const bars = Array.from({ length: 64 }, (_, i) => {
    const trend = Math.sin(i / 9) * 26 + i * 1.1;
    const noise = Math.sin(i * 12.9898) * 7;
    return 160 - trend - noise;
  });
  return (
    <svg className="backdrop" viewBox="0 0 640 260" preserveAspectRatio="none" aria-hidden="true">
      {bars.map((y, i) => {
        const prev = i === 0 ? y + 4 : (bars[i - 1] ?? y);
        const up = y < prev;
        return (
          <g key={i} stroke={up ? "#3f74ff" : "#28427f"} fill={up ? "#3f74ff" : "#28427f"}>
            <line x1={i * 10 + 4} x2={i * 10 + 4} y1={Math.min(y, prev) - 6} y2={Math.max(y, prev) + 6} strokeWidth={1} />
            <rect x={i * 10 + 1.5} y={Math.min(y, prev)} width={5} height={Math.max(2, Math.abs(y - prev))} />
          </g>
        );
      })}
    </svg>
  );
}

export function Login({ onLoggedIn }: { onLoggedIn: () => void }) {
  const [username, setUsername] = useState("admin");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await api.login(username, password);
      session.token = result.accessToken;
      onLoggedIn();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha no login");
    } finally {
      setBusy(false);
      setPassword("");
    }
  }

  return (
    <main className="login">
      <section className="login-hero">
        <div className="brand">
          <div className="brand-mark" aria-hidden="true">B3</div>
          <div>
            <strong>Robô B3</strong>
            <span>OPERAÇÃO ASSISTIDA</span>
          </div>
        </div>
        <div>
          <h1>Cada decisão com motivo, custo e trava.</h1>
          <p>
            Estratégias validadas fora da amostra, execução no MetaTrader 5 com stop na corretora, IA que só veta entradas e um
            robô que nunca se destrava sozinho.
          </p>
        </div>
        <div className="tags">
          <span>WIN · WDO · ações · ETFs</span>
          <span>Clear · MetaTrader 5</span>
          <span>roda no seu PC</span>
        </div>
        <Backdrop />
      </section>
      <section className="login-form">
        <form onSubmit={submit}>
          <h2>Entrar</h2>
          <p className="lead">Use o usuário configurado neste computador.</p>
          <label>
            Usuário
            <input value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="username" required />
          </label>
          <label>
            Senha
            <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" required autoFocus />
          </label>
          {error && <p className="callout bad">{error}</p>}
          <button type="submit" className="primary" disabled={busy}>
            {busy ? "Entrando…" : "Entrar"}
          </button>
          <div className="note">
            <span className="dot" aria-hidden="true" />
            <span>A senha é conferida pela API local e nunca sai deste computador. A sessão termina ao fechar a aba.</span>
          </div>
        </form>
      </section>
    </main>
  );
}
