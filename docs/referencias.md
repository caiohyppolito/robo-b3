# Análise dos projetos de referência

Analisados em 07/11/2026. Os repositórios foram clonados só para leitura: **nenhum código externo foi executado, instalado ou copiado**. As ideias adotadas foram reimplementadas no nosso código. Os caminhos citados são dos repositórios originais, no commit indicado.

## Escopo efetivamente analisado

| Projeto | Commit | Data | Licença |
|---|---|---|---|
| Euricojr/Dashboard-Backtesting-Python ("FinSense") | `1d951588e9a6` | 2026-05-27 | MIT |
| api-evangelist/dadosb3-com | `8d11316343fa` | 2026-11-05 | sem arquivo de licença |
| MarceloPCF/COIR | `ae53a039a4a3` | 2026-11-06 | **AGPL-3.0** (a pasta `legacy/` é MIT) |
| stocksharp/stocksharp | `455717557a9d` | 2026-10-25 | **proprietária** (`LICENSE`, `NOTICE`) |
| GwakJunwoo/24H2_Sogang_Robo_Advisor | `952827ccda95` | 2024-11-27 | MIT |
| viniciusbarbosapaiva/bot-mini-indice | `70e38d73c90a` | 2021-07-13 | MIT |
| andreolvgomes/NewRobot (MQL5) | `eb0afdbd9a5c` | 2021-05-15 | sem licença |
| yllvar/Kalshi-Quant-TeleBot | `148ca12f1eb5` | 2026-01-09 | o README cita MIT, mas não há arquivo de licença |
| Tópico `investimentos` (Python): priharumi/Agente_B3 | `4b00df38e2ab` | 2026-06-15 | sem licença |
| Tópico `investimentos`: gveronezg/InvAI | `a0904695ec2e` | 2025-04-09 | MIT |
| Tópico `investimentos`: Arthurgs27/analise-carteira-python | `656c51e0510e` | 2026-07-29 | sem licença |
| Tópico `investimentos`: KauaCastroBR8/RDII | `7408c44591cd` | 2026-06-07 | só um badge no README, sem arquivo |
| Perfil httpsDeni: DASHBOARD_GERENCIADOR_OPERACOES | `476296766cc6` | 2026-10-05 | MIT declarada só no `Cargo.toml` |
| Perfil httpsDeni: TradingAgents (fork de TauricResearch) | `be952b8eccb4` | 2026-09-07 | Apache-2.0 |

**Descoberta:** da página do tópico `investimentos` foram lidos 20 dos 22 repositórios; os outros 2 ficam atrás de "carregar mais". Do perfil httpsDeni, 30 de 46. Os demais repositórios do tópico tratam de IR, renda fixa, fundos ou previdência. Os demais do perfil são forks de segurança e ferramentas sem relação com trading, e um deles é um cheat de jogo. Nenhum desses foi analisado.

## Matriz de aproveitamento

| Projeto | Evidência no código | Ideia aproveitável | Benefício | Compatibilidade | Esforço | Limitações | Decisão |
|---|---|---|---|---|---|---|---|
| FinSense | `Live_Bot_MT5/scalper_win.py`, `backtester.py`, `screener_tecnico.py:calculate_atr_adx`, `app.py:monitor_posicoes_encerradas` | Estratégia WIN M5: cruzamento EMA9×SMA21 filtrado pela VWAP, com stop e alvo fixos em pontos. Filtro de regime: ADX14 > 25, ATR% > 2%, liquidez de 20 dias ≥ R$ 5 mi. MAE/MFE e alerta por operação. Stop e alvo na mesma barra: conta o stop | Estratégia intradiária candidata; filtro de regime; observabilidade | Média (Python procedural) | S/M | Backtest sem custos; otimização dentro da amostra; RSI semanal com informação do futuro; o Telegram envia ordens de qualquer chat | **Ideias adotadas e reimplementadas** |
| NewRobot | `Include/NewRobot/ManagerExpert.mqh`, `Signal/SignalDunnigan.mqh`, `ManagerRisk.mqh` | Setup Dunnigan (≥ 3 barras com máximas e mínimas menores → BUY STOP na máxima anterior + 5); break-even em degraus; saídas parciais; limites diários só bloqueiam entradas | Estratégia WIN candidata; gestão de saída que só reduz | Baixa (MQL5) | M | Sem licença; **aumenta a posição contra o preço (até 4× o risco)**; IOC fixo; não confere o retcode | Ideias registradas. Dunnigan depende de ordem stop de entrada (pendente). Aumento contra o preço **rejeitado** |
| StockSharp | `Messages/StateValidator.cs`, `OrderSnapshotHolder.cs`, `Algo.Testing/Emulation/MarketEmulator.cs:381`, `Algo/Slippage`, `Algo/Latency`, `Strategy.cs:1735` | Estados de ordem; slippage e latência medidos; caminho OHLC dentro da barra; modo "só reduzir"; regras de risco plugáveis | Execução e backtest mais realistas | Alta (C#) | S/M | **Licença proprietária: só conceitos.** Sem conector MT5/B3 neste repositório; o adaptador de risco destrava sozinho | Ideias adotadas: slippage, latência, stop primeiro em barra ambígua |
| COIR | `coir/domain/nota.py`, `coir/parsing/conciliar.py`, `coir/parsing/bovespa.py:74-97`, `coir/engine/rateio.py`, `tests/fixtures/notas/` | Leitura da nota SINACOR (a Clear usa a gramática da XP); custos por categoria (liquidação, emolumentos, ISS, IRRF); conferência da nota ao centavo; rateio dos custos pelo maior resto | Conciliar custos reais com o modelo de custo do backtest | Média | M/L | **AGPL: não copiar**; sem fixture de nota da Clear; o layout da nota muda | Ideia registrada para um módulo de conciliação **separado do núcleo** (pendente) |
| Arthurgs27/analise-carteira-python | `Kpis.py` (`buscar_serie_bcb`, `calcular_analise`), `test_kpis.py` | CDI diário do Banco Central (série SGS 12) no lugar de uma constante; Sharpe sobre o excesso ao CDI; comprar e manter vs rebalanceamento | CDI real nos backtests | Alta | S | Sem licença; sem custos; carteira escolhida a dedo | Ideia adotada (série do BCB) |
| Robo Advisor (Sogang) | `Engine/Pipeline.py`, `Engine/Optimizer.py`, `Engine/Evaluation.py` | Alocação hierárquica; peso pelo inverso da volatilidade; Sortino, Calmar, CVaR | Carteira de ações e métricas melhores | Média | S/M | Mercado coreano; sem custos; "risk parity" ingênuo | Métricas adotadas; alocação registrada para a etapa de carteira |
| TradingAgents | `agents/schemas.py`, `agents/utils/rating.py`, `dataflows/market_data_validator.py`, `dataflows/date_window.py` | O LLM recebe só números calculados pelo sistema; saída estruturada; resposta inválida vira "revisar", nunca sinal; janelas de data sem informação do futuro | Uso seguro do Claude | Conceitual | S | No original, o LLM decide a operação | Ideias de proteção adotadas; LLM decidindo operações **rejeitado** |
| Kalshi-Quant-TeleBot | `telegram_ui/telegram_bot.js`, `src/settings_manager.py` | Catálogo de comandos; confirmação em dois passos; `/set` com lista fixa e faixas | Formato do bot de Telegram | Baixa | M | **Sem autorização de chat**, `.env` versionado, repete POST em caso de erro (ordem duplicada), mercado de previsão | Só o formato; adotamos o oposto em segurança |
| DASHBOARD_GERENCIADOR_OPERACOES | `src/lib/mt5.ts`, `src/lib/stats.ts`, `YearCalendar.svelte` | Mapa anual de resultado por mês; leitura do relatório HTML do MT5 (UTF-16LE) | Painel e uma fonte extra de conciliação | Média | S/M | Diário manual de forex; limites só de enfeite; backend vazio | Referência |
| KauaCastroBR8/RDII | `src/api_client.py`, `src/dashboard.py` | Cartões de carteira e gráfico de alocação | Pouco | Média | S | **Códigos do BCB errados** (432 = meta Selic, não CDI) | Referência |
| dadosb3-com | só `README.md` | Eventos societários, troca de ticker, deslistagem (dados sem viés de sobrevivência) | Universo histórico | Baixa | M | **Não é API oficial da B3** ("This is not our API"); autenticação e custo desconhecidos | Só referência |
| bot-mini-indice | `app_indice_dolar.py:448-607`, `bot_mini_indice.py:32-33` | Código do contrato vigente; arredondar ao tick (já temos os dois) | Nenhum novo | Baixa | — | **Acerto anunciado medido nos dados de treino**; usuário e senha no código | Rejeitado |
| Agente_B3 / InvAI | `bot.py:743-751`; `InvAI_bot.session` versionado | Nenhuma | — | — | — | Sem lista de chats autorizados; **sessão do Telegram, histórico e fotos vazados** no repositório | Rejeitados |

## Lições que viraram regra no nosso projeto

1. **Medir resultado só fora da amostra e com custos.** Três projetos anunciam acertos ou lucros medidos nos dados de treino ou sem custos.
2. **Telegram nunca envia ordem nem destrava o robô.** Comandos só de consulta, ou que reduzem risco, com lista de chats autorizados e confirmação de uso único.
3. **Nenhuma repetição automática de ordens** (o Kalshi repete POST quando dá erro).
4. **Nenhum aumento de posição contra o preço** (NewRobot).
5. **O LLM explica, não decide**; números sempre vêm do sistema (TradingAgents).
6. **Licença primeiro:** StockSharp (proprietária), COIR (AGPL) e os projetos sem licença não tiveram código copiado.
