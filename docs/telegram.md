# Telegram: avisos e comandos

Opcional e desligado por padrão. Roda no processo `RoboB3.Workers`.

## O que faz

**Avisos** (a cada 30 s, só para as conversas liberadas):

- entradas e saídas, com o resultado;
- ordens recusadas, sem confirmação ou recuperadas depois de um reinício;
- robô travado ou destravado (com o motivo, por exemplo divergência com a corretora);
- zerar tudo;
- troca de modo de execução e mudança de limites de risco;
- modelo da IA promovido ou aposentado;
- resumo diário às 18h15 (dias úteis).

Ao iniciar, os avisos começam do presente: nada antigo é reenviado.

**Comandos:**

| Comando | Efeito |
|---|---|
| `/status` | modo, trava, posições abertas e caixa |
| `/posicoes` | posições abertas com stop |
| `/decisoes` | últimas 5 decisões do robô |
| `/pausar` | pede confirmação; `/confirmar <código>` trava o robô |

O que o Telegram **não** faz: destravar o robô, enviar ordens, zerar posições, mudar limites, modo de execução ou capital. Isso só pelo painel, com senha.

## Proteções

- **Autorização:** a conversa precisa estar em `AllowedChatIds` e o usuário em `AllowedUserIds`. Mensagens de fora são ignoradas sem resposta e ficam na auditoria (`telegram.nao-autorizado`).
- **Confirmação:** `/pausar` gera um código aleatório de 6 dígitos, de uso único, preso à conversa e ao usuário, válido por 120 s. Uma tentativa errada invalida o código.
- **Reenvio e fila:** mensagens com mais de 120 s são ignoradas. Ao iniciar, a fila acumulada é descartada. Cada mensagem é confirmada ao Telegram antes de ser tratada, então nunca roda duas vezes.
- **Auditoria:** todo comando fica em `AuditEntries` (`telegram.comando`, `telegram.confirmacao-recusada`, `kill-switch.halt` com ator `telegram:<id>`).
- **Token:** só em user-secrets ou variável de ambiente. O cliente HTTP não registra as requisições (a URL contém o token) e os erros são relançados sem a URL.
- **Texto recebido** é dado não confiável: só é comparado com a lista fixa de comandos e nunca vai para a IA.
- Sem webhook: o bot lê por long polling, então o PC não fica exposto na internet.

## Configurar

1. No Telegram, fale com `@BotFather`, crie um bot (`/newbot`) e copie o token.
2. Mande uma mensagem qualquer para o seu bot. Descubra o seu id de conversa e de usuário em `https://api.telegram.org/bot<TOKEN>/getUpdates` (campos `chat.id` e `from.id`). Abra essa URL só no seu navegador e não compartilhe.
3. Guarde o token como segredo (nunca no `appsettings.json`):

```bash
cd src/RoboB3.Workers
dotnet user-secrets set "Telegram:BotToken" "<token do BotFather>"
```

4. Ligue e libere as conversas (aqui não há segredo; pode ser no `appsettings.json` ou em user-secrets):

```bash
dotnet user-secrets set "Telegram:Enabled" "true"
dotnet user-secrets set "Telegram:AllowedChatIds:0" "<chat.id>"
dotnet user-secrets set "Telegram:AllowedUserIds:0" "<from.id>"
```

5. Reinicie os workers. No log deve aparecer `Telegram: lendo comandos`. Mande `/status` para testar.
