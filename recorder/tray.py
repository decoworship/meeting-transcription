"""App de bandeja do gravador de reunioes.

Icone colorido pelo estado, para dar a resposta que importa sem abrir nada:

    cinza    parado
    vermelho gravando
    laranja  gravando com o microfone mudo
    amarelo  gravando, mas um canal esta mudo ha muito tempo (algo errado)

Uso:
    python tray.py

Empacotar como .exe: veja build_exe.ps1
"""

from __future__ import annotations

import logging
import os
import subprocess
import sys
import threading
import time
from datetime import datetime
from pathlib import Path

import pystray
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
import calendar_sync
import settings as settings_mod
from capture import DualRecorder, TARGET_RATE, list_devices

logging.basicConfig(level=logging.INFO,
                    format="%(asctime)s %(levelname)s %(message)s")
logger = logging.getLogger("recorder")

# Marcos (em minutos) para lembrar que o microfone segue mudo.
MUTE_REMINDERS_MIN = (2, 5, 15, 30)

IDLE, RECORDING, MUTED, WARNING = "idle", "recording", "muted", "warning"
COLORS = {
    IDLE:      (110, 110, 115),
    RECORDING: (220, 50, 50),
    MUTED:     (235, 140, 40),
    WARNING:   (235, 205, 40),
}


def make_icon(state: str) -> Image.Image:
    """Circulo cheio; quando mudo, um traco atravessando."""
    size = 64
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([6, 6, size - 6, size - 6], fill=COLORS[state] + (255,))
    if state == MUTED:
        d.line([14, size - 14, size - 14, 14], fill=(255, 255, 255, 255), width=7)
    return img


class RecorderTray:
    def __init__(self):
        self.cfg = settings_mod.load()
        self.rec: DualRecorder | None = None
        self.session_dir: Path | None = None
        self._devices = {"mics": [], "loopbacks": [],
                         "default_mic": None, "default_loopback": None}
        self._lock = threading.Lock()
        self._stop_ui = threading.Event()
        self._muted_since: float | None = None
        self._mute_warned = 0
        self._event: object | None = None

        self.icon = pystray.Icon(
            "meeting-recorder", make_icon(IDLE), "Gravador de reunioes",
            menu=self._build_menu(),
        )

    # ------------------------------------------------------------------ estado

    @property
    def recording(self) -> bool:
        return self.rec is not None

    def _state(self) -> str:
        if not self.recording:
            return IDLE
        if any(t.stats.warned_silent for t in (self.rec.system, self.rec.mic)):
            return WARNING
        return MUTED if self.rec.is_muted else RECORDING

    def _status_text(self) -> str:
        if not self.recording:
            return "Parado"
        el = self.rec.elapsed_s
        txt = f"Gravando  {int(el)//60:02d}:{int(el)%60:02d}"
        if self.rec.is_muted:
            mudo = int(time.monotonic() - (self._muted_since or time.monotonic()))
            txt += f"  (mic mudo ha {mudo//60:02d}:{mudo%60:02d})"
        if self._event is not None:
            txt += f"  |  {self._event.title[:34]}"
        mudas = [t.name for t in (self.rec.system, self.rec.mic)
                 if t.stats.warned_silent]
        if mudas:
            txt += f"  [SEM AUDIO: {', '.join(mudas)}]"
        return txt

    # ------------------------------------------------------------------- menu

    def _device_items(self, kind: str):
        """Submenu de dispositivos. `kind` e 'mics' ou 'loopbacks'."""
        key = "mic_index" if kind == "mics" else "loopback_index"
        devices = self._devices[kind]
        if not devices:
            return (pystray.MenuItem("(nenhum encontrado)", None, enabled=False),)

        def make(dev):
            def on_click(icon, item):
                self.cfg[key] = dev["index"]
                settings_mod.save(self.cfg)
                self._refresh()

            def checked(item):
                sel = self.cfg.get(key)
                if sel is None:
                    sel = self._devices[f"default_{'mic' if kind == 'mics' else 'loopback'}"]
                return sel == dev["index"]

            # Trocar de dispositivo no meio da gravacao exigiria reabrir o
            # stream e realinhar as faixas; mais simples proibir.
            label = f"[{dev['index']}] {dev['name'][:44]}"
            return pystray.MenuItem(label, on_click, checked=checked,
                                    radio=True, enabled=not self.recording)

        return tuple(make(d) for d in devices)

    def _primary_label(self, item=None) -> str:
        if not self.recording:
            return "Iniciar gravacao"
        return "Desmutar microfone" if self.rec.is_muted else "Mutar microfone"

    def _build_menu(self) -> pystray.Menu:
        return pystray.Menu(
            pystray.MenuItem(lambda item: self._status_text(), None, enabled=False),
            pystray.Menu.SEPARATOR,
            # Acao do clique: iniciar quando parado, mutar quando gravando.
            # Parar NAO fica no clique de proposito -- um clique acidental que
            # encerra a gravacao perde a reuniao; um que muta se percebe na hora.
            pystray.MenuItem(self._primary_label, self.on_primary, default=True),
            pystray.MenuItem("Parar gravacao", self.on_stop,
                             enabled=lambda item: self.recording),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("Microfone", pystray.Menu(lambda: self._device_items("mics"))),
            pystray.MenuItem("Audio do sistema",
                             pystray.Menu(lambda: self._device_items("loopbacks"))),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem(
                lambda item: ("Google Calendar: ligado" if self.cfg.get("use_calendar", True)
                              else "Google Calendar: desligado"),
                self.on_toggle_calendar,
                checked=lambda item: bool(self.cfg.get("use_calendar", True))),
            pystray.MenuItem(
                lambda item: ("Reautorizar Google Calendar..." if calendar_sync.is_authorized()
                              else "Autorizar Google Calendar..."),
                self.on_authorize_calendar,
                visible=lambda item: calendar_sync.is_configured()),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("Abrir pasta das gravacoes", self.on_open_folder),
            pystray.MenuItem("Sair", self.on_quit),
        )

    def _refresh(self) -> None:
        self.icon.icon = make_icon(self._state())
        self.icon.title = f"Gravador - {self._status_text()}"
        self.icon.update_menu()

    # ----------------------------------------------------------------- acoes

    def on_primary(self, icon=None, item=None) -> None:
        """Clique no icone: inicia quando parado, muta/desmuta quando gravando."""
        if self.recording:
            self.on_toggle_mute()
            return
        with self._lock:
            self._start()
        self._refresh()

    def on_stop(self, icon=None, item=None) -> None:
        with self._lock:
            if self.recording:
                self._stop()
        self._refresh()

    def _start(self) -> None:
        stamp = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
        out = Path(self.cfg["output_dir"]) / stamp
        try:
            rec = DualRecorder(out, self.cfg.get("mic_index"),
                               self.cfg.get("loopback_index"))
            rec.set_muted(bool(self.cfg.get("start_muted")))
            rec.start()
        except Exception as e:
            logger.error(f"falha ao iniciar: {e}")
            self.icon.notify(f"Falha ao iniciar: {e}", "Gravador")
            return
        self.rec, self.session_dir = rec, out
        self._event = None
        # Depois de start(): a agenda e uma chamada de rede e nao pode
        # atrasar o inicio da captura nem roubar foco.
        if self.cfg.get("use_calendar", True):
            threading.Thread(target=self._lookup_event, daemon=True).start()
        logger.info(f"gravando em {out}")
        logger.info(f"  sistema: {rec.system.device_name}")
        logger.info(f"  mic    : {rec.mic.device_name}")
        self.icon.notify(f"Gravando\n{rec.mic.device_name[:38]}", "Gravador")

    def _stop(self) -> None:
        rec, out = self.rec, self.session_dir
        self.rec, self.session_dir = None, None
        self._muted_since, self._mute_warned = None, 0
        evento = self._event
        self._event = None
        try:
            meta = rec.stop(evento.to_meta() if evento else None)
        except Exception as e:
            logger.error(f"falha ao parar: {e}")
            self.icon.notify(f"Falha ao parar: {e}", "Gravador")
            return

        dur = meta["duration_s"]
        mudas = [n for n, t in meta["tracks"].items() if t["no_audio"]]
        msg = f"{int(dur)//60:02d}:{int(dur)%60:02d} salvos"
        if evento is not None:
            msg += f"\n{evento.title[:40]}"
        if mudas:
            msg += f"\nATENCAO: sem audio em {', '.join(mudas)}"
        logger.info(f"gravacao encerrada: {out} ({dur:.1f}s)")
        self.icon.notify(msg, "Gravador")

    def on_toggle_mute(self, icon=None, item=None) -> None:
        if not self.recording:
            return
        novo = not self.rec.is_muted
        self.rec.set_muted(novo)
        self._muted_since = time.monotonic() if novo else None
        self._mute_warned = 0
        self._refresh()

    def _lookup_event(self) -> None:
        """Procura na agenda a reuniao correspondente. Roda em thread propria."""
        r = calendar_sync.current_event()
        if not self.recording:
            return

        if r.needs_attention:
            # Nao encontrar reuniao e normal; ter autorizado e o token morrer
            # nao e. Sem este aviso o gravador pararia de identificar reunioes
            # em silencio e so se descobriria semanas depois.
            logger.warning(f"calendario indisponivel ({r.status}): {r.detail}")
            self.icon.notify(
                "Calendario indisponivel. Reautorize pelo menu.\n"
                "A gravacao continua normalmente.", "Gravador")
            return

        if r.event is None:
            logger.info(f"sem evento na agenda ({r.status})")
            return

        self._event = r.event
        logger.info(f"agenda: {r.event.title!r} "
                    f"({len(r.event.attendee_names)} participantes)")
        self.icon.notify(
            f"{r.event.title[:40]}\n{len(r.event.attendee_names)} participantes",
            "Reuniao identificada")
        self._refresh()

    def on_authorize_calendar(self, icon=None, item=None) -> None:
        """Abre o navegador uma vez para autorizar o Google Calendar."""
        if not calendar_sync.is_configured():
            self.icon.notify(
                "Falta google_client_secret.json em\n%USERPROFILE%\\.meeting-recorder",
                "Gravador")
            return

        def run():
            ok = calendar_sync.authorize()
            self.icon.notify("Calendario autorizado" if ok else "Autorizacao falhou",
                             "Gravador")
            self._refresh()

        threading.Thread(target=run, daemon=True).start()

    def on_toggle_calendar(self, icon=None, item=None) -> None:
        self.cfg["use_calendar"] = not self.cfg.get("use_calendar", True)
        settings_mod.save(self.cfg)
        self._refresh()

    def _check_forgotten_mute(self) -> None:
        """Cutuca quem esqueceu o microfone mudo.

        Desde que o clique passou a mutar em vez de parar, mute esquecido virou
        o modo de falha mais provavel -- uma gravacao de 36 min saiu 95% muda
        exatamente assim.
        """
        if not (self.recording and self.rec.is_muted and self._muted_since):
            return
        minutos = int((time.monotonic() - self._muted_since) // 60)
        for marco in MUTE_REMINDERS_MIN:
            if minutos >= marco > self._mute_warned:
                self._mute_warned = marco
                self.icon.notify(
                    f"Microfone mudo ha {marco} min.\n"
                    "Sua voz nao esta sendo gravada.", "Gravador")
                logger.warning(f"microfone mudo ha {marco} min")
                break

    def on_open_folder(self, icon=None, item=None) -> None:
        target = self.session_dir or Path(self.cfg["output_dir"])
        try:
            Path(target).mkdir(parents=True, exist_ok=True)
            os.startfile(str(target))
        except OSError as e:
            logger.warning(f"nao foi possivel abrir {target}: {e}")

    def on_quit(self, icon=None, item=None) -> None:
        with self._lock:
            if self.recording:
                self._stop()
        self._stop_ui.set()
        self.icon.stop()

    # ------------------------------------------------------------------- loop

    def _ui_loop(self) -> None:
        """Mantem icone e rotulo vivos enquanto grava."""
        while not self._stop_ui.wait(1.0):
            if self.recording:
                try:
                    self._check_forgotten_mute()
                    self._refresh()
                except Exception as e:      # a bandeja nunca deve derrubar o app
                    logger.debug(f"refresh falhou: {e}")

    def run(self) -> None:
        try:
            self._devices = list_devices()
        except Exception as e:
            logger.error(f"nao foi possivel enumerar dispositivos: {e}")
        threading.Thread(target=self._ui_loop, daemon=True).start()
        logger.info(f"gravacoes em: {self.cfg['output_dir']}")
        self.icon.run()


if __name__ == "__main__":
    RecorderTray().run()
