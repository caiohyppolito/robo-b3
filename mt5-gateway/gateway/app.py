"""API HTTP do conector. Só rotas de leitura: não há como enviar ordens por aqui."""

from __future__ import annotations

import hmac

from fastapi import Depends, FastAPI, Header, HTTPException, Query
from fastapi.responses import JSONResponse

from .backends import Backend, GatewayError, create_backend
from .config import Settings


def create_app(settings: Settings, backend: Backend | None = None) -> FastAPI:
    backend = backend or create_backend(settings)
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

    return app
