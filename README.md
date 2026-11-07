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
- [ ] **Fase 2, estratégias e backtest:** indicadores, estratégias plugáveis, backtest com custos da B3 e IR (15% swing com isenção até R$ 20 mil/mês em vendas de ações; 20% day trade) e relatórios
- [ ] **Fase 3, IA:** diário de trades, meta-modelo (ML.NET) que filtra sinais ruins e retreino walk-forward, promovendo o modelo novo só se ele vencer fora da amostra
- [ ] **Fase 4, paper trading:** integração com o MetaTrader 5 da Clear (conta demo), dados intradiários de WIN/WDO e carteira simulada
- [ ] **Fase 5, conta real:** começando com tamanho mínimo e todas as travas ligadas
