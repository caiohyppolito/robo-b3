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

/** Nome do usuário lido do token (só para exibir; a API é quem valida o token). */
export function sessionUser(): string {
  const token = session.token;
  if (!token) return "";
  try {
    const payload = JSON.parse(atob((token.split(".")[1] ?? "").replace(/-/g, "+").replace(/_/g, "/"))) as Record<string, unknown>;
    const name = payload["unique_name"] ?? payload["name"] ?? payload["sub"] ?? payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"];
    return typeof name === "string" ? name : "usuário";
  } catch {
    return "usuário";
  }
}

let onUnauthorized: () => void = () => {};
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

async function requestNoContent(path: string, method: string): Promise<void> {
  await send(path, { method });
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(path, init);
  return (await response.json()) as T;
}

async function send(path: string, init: RequestInit = {}): Promise<Response> {
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
  return response;
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

// ---------- Backtest (Fase 2) ----------

export interface ParameterDef {
  name: string;
  description: string;
  default: number;
  min: number;
  max: number;
}

export interface StrategyDef {
  key: string;
  name: string;
  description: string;
  parameters: ParameterDef[];
}

export interface BacktestDefaults {
  riskFreeRatePercent: number;
  brokeragePerOrder: number;
  feesPercent: number;
  slippagePercent: number;
  futuresFeePerContract: number;
}

export interface Simulation {
  initialCapital?: number;
  riskPerTradePercent?: number;
  maxPositionPercent?: number;
  riskFreeRatePercent?: number;
  applyTaxes?: boolean;
}

export interface Metrics {
  initialCapital: number;
  finalEquity: number;
  totalReturnPercent: number;
  cagrPercent: number;
  maxDrawdownPercent: number;
  volatilityPercent: number;
  sharpe: number;
  trades: number;
  winRatePercent: number;
  profitFactor: number;
  averageTradePercent: number;
  exposurePercent: number;
  costsPaid: number;
  taxesPaid: number;
  interestEarned: number;
  benchmarkReturnPercent: number;
  benchmarkMaxDrawdownPercent: number;
  riskFreeReturnPercent: number;
  signalsFiltered: number;
  sortino: number;
  calmar: number;
  cVaR95Percent: number;
  expectancyPerTrade: number;
  expectancyMargin95: number;
  turnoverPerYear: number;
  ambiguousTrades: number;
}

export interface BacktestSummary {
  id: string;
  createdAt: string;
  symbol: string;
  strategy: string;
  parameters: Record<string, number>;
  startDate: string;
  endDate: string;
  metrics: Metrics;
  metaModelId: string | null;
}

export interface Trade {
  direction: "Long" | "Short" | "Flat";
  entryDate: string;
  entryPrice: number;
  exitDate: string;
  exitPrice: number;
  quantity: number;
  grossProfit: number;
  costs: number;
  netProfit: number;
  returnPercent: number;
  exitReason: string;
  entryProbability?: number | null;
}

export interface EquityPoint {
  date: string;
  equity: number;
  benchmark: number;
  riskFree: number;
}

export interface BacktestDetail {
  summary: BacktestSummary;
  riskPerTradePercent: number;
  applyTaxes: boolean;
  trades: Trade[];
  equity: EquityPoint[];
  events: { date: string; message: string }[];
}

export interface ComparisonRow {
  symbol: string;
  strategyKey: string;
  parameters: Record<string, number>;
  inSampleSharpe: number;
  inSampleReturnPercent: number;
  outOfSampleSharpe: number;
  outOfSampleReturnPercent: number;
  outOfSampleMaxDrawdownPercent: number;
  outOfSampleTrades: number;
  outOfSampleBenchmarkReturnPercent: number;
  outOfSampleRiskFreeReturnPercent: number;
}

export interface StrategyRanking {
  strategyKey: string;
  strategyName: string;
  assets: number;
  averageOutOfSampleSharpe: number;
  averageOutOfSampleReturnPercent: number;
  worstOutOfSampleDrawdownPercent: number;
  beatsBuyAndHoldPercent: number;
  beatsRiskFreePercent: number;
}

export interface ComparisonResult {
  inSampleFrom: string;
  outOfSampleFrom: string;
  outOfSampleTo: string;
  ranking: StrategyRanking[];
  rows: ComparisonRow[];
  skipped: string[];
  trialsEvaluated: number;
  experimentId?: string;
}

export interface RunBacktestRequest {
  symbol: string;
  strategy: string;
  parameters: Record<string, number>;
  from?: string;
  to?: string;
  simulation: Simulation;
  useMetaModel?: boolean;
  timeframe?: Timeframe;
  flattenAtDayEnd?: boolean;
}

export interface CompareRequest {
  symbols?: string[];
  from?: string;
  to?: string;
  inSamplePercent?: number;
  simulation: Simulation;
}

export const backtestApi = {
  strategies: () => request<StrategyDef[]>("/api/strategies"),
  defaults: () => request<BacktestDefaults>("/api/backtests/defaults"),
  run: (body: RunBacktestRequest) =>
    request<BacktestDetail>("/api/backtests", { method: "POST", body: JSON.stringify(body) }),
  list: () => request<BacktestSummary[]>("/api/backtests?count=20"),
  get: (id: string) => request<BacktestDetail>(`/api/backtests/${encodeURIComponent(id)}`),
  compare: (body: CompareRequest) =>
    request<ComparisonResult>("/api/backtests/compare", { method: "POST", body: JSON.stringify(body) }),
};

// ---------- IA (Fase 3) ----------

export type MetaModelStatus = "Active" | "Retired" | "Rejected";

export interface MetaModel {
  id: string;
  strategy: string;
  createdAt: string;
  status: MetaModelStatus;
  decision: string;
  trainingFrom: string;
  splitDate: string;
  validationTo: string;
  trainingExamples: number;
  validationExamples: number;
  threshold: number;
  auc: number;
  baseWinRatePercent: number;
  filteredWinRatePercent: number;
  baseAverageReturnPercent: number;
  filteredAverageReturnPercent: number;
  keptPercent: number;
  unfilteredSharpe: number;
  filteredSharpe: number;
  championSharpe: number | null;
  featureImportance: Record<string, number>;
  activatedAt: string | null;
  retiredAt: string | null;
  analysis: string | null;
  calibration: Calibration | null;
}

export interface CalibrationBin { from: number; to: number; count: number; meanPredicted: number; observedWinRate: number }
export interface Calibration { examples: number; brier: number; baseRateBrier: number; expectedCalibrationError: number; bins: CalibrationBin[]; beatsBaseRate: boolean }

export type DriftLevel = "InsufficientData" | "Stable" | "Moderate" | "Strong";
export interface DriftReport { recentSamples: number; level: DriftLevel; maxPsi: number; features: { name: string; psi: number; level: DriftLevel }[] }

export interface VetoOutcome {
  decisionId: number;
  time: string;
  symbol: string;
  strategy: string;
  direction: string;
  probability: number | null;
  status: string;
  entryDate: string | null;
  exitDate: string | null;
  returnPercent: number | null;
  exitReason: string | null;
}
export interface VetoReview {
  vetoes: number;
  evaluated: number;
  wouldHaveWon: number;
  wouldHaveLost: number;
  avoidedLossPercent: number;
  missedGainPercent: number;
  hitRatePercent: number | null;
  items: VetoOutcome[];
}

export interface JournalEntry {
  id: string;
  source: "Paper" | "Live";
  strategy: string;
  symbol: string;
  direction: string;
  entryDate: string;
  exitDate: string;
  netProfit: number;
  returnPercent: number;
  modelProbability: number | null;
}

export const learningApi = {
  models: () => request<MetaModel[]>("/api/learning/models?count=50"),
  train: (strategy: string) =>
    request<MetaModel>("/api/learning/train", { method: "POST", body: JSON.stringify({ strategy }) }),
  analyze: (id: string) =>
    request<MetaModel>(`/api/learning/models/${encodeURIComponent(id)}/analysis`, { method: "POST" }),
  analyst: () => request<{ isConfigured: boolean }>("/api/learning/analyst"),
  journal: () => request<JournalEntry[]>("/api/learning/journal?count=50"),
  drift: (id: string) => request<DriftReport>(`/api/learning/models/${encodeURIComponent(id)}/drift`),
  reactivate: (id: string) => request<MetaModel>(`/api/learning/models/${encodeURIComponent(id)}/reactivate`, { method: "POST" }),
  vetoes: () => request<VetoReview>("/api/learning/vetoes?count=100"),
};

// ---------- Operação simulada (Fase 4) ----------

export type Timeframe = "M1" | "M5" | "M15" | "H1" | "D1";

export interface FeedStatus {
  connected: boolean;
  mode: string;
  server: string | null;
  login: number | null;
  isDemo: boolean | null;
  company: string | null;
  message: string | null;
}

export interface Assignment {
  id: string;
  symbol: string;
  strategy: string;
  parameters: Record<string, number>;
  timeframe: Timeframe;
  useMetaModel: boolean;
  flattenAtDayEnd: boolean;
  isEnabled: boolean;
  lastProcessedBar: string | null;
}

export interface AssignmentRequest {
  symbol: string;
  strategy: string;
  parameters: Record<string, number>;
  timeframe: Timeframe;
  useMetaModel: boolean;
  flattenAtDayEnd: boolean;
}

export interface PaperPosition {
  id: string;
  assignmentId: string | null;
  symbol: string;
  contract: string;
  strategy: string;
  direction: "Long" | "Short";
  quantity: number;
  entryPrice: number;
  stop: number | null;
  markPrice: number;
  markTime: string;
  unrealizedProfit: number;
  probability: number | null;
  entryTime: string;
  brokerTicket: number | null;
}

export interface PaperFill {
  time: string;
  symbol: string;
  side: "Buy" | "Sell";
  quantity: number;
  price: number;
  cost: number;
  realizedProfit: number | null;
  reason: string;
}

export interface PaperAccount {
  mode: ExecutionMode;
  initialCapital: number;
  equity: number;
  cash: number;
  dayProfit: number;
  totalProfit: number;
  realizedProfit: number;
  costsPaid: number;
  peakEquity: number;
  positions: PaperPosition[];
  fills: PaperFill[];
}

export interface CycleReport {
  feedConnected: boolean;
  opened: number;
  closed: number;
  messages: string[];
}

export const paperApi = {
  feed: () => request<FeedStatus>("/api/feed/status"),
  account: () => request<PaperAccount>("/api/paper/account"),
  cycle: () => request<CycleReport>("/api/paper/cycle", { method: "POST" }),
  closePosition: (id: string) => requestNoContent(`/api/paper/positions/${encodeURIComponent(id)}/close`, "POST"),
  reset: () => requestNoContent("/api/paper/reset", "POST"),
  assignments: () => request<Assignment[]>("/api/paper/assignments"),
  createAssignment: (body: AssignmentRequest) =>
    request<Assignment>("/api/paper/assignments", { method: "POST", body: JSON.stringify(body) }),
  setEnabled: (id: string, enabled: boolean) =>
    request<Assignment>(`/api/paper/assignments/${encodeURIComponent(id)}/${enabled ? "enable" : "disable"}`, { method: "POST" }),
  deleteAssignment: (id: string) => requestNoContent(`/api/paper/assignments/${encodeURIComponent(id)}`, "DELETE"),
};

// ---------- Execução (Fase 5) ----------

export type ExecutionMode = "Paper" | "Mt5Demo" | "Mt5Real";

export interface ExecutionStatus {
  mode: ExecutionMode;
  changedBy: string;
  changedAt: string;
  allowMt5: boolean;
  allowRealAccount: boolean;
  feed: FeedStatus;
}

export const executionApi = {
  get: () => request<ExecutionStatus>("/api/execution"),
  change: (mode: ExecutionMode, confirmation?: string) =>
    request<ExecutionStatus>("/api/execution", { method: "PUT", body: JSON.stringify({ mode, confirmation }) }),
};

export const MODE_LABEL: Record<ExecutionMode, string> = {
  Paper: "Simulado",
  Mt5Demo: "MT5 conta demo",
  Mt5Real: "MT5 CONTA REAL",
};

// ---------- Painel (situação agora) ----------

export interface WatchItem {
  symbol: string;
  name: string;
  contract: string;
  assetClass: AssetClass;
  last: number | null;
  changePercent: number | null;
  quoteTime: string | null;
  isFresh: boolean;
  spark: number[];
  error: string | null;
}

export interface MarketWatch {
  connected: boolean;
  message: string | null;
  items: WatchItem[];
}

export type DecisionOutcome = "NoSignal" | "Held" | "Entered" | "Exited" | "Blocked" | "Vetoed" | "Rejected" | "Failed" | "Armed";

export interface Decision {
  id: number;
  time: string;
  symbol: string;
  contract: string;
  strategy: string;
  barTime: string;
  signal: "Long" | "Short" | "Flat";
  outcome: DecisionOutcome;
  detail: string;
  probability: number | null;
  quantity: number | null;
  price: number | null;
  stop: number | null;
  sizing: string | null;
}

export const overviewApi = {
  watch: () => request<MarketWatch>("/api/market/watch"),
  decisions: (count = 50) => request<Decision[]>(`/api/decisions?count=${count}`),
};

export interface PaperSettings {
  riskPerTradePercent: number;
  maxQuoteAgeSeconds: number;
  noEntriesBefore: string;
  noEntriesAfter: string;
  dayTradeFlattenAt: string;
  barsToLoad: number;
}

export const settingsApi = {
  paper: () => request<PaperSettings>("/api/paper/settings"),
};

// ---------- Robustez: ordens e emergência ----------

export interface OrderIntent {
  clientOrderId: string;
  createdAt: string;
  purpose: "Entry" | "Exit";
  symbol: string;
  side: "Buy" | "Sell";
  quantity: number;
  referencePrice: number;
  stopLoss: number | null;
  status: "Pending" | "Filled" | "Rejected" | "Unconfirmed" | "Recovered";
  fillPrice: number | null;
  fillQuantity: number | null;
  slippage: number | null;
  latencyMs: number | null;
  brokerTicket: number | null;
  reason: string;
  message: string | null;
}

export const ordersApi = {
  list: () => request<OrderIntent[]>("/api/orders?count=50"),
  flattenAll: (confirmation: string) =>
    request<CycleReport>("/api/emergency/flatten", { method: "POST", body: JSON.stringify({ confirmation }) }),
};

export interface ChartCandle { time: number; open: number; high: number; low: number; close: number; volume: number }
export interface ChartPoint { time: number; value: number }
export interface ChartMarker { time: number; side: "buy" | "sell"; price: number; reason: string; result: number | null }
export interface ChartReading {
  last: number;
  changePercent: number;
  adx14: number | null;
  atr14: number | null;
  atrPercentile: number | null;
  rsi14: number | null;
  volumeRatio: number | null;
  sma20: number | null;
  sma50: number | null;
  vwap: number | null;
  trend: "alta" | "baixa" | "lateral" | "indefinida";
  trendDetail: string;
}
export interface ChartData {
  symbol: string;
  contract: string;
  timeframe: Timeframe;
  source: string;
  candles: ChartCandle[];
  sma20: ChartPoint[];
  sma50: ChartPoint[];
  vwap: ChartPoint[];
  markers: ChartMarker[];
  reading: ChartReading | null;
}

export interface CdiInfo { date: string | null; dailyPercent: number | null; annualizedPercent: number | null; source: string }

export const marketApi = {
  chart: (symbol: string, timeframe: Timeframe, count = 300) =>
    request<ChartData>(`/api/market/chart/${encodeURIComponent(symbol)}?timeframe=${timeframe}&count=${count}`),
  cdi: () => request<CdiInfo>("/api/cdi"),
  syncCdi: () => request<{ saved: number }>("/api/cdi/sync", { method: "POST" }),
};

export interface ClosedTrade {
  positionId: string;
  mode: ExecutionMode;
  symbol: string;
  strategy: string | null;
  direction: "Compra" | "Venda";
  quantity: number;
  entryTime: string | null;
  entryPrice: number | null;
  exitTime: string;
  exitPrice: number;
  exitReason: string;
  costs: number;
  netProfit: number;
}

export interface TradeHistory {
  trades: ClosedTrade[];
  wins: number;
  netProfit: number;
  averageProfit: number;
  winRatePercent: number;
  profitFactor: number | null;
}

export const historyApi = {
  trades: (mode?: ExecutionMode, symbol?: string) => {
    const query = new URLSearchParams({ count: "500" });
    if (mode) query.set("mode", mode);
    if (symbol) query.set("symbol", symbol);
    return request<TradeHistory>(`/api/history/trades?${query.toString()}`);
  },
};

export interface ScanItem {
  symbol: string;
  name: string;
  averageDailyVolumeMillions: number;
  liquid: boolean;
  strategy: string | null;
  parameters: Record<string, number> | null;
  inSampleSharpe: number | null;
  outOfSampleSharpe: number | null;
  outOfSampleReturnPercent: number | null;
  outOfSampleMaxDrawdownPercent: number | null;
  outOfSampleTrades: number | null;
  buyAndHoldReturnPercent: number | null;
  riskFreeReturnPercent: number | null;
  verdict: "candidato" | "observar" | "descartar" | "ilíquido" | "sem dados";
  reason: string;
}
export interface ScanResult { outOfSampleFrom: string | null; outOfSampleTo: string | null; trialsEvaluated: number; items: ScanItem[]; skipped: string[] }

export const scanApi = {
  scan: (minDailyVolumeMillions: number, inSamplePercent: number, from?: string) =>
    request<ScanResult>("/api/backtests/scan", { method: "POST", body: JSON.stringify({ minDailyVolumeMillions, inSamplePercent, from: from || null }) }),
  universe: (active: boolean) =>
    request<{ changed: number; universe: number }>("/api/backtests/scan/universe", { method: "POST", body: JSON.stringify({ active }) }),
  ingest: () => request<{ assetsProcessed: number; candlesSaved: number; failures: string[] }>("/api/backtests/ingest", { method: "POST" }),
};

export type PortfolioWeighting = "Equal" | "InverseVolatility";
export interface PortfolioRebalance { date: string; targetWeights: Record<string, number>; cashWeight: number; turnover: number; costs: number }
export interface PortfolioResponse {
  from: string;
  to: string;
  skipped: string[];
  result: {
    equity: EquityPoint[];
    metrics: Metrics;
    rebalances: PortfolioRebalance[];
    finalWeights: Record<string, number>;
    averageInvestedPercent: number;
  };
}
export const portfolioApi = {
  run: (body: { symbols: string[]; from?: string; to?: string; weighting: PortfolioWeighting; maxWeightPercent: number; trendFilter: boolean; initialCapital: number }) =>
    request<PortfolioResponse>("/api/backtests/portfolio", { method: "POST", body: JSON.stringify(body) }),
};
