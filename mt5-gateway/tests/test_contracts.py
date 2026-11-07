from datetime import datetime, timezone

from gateway.contracts import ContractInfo, resolve_current_contract

NOW = datetime(2026, 11, 7, 15, 0, tzinfo=timezone.utc)


def test_escolhe_o_vencimento_mais_proximo_ainda_valido():
    contracts = [
        ContractInfo("WINV26", int(datetime(2026, 10, 14, tzinfo=timezone.utc).timestamp())),
        ContractInfo("WINZ26", int(datetime(2026, 12, 16, tzinfo=timezone.utc).timestamp())),
        ContractInfo("WING27", int(datetime(2027, 2, 17, tzinfo=timezone.utc).timestamp())),
        ContractInfo("WIN$N", 0),
    ]
    assert resolve_current_contract("WIN", contracts, NOW) == "WINZ26"


def test_sem_vencimento_informado_usa_o_codigo_do_mes():
    contracts = [ContractInfo(name, 0) for name in ("WDOX26", "WDOZ26", "WDOF27")]
    # WDO vence no 1º dia útil: em 7/11 o vigente é o de dezembro.
    assert resolve_current_contract("WDO", contracts, NOW) == "WDOZ26"


def test_ignora_simbolos_que_nao_sao_contratos():
    contracts = [ContractInfo("WIN$", 0), ContractInfo("WINZ26C130000", 0)]
    assert resolve_current_contract("WIN", contracts, NOW) is None
