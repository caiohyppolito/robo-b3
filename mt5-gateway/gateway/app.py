"""API HTTP do conector. Ordens só saem com ROBOB3_ORDERS ligado e a conta do tipo certo (ver orders.py)."""

from __future__ import annotations

import hmac

from fastapi import Depends, FastAPI, Header, HTTPException, Query
from fastapi.responses import JSONResponse

from .backends import Backend, GatewayError, create_backend
from .config import Settings
from .orders import IdempotencyCache, OrderIn, check_order


def create_app(settings: Settings, backend: Backend | None = None) -> FastAPI:
    backend = backend or create_backend(settings)
    sent = IdempotencyCache()
    app = FastAPI(title="Robô B3 - conector MT5", docs_url=None, redoc_url=None, openapi_url=None)

    def authorize(x_gateway_token: str = Header(default="")) -> None:
        if not hmac.compare_digest(x_gateway_token.encode(), settings.token.encode()):
            raise HTTPException(status_code=401, detail="Token inválido.")

    @app.exception_handler(GatewayError)
    async def gateway_error(_, error: GatewayError) -> JSONResponse:
        return JSONResponse(status_code=error.status, content={"detail": str(error)})

    @app.get("/health", dependencies=[Depends(authorize)])
    def health() -> dict:
        return backend.health()

    @app.get("/symbols/{code}/resolve", dependencies=[Depends(authorize)])
    def resolve(code: str) -> dict:
        return {"symbol": backend.resolve(code)}

    @app.get("/rates/{symbol}", dependencies=[Depends(authorize)])
    def rates(symbol: str, timeframe: str = Query("M5"), count: int = Query(400)) -> dict:
        return {"symbol": symbol.upper(), "bars": backend.rates(symbol.upper(), timeframe.upper(), count)}

    @app.get("/history/{root}", dependencies=[Depends(authorize)])
    def history(root: str, timeframe: str = Query("M5"), count: int = Query(5000)) -> dict:
        symbol, bars = backend.history(root.upper(), timeframe.upper(), count)
        return {"symbol": symbol, "bars": bars}

    @app.get("/tick/{symbol}", dependencies=[Depends(authorize)])
    def tick(symbol: str) -> dict:
        return backend.tick(symbol.upper())

    # ---------- Ordens (Fase 5): desligadas por padrão; ver ROBOB3_ORDERS ----------

    @app.get("/account", dependencies=[Depends(authorize)])
    def account() -> dict:
        return backend.account()

    @app.get("/positions", dependencies=[Depends(authorize)])
    def positions() -> list[dict]:
        return backend.positions()

    @app.post("/orders", dependencies=[Depends(authorize)])
    def send_order(order: OrderIn) -> dict:
        previous = sent.get(order.client_id)
        if previous is not None:
            return {**previous, "repeated": True}
        check_order(settings, backend.account().get("isDemo"), order)
        result = backend.send_order(order.model_copy(update={"symbol": order.symbol.upper()}))
        sent.put(order.client_id, result)
        return result

    @app.post("/positions/{ticket}/stop", dependencies=[Depends(authorize)])
    def set_stop(ticket: int, symbol: str = Query(...), stop_loss: float = Query(..., gt=0)) -> dict:
        if settings.orders == "off":
            raise GatewayError("Envio de ordens desligado no conector (defina ROBOB3_ORDERS).", 403)
        return backend.set_stop(symbol.upper(), ticket, stop_loss)

    @app.get("/positions/{ticket}/closing-price", dependencies=[Depends(authorize)])
    def closing_price(ticket: int) -> dict:
        return {"ticket": ticket, "price": backend.closing_price(ticket)}

    return app
