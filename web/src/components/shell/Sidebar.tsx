import type { ExecutionMode } from "../../api";
import { Icon } from "./Icon";
import { NAV, type PageId } from "./navigation";

interface Props {
  page: PageId;
  open: boolean;
  mode?: ExecutionMode;
  user: string;
  onNavigate: (page: PageId) => void;
  onLogout: () => void;
  badges?: Partial<Record<PageId, number>>;
}

const ENV: Record<ExecutionMode, { label: string; className: string }> = {
  Paper: { label: "SIMULADO", className: "env" },
  Mt5Demo: { label: "DEMO", className: "env" },
  Mt5Real: { label: "REAL", className: "env real" },
};

export function Sidebar({ page, open, mode, user, onNavigate, onLogout, badges = {} }: Props) {
  return (
    <aside className={`sidebar ${open ? "open" : ""}`} aria-label="Menu">
      <div className="brand">
        <div className="brand-mark" aria-hidden="true">B3</div>
        <div>
          <strong>Robô B3</strong>
          <span>OPERAÇÃO ASSISTIDA</span>
        </div>
        {mode && <span className={ENV[mode].className} title="Modo de execução">{ENV[mode].label}</span>}
      </div>
      <nav>
        {NAV.map((group) => (
          <div key={group.group} className="nav-group">
            <h3>{group.group.toUpperCase()}</h3>
            {group.items.map((item) => (
              <button
                key={item.id}
                type="button"
                className={`nav-item ${page === item.id ? "active" : ""}`}
                aria-current={page === item.id ? "page" : undefined}
                onClick={() => onNavigate(item.id)}
              >
                <Icon name={item.icon} />
                {item.label}
                {badges[item.id] ? <span className="count">{badges[item.id]}</span> : null}
              </button>
            ))}
          </div>
        ))}
      </nav>
      <div className="sidebar-footer">
        <span className="avatar" aria-hidden="true">{user.slice(0, 2).toUpperCase()}</span>
        <div className="who">
          <strong>{user}</strong>
          <span>sessão local</span>
        </div>
        <button type="button" onClick={onLogout} title="Sair">
          <Icon name="logout" size={15} />
          Sair
        </button>
      </div>
    </aside>
  );
}
