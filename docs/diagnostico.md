# Diagnóstico, o que mudou e critérios de prontidão

Data: 08/11/2026. Estado da `main` depois da rodada de análise das referências.

## 1. Diagnóstico

O ponto de partida já tinha boa base: arquitetura em camadas, backtest com custos, IR e CDI, risco com kill switch, IA que só veta entradas e execução no MT5 com três travas independentes. Os problemas encontrados, por gravidade:

| # | Problema | Risco | Situação |
|---|---|---|---|
| 1 | Dois ciclos simultâneos (worker e botão do painel) podiam mandar a mesma ordem duas vezes | Ordem duplicada | Corrigido: trava de ciclo no SQL Server (`sp_getapplock`) |
| 2 | Queda do processo entre enviar a ordem e gravar o resultado deixava posição sem registro | Posição órfã | Corrigido: intenção de ordem gravada antes do envio e recuperação no reinício |
| 3 | Stop enviado junto da ordem podia não ficar registrado na corretora, sem ninguém conferir | Posição sem proteção com o PC desligado | Corrigido: o conector confere a posição, registra o stop à parte uma vez e, se falhar, o robô trava novas entradas |
| 4 | Stop móvel mandava `tp=0` e apagava o alvo | Alvo perdido | Corrigido |
| 5 | Filtro da IA aplicado a barras de 5 minutos com modelo treinado em barras diárias | Vetos sem sentido | Corrigido: IA só em alocações D1; o diário só guarda variáveis diárias |
| 6 | Decisões antigas com texto vazio no lugar de JSON derrubavam a lista de decisões | Painel com erro 500 | Corrigido: leitura tolerante e migração que corrige os dados |
| 7 | Tamanho da posição no MT5 usava o patrimônio do primeiro dia | Posição maior que a conta suporta após perdas ou saques | Corrigido: menor entre o livro e o saldo atual da conta |
| 8 | CDI fixo no backtest | Comparação com o CDI distorcida em períodos de juros diferentes | Corrigido: série diária do Banco Central |
| 9 | Sem botão de emergência para zerar tudo | Saída lenta numa crise | Corrigido: "Zerar tudo" com frase de confirmação |
| 10 | Sem registro do número de tentativas da pesquisa | Melhor estratégia pode ser sorte | Corrigido: cada comparação grava quantas simulações rodou |

## 2. O que foi implementado nesta rodada

- **Execução e robustez:** os itens 1, 2, 3, 4, 7 e 9 da tabela acima.
- **Registro das decisões:** cada decisão grava os parâmetros, as variáveis, o modelo e a versão do código. Cada operação grava a excursão máxima a favor e contra (MAE/MFE), o slippage e a latência.
- **Estratégias e backtest:**
  - EMA, VWAP do pregão e ADX;
  - estratégia EMA × SMA com VWAP para intradiário;
  - filtro de ADX opcional nas estratégias de tendência;
  - alvo de lucro (no backtest, no paper e na corretora);
  - stop e alvo na mesma barra contam como stop, e a operação fica marcada como ambígua;
  - Sortino, Calmar, CVaR 95%, expectativa por operação com margem de 95% e giro anual;
  - registro de pesquisas.
- **CDI diário do Banco Central:** série 12 do SGS, usada no caixa, na linha de comparação e no Sharpe.
- **IA:**
  - calibração (Brier contra a taxa-base, ECE e faixas de confiabilidade);
  - mudança de regime por PSI, só como alerta;
  - reativação manual de um modelo aposentado;
  - contrafactual dos vetos, marcado como simulação;
  - correções de desvio entre treino e uso (item 5).
- **Telegram (opcional):** avisos, consultas e o comando `/pausar` com código de uso único. Pelo Telegram nunca se destrava o robô nem se envia ordem. Ver [telegram.md](telegram.md).
- **Interface:**
  - login novo, menu agrupado e topo com cotação e status;
  - Gráfico de candles com indicadores e execuções;
  - Histórico de operações e Configurações em abas;
  - Backtest com resultado em abas.
- **Referências:** a análise e a matriz estão em [referencias.md](referencias.md). Nenhum código de terceiros foi copiado: as ideias foram reimplementadas e as licenças foram conferidas.

## 3. Verificação

Rodado na `main` nesta data:

- **`dotnet build`:** sem avisos, e avisos contam como erro no projeto.
- **`dotnet test`:** 212 testes, incluindo os de SQL Server real com `ROBOB3_TEST_SQLSERVER`.
- **`cd web && npm run build`:** ok.
- **`cd mt5-gateway && python -m pytest`:** 33 testes, incluindo o caminho real do `Mt5Backend` com um MetaTrader 5 simulado.
- **Telas conferidas no navegador:** login, Painel, Gráfico, Backtest, Histórico, Configurações e IA, nos temas claro e escuro e na largura de celular.

O que **não** foi verificado, porque depende do seu PC ou de rede bloqueada aqui:

- Envio real de ordem na conta demo da Clear. Siga o roteiro em [teste-demo-ordens.md](teste-demo-ordens.md).
- Resposta real da API do Banco Central. O formato foi implementado pela documentação do SGS, com leitura tolerante e testes com exemplo fixo. Confira em Configurações → Dados e CDI → "Atualizar agora".
- Resultados das estratégias nos seus dados. O Yahoo é bloqueado neste ambiente. Na comparação feita na outra sessão, nenhuma estratégia simples venceu o CDI fora da amostra.

## 4. Critérios para algum dia ligar a conta real

A conta real só deve ser considerada com **todos** os itens abaixo cumpridos. O robô não verifica esta lista por conta própria: a decisão é sua, e a troca exige as três travas e a frase de confirmação.

**Evidência da estratégia**

- [ ] Vence o CDI e o comprar e segurar **fora da amostra** na comparação, com custos e IR, em mais da metade dos ativos testados.
- [ ] Pelo menos 100 operações fora da amostra, com a margem de 95% da expectativa por operação acima de zero.
- [ ] O resultado sobrevive a uma variação de ±20% nos parâmetros (sensibilidade) e a custos 50% maiores.
- [ ] Poucas operações ambíguas (stop e alvo na mesma barra), ou o resultado se mantém ao tratá-las como stop.

**Evidência operacional (na conta demo)**

- [ ] O roteiro [teste-demo-ordens.md](teste-demo-ordens.md) foi cumprido: o stop e o alvo ficam na corretora, inclusive com o PC desligado.
- [ ] 30 pregões seguidos no MT5 demo **sem divergência de conciliação**, sem ordem sem confirmação e sem trava por stop não registrado.
- [ ] O resultado no demo, depois dos custos, está dentro da faixa esperada pelo backtest do mesmo período. Uma diferença grande indica slippage ou execução diferentes do simulado.
- [ ] O slippage e a latência médios registrados estão abaixo dos usados no backtest.
- [ ] "Zerar tudo" foi testado no demo com posição aberta.

**Limites para começar**

- [ ] `ROBOB3_MAX_CONTRACTS=1` no conector e o limite de contratos do robô em 1.
- [ ] Perda máxima do dia e drawdown apertados (por exemplo 1% e 5%) nas primeiras semanas.
- [ ] Capital pequeno na conta real, com a perda máxima tolerável definida por você antes do primeiro dia.
- [ ] Telegram configurado, para receber as travas e o resumo diário.

## 5. Limitações conhecidas

- **Contrafactual dos vetos:** é uma aproximação. Refaz o sinal no histórico diário, mas o robô real poderia estar em outra situação, como já posicionado ou travado.
- **Variáveis antigas no diário:** operações intradiárias registradas antes desta rodada continuam com variáveis calculadas em barras de minutos. Para não usá-las no treino, apague-as do diário ou retreine depois de acumular operações diárias.
- **Estratégia nas execuções antigas:** execuções gravadas antes da coluna de estratégia aparecem sem ela no Histórico.
- **Conciliação de notas de corretagem:** a conferência das notas da Clear e do IR contra a corretora (como faz o COIR) não foi feita. Fica como módulo separado, futuro, sem copiar código AGPL.
- **Fora do escopo por enquanto:** carteira com alocação entre ativos (risk parity com limite por ativo) e varredura automática de ativos.
- **Stop-entry:** a estratégia Dunnigan, das referências, depende de ordem stop de entrada, que o robô ainda não envia.
- **Mercado:** o MT5 fornece o histórico intradiário. O diário vem do Yahoo, uma fonte não oficial que pode ter falhas ou ajustes.
