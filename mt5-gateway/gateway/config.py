"""Configuração por variáveis de ambiente (nunca por arquivo versionado)."""

from __future__ import annotations

import os
from dataclasses import dataclass, field


@dataclass(frozen=True)
class Settings:
    token: str
    host: str = "127.0.0.1"
    port: int = 8765
    fake: bool = False
    # Horário do servidor da corretora em relação ao UTC (B3/Clear: -3).
    server_utc_offset_hours: int = -3
    # Séries contínuas de futuros, em ordem de preferência ({root} vira WIN, WDO...).
    continuous_patterns: tuple[str, ...] = field(default=("{root}$N", "{root}$"))

    @staticmethod
    def from_env() -> "Settings":
        token = os.environ.get("ROBOB3_GATEWAY_TOKEN", "").strip()
        if len(token) < 16:
            raise SystemExit("Defina ROBOB3_GATEWAY_TOKEN com pelo menos 16 caracteres (o mesmo de Mt5Gateway:Token na API).")

        host = os.environ.get("ROBOB3_GATEWAY_HOST", "127.0.0.1")
        if host not in ("127.0.0.1", "localhost", "::1"):
            raise SystemExit("Por segurança, o conector só escuta em localhost.")

        patterns = os.environ.get("ROBOB3_CONTINUOUS_PATTERNS")
        return Settings(
            token=token,
            host=host,
            port=int(os.environ.get("ROBOB3_GATEWAY_PORT", "8765")),
            fake=os.environ.get("ROBOB3_GATEWAY_FAKE", "").lower() in ("1", "true", "sim"),
            server_utc_offset_hours=int(os.environ.get("ROBOB3_SERVER_UTC_OFFSET", "-3")),
            continuous_patterns=tuple(p.strip() for p in patterns.split(",")) if patterns else ("{root}$N", "{root}$"),
        )
