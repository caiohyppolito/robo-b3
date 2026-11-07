import { useState } from "react";
import { ordersApi, type CycleReport } from "../api";

const PHRASE = "ZERAR TUDO";

/** Emergência: trava o robô e encerra a mercado todas as posições do modo atual. */
export function EmergencyCard({ onDone }: { onDone: () => void }) {
  const [confirmation, setConfirmation] = useState("");
  const [busy, setBusy] = useState(false);
  const [report, setReport] = useState<CycleReport | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function flatten() {
    setBusy(true);
    setError(null);
    try {
      setReport(await ordersApi.flattenAll(confirmation));
      setConfirmation("");
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="card emergency">
      <h2>Emergência</h2>
      <dl className="behaviour">
        <dt>KILL SWITCH (Painel)</dt>
        <dd>Bloqueia novas entradas. Posições abertas e seus stops continuam; saídas seguem permitidas.</dd>
        <dt>Zerar tudo</dt>
        <dd>Trava o robô e encerra a mercado todas as posições do modo atual. O que a corretora não encerrar fica listado abaixo e continua aberto.</dd>
        <dt>Ordens pendentes</dt>
        <dd>O robô só usa ordens a mercado; não há ordens abertas além dos stops das posições, que saem junto com elas.</dd>
      </dl>
      <label>
        Digite {PHRASE} para confirmar
        <input value={confirmation} onChange={(e) => setConfirmation(e.target.value)} autoComplete="off" spellCheck={false} />
      </label>
      <button type="button" className="danger" disabled={busy || confirmation.trim() !== PHRASE} onClick={() => void flatten()}>
        {busy ? "Zerando…" : "Zerar tudo agora"}
      </button>
      {error && <p className="error">{error}</p>}
      {report && (
        <ul className="small">
          <li>{report.closed} posições encerradas.</li>
          {report.messages.map((m, i) => (
            <li key={i}>{m}</li>
          ))}
        </ul>
      )}
    </section>
  );
}
