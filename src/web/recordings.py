"""Consumo das gravações de duas faixas produzidas pelo ``recorder/``.

O gravador do Windows entrega, por sessão::

    <pasta>/system.wav   16 kHz mono — os outros participantes (loopback)
    <pasta>/mic.wav      16 kHz mono — você
    <pasta>/meta.json    duração, dispositivos, campos da reunião

Ter as faixas separadas resolve metade do problema de diarização de graça:
qualquer trecho com energia no microfone é você, com certeza. O pyannote passa
a lidar só com o ``system.wav``, onde ele de fato precisa separar pessoas.

O fluxo é:

1. ``mix_tracks`` soma as faixas num arquivo só, para a transcrição enxergar a
   conversa inteira (inclusive sobreposições) como sempre enxergou;
2. a diarização roda apenas no ``system.wav``;
3. ``assign_owner`` decide, segmento a segmento, se quem falou foi você ou um
   dos falantes que o pyannote encontrou.
"""

from __future__ import annotations

import json
import os
import logging
import wave
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

import numpy as np

logger = logging.getLogger(__name__)

# Onde o docker-compose monta as gravações do gravador do Windows. Sobrescrever
# com RECORDINGS_DIR permite rodar o app fora do container, onde esse caminho
# não existe (ou não é legível).
RECORDINGS_DIR = Path(os.environ.get("RECORDINGS_DIR", "/root/recordings"))

SAMPLE_RATE = 16000
# Quanto o microfone precisa superar o áudio do sistema para o segmento ser
# considerado seu. 2.0 (~6 dB) tolera o vazamento acústico de quem usa caixas
# de som em vez de fone, sem exigir isolamento perfeito.
OWNER_MARGIN = 2.0
# Abaixo disso o microfone é ruído de fundo, não fala.
OWNER_MIN_RMS = 5e-3


@dataclass
class Recording:
    """Uma sessão gravada, com as duas faixas."""
    path: Path
    mic: Path
    system: Path
    meta: dict

    @property
    def name(self) -> str:
        return self.path.name

    @property
    def duration_s(self) -> float:
        return float(self.meta.get("duration_s", 0.0))

    @property
    def has_both_tracks(self) -> bool:
        return self.mic.is_file() and self.system.is_file()

    def warnings(self) -> list[str]:
        """Problemas visíveis nos metadados, do mais grave ao menos.

        Um booleano "nunca teve áudio" não basta: uma gravação de 36 min saiu
        95% muda depois de um início saudável, e o meta.json a declarou boa.
        """
        out = []
        for nome, t in (self.meta.get("tracks") or {}).items():
            if t.get("no_audio"):
                out.append(f"{nome} sem audio")
                continue
            util = t.get("usable_pct")
            if util is not None and util < 50:
                out.append(f"{nome} so {util:.0f}% util")
            mudo = t.get("muted_s") or 0
            if self.duration_s and mudo > 0.25 * self.duration_s:
                out.append(f"{nome} mudo {mudo / 60:.0f}min")
        return out

    def label(self) -> str:
        """Rótulo para o seletor da UI."""
        mins, secs = divmod(int(self.duration_s), 60)
        parts = [self.name, f"{mins:02d}:{secs:02d}"]
        title = (self.meta.get("meeting") or {}).get("title")
        if title:
            parts.append(title)
        avisos = self.warnings()
        if avisos:
            parts.append("ATENCAO: " + ", ".join(avisos))
        return "  |  ".join(parts)


def list_recordings(base: Path = RECORDINGS_DIR) -> list[Recording]:
    """Gravações disponíveis, mais recentes primeiro.

    Nunca levanta: a pasta pode não existir ou não ser legível (o app roda tanto
    dentro do container, onde ela é montada, quanto direto do fonte, onde não).
    Sem gravações a UI simplesmente mostra a lista vazia.
    """
    try:
        entradas = sorted(base.iterdir(), reverse=True)
    except OSError as e:
        logger.debug(f"pasta de gravações indisponível ({base}): {e}")
        return []

    out = []
    for d in entradas:
        if not d.is_dir():
            continue
        meta_path = d / "meta.json"
        meta = {}
        if meta_path.is_file():
            try:
                meta = json.loads(meta_path.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError) as e:
                logger.warning(f"meta.json ilegível em {d}: {e}")
        rec = Recording(path=d, mic=d / "mic.wav", system=d / "system.wav", meta=meta)
        if rec.has_both_tracks:
            out.append(rec)
    return out


def find(name: str, base: Path = RECORDINGS_DIR) -> Optional[Recording]:
    """Localiza uma gravação pelo nome da pasta (o que o seletor da UI envia)."""
    if not name:
        return None
    # O rótulo do seletor carrega mais coisa além do nome; o nome vem primeiro.
    key = name.split("  |  ")[0].strip()
    return next((r for r in list_recordings(base) if r.name == key), None)


def _read_wav(path: Path) -> np.ndarray:
    with wave.open(str(path), "rb") as w:
        if w.getframerate() != SAMPLE_RATE or w.getnchannels() != 1:
            raise ValueError(
                f"{path.name}: esperado 16 kHz mono, veio "
                f"{w.getframerate()} Hz {w.getnchannels()}ch"
            )
        raw = w.readframes(w.getnframes())
    return np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0


def mix_tracks(rec: Recording, out_path: Path) -> Path:
    """Soma as duas faixas num WAV só, para a transcrição.

    As faixas já saem alinhadas do gravador (ancoradas no relógio de parede),
    então basta somar. A soma é reduzida se estourar, em vez de normalizada,
    para não alterar o equilíbrio relativo entre os canais -- é ele que o
    ``assign_owner`` usa depois.
    """
    mic, sys_ = _read_wav(rec.mic), _read_wav(rec.system)
    n = max(mic.size, sys_.size)
    mic = np.pad(mic, (0, n - mic.size))
    sys_ = np.pad(sys_, (0, n - sys_.size))

    mixed = mic + sys_
    peak = float(np.abs(mixed).max()) if mixed.size else 0.0
    if peak > 1.0:
        mixed /= peak
        logger.info(f"mix reduzido por {peak:.2f}x para evitar clipping")

    out_path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(out_path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SAMPLE_RATE)
        w.writeframes((mixed * 32767).astype(np.int16).tobytes())
    logger.info(f"mix gerado: {out_path} ({n / SAMPLE_RATE:.1f}s)")
    return out_path


def _segment_rms(audio: np.ndarray, start: float, end: float) -> float:
    a = int(max(0, start) * SAMPLE_RATE)
    b = int(min(end, audio.size / SAMPLE_RATE) * SAMPLE_RATE)
    if b <= a:
        return 0.0
    seg = audio[a:b]
    return float(np.sqrt(np.mean(seg ** 2))) if seg.size else 0.0


def assign_owner(result, rec: Recording, user_label: str = "You") -> tuple[int, int]:
    """Marca como ``user_label`` os segmentos em que o microfone domina.

    Roda DEPOIS da diarização: sobrescreve o palpite do pyannote apenas onde o
    microfone tem energia claramente maior que o áudio do sistema, que é o caso
    em que sabemos a resposta em vez de estimá-la.

    Devolve ``(marcados_como_voce, total_de_segmentos)``.
    """
    mic, sys_ = _read_wav(rec.mic), _read_wav(rec.system)
    meus = 0
    for seg in result.segments:
        r_mic = _segment_rms(mic, seg.start, seg.end)
        r_sys = _segment_rms(sys_, seg.start, seg.end)
        if r_mic >= OWNER_MIN_RMS and r_mic > r_sys * OWNER_MARGIN:
            seg.speaker = user_label
            meus += 1
    logger.info(f"faixa do microfone: {meus}/{len(result.segments)} segmentos "
                f"atribuídos a '{user_label}'")
    return meus, len(result.segments)
