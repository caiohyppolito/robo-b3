from datetime import datetime, timedelta, timezone

import pytest
from fastapi.testclient import TestClient

from gateway.app import create_app
from gateway.backends import FakeBackend
from gateway.config import Settings

TOKEN = "token-de-teste-1234567890"
NOW = datetime(2026, 11, 9, 14, 3, tzinfo=timezone.utc)


@pytest.fixture
def client() -> TestClient:
    app = create_app(Settings(token=TOKEN, fake=True), FakeBackend(clock=lambda: NOW))
    return TestClient(app, headers={"X-Gateway-Token": TOKEN})


def test_sem_token_e_recusado():
    app = create_app(Settings(token=TOKEN, fake=True), FakeBackend(clock=lambda: NOW))
    assert TestClient(app).get("/health").status_code == 401
    assert TestClient(app, headers={"X-Gateway-Token": "errado"}).get("/health").status_code == 401


def test_nao_existe_rota_de_ordens(client):
    for path in ("/order", "/orders", "/trade", "/positions"):
        assert client.post(path, json={}).status_code in (404, 405)


def test_saude_no_modo_simulacao(client):
    body = client.get("/health").json()
    assert body["connected"] is True
    assert body["mode"] == "fake"
    assert body["isDemo"] is True


def test_resolve_contrato_vigente_e_acoes(client):
    assert client.get("/symbols/WIN/resolve").json()["symbol"] == "WINZ26"
    assert client.get("/symbols/petr4/resolve").json()["symbol"] == "PETR4"


def test_barras_sao_consistentes_entre_chamadas_e_terminam_na_barra_atual(client):
    first = client.get("/rates/WINZ26", params={"timeframe": "M5", "count": 50}).json()["bars"]
    second = client.get("/rates/WINZ26", params={"timeframe": "M5", "count": 20}).json()["bars"]
    assert len(first) == 50
    assert first[-20:] == second
    last = datetime.fromisoformat(first[-1]["time"])
    assert last <= NOW < last + timedelta(minutes=5)
    assert all(b["low"] <= min(b["open"], b["close"]) and b["high"] >= max(b["open"], b["close"]) for b in first)


def test_historico_de_futuro_usa_serie_continua(client):
    body = client.get("/history/WDO", params={"timeframe": "M15", "count": 10}).json()
    assert body["symbol"] == "WDO$N"
    assert len(body["bars"]) == 10


def test_cotacao_tem_spread_e_horario(client):
    tick = client.get("/tick/WINZ26").json()
    assert tick["bid"] < tick["last"] < tick["ask"]
    assert datetime.fromisoformat(tick["time"]) == NOW


def test_parametros_invalidos_viram_erro_claro(client):
    response = client.get("/rates/PETR4", params={"timeframe": "M2", "count": 10})
    assert response.status_code == 400
    assert "Tempo gráfico inválido" in response.json()["detail"]
