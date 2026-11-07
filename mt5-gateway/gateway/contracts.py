"""Escolha do contrato futuro vigente (ex.: WIN -> WINZ26). Lógica pura, testável sem MT5."""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import date, datetime, timezone

MONTH_CODES = {"F": 1, "G": 2, "H": 3, "J": 4, "K": 5, "M": 6, "N": 7, "Q": 8, "U": 9, "V": 10, "X": 11, "Z": 12}


@dataclass(frozen=True)
class ContractInfo:
    name: str
    expiration: int  # epoch em segundos; 0 quando a corretora não informa


def _approximate_expiry(root: str, name: str) -> date | None:
    match = re.fullmatch(rf"{re.escape(root)}([FGHJKMNQUVXZ])(\d{{2}})", name)
    if not match:
        return None
    month = MONTH_CODES[match.group(1)]
    year = 2000 + int(match.group(2))
    # WDO vence no 1º dia útil do mês; WIN e demais, por volta do dia 15.
    return date(year, month, 1 if root == "WDO" else 15)


def resolve_current_contract(root: str, contracts: list[ContractInfo], now: datetime | None = None) -> str | None:
    """Contrato com o vencimento mais próximo que ainda não passou."""
    now = now or datetime.now(timezone.utc)
    candidates: list[tuple[float, str]] = []
    for contract in contracts:
        approx = _approximate_expiry(root, contract.name)
        if approx is None:
            continue  # séries contínuas (WIN$N), opções e outros símbolos
        expiry = (
            datetime.fromtimestamp(contract.expiration, tz=timezone.utc)
            if contract.expiration > 0
            else datetime(approx.year, approx.month, approx.day, 21, tzinfo=timezone.utc)
        )
        if expiry >= now:
            candidates.append((expiry.timestamp(), contract.name))
    return min(candidates)[1] if candidates else None
