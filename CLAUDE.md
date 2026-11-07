# Robô B3: guia para agentes

- Backend .NET 10, Clean Architecture: `Domain` (puro, sem I/O) ← `Application` (casos de uso + portas) ← `Infrastructure` (EF Core/SQL Server, HTTP) ← `Api` / `Workers`.
- Pacotes NuGet com versão central em `Directory.Packages.props`; warnings são erros (`Directory.Build.props`).
- Regras de risco ficam em `RoboB3.Domain/Risk/RiskManager.cs` e precisam de teste para qualquer mudança.
- Backtest (`RoboB3.Domain/Backtesting`) deve reproduzir o robô real: sinal no fechamento, execução na abertura seguinte, mesmo `RiskManager`. Estratégias só acessam o passado via `History`.
- Testes com SQL Server real: definir `ROBOB3_TEST_SQLSERVER` (sem ela são pulados). O provedor InMemory do EF não lê complex types.
- Ordens que só reduzem posição nunca podem ser bloqueadas. O robô nunca se destrava sozinho.
- A IA (`RoboB3.Domain/Learning`) só pode vetar entradas (`ISignalFilter`); nunca cria operações nem bloqueia saídas. Modelos só são promovidos com evidência fora da amostra (`PromotionPolicy`).
- Paper trading (`RoboB3.Domain/Paper`, `PaperTradingService`) usa as mesmas regras do backtest (`PositionSizer`, `RiskManager`, estratégias) e decide só em barras fechadas. O conector `mt5-gateway/` (Python) só envia ordens com `ROBOB3_ORDERS` ligado e a conta do tipo certo (`gateway/orders.py`).
- Execução (`RoboB3.Domain/Execution`): modos Simulado, MT5 demo e MT5 real; troca só via `ExecutionPolicy`. O cliente HTTP de ordens não pode ter retry automático (ordem duplicada). Divergência com a corretora trava o robô.
- Horários de pregão em Brasília via `B3Calendar`; candles diários são gravados à meia-noite UTC da data do pregão.
- Mudou o modelo EF? Gere migração: `dotnet ef migrations add <Nome> --project src/RoboB3.Infrastructure --output-dir Persistence/Migrations`.
- Nunca versionar segredos: usar `user-secrets` ou variáveis de ambiente.
- Textos para o usuário, logs e nomes de testes em português; identificadores de código em inglês.
- Verificar: `dotnet build && dotnet test`, `cd web && npm run build` e `cd mt5-gateway && python -m pytest`.
