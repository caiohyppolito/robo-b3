"""Fontes de dados: MetaTrader 5 (Windows) e uma simulação determinística para testes."""

from __future__ import annotations

import hashlib
import math
from datetime import datetime, timedelta, timezone
from typing import Protocol

from .config import Settings
from .contracts import ContractInfo, resolve_current_contract

TIMEFRAME_MINUTES = {"M1": 1, "M5": 5, "M15": 15, "H1": 60, "D1": 1440}
FUTURE_ROOTS = ("WIN", "WDO", "IND", "DOL")


class GatewayError(Exception):
    """Erro com mensagem para o usuário (vira HTTP 4xx/5xx)."""

    def __init__(self, message: str, status: int = 400) -> None:
        super().__init__(message)
        self.status = status


class Backend(Protocol):
    def health(self) -> dict: ...

    def resolve(self, code: str) -> str: ...

    def rates(self, symbol: str, timeframe: str, count: int) -> list[dict]: ...

    def history(self, root: str, timeframe: str, count: int) -> tuple[str, list[dict]]: ...

    def tick(self, symbol: str) -> dict: ...

    def account(self) -> dict: ...

    def positions(self) -> list[dict]: ...

    def send_order(self, order) -> dict: ...

    def set_stop(self, symbol: str, ticket: int, stop_loss: float) -> dict: ...

    def closing_price(self, ticket: int) -> float | None: ...


def _validate(timeframe: str, count: int) -> None:
    if timeframe not in TIMEFRAME_MINUTES:
        raise GatewayError(f"Tempo gráfico inválido: {timeframe}. Use {', '.join(TIMEFRAME_MINUTES)}.")
    if not 1 <= count <= 100_000:
        raise GatewayError("Quantidade de barras deve estar entre 1 e 100000.")


class Mt5Backend:
    """Terminal MetaTrader 5 já logado: dados e, com ROBOB3_ORDERS ligado (ver orders.py), ordens."""

    def __init__(self, settings: Settings) -> None:
        import MetaTrader5 as mt5  # disponível só no Windows

        self._mt5 = mt5
        self._offset = timedelta(hours=settings.server_utc_offset_hours)
        self._patterns = settings.continuous_patterns
        self._timeframes = {
            "M1": mt5.TIMEFRAME_M1, "M5": mt5.TIMEFRAME_M5, "M15": mt5.TIMEFRAME_M15,
            "H1": mt5.TIMEFRAME_H1, "D1": mt5.TIMEFRAME_D1,
        }

    def _ensure(self) -> None:
        if self._mt5.terminal_info() is None and not self._mt5.initialize():
            raise GatewayError(f"MetaTrader 5 indisponível: {self._mt5.last_error()}", 503)

    def _utc(self, server_seconds: int) -> str:
        # O MT5 devolve o horário do servidor como se fosse UTC; corrige pelo fuso do servidor.
        return (datetime.fromtimestamp(int(server_seconds), tz=timezone.utc) - self._offset).isoformat()

    def _select(self, symbol: str) -> None:
        if not self._mt5.symbol_select(symbol, True):
            raise GatewayError(f"Símbolo {symbol} não encontrado no MetaTrader 5.", 404)

    def health(self) -> dict:
        try:
            self._ensure()
        except GatewayError as error:
            return {"connected": False, "mode": "mt5", "message": str(error)}
        account = self._mt5.account_info()
        terminal = self._mt5.terminal_info()
        connected = bool(terminal and terminal.connected and account)
        return {
            "connected": connected,
            "mode": "mt5",
            "server": account.server if account else None,
            "login": account.login if account else None,
            "isDemo": (account.trade_mode == self._mt5.ACCOUNT_TRADE_MODE_DEMO) if account else None,
            "company": account.company if account else None,
            "message": None if connected else "Terminal aberto, mas sem conexão com a corretora.",
        }

    def resolve(self, code: str) -> str:
        self._ensure()
        code = code.upper()
        if code not in FUTURE_ROOTS:
            self._select(code)
            return code
        symbols = self._mt5.symbols_get(f"{code}*") or []
        contract = resolve_current_contract(code, [ContractInfo(s.name, int(s.expiration_time or 0)) for s in symbols])
        if contract is None:
            raise GatewayError(f"Nenhum contrato vigente de {code} encontrado.", 404)
        self._select(contract)
        return contract

    def rates(self, symbol: str, timeframe: str, count: int) -> list[dict]:
        _validate(timeframe, count)
        self._ensure()
        self._select(symbol)
        data = self._mt5.copy_rates_from_pos(symbol, self._timeframes[timeframe], 0, count)
        if data is None:
            raise GatewayError(f"Sem barras de {symbol}: {self._mt5.last_error()}", 404)
        return [
            {
                "time": self._utc(r["time"]),
                "open": float(r["open"]), "high": float(r["high"]), "low": float(r["low"]), "close": float(r["close"]),
                "volume": int(r["real_volume"] or r["tick_volume"]),
            }
            for r in data
        ]

    def history(self, root: str, timeframe: str, count: int) -> tuple[str, list[dict]]:
        self._ensure()
        root = root.upper()
        if root in FUTURE_ROOTS:
            for pattern in self._patterns:
                candidate = pattern.format(root=root)
                if self._mt5.symbol_info(candidate) is not None:
                    return candidate, self.rates(candidate, timeframe, count)
        symbol = self.resolve(root)
        return symbol, self.rates(symbol, timeframe, count)

    def tick(self, symbol: str) -> dict:
        self._ensure()
        self._select(symbol)
        tick = self._mt5.symbol_info_tick(symbol)
        if tick is None:
            raise GatewayError(f"Sem cotação de {symbol}.", 404)
        return {"symbol": symbol, "bid": tick.bid, "ask": tick.ask, "last": tick.last, "time": self._utc(tick.time)}

    # ---------- Ordens (Fase 5) ----------

    def account(self) -> dict:
        self._ensure()
        info = self._mt5.account_info()
        if info is None:
            raise GatewayError("Conta não identificada no MT5.", 503)
        return {"login": info.login, "isDemo": info.trade_mode == self._mt5.ACCOUNT_TRADE_MODE_DEMO,
                "balance": info.balance, "equity": info.equity, "currency": info.currency}

    def positions(self) -> list[dict]:
        self._ensure()
        result = []
        for p in self._mt5.positions_get() or []:
            result.append({
                "ticket": p.ticket, "symbol": p.symbol,
                "side": "buy" if p.type == self._mt5.POSITION_TYPE_BUY else "sell",
                "volume": int(p.volume), "price": p.price_open, "stopLoss": p.sl or None,
                "profit": p.profit, "magic": p.magic,
            })
        return result

    def _filling(self, symbol: str) -> int:
        info = self._mt5.symbol_info(symbol)
        mode = info.filling_mode if info else 0
        if mode & 1:
            return self._mt5.ORDER_FILLING_FOK
        if mode & 2:
            return self._mt5.ORDER_FILLING_IOC
        return self._mt5.ORDER_FILLING_RETURN

    def _deal_for_order(self, order_ticket: int, wait_seconds: float = 5.0):
        """Na B3 a execução é na bolsa: a ordem pode voltar 'colocada' e o negócio sair logo depois."""
        import time

        deadline = time.monotonic() + wait_seconds
        while time.monotonic() < deadline:
            deals = self._mt5.history_deals_get(datetime.now(timezone.utc) - timedelta(days=1), datetime.now(timezone.utc) + timedelta(days=1)) or []
            for deal in deals:
                if deal.order == order_ticket:
                    return deal
            time.sleep(0.25)
        return None

    def send_order(self, order) -> dict:
        from .orders import MAGIC

        self._ensure()
        self._select(order.symbol)
        tick = self._mt5.symbol_info_tick(order.symbol)
        if tick is None:
            raise GatewayError(f"Sem cotação de {order.symbol}; ordem não enviada.", 409)
        is_buy = order.side == "buy"
        request = {
            "action": self._mt5.TRADE_ACTION_DEAL,
            "symbol": order.symbol,
            "volume": float(order.volume),
            "type": self._mt5.ORDER_TYPE_BUY if is_buy else self._mt5.ORDER_TYPE_SELL,
            "price": tick.ask if is_buy else tick.bid,
            "deviation": 20,
            "magic": MAGIC,
            "comment": order.comment,
            "type_time": self._mt5.ORDER_TIME_DAY,
            "type_filling": self._filling(order.symbol),
        }
        if order.stop_loss:
            request["sl"] = float(order.stop_loss)
        if order.take_profit:
            request["tp"] = float(order.take_profit)
        if order.position_ticket:
            request["position"] = int(order.position_ticket)

        result = self._mt5.order_send(request)
        if result is None:
            raise GatewayError(f"MT5 não aceitou a ordem: {self._mt5.last_error()}", 502)
        done = result.retcode in (self._mt5.TRADE_RETCODE_DONE, self._mt5.TRADE_RETCODE_DONE_PARTIAL)
        placed = result.retcode == self._mt5.TRADE_RETCODE_PLACED
        if not (done or placed):
            return {"filled": False, "retcode": result.retcode, "message": f"Ordem recusada ({result.retcode}): {result.comment}"}

        deal = self._deal_for_order(result.order)
        if deal is None:
            return {"filled": False, "retcode": result.retcode, "order": result.order,
                    "message": "Ordem enviada, mas o negócio não foi confirmado em 5 s. Confira no MT5."}
        message = "Executada."
        stop_confirmed = None
        if order.stop_loss and not order.position_ticket:
            stop_confirmed = self._ensure_protection(order.symbol, deal.position_id, order.stop_loss, order.take_profit)
            if not stop_confirmed:
                message = "Executada, mas o stop NÃO ficou registrado na corretora. Só o stop do robô protege a posição."
        return {"filled": True, "retcode": result.retcode, "order": result.order, "deal": deal.ticket,
                "ticket": deal.position_id, "price": deal.price, "volume": int(deal.volume), "message": message,
                "stop_confirmed": stop_confirmed}

    def _position(self, ticket: int):
        found = self._mt5.positions_get(ticket=int(ticket)) or []
        return found[0] if found else None

    def _ensure_protection(self, symbol: str, ticket: int, stop_loss: float, take_profit: float | None) -> bool:
        """Na B3 o stop junto da ordem pode não ser registrado. Confere na posição; se faltar, registra à parte uma vez."""
        position = self._position(ticket)
        if position is not None and position.sl:
            return True
        result = self._mt5.order_send({
            "action": self._mt5.TRADE_ACTION_SLTP, "symbol": symbol, "position": int(ticket),
            "sl": float(stop_loss), "tp": float(take_profit or 0.0),
        })
        if result is None or result.retcode != self._mt5.TRADE_RETCODE_DONE:
            return False
        position = self._position(ticket)
        return position is not None and bool(position.sl)

    def set_stop(self, symbol: str, ticket: int, stop_loss: float) -> dict:
        self._ensure()
        # Mantém o alvo que já estiver na posição: mandar tp=0 apagaria o take profit.
        current = self._position(ticket)
        result = self._mt5.order_send({
            "action": self._mt5.TRADE_ACTION_SLTP, "symbol": symbol, "position": int(ticket), "sl": float(stop_loss),
            "tp": float(current.tp) if current is not None and current.tp else 0.0,
        })
        ok = result is not None and result.retcode == self._mt5.TRADE_RETCODE_DONE
        return {"ok": ok, "message": "Stop atualizado." if ok else f"Stop não atualizado: {result.comment if result else self._mt5.last_error()}"}

    def closing_price(self, ticket: int) -> float | None:
        self._ensure()
        deals = self._mt5.history_deals_get(position=int(ticket)) or []
        exits = [d for d in deals if d.entry in (self._mt5.DEAL_ENTRY_OUT, self._mt5.DEAL_ENTRY_INOUT)]
        return exits[-1].price if exits else None


class FakeBackend:
    """
    Mercado simulado, determinístico e sempre aberto, para testar o robô sem o MetaTrader 5.
    Os preços oscilam em ondas, o que faz as estratégias gerarem sinais.
    """

    BASE_PRICES = {"WIN": 130_000.0, "WDO": 5_500.0, "IND": 130_000.0, "DOL": 5_500.0, "BOVA11": 120.0}

    def __init__(self, clock=lambda: datetime.now(timezone.utc), price_override=None, drop_stops: bool = False) -> None:
        # drop_stops simula a corretora que ignora o stop enviado junto com a ordem.
        self.drop_stops = drop_stops
        self._clock = clock
        # Para testes: função (símbolo) -> preço, no lugar das ondas sintéticas.
        self._price_override = price_override
        self._positions: dict[str, dict] = {}
        self._closed: dict[int, float] = {}
        self._next_ticket = 1000

    @staticmethod
    def _root(symbol: str) -> str:
        return symbol[:3] if symbol[:3] in FUTURE_ROOTS else symbol

    def _price(self, symbol: str, minute_index: float) -> float:
        root = self._root(symbol)
        seed = int(hashlib.sha256(root.encode()).hexdigest()[:8], 16)
        base = self.BASE_PRICES.get(root, 20.0 + seed % 60)
        phase = (seed % 1000) / 100
        wave = 0.03 * math.sin(minute_index / 900 + phase) + 0.01 * math.sin(minute_index / 97 + phase * 2)
        noise = (int(hashlib.sha256(f"{root}:{int(minute_index)}".encode()).hexdigest()[:6], 16) / 0xFFFFFF - 0.5) * 0.002
        price = base * math.exp(wave + noise)
        step = 5.0 if root in ("WIN", "IND") else 0.5 if root in ("WDO", "DOL") else 0.01
        return round(round(price / step) * step, 2)

    def health(self) -> dict:
        return {"connected": True, "mode": "fake", "server": "Simulação", "login": 0, "isDemo": True,
                "company": "Robô B3 (dados sintéticos)", "message": "Modo de simulação: preços sintéticos."}

    def resolve(self, code: str) -> str:
        code = code.upper()
        if code not in FUTURE_ROOTS:
            return code
        now = self._clock()
        months = "FGHJKMNQUVXZ"
        contracts = []
        for offset in range(0, 13):
            month = (now.month - 1 + offset) % 12 + 1
            year = now.year + (now.month - 1 + offset) // 12
            # Índice (WIN/IND) só tem vencimentos em meses pares; dólar (WDO/DOL), todo mês.
            if code in ("WIN", "IND") and month % 2 == 1:
                continue
            contracts.append(ContractInfo(f"{code}{months[month - 1]}{year % 100:02d}", 0))
        return resolve_current_contract(code, contracts, now) or contracts[0].name

    def rates(self, symbol: str, timeframe: str, count: int) -> list[dict]:
        _validate(timeframe, count)
        minutes = TIMEFRAME_MINUTES[timeframe]
        now = self._clock()
        # Como no MT5 da B3, a barra diária começa à meia-noite de Brasília (03:00 UTC).
        epoch = datetime(2020, 1, 1, 3 if timeframe == "D1" else 0, tzinfo=timezone.utc)
        current = int((now - epoch).total_seconds() // 60 // minutes)
        bars = []
        for index in range(current - count + 1, current + 1):
            start = epoch + timedelta(minutes=index * minutes)
            m0 = (start - epoch).total_seconds() / 60
            last_minute = min(m0 + minutes, (now - epoch).total_seconds() / 60)
            points = [self._price(symbol, m0 + k * max(1, minutes // 4)) for k in range(4)] + [self._price(symbol, last_minute)]
            bars.append({
                "time": start.isoformat(), "open": points[0], "high": max(points), "low": min(points),
                "close": points[-1], "volume": 1000 + int(abs(points[-1] - points[0]) * 10),
            })
        return bars

    def history(self, root: str, timeframe: str, count: int) -> tuple[str, list[dict]]:
        symbol = f"{root.upper()}$N" if root.upper() in FUTURE_ROOTS else root.upper()
        return symbol, self.rates(symbol, timeframe, count)

    def _last(self, symbol: str) -> float:
        if self._price_override is not None:
            return float(self._price_override(symbol))
        minute = (self._clock() - datetime(2020, 1, 1, tzinfo=timezone.utc)).total_seconds() / 60
        return self._price(symbol, minute)

    def tick(self, symbol: str) -> dict:
        now = self._clock()
        self._check_stops()
        last = self._last(symbol)
        spread = 5.0 if self._root(symbol) in ("WIN", "IND") else 0.5 if self._root(symbol) in ("WDO", "DOL") else 0.01
        return {"symbol": symbol, "bid": last - spread, "ask": last + spread, "last": last, "time": now.isoformat()}


    # ---------- Ordens simuladas (conta demo fictícia, compensação por símbolo) ----------

    def account(self) -> dict:
        return {"login": 0, "isDemo": True, "balance": 100_000.0, "equity": 100_000.0, "currency": "BRL"}

    def _spread(self, symbol: str) -> float:
        root = self._root(symbol)
        return 5.0 if root in ("WIN", "IND") else 0.5 if root in ("WDO", "DOL") else 0.01

    def _check_stops(self) -> None:
        """Simula o stop executado pela corretora."""
        for symbol, position in list(self._positions.items()):
            last = self._last(symbol)
            is_buy = position["side"] == "buy"
            stop, target = position.get("stopLoss"), position.get("takeProfit")
            if stop and (last <= stop if is_buy else last >= stop):
                self._closed[position["ticket"]] = stop
                del self._positions[symbol]
            elif target and (last >= target if is_buy else last <= target):
                self._closed[position["ticket"]] = target
                del self._positions[symbol]

    def positions(self) -> list[dict]:
        from .orders import MAGIC

        self._check_stops()
        return [{**{k: v for k, v in p.items() if k != "takeProfit"}, "profit": 0.0, "magic": MAGIC} for p in self._positions.values()]

    def send_order(self, order) -> dict:
        self._check_stops()
        last = self._last(order.symbol)
        price = last + self._spread(order.symbol) if order.side == "buy" else last - self._spread(order.symbol)
        current = self._positions.get(order.symbol)
        if current is None:
            self._next_ticket += 1
            self._positions[order.symbol] = {"ticket": self._next_ticket, "symbol": order.symbol, "side": order.side,
                                             "volume": order.volume, "price": price,
                                             "stopLoss": None if self.drop_stops else order.stop_loss,
                                             "takeProfit": order.take_profit}
            ticket = self._next_ticket
        elif current["side"] == order.side:
            current["volume"] += order.volume
            ticket = current["ticket"]
        else:
            ticket = current["ticket"]
            remaining = current["volume"] - order.volume
            if remaining > 0:
                current["volume"] = remaining
            else:
                self._closed[ticket] = price
                del self._positions[order.symbol]
                if remaining < 0:
                    self._next_ticket += 1
                    self._positions[order.symbol] = {"ticket": self._next_ticket, "symbol": order.symbol, "side": order.side,
                                                     "volume": -remaining, "price": price, "stopLoss": order.stop_loss}
                    ticket = self._next_ticket
        self._next_ticket += 1
        stop_confirmed = None
        if order.stop_loss and not order.position_ticket and order.symbol in self._positions:
            stop_confirmed = bool(self._positions[order.symbol].get("stopLoss"))
        return {"filled": True, "retcode": 10009, "order": self._next_ticket, "deal": self._next_ticket,
                "ticket": ticket, "price": round(price, 2), "volume": order.volume, "message": "Executada (simulação).",
                "stop_confirmed": stop_confirmed}

    def set_stop(self, symbol: str, ticket: int, stop_loss: float) -> dict:
        position = self._positions.get(symbol)
        if position is None or position["ticket"] != ticket:
            return {"ok": False, "message": "Posição não encontrada."}
        position["stopLoss"] = stop_loss
        return {"ok": True, "message": "Stop atualizado (simulação)."}

    def closing_price(self, ticket: int) -> float | None:
        return self._closed.get(ticket)


def create_backend(settings: Settings) -> Backend:
    return FakeBackend() if settings.fake else Mt5Backend(settings)
