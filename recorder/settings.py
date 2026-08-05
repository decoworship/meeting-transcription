"""Configuracoes do gravador, persistidas entre execucoes.

Fica ao lado do ambiente (``%USERPROFILE%\\.meeting-recorder``) e nao dentro do
repositorio: e estado da maquina, nao do projeto.
"""

from __future__ import annotations

import json
import logging
import os
from pathlib import Path

logger = logging.getLogger(__name__)

SETTINGS_PATH = Path.home() / ".meeting-recorder" / "settings.json"

DEFAULTS = {
    # None = usar o dispositivo padrao do Windows no momento da gravacao.
    "mic_index": None,
    "loopback_index": None,
    "output_dir": None,      # None = pasta padrao (ver default_output_dir)
    "start_muted": False,
}


def default_output_dir() -> str:
    r"""Onde as gravacoes caem por padrao.

    Aponta para ``data/recordings`` do repositorio via \\wsl$, que e a pasta
    que o container de transcricao enxerga. Se o WSL nao estiver acessivel,
    cai para Documentos.
    """
    wsl = Path(r"\\wsl$\Ubuntu\home\andre\projects\meeting-transcription\data\recordings")
    try:
        if wsl.parent.exists():
            return str(wsl)
    except OSError:
        pass
    return str(Path.home() / "Documents" / "MeetingRecordings")


def load() -> dict:
    data = dict(DEFAULTS)
    data["output_dir"] = default_output_dir()
    if SETTINGS_PATH.is_file():
        try:
            saved = json.loads(SETTINGS_PATH.read_text(encoding="utf-8"))
            data.update({k: v for k, v in saved.items() if k in DEFAULTS})
        except (OSError, json.JSONDecodeError) as e:
            logger.warning(f"settings.json ilegivel, usando padroes: {e}")
    return data


def save(data: dict) -> None:
    SETTINGS_PATH.parent.mkdir(parents=True, exist_ok=True)
    payload = {k: v for k, v in data.items() if k in DEFAULTS}
    try:
        # Escrita atomica: um Ctrl+C no meio nao deixa o arquivo corrompido.
        tmp = SETTINGS_PATH.with_suffix(".tmp")
        tmp.write_text(json.dumps(payload, indent=2), encoding="utf-8")
        os.replace(tmp, SETTINGS_PATH)
    except OSError as e:
        logger.warning(f"nao foi possivel salvar settings.json: {e}")
