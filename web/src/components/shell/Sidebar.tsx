import { Icon } from "./Icon";
import { NAV, type PageId } from "./navigation";

interface Props {
  page: PageId;
  open: boolean;
  onNavigate: (page: PageId) => void;
  onLogout: () => void;
  badges?: Partial<Record<PageId, number>>;
}

export function Sidebar({ page, open, onNavigate, onLogout, badges = {} }: Props) {
  return (
    <aside className={`sidebar ${open ? "open" : ""}`} aria-label="Menu">
      <div className="brand">
        <div className="brand-mark" aria-hidden="true">B3</div>
        <div>
          <strong>Robô B3</strong>
          <span>OPERAÇÃO ASSISTIDA</span>
        </div>
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
        <span>admin</span>
        <button type="button" onClick={onLogout}>
          Sair
        </button>
      </div>
    </aside>
  );
}
