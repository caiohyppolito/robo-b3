const brl = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const number = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 2 });
const dateTime = new Intl.DateTimeFormat("pt-BR", { dateStyle: "short", timeStyle: "short" });

export const formatBrl = (value: number) => brl.format(value);
export const formatNumber = (value: number) => number.format(value);
export const formatPercent = (value: number) => `${number.format(value)}%`;
export const formatDateTime = (iso: string) => dateTime.format(new Date(iso));
