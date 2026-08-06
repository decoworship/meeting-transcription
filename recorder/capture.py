"""Captura de duas faixas (loopback do sistema + microfone) no Windows via WASAPI.

Sem UI de propósito — a bandeja usa este módulo. Dá para rodar direto para um
teste de captura:

    python capture.py --seconds 30 --out ..\\data\\recordings

Decisões que valem saber:

* **Duas faixas separadas.** Permite redução de ruído independente por canal e,
  no transcritor, tratar o microfone como falante conhecido sem passar pelo
  pyannote.
* **16 kHz mono.** É o que o Whisper consome; gravar direto nesse formato evita
  uma conversão e corta o disco em ~10x. Irreversível — se um dia quiser
  qualidade de arquivo, mude TARGET_RATE.
* **Âncora no relógio de parede.** Os dois dispositivos têm clocks de hardware
  independentes. Uma deriva de 0,05% (normal) dá ~1,8 s de desalinhamento em uma
  hora, o que arruinaria o casamento das faixas. Cada escrita é corrigida contra
  o tempo decorrido, inserindo ou descartando amostras.
* **Mute escreve silêncio**, não interrompe a escrita — parar deslocaria a faixa.
"""

from __future__ import annotations

import argparse
import sys
import json
import logging
import queue
import threading
import time
import wave
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import soxr

# O console do Windows usa code page ANSI por padrao e embaralha acentos.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

logger = logging.getLogger(__name__)

TARGET_RATE = 16000
CHUNK = 1024
# Tolerância antes de corrigir deriva. Abaixo disso não mexe, para não ficar
# inserindo/removendo amostra a toda hora por jitter de agendamento.
DRIFT_TOLERANCE_S = 0.050
SILENCE_RMS = 1e-4
# Um canal que NUNCA teve áudio é falha de configuração (microfone no mudo,
# dispositivo errado, cabo solto) e merece alarme rápido.
NEVER_HEARD_WARN_S = 45.0
# Um canal que já funcionou e ficou quieto é só uma pausa. Reuniões reais têm
# silêncios longos — medimos 36,9s numa reunião de 8 minutos — então este limiar
# tem que ser bem folgado para o aviso não virar ruído.
GONE_QUIET_WARN_S = 240.0


@dataclass
class TrackStats:
    """Estado observável de uma faixa, para a UI mostrar."""
    name: str
    frames_written: int = 0
    drift_corrections: int = 0
    drift_samples_net: int = 0
    silent_for_s: float = 0.0
    peak_rms: float = 0.0
    warned_silent: bool = False
    # Se o canal já produziu áudio alguma vez. Separa "configuração errada" de
    # "pausa na conversa", que precisam de limiares muito diferentes.
    ever_heard: bool = False
    # Um booleano "nunca teve áudio" não basta: uma gravação de 36 min ficou 95%
    # muda depois de um início saudável e o meta.json a declarou boa. Estes três
    # campos tornam esse tipo de falha visível depois do fato.
    total_silent_s: float = 0.0
    longest_silence_s: float = 0.0
    muted_s: float = 0.0


@dataclass
class Track:
    """Uma faixa em gravação: stream de entrada -> fila -> arquivo WAV."""
    name: str
    device_index: int
    device_name: str
    native_rate: int
    channels: int
    path: Path
    q: queue.Queue = field(default_factory=lambda: queue.Queue(maxsize=512))
    stats: TrackStats = None
    muted: bool = False
    _wav: wave.Wave_write = None
    _stream: object = None
    # Resampler COM ESTADO. soxr.resample() é one-shot: chamada por chunk, ela
    # reinicia o filtro a cada bloco e injeta uma descontinuidade em cada
    # fronteira — audível como crepitação. ResampleStream carrega o estado
    # entre os blocos, que é o que a captura contínua exige.
    _resampler: object = None

    def __post_init__(self):
        if self.stats is None:
            self.stats = TrackStats(name=self.name)


class DualRecorder:
    """Grava loopback do sistema e microfone em dois WAVs alinhados."""

    def __init__(self, out_dir: Path, mic_index: int | None = None,
                 loopback_index: int | None = None):
        import pyaudiowpatch as pyaudio

        self._pa = pyaudio.PyAudio()
        self._pyaudio = pyaudio
        self.out_dir = Path(out_dir)
        self._stop = threading.Event()
        self._writers: list[threading.Thread] = []
        self._t0: float | None = None

        wasapi = self._pa.get_host_api_info_by_type(pyaudio.paWASAPI)

        # --- loopback: o que sai pelos alto-falantes (os outros participantes) ---
        if loopback_index is None:
            speakers = self._pa.get_device_info_by_index(wasapi["defaultOutputDevice"])
            lb = next((d for d in self._pa.get_loopback_device_info_generator()
                       if speakers["name"] in d["name"]), None)
            if lb is None:
                raise RuntimeError(
                    f"Nenhum dispositivo de loopback para '{speakers['name']}'. "
                    "Rode probe_devices.py para ver o que existe."
                )
        else:
            lb = self._pa.get_device_info_by_index(loopback_index)

        # --- microfone ---
        mic_idx = wasapi["defaultInputDevice"] if mic_index is None else mic_index
        mic = self._pa.get_device_info_by_index(mic_idx)

        self.system = Track(
            name="system", device_index=lb["index"], device_name=lb["name"],
            native_rate=int(lb["defaultSampleRate"]),
            channels=int(lb["maxInputChannels"]), path=self.out_dir / "system.wav",
        )
        self.mic = Track(
            name="mic", device_index=mic["index"], device_name=mic["name"],
            native_rate=int(mic["defaultSampleRate"]),
            channels=min(int(mic["maxInputChannels"]), 2),
            path=self.out_dir / "mic.wav",
        )

    # ---------------------------------------------------------------- captura

    def _callback_factory(self, track: Track):
        def cb(in_data, frame_count, time_info, status):
            if not self._stop.is_set():
                try:
                    track.q.put_nowait(in_data)
                except queue.Full:
                    # Preferimos perder áudio a travar o callback do driver: um
                    # callback lento causa glitch em TODO o áudio da máquina.
                    pass
            return (None, self._pyaudio.paContinue)
        return cb

    def _writer(self, track: Track):
        """Consome a fila, converte para 16 kHz mono e escreve corrigindo deriva."""
        wav = wave.open(str(track.path), "wb")
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(TARGET_RATE)
        track._wav = wav

        if track.native_rate != TARGET_RATE:
            track._resampler = soxr.ResampleStream(
                track.native_rate, TARGET_RATE, 1, dtype="float32", quality="HQ")

        silent_since: float | None = None
        try:
            while not self._stop.is_set() or not track.q.empty():
                try:
                    raw = track.q.get(timeout=0.2)
                except queue.Empty:
                    continue

                audio = np.frombuffer(raw, dtype=np.float32)
                if track.channels > 1:
                    audio = audio.reshape(-1, track.channels).mean(axis=1)

                if track.muted:
                    audio = np.zeros_like(audio)

                rms = float(np.sqrt(np.mean(audio ** 2))) if audio.size else 0.0
                track.stats.peak_rms = max(track.stats.peak_rms, rms)

                # Contabiliza o tempo deste bloco antes de classificá-lo, para
                # os totais baterem com a duração do arquivo.
                bloco_s = audio.size / track.native_rate if audio.size else 0.0
                if track.muted:
                    track.stats.muted_s += bloco_s
                elif rms < SILENCE_RMS:
                    track.stats.total_silent_s += bloco_s

                now = time.monotonic()
                if rms >= SILENCE_RMS:
                    # Um canal que produziu áudio está comprovadamente vivo.
                    track.stats.ever_heard = True
                    silent_since = None
                    track.stats.silent_for_s = 0.0
                    track.stats.warned_silent = False
                elif not track.muted:
                    silent_since = silent_since if silent_since is not None else now
                    track.stats.silent_for_s = now - silent_since
                    track.stats.longest_silence_s = max(
                        track.stats.longest_silence_s, track.stats.silent_for_s)
                    limite = (GONE_QUIET_WARN_S if track.stats.ever_heard
                              else NEVER_HEARD_WARN_S)
                    if (track.stats.silent_for_s > limite
                            and not track.stats.warned_silent):
                        track.stats.warned_silent = True
                        motivo = ("sem áudio desde o início" if not track.stats.ever_heard
                                  else f"silenciosa há {track.stats.silent_for_s:.0f}s")
                        print(f"\n  AVISO: faixa '{track.name}' {motivo} "
                              f"({track.device_name})", flush=True)

                if track._resampler is not None:
                    audio = track._resampler.resample_chunk(audio)
                    if audio.size == 0:
                        continue          # o filtro ainda está enchendo
                    audio = np.ascontiguousarray(audio).ravel()

                audio = self._correct_drift(track, audio)

                wav.writeframes(np.clip(audio * 32767, -32768, 32767)
                                .astype(np.int16).tobytes())
                track.stats.frames_written += audio.size
        finally:
            # Drena o que ficou dentro do filtro, senão a cauda da gravação some.
            if track._resampler is not None:
                try:
                    tail = track._resampler.resample_chunk(
                        np.zeros(0, dtype=np.float32), last=True)
                    if tail.size:
                        tail = np.ascontiguousarray(tail).ravel()
                        wav.writeframes(np.clip(tail * 32767, -32768, 32767)
                                        .astype(np.int16).tobytes())
                        track.stats.frames_written += tail.size
                except Exception as e:
                    logger.debug(f"flush do resampler falhou: {e}")
            wav.close()

    def _correct_drift(self, track: Track, audio: np.ndarray) -> np.ndarray:
        """Mantém a faixa ancorada no relógio de parede.

        Compara as amostras já escritas com as que *deveriam* existir para o
        tempo decorrido e insere silêncio (dispositivo atrasado) ou descarta
        amostras (adiantado). Sem isso as duas faixas divergem ao longo da
        reunião e o casamento com a diarização quebra.
        """
        if self._t0 is None:
            return audio

        elapsed = time.monotonic() - self._t0
        expected = int(elapsed * TARGET_RATE)
        actual = track.stats.frames_written + audio.size
        delta = expected - actual
        tol = int(DRIFT_TOLERANCE_S * TARGET_RATE)

        if delta > tol:
            audio = np.concatenate([audio, np.zeros(delta, dtype=audio.dtype)])
            track.stats.drift_corrections += 1
            track.stats.drift_samples_net += delta
        elif delta < -tol:
            drop = min(-delta, audio.size)
            audio = audio[:audio.size - drop]
            track.stats.drift_corrections += 1
            track.stats.drift_samples_net -= drop
        return audio

    # ------------------------------------------------------------- ciclo de vida

    def start(self) -> None:
        self.out_dir.mkdir(parents=True, exist_ok=True)
        self._t0 = time.monotonic()
        for track in (self.system, self.mic):
            track._stream = self._pa.open(
                format=self._pyaudio.paFloat32,
                channels=track.channels,
                rate=track.native_rate,
                input=True,
                input_device_index=track.device_index,
                frames_per_buffer=CHUNK,
                stream_callback=self._callback_factory(track),
            )
            t = threading.Thread(target=self._writer, args=(track,), daemon=True)
            t.start()
            self._writers.append(t)
            track._stream.start_stream()

    def stop(self) -> dict:
        self._stop.set()
        for track in (self.system, self.mic):
            if track._stream is not None:
                track._stream.stop_stream()
                track._stream.close()
        for t in self._writers:
            t.join(timeout=10)
        self._pa.terminate()

        # Pela faixa mais longa, nao pela do sistema: o loopback WASAPI nao
        # entrega nada enquanto nada toca, entao uma reuniao so de escuta (ou um
        # trecho em silencio) reportaria duracao zero.
        duration = max(self.system.stats.frames_written,
                       self.mic.stats.frames_written) / TARGET_RATE
        meta = {
            "recorded_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
            "duration_s": round(duration, 2),
            "sample_rate": TARGET_RATE,
            "tracks": {
                t.name: {
                    "file": t.path.name,
                    "device": t.device_name,
                    "native_rate": t.native_rate,
                    "frames": t.stats.frames_written,
                    "drift_corrections": t.stats.drift_corrections,
                    "drift_net_samples": t.stats.drift_samples_net,
                    "peak_rms": round(t.stats.peak_rms, 6),
                    "ever_heard": t.stats.ever_heard,
                    # Falha de configuração: o canal nunca produziu áudio.
                    "no_audio": not t.stats.ever_heard,
                    "total_silent_s": round(t.stats.total_silent_s, 1),
                    "longest_silence_s": round(t.stats.longest_silence_s, 1),
                    "muted_s": round(t.stats.muted_s, 1),
                    # A leitura que interessa de relance: quanto da faixa tem
                    # conteudo util. Baixo aqui significa gravacao suspeita,
                    # mesmo com no_audio=false.
                    "usable_pct": round(
                        100 * max(0.0, duration - t.stats.total_silent_s
                                  - t.stats.muted_s) / duration, 1
                    ) if duration > 0 else 0.0,
                }
                for t in (self.system, self.mic)
            },
            # Preenchido depois pela integração com o Google Calendar; o contrato
            # já existe para não mudar quando ela entrar.
            "meeting": {"title": None, "client": None, "project": None,
                        "attendees": [], "calendar_event_id": None},
        }
        (self.out_dir / "meta.json").write_text(
            json.dumps(meta, ensure_ascii=False, indent=2), encoding="utf-8")
        return meta

    def set_muted(self, muted: bool) -> None:
        self.mic.muted = muted

    @property
    def is_muted(self) -> bool:
        return self.mic.muted

    @property
    def elapsed_s(self) -> float:
        return 0.0 if self._t0 is None else time.monotonic() - self._t0


def list_devices() -> dict:
    """Enumera microfones e loopbacks para a UI montar os menus.

    Abre e fecha o PyAudio por chamada de proposito: a lista muda quando o
    usuario pluga um headset, e manter uma instancia viva serviria dados velhos.
    """
    import pyaudiowpatch as pyaudio

    p = pyaudio.PyAudio()
    try:
        wasapi = p.get_host_api_info_by_type(pyaudio.paWASAPI)
        mics, loopbacks = [], []

        for i in range(p.get_device_count()):
            d = p.get_device_info_by_index(i)
            if d["hostApi"] != wasapi["index"] or d["maxInputChannels"] <= 0:
                continue
            if "loopback" not in d["name"].lower():
                mics.append({"index": i, "name": d["name"]})

        for d in p.get_loopback_device_info_generator():
            loopbacks.append({"index": d["index"], "name": d["name"]})

        try:
            default_out = p.get_device_info_by_index(wasapi["defaultOutputDevice"])
            default_lb = next((d["index"] for d in loopbacks
                               if default_out["name"] in d["name"]), None)
        except OSError:
            default_lb = None

        return {
            "mics": mics,
            "loopbacks": loopbacks,
            "default_mic": wasapi.get("defaultInputDevice"),
            "default_loopback": default_lb,
        }
    finally:
        p.terminate()


def main() -> int:
    ap = argparse.ArgumentParser(description="Teste de captura de duas faixas")
    ap.add_argument("--seconds", type=float, default=30)
    ap.add_argument("--out", default="../data/recordings")
    ap.add_argument("--mic-index", type=int, default=None)
    ap.add_argument("--loopback-index", type=int, default=None)
    args = ap.parse_args()

    stamp = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    out = Path(args.out) / stamp

    rec = DualRecorder(out, args.mic_index, args.loopback_index)
    print(f"sistema : [{rec.system.device_index}] {rec.system.device_name} "
          f"({rec.system.native_rate} Hz, {rec.system.channels}ch)")
    print(f"micro   : [{rec.mic.device_index}] {rec.mic.device_name} "
          f"({rec.mic.native_rate} Hz, {rec.mic.channels}ch)")
    print(f"saída   : {out}\n")
    print(f"gravando {args.seconds:.0f}s — TOQUE UM VÍDEO e FALE AO MICROFONE\n")

    rec.start()
    try:
        t0 = time.monotonic()
        while time.monotonic() - t0 < args.seconds:
            time.sleep(0.5)
            el = time.monotonic() - t0
            print(f"\r  {el:5.1f}s | sistema RMS {rec.system.stats.peak_rms:.4f} "
                  f"| mic RMS {rec.mic.stats.peak_rms:.4f}   ", end="", flush=True)
    except KeyboardInterrupt:
        print("\ninterrompido")

    meta = rec.stop()
    print("\n\n--- resultado ---")
    for name, t in meta["tracks"].items():
        secs = t["frames"] / TARGET_RATE
        print(f"  {name:7} {secs:6.2f}s  pico RMS {t['peak_rms']:.5f}  "
              f"deriva: {t['drift_corrections']} correções "
              f"({t['drift_net_samples']:+d} amostras)"
              + ("  <- SEM AUDIO" if t["no_audio"] else ""))
    d = abs(meta["tracks"]["system"]["frames"] - meta["tracks"]["mic"]["frames"])
    print(f"\n  desalinhamento final entre as faixas: {d} amostras "
          f"({d/TARGET_RATE*1000:.1f} ms)")
    print(f"  arquivos em: {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
