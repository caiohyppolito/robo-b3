// Cliente HTTP do painel. O token fica só na sessionStorage (some ao fechar o navegador).

export type AssetClass = "Stock" | "Etf" | "Index" | "Future";

export interface TradingState {
  isHalted: boolean;
  haltReason: string | null;
  changedBy: string | null;
  changedAt: string;
}

export interface RiskLimits {
  maxDailyLossPercent: number;
  maxDrawdownPercent: number;
  maxPositionPercentPerAsset: number;
  maxOpenPositions: number;
  maxFuturesContracts: number;
}

export interface Portfolio {
  equity: number;
  dailyLossPercent: number;
  drawdownPercent: number;
  openPositions: number;
}

export interface Status {
  trading: TradingState;
  limits: RiskLimits;
  portfolio: Portfolio;
}

export interface Asset {
  symbol: string;
  name: string;
  assetClass: AssetClass;
  dailyDataSymbol: string | null;
  lotSize: number;
  isActive: boolean;
}

export interface Candle {
  openTime: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface AuditEntry {
  action: string;
  actor: string;
  details: string | null;
  occurredAt: string;
}

const TOKEN_KEY = "robo-b3.token";

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
  }
}

export const session = {
  get token(): string | null {
    try {
      return sessionStorage.getItem(TOKEN_KEY);
    } catch {
      return null;
    }
  },
  set token(value: string | null) {
    try {
      if (value) sessionStorage.setItem(TOKEN_KEY, value);
      else sessionStorage.removeItem(TOKEN_KEY);
    } catch {
      // Sem storage disponível: o login dura só enquanto a página estiver aberta.
    }
  },
};

let onUnauthorized: () => void = () => {};
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body) headers.set("Content-Type", "application/json");
  const token = session.token;
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const response = await fetch(path, { ...init, headers });
  if (response.status === 401 && token) {
    session.token = null;
    onUnauthorized();
  }
  if (!response.ok) {
    let message = `Erro ${response.status}`;
    try {
      const problem = (await response.json()) as { title?: string; detail?: string };
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // Resposta sem corpo JSON.
    }
    throw new ApiError(message, response.status);
  }
  return (await response.json()) as T;
}

export const api = {
  login: (username: string, password: string) =>
    request<{ accessToken: string; expiresAt: string }>("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password }),
    }),
  status: () => request<Status>("/api/status"),
  halt: (reason: string) =>
    request<TradingState>("/api/kill-switch/halt", { method: "POST", body: JSON.stringify({ reason }) }),
  resume: () => request<TradingState>("/api/kill-switch/resume", { method: "POST" }),
  updateLimits: (limits: RiskLimits) =>
    request<RiskLimits>("/api/risk/limits", { method: "PUT", body: JSON.stringify(limits) }),
  assets: () => request<Asset[]>("/api/assets"),
  candles: (symbol: string, days = 120) =>
    request<Candle[]>(`/api/assets/${encodeURIComponent(symbol)}/candles?days=${days}`),
  audit: (count = 20) => request<AuditEntry[]>(`/api/audit?count=${count}`),
};
