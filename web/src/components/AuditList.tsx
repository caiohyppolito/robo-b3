import { useEffect, useState } from "react";
import { api, type AuditEntry } from "../api";
import { formatDateTime } from "../format";

export function AuditList({ refreshKey, count = 15 }: { refreshKey: number; count?: number }) {
  const [entries, setEntries] = useState<AuditEntry[]>([]);

  useEffect(() => {
    api.audit(count).then(setEntries).catch(() => setEntries([]));
  }, [refreshKey, count]);

  return (
    <section className="card">
      <h2>Auditoria</h2>
      {entries.length === 0 ? (
        <p className="muted">Nenhum registro.</p>
      ) : (
        <ul className="audit">
          {entries.map((e, i) => (
            <li key={`${e.occurredAt}-${i}`}>
              <span className="muted small">{formatDateTime(e.occurredAt)}</span>
              <code>{e.action}</code>
              <span>{e.actor}</span>
              {e.details && <span className="muted small block">{e.details}</span>}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
