import { useEffect, useState } from "react";
import { MODE_LABEL, taxApi, type ExecutionMode, type TaxReport } from "../../api";
import { formatBrl, formatDate } from "../../format";

const MONTH = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

export function TaxPage() {
  const [mode, setMode] = useState<ExecutionMode>("Mt5Real");
  const [year, setYear] = useState(new Date().getFullYear());
  const [report, setReport] = useState<TaxReport | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    taxApi
      .report(mode, year)
      .then((r) => alive && (setReport(r), setError(null)))
      .catch((e: unknown) => alive && setError(e instanceof Error ? e.message : "Falha"));
    return () => {
      alive = false;
    };
  }, [mode, year]);

  const money = (v: number) => (v === 0 ? <span className="muted">—</span> : formatBrl(v));

  return (
    <>
      <section className="card">
        <div className="filters">
          <label>
            Conta
            <select value={mode} onChange={(e) => setMode(e.target.value as ExecutionMode)}>
              {(Object.keys(MODE_LABEL) as ExecutionMode[]).map((m) => (
                <option key={m} value={m}>{MODE_LABEL[m]}{m !== "Mt5Real" ? " (simulação, não gera imposto)" : ""}</option>
              ))}
            </select>
          </label>
          <label>
            Ano
            <select value={year} onChange={(e) => setYear(Number(e.target.value))}>
              {[0, 1, 2, 3].map((d) => new Date().getFullYear() - d).map((y) => (
                <option key={y} value={y}>{y}</option>
              ))}
            </select>
          </label>
        </div>
        <p className="callout warn" style={{ margin: "1rem 0 0" }}>
          Estimativa a partir das operações do robô, regra a regra: 15% comum, 20% day trade, isenção de ações até R$ 20 mil de vendas no mês (units, ETFs e
          futuros fora), prejuízo compensado na mesma modalidade, IRRF estimado (0,005% e 1%), DARF {report?.darfCode ?? "6015"} mínimo de R$ 10, vencimento
          no último dia útil do mês seguinte. Não considera operações feitas fora do robô nem proventos. Confira com as notas da corretora e um contador.
        </p>
      </section>

      {error && <p className="callout bad">{error}</p>}

      {report && (
        <>
          <div className="metrics">
            <div className="metric kpi"><span className="label">DARFs no ano</span><strong>{formatBrl(report.totalDarf)}</strong><span className="sub">código {report.darfCode}</span></div>
            <div className="metric kpi"><span className="label">Lucro isento (ações)</span><strong className="up">{formatBrl(report.totalExemptProfit)}</strong><span className="sub">vai na declaração anual</span></div>
            <div className="metric kpi"><span className="label">Prejuízo a compensar</span><strong>{formatBrl((report.months.at(-1)?.swingLossCarry ?? 0) + (report.months.at(-1)?.dayTradeLossCarry ?? 0))}</strong><span className="sub">comum + day trade</span></div>
            <div className="metric kpi"><span className="label">IRRF a usar</span><strong>{formatBrl(report.months.at(-1)?.irrfCarry ?? 0)}</strong><span className="sub">saldo no fim do período</span></div>
          </div>
          <section className="card">
            <div className="card-head">
              <h2>Apuração mês a mês · {MODE_LABEL[report.mode]} · {report.year}</h2>
            </div>
            {report.months.length === 0 ? (
              <p className="muted" style={{ margin: 0 }}>Nenhuma operação encerrada nesta conta em {report.year}.</p>
            ) : (
              <div className="table-wrap">
                <table className="nowrap">
                  <thead>
                    <tr>
                      <th>Mês</th>
                      <th className="num">Operações</th>
                      <th className="num">Vendas de ações</th>
                      <th className="num">Isento</th>
                      <th className="num">Comum</th>
                      <th className="num">Day trade</th>
                      <th className="num">Prejuízo usado</th>
                      <th className="num">Imposto</th>
                      <th className="num">IRRF deduzido</th>
                      <th className="num">DARF</th>
                      <th>Vencimento</th>
                    </tr>
                  </thead>
                  <tbody>
                    {report.months.map((m) => (
                      <tr key={`${m.year}-${m.month}`}>
                        <td className="mono">{MONTH[m.month - 1]}/{String(m.year).slice(2)}</td>
                        <td className="num">{m.trades}</td>
                        <td className="num">{money(m.stockSales)}{m.stockExempt && <span className="chip tone good" style={{ marginLeft: 6 }}>isento</span>}</td>
                        <td className="num">{money(m.exemptProfit)}</td>
                        <td className={`num ${m.swingResult > 0 ? "up" : m.swingResult < 0 ? "down" : ""}`}>{money(m.swingResult)}</td>
                        <td className={`num ${m.dayTradeResult > 0 ? "up" : m.dayTradeResult < 0 ? "down" : ""}`}>{money(m.dayTradeResult)}</td>
                        <td className="num">{money(m.swingLossUsed + m.dayTradeLossUsed)}</td>
                        <td className="num">{money(m.swingTax + m.dayTradeTax)}</td>
                        <td className="num">{money(m.irrfDeducted)}</td>
                        <td className="num"><strong>{m.darfAmount > 0 ? formatBrl(m.darfAmount) : m.taxDue + m.carriedFromPrevious > 0 ? <span className="muted small">acumula (&lt; R$ 10)</span> : "—"}</strong></td>
                        <td className="mono">{m.darfDueDate ? formatDate(m.darfDueDate) : "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </>
      )}
    </>
  );
}
