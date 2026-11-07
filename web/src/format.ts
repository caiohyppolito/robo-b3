const brl = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const number = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 2 });
const dateTime = new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short" });

export const formatBrl = (value: number) => brl.format(value);
export const formatNumber = (value: number) => number.format(value);
export const formatPercent = (value: number) => `${number.format(value)}%`;
export const formatDateTime = (iso: string) => dateTime.format(new Date(iso));

const shortDate = new Intl.DateTimeFormat("pt-BR", { day: "2-digit", month: "2-digit", year: "numeric", timeZone: "UTC" });
const monthYear = new Intl.DateTimeFormat("pt-BR", { month: "short", year: "2-digit", timeZone: "UTC" });
const compactBrl = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", notation: "compact", maximumFractionDigits: 1 });

/** Datas "AAAA-MM-DD" (DateOnly) são tratadas como UTC para não mudar de dia pelo fuso. */
export const formatDate = (iso: string) => shortDate.format(new Date(`${iso.slice(0, 10)}T00:00:00Z`));
export const formatMonthYear = (iso: string) => monthYear.format(new Date(`${iso.slice(0, 10)}T00:00:00Z`));
export const formatCompactBrl = (value: number) => compactBrl.format(value);
export const formatSignedPercent = (value: number) => `${value > 0 ? "+" : ""}${formatPercent(value)}`;
