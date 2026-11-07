"""Caminho de ordens do Mt5Backend com um MetaTrader5 simulado (a biblioteca real só existe no Windows)."""

import sys
import types
from types import SimpleNamespace as NS

import pytest

from gateway.backends import Mt5Backend
from gateway.config import Settings
from gateway.orders import OrderIn


class FakeMt5(types.ModuleType):
    TRADE_ACTION_DEAL, TRADE_ACTION_SLTP = 1, 6
    ORDER_TYPE_BUY, ORDER_TYPE_SELL = 0, 1
    ORDER_TIME_DAY = 1
    ORDER_FILLING_FOK, ORDER_FILLING_IOC, ORDER_FILLING_RETURN = 0, 1, 2
    TRADE_RETCODE_DONE, TRADE_RETCODE_DONE_PARTIAL, TRADE_RETCODE_PLACED = 10009, 10010, 10008
    TIMEFRAME_M1, TIMEFRAME_M5, TIMEFRAME_M15, TIMEFRAME_H1, TIMEFRAME_D1 = 1, 5, 15, 16385, 16408

    def __init__(self, keeps_sl_on_deal: bool, accepts_sltp: bool = True) -> None:
        super().__init__("MetaTrader5")
        self.keeps_sl_on_deal = keeps_sl_on_deal
        self.accepts_sltp = accepts_sltp
        self.requests: list[dict] = []
        self.position = None

    def terminal_info(self):
        return NS()

    def symbol_select(self, symbol, enable):
        return True

    def symbol_info(self, symbol):
        return NS(filling_mode=0)

    def symbol_info_tick(self, symbol):
        return NS(bid=130_000.0, ask=130_005.0)

    def order_send(self, request):
        self.requests.append(request)
        if request["action"] == self.TRADE_ACTION_DEAL:
            sl = request.get("sl", 0.0) if self.keeps_sl_on_deal else 0.0
            self.position = NS(ticket=77, sl=sl, tp=request.get("tp", 0.0))
            return NS(retcode=self.TRADE_RETCODE_DONE, order=900, comment="ok")
        if not self.accepts_sltp:
            return NS(retcode=10016, order=0, comment="Invalid stops")
        self.position.sl, self.position.tp = request["sl"], request["tp"]
        return NS(retcode=self.TRADE_RETCODE_DONE, order=0, comment="ok")

    def history_deals_get(self, start, end):
        return [NS(order=900, ticket=901, position_id=77, price=130_005.0, volume=1.0)]

    def positions_get(self, ticket=None):
        return [self.position] if self.position is not None else []


def backend(monkeypatch, fake: FakeMt5) -> Mt5Backend:
    monkeypatch.setitem(sys.modules, "MetaTrader5", fake)
    return Mt5Backend(Settings(token="t" * 20))


def entry(**changes) -> OrderIn:
    base = {"symbol": "WINZ26", "side": "buy", "volume": 1, "client_id": "pedido-0001", "expected_mode": "demo",
            "stop_loss": 129_800, "take_profit": 130_400}
    return OrderIn(**{**base, **changes})


def test_stop_aceito_junto_da_ordem_nao_gera_segundo_envio(monkeypatch):
    fake = FakeMt5(keeps_sl_on_deal=True)

    result = backend(monkeypatch, fake).send_order(entry())

    assert result["stop_confirmed"] is True
    assert [r["action"] for r in fake.requests] == [fake.TRADE_ACTION_DEAL]
    assert fake.requests[0]["type_filling"] == fake.ORDER_FILLING_RETURN


def test_stop_ignorado_na_ordem_e_registrado_a_parte_com_o_alvo(monkeypatch):
    fake = FakeMt5(keeps_sl_on_deal=False)

    result = backend(monkeypatch, fake).send_order(entry())

    assert result["stop_confirmed"] is True
    sltp = fake.requests[-1]
    assert sltp["action"] == fake.TRADE_ACTION_SLTP and sltp["position"] == 77
    assert (sltp["sl"], sltp["tp"]) == (129_800.0, 130_400.0)


def test_stop_recusado_nas_duas_tentativas_e_informado(monkeypatch):
    fake = FakeMt5(keeps_sl_on_deal=False, accepts_sltp=False)

    result = backend(monkeypatch, fake).send_order(entry())

    assert result["filled"] is True
    assert result["stop_confirmed"] is False
    assert "NÃO ficou registrado" in result["message"]
    assert len(fake.requests) == 2


def test_stop_movel_preserva_o_alvo(monkeypatch):
    fake = FakeMt5(keeps_sl_on_deal=True)
    mt5 = backend(monkeypatch, fake)
    mt5.send_order(entry())

    mt5.set_stop("WINZ26", 77, 129_900)

    assert fake.requests[-1]["tp"] == 130_400.0
    assert fake.position.sl == 129_900
