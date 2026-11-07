"""Regras de segurança do envio de ordens. Lógica pura, testável sem MT5."""

from __future__ import annotations

from collections import OrderedDict
from typing import Literal

from pydantic import BaseModel, Field

from .backends import FUTURE_ROOTS, GatewayError
from .config import Settings

# Identifica as ordens do robô na corretora.
MAGIC = 20261107


class OrderIn(BaseModel):
    symbol: str = Field(min_length=3, max_length=30)
    side: Literal["buy", "sell"]
    volume: int = Field(gt=0)
    stop_loss: float | None = Field(default=None, gt=0)
    # Ticket da posição a reduzir/encerrar (opcional).
    position_ticket: int | None = None
    # Identificador único da ordem no robô: repetir a chamada não repete a ordem.
    client_id: str = Field(min_length=8, max_length=64)
    expected_mode: Literal["demo", "real"]
    comment: str = Field(default="robo-b3", max_length=31)


def check_order(settings: Settings, account_is_demo: bool | None, order: OrderIn) -> None:
    """Lança GatewayError se a ordem não puder sair. Cada regra é uma trava independente do robô."""
    if settings.orders == "off":
        raise GatewayError("Envio de ordens desligado no conector (defina ROBOB3_ORDERS).", 403)
    if order.expected_mode != settings.orders:
        raise GatewayError(
            f"O conector está configurado para ordens '{settings.orders}', mas a ordem pediu '{order.expected_mode}'.", 403)
    if account_is_demo is None:
        raise GatewayError("Não foi possível identificar o tipo da conta logada no MT5.", 503)
    actual = "demo" if account_is_demo else "real"
    if actual != order.expected_mode:
        raise GatewayError(f"A conta logada no MT5 é {actual}, mas a ordem é para conta {order.expected_mode}.", 409)

    is_future = order.symbol.upper()[:3] in FUTURE_ROOTS
    cap = settings.max_contracts if is_future else settings.max_shares
    if order.volume > cap:
        unit = "contratos" if is_future else "ações"
        raise GatewayError(f"Volume {order.volume} acima do teto do conector ({cap} {unit} por ordem).", 400)


class IdempotencyCache:
    """Guarda o resultado das últimas ordens por client_id, para uma nova tentativa não enviar outra ordem."""

    def __init__(self, capacity: int = 2000) -> None:
        self._items: OrderedDict[str, dict] = OrderedDict()
        self._capacity = capacity

    def get(self, client_id: str) -> dict | None:
        return self._items.get(client_id)

    def put(self, client_id: str, result: dict) -> None:
        self._items[client_id] = result
        while len(self._items) > self._capacity:
            self._items.popitem(last=False)
