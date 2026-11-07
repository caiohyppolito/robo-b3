# Robô B3

Robô de investimentos para a B3 (ações do Ibovespa, ETFs, mini-índice WIN e mini-dólar WDO), com day trade e swing trade, gestão de risco rígida e aprendizado contínuo a partir do histórico de operações.

> ⚠️ **Aviso.** Este projeto é experimental e não é recomendação de investimento. Operar na bolsa, principalmente futuros alavancados, pode causar perdas maiores que o capital. O robô nasce **travado** e só opera depois de liberado manualmente.

## Arquitetura

```
web/  (React + TypeScript)  ──HTTP──▶  RoboB3.Api  ──┐
                                                     ├──▶  SQL Server
                          RoboB3.Workers (jobs)  ────┘
                                   │
                                   └──▶ provedores de dados (Yahoo; MT5 na Fase 4)
```

| Projeto | Responsabilidade |
|---|---|
| `RoboB3.Domain` | Regras de negócio puras: ativos, candles, limites de risco, kill switch, `RiskManager` |
| `RoboB3.Application` | Casos de uso e portas (interfaces): coleta de dados, kill switch, monitor de risco |
| `RoboB3.Infrastructure` | EF Core/SQL Server, repositórios, provedor Yahoo Finance, hash de senha |
| `RoboB3.Api` | API REST com JWT, rate limit no login, auditoria e cabeçalhos de segurança |
| `RoboB3.Workers` | Jobs em segundo plano: coleta de dados e monitor de risco (pode rodar como Serviço do Windows) |
| `web/` | Painel: estado do robô, kill switch, patrimônio, limites de risco, auditoria e ativos |

As dependências seguem a Clean Architecture: `Domain ← Application ← Infrastructure ← Api/Workers`.

## Gestão de risco

| Limite | Padrão | Efeito |
|---|---|---|
| Perda máxima no dia | 2% | Trava o robô automaticamente |
| Drawdown máximo | 15% | Trava o robô automaticamente |
| Exposição por ativo | 20% do patrimônio | Rejeita a ordem |
| Posições abertas | 5 | Rejeita novas posições |
| Contratos futuros | 2 (WIN + WDO) | Rejeita a ordem |

- Todos os limites são editáveis no painel, e cada alteração fica registrada na auditoria.
- Ordens que **só reduzem** uma posição (zerar) são sempre aceitas, mesmo com o robô travado.
- Destravar o robô exige ação humana. Nada destrava sozinho.

## Backtest e comparação de estratégias

Na aba **Backtest** do painel:

- **Rodar backtest:** escolha ativo, estratégia, parâmetros e período. O resultado mostra retorno, drawdown, Sharpe, operações, custos, IR e a curva de patrimônio comparada ao "comprar e segurar" e ao CDI.
- **Comparar todas as estratégias:** os parâmetros de cada estratégia são escolhidos só com a parte inicial do histórico (70%, *dentro da amostra*) e avaliados na parte final, que a otimização nunca viu (*fora da amostra*). O ranking usa o resultado fora da amostra, o único que importa.

| Estratégia | Ideia |
|---|---|
| Comprar e segurar | Referência que toda estratégia precisa vencer |
| Cruzamento de médias | Tendência: entra no cruzamento da média rápida sobre a lenta; stop móvel em ATRs |
| Rompimento de canal | Momentum: rompimento da máxima/mínima de N dias (estilo Turtle) |
| Reversão à média (RSI) | Compra quedas fortes dentro de tendência de alta e sai no repique |

**Como a simulação reproduz o robô real:**

- O sinal é calculado no fechamento e executado na **abertura seguinte**. As estratégias só enxergam o passado: acessar uma barra futura lança erro.
- Os preços são ajustados por proventos (fechamento ajustado do Yahoo).
- Cada entrada passa pelo mesmo `RiskManager` do robô. Estourar a perda diária ou o drawdown **bloqueia novas entradas**, e as saídas continuam permitidas.
- Tamanho da posição pelo risco: perda de no máximo *X%* do patrimônio se o stop for atingido, limitada pela exposição máxima por ativo.
- O stop considera gaps: se o pregão abre além do stop, a saída é na abertura.
- Custos configuráveis em `Backtest` no `appsettings.json`: corretagem (Clear: R$ 0), taxas da B3, slippage e taxa por contrato futuro.
- **IR mensal:** 15% em swing trade, 20% em day trade, isenção para ações com vendas de até R$ 20 mil no mês, compensação de prejuízo e DARF mínimo de R$ 10.
- O caixa parado rende CDI (taxa em `Backtest:RiskFreeRatePercent`; **ajuste para o CDI atual**). O IR da renda fixa não é descontado.
- Futuros: WIN rende R$ 0,20 por ponto e WDO R$ 10,00 por ponto, com venda a descoberto permitida. O backtest de WIN/WDO depende dos dados intradiários do MT5 (Fase 4).

> Backtest bom não garante lucro futuro. Desconfie de resultados que só aparecem dentro da amostra.

## Como rodar no Windows

### 1. Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [Node.js 22 LTS](https://nodejs.org/)
- SQL Server: **uma** das opções abaixo
  - SQL Server Express LocalDB (vem com o Visual Studio): é o padrão, não exige configuração;
  - SQL Server Express/Developer;
  - Docker: `copy .env.example .env`, ajuste a senha e rode `docker compose up -d`.

### 2. Segredos (ficam só no seu PC, fora do Git)

```powershell
# Chave de assinatura do JWT
$key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "Auth:SigningKey" $key --project src/RoboB3.Api

# Senha do painel (gera o hash e mostra o comando para gravá-lo)
dotnet run --project src/RoboB3.Api -- hash-password
```

Se **não** usar o LocalDB, configure a connection string na API e nos Workers:

```powershell
dotnet user-secrets set "ConnectionStrings:RoboB3" "Server=localhost,1433;Database=RoboB3;User Id=sa;Password=SUA_SENHA;TrustServerCertificate=True" --project src/RoboB3.Api
dotnet user-secrets set "ConnectionStrings:RoboB3" "Server=localhost,1433;Database=RoboB3;User Id=sa;Password=SUA_SENHA;TrustServerCertificate=True" --project src/RoboB3.Workers
```

### 3. Subir tudo (três terminais)

```powershell
dotnet run --project src/RoboB3.Api        # API em http://localhost:5080 (cria o banco na primeira execução)
dotnet run --project src/RoboB3.Workers    # coleta de dados e monitor de risco
cd web; npm install; npm run dev           # painel em http://localhost:5173
```

Usuário padrão do painel: `admin`, com a senha definida no passo 2.

### Testes

```powershell
dotnet test
cd web; npm run build
```

Os testes de integração com SQL Server (migrações, gravação dos backtests) rodam quando a variável `ROBOB3_TEST_SQLSERVER` aponta para um servidor; sem ela, são pulados. O CI sobe um SQL Server em container e roda todos.

```powershell
$env:ROBOB3_TEST_SQLSERVER = "Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True"
dotnet test
```

## Segurança

- API e banco escutam **apenas em localhost**.
- Todas as rotas exigem JWT, exceto login e `/health`. O login tem limite de 5 tentativas por minuto.
- Senha com PBKDF2-SHA256 (600 mil iterações) e comparação em tempo constante.
- Segredos via `user-secrets` ou variáveis de ambiente, nunca em `appsettings.json` ou no Git.
- O login da corretora **nunca** passa pelo robô: você entra no MetaTrader 5, e o robô só conversa com o terminal já logado.
- Auditoria de login, kill switch e alteração de limites.
- Dependabot e `npm audit` no CI.

## Roadmap

- [x] **Fase 1, fundação:** solution, banco, API, workers, painel, gestão de risco, kill switch e CI
- [x] **Fase 2, estratégias e backtest:** indicadores, quatro estratégias, backtest com custos, IR e CDI, comparação com validação fora da amostra e painel com curva de patrimônio
- [ ] **Fase 3, IA:** diário de trades, meta-modelo (ML.NET) que filtra sinais ruins e retreino walk-forward, promovendo o modelo novo só se ele vencer fora da amostra
- [ ] **Fase 4, paper trading:** integração com o MetaTrader 5 da Clear (conta demo), dados intradiários de WIN/WDO e carteira simulada
- [ ] **Fase 5, conta real:** começando com tamanho mínimo e todas as travas ligadas
