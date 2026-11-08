import type { IconName } from "./Icon";

export type PageId =
  | "situacao"
  | "grafico"
  | "operacao"
  | "decisoes"
  | "risco"
  | "backtest"
  | "ia"
  | "varredura"
  | "carteira"
  | "ir"
  | "historico"
  | "auditoria"
  | "config"
  | "ativos";

export interface NavItem {
  id: PageId;
  label: string;
  icon: IconName;
  /** Uma linha abaixo do título da página. */
  description: string;
}

export const NAV: { group: string; items: NavItem[] }[] = [
  {
    group: "Operar",
    items: [
      { id: "situacao", label: "Painel", icon: "overview", description: "O pregão em uma tela: mercado, conta, posições, travas e o que o robô decidiu." },
      { id: "grafico", label: "Gráfico", icon: "candles", description: "Candles com as médias, a VWAP do dia e as entradas e saídas do robô." },
      { id: "operacao", label: "Operação", icon: "operation", description: "Modo de execução, alocações de estratégia, posições e ordens." },
      { id: "decisoes", label: "Decisões", icon: "decisions", description: "Cada barra avaliada: o sinal, o que foi feito e por quê." },
    ],
  },
  {
    group: "Risco",
    items: [{ id: "risco", label: "Risco e travas", icon: "risk", description: "Kill switch, perda do dia, drawdown, limites e o botão de emergência." }],
  },
  {
    group: "Validar",
    items: [
      { id: "backtest", label: "Backtest", icon: "backtest", description: "Simula o histórico com custos, IR, CDI no caixa e execução na abertura seguinte." },
      { id: "varredura", label: "Varredura", icon: "scan", description: "Procura ativos líquidos em que alguma estratégia venceu o CDI e o comprar e segurar fora da amostra." },
      { id: "carteira", label: "Carteira", icon: "pie", description: "Backtest de uma carteira de vários ativos com teto por ativo, rebalanceamento e custos." },
      { id: "ia", label: "IA", icon: "ai", description: "Modelos que vetam entradas ruins: evidência fora da amostra, calibração e mudança de regime." },
    ],
  },
  {
    group: "Registros",
    items: [
      { id: "historico", label: "Histórico", icon: "history", description: "Todas as operações encerradas em todos os modos, com horários, motivo de saída, custos e resultado." },
      { id: "ir", label: "Imposto de renda", icon: "tax", description: "Apuração mensal das operações do robô: swing e day trade, isenção, prejuízo a compensar, IRRF e DARF." },
      { id: "auditoria", label: "Auditoria", icon: "audit", description: "Logins, travas, limites, modo de execução, ordens e decisões da IA." },
    ],
  },
  {
    group: "Sistema",
    items: [
      { id: "config", label: "Configurações", icon: "settings", description: "Limites de risco, execução, dados de mercado, CDI e notificações." },
      { id: "ativos", label: "Ativos", icon: "markets", description: "Ativos acompanhados e o histórico diário gravado de cada um." },
    ],
  },
];

const ITEMS = NAV.flatMap((g) => g.items.map((i) => ({ ...i, group: g.group })));

export type PageInfo = (typeof ITEMS)[number];

export const PAGE_TITLE: Record<PageId, string> = Object.fromEntries(ITEMS.map((i) => [i.id, i.label])) as Record<PageId, string>;

export const pageInfo = (id: PageId): PageInfo => ITEMS.find((i) => i.id === id) ?? (ITEMS[0] as PageInfo);

export function pageFromHash(): PageId {
  const id = window.location.hash.replace(/^#\/?/, "") as PageId;
  return id in PAGE_TITLE ? id : "situacao";
}
