import { useEffect, useState, type FormEvent } from "react";
import { api, type RiskLimits } from "../api";

const fields: { key: keyof RiskLimits; label: string; step: string; hint: string }[] = [
  { key: "maxDailyLossPercent", label: "Perda máxima no dia (%)", step: "0.1", hint: "Trava o robô até a liberação manual" },
  { key: "maxDrawdownPercent", label: "Drawdown máximo (%)", step: "0.1", hint: "Queda a partir do maior patrimônio" },
  { key: "maxPositionPercentPerAsset", label: "Exposição máxima por ativo (%)", step: "0.1", hint: "Ações e ETFs" },
  { key: "maxOpenPositions", label: "Posições abertas (máx.)", step: "1", hint: "Ativos diferentes ao mesmo tempo" },
  { key: "maxFuturesContracts", label: "Contratos futuros (máx.)", step: "1", hint: "Soma de WIN + WDO" },
];

export function RiskLimitsForm({ limits, onSaved }: { limits: RiskLimits; onSaved: () => void }) {
  const [draft, setDraft] = useState(limits);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => setDraft(limits), [limits]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setMessage(null);
    try {
      await api.updateLimits(draft);
      setMessage({ ok: true, text: "Limites salvos." });
      onSaved();
    } catch (e) {
      setMessage({ ok: false, text: e instanceof Error ? e.message : "Falha ao salvar" });
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="card" onSubmit={submit}>
      <h2>Limites de risco</h2>
      <div className="form-grid">
        {fields.map((f) => (
          <label key={f.key}>
            {f.label}
            <input
              type="number"
              min="0"
              step={f.step}
              value={draft[f.key]}
              onChange={(e) => setDraft({ ...draft, [f.key]: Number(e.target.value) })}
              required
            />
            <span className="muted small">{f.hint}</span>
          </label>
        ))}
      </div>
      {message && <p className={message.ok ? "success" : "error"}>{message.text}</p>}
      <button type="submit" className="primary" disabled={busy}>
        Salvar limites
      </button>
    </form>
  );
}
