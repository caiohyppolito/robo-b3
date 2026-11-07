"""Uso: python -m gateway  (variáveis de ambiente no README)."""

import uvicorn

from .app import create_app
from .config import Settings

if __name__ == "__main__":
    settings = Settings.from_env()
    uvicorn.run(create_app(settings), host=settings.host, port=settings.port, log_level="info")
