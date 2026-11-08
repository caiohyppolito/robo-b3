import type { DecisionOutcome } from "../../api";

/** Rótulo e tom de cada resultado de decisão. O tom sempre vem acompanhado do texto. */
export const OUTCOME: Record<DecisionOutcome, { label: string; tone: "good" | "bad" | "warn" | "neutral" }> = {
  Entered: { label: "Entrou", tone: "good" },
  Exited: { label: "Saiu", tone: "neutral" },
  Held: { label: "Manteve", tone: "neutral" },
  NoSignal: { label: "Sem sinal", tone: "neutral" },
  Blocked: { label: "Bloqueada", tone: "warn" },
  Vetoed: { label: "Vetada pela IA", tone: "warn" },
  Rejected: { label: "Recusada (risco)", tone: "bad" },
  Failed: { label: "Falhou na corretora", tone: "bad" },
  Armed: { label: "Stop de entrada armado", tone: "warn" },
};
