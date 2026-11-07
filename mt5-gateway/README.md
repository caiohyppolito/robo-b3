# Conector do MetaTrader 5 (mt5-gateway)

Pequeno servidor local que lê **cotações e barras** do MetaTrader 5 aberto no seu PC e as entrega ao Robô B3.

- **Só leitura:** não existe rota de envio de ordens. Nem um bug consegue operar a sua conta por aqui.
- **Só localhost:** escuta em `127.0.0.1` e exige o token `X-Gateway-Token` em todas as rotas.
- **Seu login fica no MT5:** o conector usa o terminal já logado; ele não recebe nem guarda a sua senha.

## Instalação (Windows)

1. MetaTrader 5 da Clear instalado, aberto e logado.
2. Python 3.12 ou mais novo ([python.org](https://www.python.org/downloads/)).
3. No PowerShell, na pasta `mt5-gateway`:

```powershell
python -m venv .venv
.venv\Scripts\pip install -r requirements.txt
```

## Executar

Gere um token (o mesmo valor vai para a API e para os workers):

```powershell
$token = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set "Mt5Gateway:Token" $token --project ..\src\RoboB3.Api
dotnet user-secrets set "Mt5Gateway:Token" $token --project ..\src\RoboB3.Workers
$env:ROBOB3_GATEWAY_TOKEN = $token
.venv\Scripts\python -m gateway
```

Para testar sem o MT5 (preços sintéticos, mercado sempre aberto), defina `$env:ROBOB3_GATEWAY_FAKE = "1"` antes de iniciar.

## Variáveis

| Variável | Padrão | Descrição |
|---|---|---|
| `ROBOB3_GATEWAY_TOKEN` | (obrigatória) | Token compartilhado com a API, mínimo de 16 caracteres |
| `ROBOB3_GATEWAY_PORT` | `8765` | Porta local |
| `ROBOB3_GATEWAY_FAKE` | desligado | `1` = dados sintéticos, sem MT5 |
| `ROBOB3_SERVER_UTC_OFFSET` | `-3` | Fuso do servidor da corretora em relação ao UTC |
| `ROBOB3_CONTINUOUS_PATTERNS` | `{root}$N,{root}$` | Nomes da série contínua de futuros, em ordem de preferência |

## Rotas

| Rota | Retorno |
|---|---|
| `GET /health` | Conexão, servidor, conta, se é demo |
| `GET /symbols/{código}/resolve` | Contrato vigente (WIN → WINZ26) ou o próprio código da ação |
| `GET /rates/{símbolo}?timeframe=M5&count=400` | Últimas barras (a última ainda está em formação) |
| `GET /history/{raiz}?timeframe=M5&count=20000` | Histórico longo; para futuros, usa a série contínua se existir |
| `GET /tick/{símbolo}` | Bid, ask, último e horário |

## Testes

```powershell
.venv\Scripts\pip install -r requirements-dev.txt
.venv\Scripts\python -m pytest
```
