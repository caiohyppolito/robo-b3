# Roteiro: conferir ordens na conta demo da Clear

Antes de deixar o robô mandar ordens sozinho na demo, confira o caminho com 1 contrato de WIN.
Os testes automáticos cobrem a lógica com um MT5 simulado; o comportamento real da Clear/B3 só se vê aqui.

## Por que

Um projeto de referência (`Live_Bot_MT5/scalper_win.py`) envia a ordem sem stop e só depois registra o stop,
com um stop por software de reserva e o comentário "Proteção contra Falha de OCO B3". Se o stop enviado junto
com a ordem não ficar registrado, a posição só estaria protegida enquanto o PC estiver ligado.

## O que o robô já faz

1. Envia a ordem a mercado com `sl` e `tp` no mesmo pedido, e o modo de preenchimento é o que o símbolo aceita (FOK → IOC → RETURN).
2. Depois da execução, confere na posição se o stop ficou lá. Se não ficou, tenta registrar à parte (`TRADE_ACTION_SLTP`) uma vez.
3. Se mesmo assim o stop não estiver na posição, a resposta vem com `stop_confirmed: false`. O robô **trava novas entradas** e avisa (painel e Telegram). Saídas e o stop por software continuam.
4. O stop móvel preserva o alvo da posição.

## Passo a passo (pregão aberto, conta DEMO)

1. Conector com `ROBOB3_ORDERS=demo` e `ROBOB3_MAX_CONTRACTS=1`; painel em "MT5 demo".
2. No PowerShell, envie a ordem direto ao conector (troque o token e os preços; o conector só aceita chamadas do próprio PC):

   ```powershell
   $h = @{ "X-Gateway-Token" = "<seu token do conector>" }
   $body = @{ symbol = "WINZ26"; side = "buy"; volume = 1; stop_loss = <preço-300>; take_profit = <preço+300>; client_id = "teste-manual-0001"; expected_mode = "demo"; comment = "teste" } | ConvertTo-Json
   Invoke-RestMethod -Method Post -Uri http://127.0.0.1:8765/orders -Headers $h -ContentType "application/json" -Body $body
   ```

3. Verifique:
   - [ ] `filled: true`. Se vier recusada, anote o `retcode` e a mensagem (ex.: 10030 = modo de preenchimento não suportado).
   - [ ] `stop_confirmed: true`.
   - [ ] No MT5 (aba Negociação), a posição mostra S/L e T/P.
   - [ ] Feche o conector e o robô: o S/L continua no MT5.
4. Mova o stop pelo robô (ou `Invoke-RestMethod -Method Post -Headers $h -Uri "http://127.0.0.1:8765/positions/<ticket>/stop?symbol=WINZ26&stop_loss=<novo>"`) e confira que o T/P **não** sumiu.
5. Zere a posição pelo painel ("Encerrar") e confira que não sobrou nada no MT5.

Se o item 3 falhar, não ligue as alocações no modo MT5 demo: anote o resultado e ajustamos o envio (por exemplo, ordem sem stop seguida de `TRADE_ACTION_SLTP`, como na referência).
