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


def _validate(timeframe: str, count: int) -> None:
    if timeframe not in TIMEFRAME_MINUTES:
        raise GatewayError(f"Tempo gráfico inválido: {timeframe}. Use {', '.join(TIMEFRAME_MINUTES)}.")
    if not 1 <= count <= 100_000:
        raise GatewayError("Quantidade de barras deve estar entre 1 e 100000.")


class Mt5Backend:
    """Leitura de dados do terminal MetaTrader 5 já logado. Não existe envio de ordens aqui."""

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


class FakeBackend:
    """
    Mercado simulado, determinístico e sempre aberto, para testar o robô sem o MetaTrader 5.
    Os preços oscilam em ondas, o que faz as estratégias gerarem sinais.
    """

    BASE_PRICES = {"WIN": 130_000.0, "WDO": 5_500.0, "IND": 130_000.0, "DOL": 5_500.0, "BOVA11": 120.0}

    def __init__(self, clock=lambda: datetime.now(timezone.utc)) -> None:
        self._clock = clock

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
        epoch = datetime(2020, 1, 1, tzinfo=timezone.utc)
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

    def tick(self, symbol: str) -> dict:
        now = self._clock()
        minute = (now - datetime(2020, 1, 1, tzinfo=timezone.utc)).total_seconds() / 60
        last = self._price(symbol, minute)
        spread = 5.0 if self._root(symbol) in ("WIN", "IND") else 0.5 if self._root(symbol) in ("WDO", "DOL") else 0.01
        return {"symbol": symbol, "bid": last - spread, "ask": last + spread, "last": last, "time": now.isoformat()}


def create_backend(settings: Settings) -> Backend:
    return FakeBackend() if settings.fake else Mt5Backend(settings)
