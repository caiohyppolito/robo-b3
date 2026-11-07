import { useState } from "react";
import { executionApi, MODE_LABEL, type ExecutionMode, type ExecutionStatus } from "../../api";
import { formatDateTime } from "../../format";

const CONFIRMATION = "OPERAR CONTA REAL";

const DESCRIPTION: Record<ExecutionMode, string> = {
  Paper: "Ordens executadas só dentro do robô, com preços reais. Nada vai para a corretora.",
  Mt5Demo: "Ordens enviadas ao MetaTrader 5 numa conta demo: execução de verdade, dinheiro fictício.",
  Mt5Real: "Ordens enviadas à sua conta real. Dinheiro de verdade.",
};

interface Props {
  status: ExecutionStatus;
  onChanged: () => void;
}

/** Troca do modo de execução: exige robô travado, sem posições, as travas de configuração e, na conta real, a frase digitada. */
export function ExecutionModeCard({ status, onChanged }: Props) {
  const [target, setTarget] = useState<ExecutionMode>(status.mode === "Paper" ? "Mt5Demo" : "Paper");
  const [confirmation, setConfirmation] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const options: { mode: ExecutionMode; disabledReason?: string }[] = [
    { mode: "Paper" },
    { mode: "Mt5Demo", disabledReason: status.allowMt5 ? undefined : "habilite Execution:AllowMt5" },
    {
      mode: "Mt5Real",
      disabledReason: !status.allowMt5 || !status.allowRealAccount ? "habilite Execution:AllowMt5 e Execution:AllowRealAccount" : undefined,
    },
  ];

  async function change() {
    setBusy(true);
    setError(null);
    try {
      await executionApi.change(target, target === "Mt5Real" ? confirmation : undefined);
      setConfirmation("");
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Falha ao mudar o modo");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className={`card stack mode-card mode-${status.mode.toLowerCase()}`}>
      <div>
        <h2>Modo de execução</h2>
        <p className="big">{MODE_LABEL[status.mode]}</p>
        <p className="small">{DESCRIPTION[status.mode]}</p>
        <p className="muted small">
          Desde {formatDateTime(status.changedAt)}, por {status.changedBy}.
          {status.feed.connected && status.feed.isDemo !== null && ` Conta logada no MT5: ${status.feed.isDemo ? "demo" : "REAL"} (${status.feed.login}).`}
        </p>
      </div>
      <div className="form-grid">
        <label>
          Mudar para
          <select value={target} onChange={(e) => setTarget(e.target.value as ExecutionMode)}>
            {options
              .filter((o) => o.mode !== status.mode)
              .map((o) => (
                <option key={o.mode} value={o.mode} disabled={!!o.disabledReason}>
                  {MODE_LABEL[o.mode]}
                  {o.disabledReason ? ` (${o.disabledReason})` : ""}
                </option>
              ))}
          </select>
        </label>
        {target === "Mt5Real" && (
          <label>
            Digite {CONFIRMATION} para confirmar
            <input value={confirmation} onChange={(e) => setConfirmation(e.target.value)} autoComplete="off" spellCheck={false} />
          </label>
        )}
      </div>
      <p className="muted small">Para mudar: trave o robô no Painel e encerre as posições abertas do modo atual. A mudança fica na auditoria.</p>
      {error && <p className="error">{error}</p>}
      <div>
        <button
          type="button"
          className={target === "Mt5Real" ? "danger" : ""}
          disabled={busy || (target === "Mt5Real" && confirmation.trim() !== CONFIRMATION)}
          onClick={() => void change()}
        >
          {busy ? "Mudando…" : `Mudar para ${MODE_LABEL[target]}`}
        </button>
      </div>
    </section>
  );
}
