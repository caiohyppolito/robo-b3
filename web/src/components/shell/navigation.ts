import type { IconName } from "./Icon";

export type PageId = "situacao" | "operacao" | "decisoes" | "backtest" | "ia" | "risco" | "auditoria" | "ativos";

export interface NavItem {
  id: PageId;
  label: string;
  icon: IconName;
}

export const NAV: { group: string; items: NavItem[] }[] = [
  {
    group: "Operar",
    items: [
      { id: "situacao", label: "Situação agora", icon: "overview" },
      { id: "operacao", label: "Operação", icon: "operation" },
      { id: "decisoes", label: "Decisões", icon: "decisions" },
    ],
  },
  {
    group: "Validar",
    items: [
      { id: "backtest", label: "Backtest", icon: "backtest" },
      { id: "ia", label: "IA", icon: "ai" },
    ],
  },
  {
    group: "Controle",
    items: [
      { id: "risco", label: "Risco e travas", icon: "risk" },
      { id: "auditoria", label: "Auditoria", icon: "audit" },
    ],
  },
  { group: "Mercados", items: [{ id: "ativos", label: "Ativos", icon: "markets" }] },
];

export const PAGE_TITLE: Record<PageId, string> = Object.fromEntries(NAV.flatMap((g) => g.items.map((i) => [i.id, i.label]))) as Record<PageId, string>;

export function pageFromHash(): PageId {
  const id = window.location.hash.replace(/^#\/?/, "") as PageId;
  return id in PAGE_TITLE ? id : "situacao";
}
