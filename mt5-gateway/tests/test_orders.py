from datetime import datetime, timezone

import pytest
from fastapi.testclient import TestClient

from gateway.app import create_app
from gateway.backends import FakeBackend, GatewayError
from gateway.config import Settings
from gateway.orders import OrderIn, check_order

TOKEN = "token-de-teste-1234567890"
NOW = datetime(2026, 11, 9, 14, 3, tzinfo=timezone.utc)


def order(**changes) -> dict:
    base = {"symbol": "WINZ26", "side": "buy", "volume": 1, "client_id": "pedido-0001", "expected_mode": "demo"}
    return {**base, **changes}


@pytest.fixture
def prices() -> dict:
    return {"WINZ26": 130_000.0, "PETR4": 36.0}


@pytest.fixture
def client(prices) -> TestClient:
    backend = FakeBackend(clock=lambda: NOW, price_override=lambda symbol: prices[symbol])
    app = create_app(Settings(token=TOKEN, fake=True, orders="demo"), backend)
    return TestClient(app, headers={"X-Gateway-Token": TOKEN})


@pytest.mark.parametrize(
    ("settings", "is_demo", "changes", "status", "text"),
    [
        (Settings(token=TOKEN), True, {}, 403, "desligado"),
        (Settings(token=TOKEN, orders="demo"), True, {"expected_mode": "real"}, 403, "configurado para ordens 'demo'"),
        (Settings(token=TOKEN, orders="real"), True, {"expected_mode": "real"}, 409, "conta logada no MT5 é demo"),
        (Settings(token=TOKEN, orders="demo"), False, {}, 409, "conta logada no MT5 é real"),
        (Settings(token=TOKEN, orders="demo"), True, {"volume": 3}, 400, "teto do conector (2 contratos"),
        (Settings(token=TOKEN, orders="demo"), True, {"symbol": "PETR4", "volume": 1100}, 400, "1000 ações"),
        (Settings(token=TOKEN, orders="demo"), None, {}, 503, "tipo da conta"),
    ],
)
def test_travas_do_envio_de_ordens(settings, is_demo, changes, status, text):
    with pytest.raises(GatewayError) as error:
        check_order(settings, is_demo, OrderIn(**order(**changes)))
    assert error.value.status == status
    assert text in str(error.value)


def test_ordem_valida_passa_nas_travas():
    check_order(Settings(token=TOKEN, orders="demo"), True, OrderIn(**order(volume=2)))
    check_order(Settings(token=TOKEN, orders="real"), False, OrderIn(**order(expected_mode="real", symbol="PETR4", volume=500)))


def test_compra_executa_no_ask_com_stop_e_aparece_nas_posicoes(client):
    result = client.post("/orders", json=order(volume=2, stop_loss=129_800)).json()

    assert result["filled"] is True
    assert result["price"] == 130_005.0
    positions = client.get("/positions").json()
    assert positions == [{"ticket": result["ticket"], "symbol": "WINZ26", "side": "buy", "volume": 2, "price": 130_005.0,
                          "stopLoss": 129_800.0, "profit": 0.0, "magic": 20261107}]


def test_repetir_o_mesmo_client_id_nao_repete_a_ordem(client):
    first = client.post("/orders", json=order()).json()
    second = client.post("/orders", json=order()).json()

    assert second["repeated"] is True
    assert second["ticket"] == first["ticket"]
    assert client.get("/positions").json()[0]["volume"] == 1


def test_ordem_contraria_encerra_e_informa_preco_de_saida(client, prices):
    opened = client.post("/orders", json=order()).json()
    prices["WINZ26"] = 130_100.0
    closed = client.post("/orders", json=order(side="sell", client_id="pedido-0002", position_ticket=opened["ticket"])).json()

    assert closed["price"] == 130_095.0
    assert client.get("/positions").json() == []
    assert client.get(f"/positions/{opened['ticket']}/closing-price").json()["price"] == 130_095.0


def test_stop_da_corretora_encerra_a_posicao(client, prices):
    opened = client.post("/orders", json=order(stop_loss=129_900)).json()
    assert client.post(f"/positions/{opened['ticket']}/stop", params={"symbol": "WINZ26", "stop_loss": 129_950}).json()["ok"]

    prices["WINZ26"] = 129_940.0
    assert client.get("/positions").json() == []
    assert client.get(f"/positions/{opened['ticket']}/closing-price").json()["price"] == 129_950.0


def test_conta_informa_tipo_e_saldo(client):
    account = client.get("/account").json()
    assert account["isDemo"] is True
    assert account["equity"] > 0
