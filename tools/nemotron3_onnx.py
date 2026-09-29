"""Atalho para o porte do Nemotron-3, que mora no sidecar desde 29/09/2026.

As ferramentas de medição (``medir_nemotron3.py``, ``medir_mod1.py``,
``medir_mod2.py``) importam daqui; o código é o de
``motores/diarizacao/pipeline/nemotron3.py``, o mesmo que o app roda.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "motores" / "diarizacao" / "pipeline"))

from nemotron3 import *  # noqa: E402,F401,F403
from nemotron3 import CacheDeFalantes, Nemotron3, MODOS, SUB, NSPK, HOP, mel, sigmoid, segmentos  # noqa: E402,F401
