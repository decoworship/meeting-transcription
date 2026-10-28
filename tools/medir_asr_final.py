#!/usr/bin/env python3
"""Candidatos à passada final contra o ``large-v3`` de hoje, na mesma rodada (26/10/2026).

**Por que não usar o ``transcricao.json`` do app como referência**, como o
``medir_parakeet.py`` fazia: ele não registra de que modelo saiu, e as seis
gravações com Gemini foram transcritas entre agosto e outubro, por versões
diferentes do app. A referência aqui é o ``large-v3`` **rodado de novo agora**,
pela classe ``Modelo`` do ``motores/asr/motor.py`` da instalação — os mesmos
parâmetros de produção, sem cópia que possa divergir —, e sem hotwords, que é o
padrão do app (``usar_hotwords`` desligado).

Dois braços, cada um no seu Python, porque não cabem no mesmo:

``large-v3``
    O faster-whisper do Python embarcado da instalação oficial (só leitura).
``gguf``
    Qualquer modelo do ``transcribe.cpp`` — o ``parakeet-ultra``, o Parakeet v3,
    o Nemotron da legenda —, num venv com a 0.4.1, porque o ``parakeet-ultra``
    só entrou na série 0.3 (PR #192) e o app embarca a 0.2.3. O áudio vai
    **inteiro**: o Parakeet corta sozinho em trechos de até 30 s pela cabeça de
    VAD do próprio modelo, que é o caminho que o porte validou. ``--bloco``
    corta antes, para modelos que não fazem isso.

A régua é a do projeto (``tools/wer_contra_gemini.py``): divergência do Gemini,
que **não é WER** — só a comparação entre linhas do mesmo áudio tem sentido. E
ao lado dela, a forma dos trechos: um motor sem trecho com tempo não serve à
passada final, por melhor que seja o texto (``docs/CONVERGENCIA.md``).

Uso::

    APP=/mnt/c/Users/andre/AppData/Local/Programs/MeetingApp/motores/python/python.exe
    VENV=/mnt/c/Users/andre/pulsemeet-medicoes/venv-tcpp041/Scripts/python.exe
    M='C:\\Users\\andre\\pulsemeet-medicoes\\modelos'
    $APP tools/medir_asr_final.py large-v3
    $VENV tools/medir_asr_final.py gguf --modelo "$M\\parakeet-ultra-F16.gguf" --nome parakeet-ultra

    uv run python tools/wer_contra_gemini.py \\
        /mnt/c/Users/andre/pulsemeet-medicoes/asr-final/*/<gravacao>
    uv run python tools/medir_asr_final.py resumo
"""
from __future__ import annotations

import argparse
import json
import shutil
import statistics
import subprocess
import sys
import threading
import time
import wave
from pathlib import Path

WINDOWS = sys.platform == "win32"
ACERVO = Path(r"C:\Users\andre\OneDrive\Documents\MeetingRecordings" if WINDOWS
              else "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
MOTORES = Path(r"C:\Users\andre\AppData\Local\Programs\MeetingApp\motores")
SAIDA = Path(r"C:\Users\andre\pulsemeet-medicoes\asr-final" if WINDOWS
             else "/mnt/c/Users/andre/pulsemeet-medicoes/asr-final")
TAXA = 16000

#: As seis com a aba de transcrição do Gemini (ver tools/medir_nemotron3.py).
#: As duas de outubro com só anotações ficam de fora: o leitor acharia turnos falsos.
COM_GEMINI = ["2026-08-21_11-00-33", "2026-08-25_08-59-22", "2026-10-21_12-01-47",
              "2026-09-30_09-58-30", "2026-08-20_15-59-20", "2026-08-27_15-28-37"]


class Vram:
    """Pico de VRAM da placa durante a rodada, pelo nvidia-smi."""

    def __init__(self):
        self.base = self.pico = self._ler()
        self._vivo = True
        threading.Thread(target=self._vigiar, daemon=True).start()

    @staticmethod
    def _ler() -> int:
        r = subprocess.run(["nvidia-smi", "--query-gpu=memory.used",
                            "--format=csv,noheader,nounits"], capture_output=True, text=True)
        return int(r.stdout.split()[0])

    def _vigiar(self):
        while self._vivo:
            self.pico = max(self.pico, self._ler())
            time.sleep(0.2)

    def parar(self) -> int:
        self._vivo = False
        return self.pico - self.base


def onda(gravacao: str):
    import numpy as np
    with wave.open(str(ACERVO / gravacao / "mix.wav"), "rb") as w:
        assert w.getframerate() == TAXA and w.getsampwidth() == 2, gravacao
        a = np.frombuffer(w.readframes(w.getnframes()), "<i2").astype(np.float32) / 32768
        return a.reshape(-1, w.getnchannels()).mean(1)


def gravar(variante: str, gravacao: str, segs: list[dict]) -> None:
    d = SAIDA / variante / gravacao
    d.mkdir(parents=True, exist_ok=True)
    shutil.copy(ACERVO / gravacao / "gemini.md", d / "gemini.md")
    (d / "transcricao.json").write_text(
        json.dumps({"segments": segs}, ensure_ascii=False, indent=1), encoding="utf-8")


def forma(segs: list[dict]) -> dict:
    """A estrutura de tempo, que decide antes da qualidade do texto."""
    # float() porque o faster-whisper devolve np.float64, e o json recusa o np.int64
    # que a soma de comparações deles produz
    dur = [float(s["end"] - s["start"]) for s in segs] or [0.0]
    return {"trechos": len(segs), "mediana_s": round(statistics.median(dur), 2),
            "maior_s": round(max(dur), 1), "acima_de_25s": sum(d > 25 for d in dur)}


# ------------------------------------------------------------------ braços

def braco_large_v3(_a):
    sys.path.insert(0, str(MOTORES / "asr"))
    import motor   # prepara o CUDA no import, e desvia o stdout para o stderr

    motor._enviar = lambda **_: None   # o progresso do protocolo não interessa aqui
    m = motor.Modelo("large-v3")
    t0 = time.perf_counter()
    m.carregar(0)
    carga = time.perf_counter() - t0
    if m.dispositivo != "cuda":
        raise SystemExit(f"o large-v3 caiu para {m.dispositivo}: {m.motivo}")

    def rodar(gravacao: str):
        r = m.transcrever(str(ACERVO / gravacao / "mix.wav"), 0, None, "pt")
        return [com_palavras(s) for s in r["segmentos"] if s["texto"].strip()]

    return "large-v3", carga, rodar


def com_palavras(s: dict) -> dict:
    """Um trecho do protocolo, com as palavras junto.

    As palavras são o que o app usa para cortar o trecho onde o falante troca
    (``Montagem.RepartirPorFalante``), e o ``medir_mod2.py`` rotula por elas —
    sem elas, a régua de falante castiga quem devolve trechos maiores.
    """
    return {"start": float(s["inicio"]), "end": float(s["fim"]), "text": s["texto"],
            "words": [{"start": float(p["inicio"]), "end": float(p["fim"]), "text": p["texto"]}
                      for p in s.get("palavras") or []]}


#: Corte dos trechos pela palavra. O ``timestamps="segment"`` do Parakeet devolve
#: os trechos da cabeça de VAD dele, de até 30 s — 15 trechos em 7,7 min, mediana
#: de 24 s, contra 124 do ``large-v3` —, e trecho longo mistura falantes. Isto é
#: escolha desta ferramenta, não do modelo, e não mexe na régua de texto.
PAUSA_S = 0.5           # silêncio entre palavras que fecha o trecho
PAUSA_NO_PONTO_S = 0.2  # basta isto quando a palavra fecha frase
TETO_S = 25.0           # o max_speech_duration_s do VAD do large-v3


def trechos_por_palavra(palavras, deslocamento: float) -> list[dict]:
    segs, atual = [], []
    for i, w in enumerate(palavras):
        atual.append(w)
        prox = palavras[i + 1] if i + 1 < len(palavras) else None
        pausa = (prox.t0_ms - w.t1_ms) / 1000 if prox else 1e9
        fecha = w.text.rstrip().endswith((".", "?", "!"))
        longo = (w.t1_ms - atual[0].t0_ms) / 1000 > TETO_S
        if pausa >= PAUSA_S or (fecha and pausa >= PAUSA_NO_PONTO_S) or longo:
            segs.append({"start": deslocamento + atual[0].t0_ms / 1000,
                         "end": deslocamento + w.t1_ms / 1000,
                         "text": " ".join(x.text.strip() for x in atual)})
            atual = []
    return [s for s in segs if s["text"].strip()]


def braco_motor(a):
    """O ``motores/asr/motor.py`` **do repositório**, com um id do ``GGUF`` dele.

    É a prova de que o motor que vai para o app — com os blocos cortados no
    silêncio e os trechos montados por ele — mede o mesmo que o braço ``gguf``,
    que passava o áudio inteiro.
    """
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "motores" / "asr"))
    import motor

    motor._enviar = lambda **_: None
    if a.pasta_modelos:
        motor.PASTA_DOS_MODELOS = Path(a.pasta_modelos)
    m = motor.Modelo(a.id)
    t0 = time.perf_counter()
    m.carregar(0)
    carga = time.perf_counter() - t0
    if m.dispositivo != "cuda":
        raise SystemExit(f"{a.id} caiu para {m.dispositivo}: {m.motivo}")

    def rodar(gravacao: str):
        r = m.transcrever(str(ACERVO / gravacao / "mix.wav"), 0, None, "pt")
        return [com_palavras(s) for s in r["segmentos"]]

    return a.nome or f"motor-{a.id}", carga, rodar


def braco_gguf(a):
    import transcribe_cpp as t

    t0 = time.perf_counter()
    modelo = t.Model(a.modelo, backend="cuda")
    carga = time.perf_counter() - t0
    # n_threads=0 é o padrão da biblioteca, que na legenda foi o pior de três
    # (motores/legenda/motor.py, _threads)
    opcoes = dict(language=a.idioma, timestamps="word", n_threads=a.threads)
    if a.vocabulario:
        opcoes["vocabulary"] = json.loads(Path(a.vocabulario).read_text(encoding="utf-8"))

    def rodar(gravacao: str):
        audio = onda(gravacao)
        passo = int(a.bloco * TAXA) if a.bloco else len(audio)
        segs = []
        for ini in range(0, len(audio), passo):
            pcm = audio[ini: ini + passo]
            if len(pcm) < TAXA:
                continue
            r = t.transcribe(modelo, pcm, **opcoes)
            segs += trechos_por_palavra(r.words, ini / TAXA)
        return segs

    return a.nome, carga, rodar


QUADRO = 3200   # 200 ms, o quadro da LegendaAoVivo


def braco_legenda(a):
    """A legenda como ela roda, com só o ``att_context_right`` trocado (``VIVO-8``).

    O ``Legendador`` é o da instalação — destravar, recomeçar e encerrar são os de
    produção —, e só o fluxo é reaberto com o ``ParakeetStreamOptions``. Alimenta o
    mais rápido que a placa aguenta: a decisão do modelo depende dos quadros, não
    do relógio (o mesmo argumento do ``reproduzir_legenda.py``). O áudio é o
    ``mix.wav``, o mesmo dos outros braços, e não a mistura por janela do app.
    """
    import transcribe_cpp as t
    sys.path.insert(0, str(MOTORES / "legenda"))
    import motor

    motor._enviar = lambda **_: None
    L = motor.Legendador()
    t0 = time.perf_counter()
    L.carregar(0)
    carga = time.perf_counter() - t0
    if L.dispositivo != "cuda":
        raise SystemExit(f"a legenda caiu para {L.dispositivo}: {L.motivo}")

    def rodar(gravacao: str):
        L.abrir(0, "pt-BR")
        if a.direita is not None:
            # um fluxo por modelo de cada vez: o que o abrir() criou sai antes
            L._sessao.reset()
            L._opcoes["family"] = t.ParakeetStreamOptions(att_context_right=a.direita)
            L._sessao = L._modelo.session(n_threads=motor._threads()).stream(**L._opcoes)
        audio = onda(gravacao)
        atraso = []
        for ini in range(0, len(audio), QUADRO):
            u = L.alimentar(audio[ini: ini + QUADRO])
            if L.travou():
                L.destravar()
            # quanto o firme está atrás do que já entrou, amostrado a cada 1 s
            if (ini // QUADRO) % 5 == 0:
                atraso.append((ini + QUADRO) / TAXA - L.ate_ms(u) / 1000)
        texto = L.encerrar()
        rodar.extra[gravacao] = {
            "pontos_por_mil": round(1000 * sum(texto.count(c) for c in ".?!")
                                    / max(1, len(texto.split())), 1),
            "virgulas_por_mil": round(1000 * texto.count(",") / max(1, len(texto.split())), 1),
            "atraso_mediano_s": round(statistics.median(atraso), 2) if atraso else None,
            "recomecos": L.recomecos}
        # um trecho só: a legenda não tem estrutura de tempo, e a régua de texto
        # não precisa dela
        return [{"start": 0.0, "end": len(audio) / TAXA, "text": texto}]

    rodar.extra = {}
    nome = a.nome or f"legenda-r{'padrao' if a.direita is None else a.direita}"
    return nome, carga, rodar


# ------------------------------------------------------------------ resumo

def resumo(_a) -> int:
    """Uma tabela por gravação, com a régua do projeto e a forma dos trechos."""
    sys.path.insert(0, str(Path(__file__).parent))
    import wer_contra_gemini as W

    variantes = sorted(p.name for p in SAIDA.iterdir() if p.is_dir())
    tempos = {v: json.loads((SAIDA / v / "tempos.json").read_text(encoding="utf-8"))
              for v in variantes if (SAIDA / v / "tempos.json").exists()}
    soma = {v: [0, 0, 0, 0, 0] for v in variantes}   # trocadas, faltando, sobrando, iguais, ref
    # a soma só vale sobre as gravações que todas as variantes têm: a varredura da
    # legenda roda nas quatro mais curtas, e somar conjuntos diferentes compararia
    # áudios diferentes
    comuns = [g for g in COM_GEMINI
              if all((SAIDA / v / g / "transcricao.json").exists() for v in variantes)]
    for g in COM_GEMINI:
        print(f"\n{g}")
        print(f"  {'variante':<22} {'diverg.':>8} {'cobert.':>8} {'trocadas':>9} "
              f"{'faltando':>9} {'sobrando':>9} {'trechos':>8} {'>25s':>5} {'xRT':>7}")
        for v in variantes:
            pasta = SAIDA / v / g
            if not (pasta / "transcricao.json").exists():
                continue
            m = W.medir(W.nossas(pasta), W.deles(pasta, None))
            t = tempos.get(v, {}).get("gravacoes", {}).get(g, {})
            print(f"  {v:<22} {m['divergencia']:>7.1f}% {m['cobertura']:>7.1f}% "
                  f"{m['trocadas']:>9} {m['faltando']:>9} {m['sobrando']:>9} "
                  f"{t.get('trechos', '?'):>8} {t.get('acima_de_25s', '?'):>5} "
                  f"{t.get('xrt', '?'):>7}"
                  + (f"  pontos/mil {t['pontos_por_mil']}, atraso {t['atraso_mediano_s']} s"
                     if "pontos_por_mil" in t else ""))
            if g in comuns:
                for i, k in enumerate(("trocadas", "faltando", "sobrando", "iguais", "referencia")):
                    soma[v][i] += m[k]
    print(f"\nsomadas as {len(comuns)} que todas têm: {', '.join(c[:10] for c in comuns)}"
          " (ponderado por palavra do Gemini)")
    for v, (tr, fa, so, ig, ref) in soma.items():
        if ref:
            print(f"  {v:<22} {100 * (tr + fa + so) / ref:>7.1f}% {100 * ig / ref:>7.1f}%  "
                  f"VRAM ~{tempos.get(v, {}).get('vram_mb', '?')} MB")
    return 0


# ------------------------------------------------------------------ main

def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = p.add_subparsers(dest="braco", required=True)
    sub.add_parser("large-v3")
    g = sub.add_parser("gguf")
    g.add_argument("--modelo", required=True)
    g.add_argument("--nome", required=True, help="nome da variante na saída")
    g.add_argument("--idioma", default=None, help="dica de idioma, se o modelo aceitar")
    g.add_argument("--bloco", type=float, default=0, help="cortar em blocos de N s")
    g.add_argument("--vocabulario", help="JSON com a lista de termos")
    g.add_argument("--threads", type=int, default=0, help="n_threads; 0 é o padrão da biblioteca")
    mt = sub.add_parser("motor")
    mt.add_argument("--id", required=True, help="um id do GGUF do motor.py")
    mt.add_argument("--pasta-modelos", help="onde está o GGUF, se não em motores/asr/modelos")
    mt.add_argument("--nome", default="")
    lg = sub.add_parser("legenda")
    lg.add_argument("--direita", type=int, default=None,
                    help="att_context_right; sem ele, o padrão do modelo (o de produção)")
    lg.add_argument("--nome", default="")
    sub.add_parser("resumo")
    for s in sub.choices.values():
        s.add_argument("--gravacoes", nargs="*", default=COM_GEMINI)
    a = p.parse_args()

    if a.braco == "resumo":
        return resumo(a)

    vram = Vram()
    nome, carga, rodar = {"large-v3": braco_large_v3, "gguf": braco_gguf,
                          "motor": braco_motor, "legenda": braco_legenda}[a.braco](a)
    arq = SAIDA / nome / "tempos.json"
    tempos = (json.loads(arq.read_text(encoding="utf-8")) if arq.exists()
              else {"variante": nome, "gravacoes": {}})
    tempos["carga_s"] = round(carga, 1)
    print(f"{nome}: carregou em {carga:.1f} s", flush=True)

    for gravacao in a.gravacoes:
        with wave.open(str(ACERVO / gravacao / "mix.wav"), "rb") as w:
            dur = w.getnframes() / w.getframerate()
        t0 = time.perf_counter()
        segs = rodar(gravacao)
        gasto = time.perf_counter() - t0
        gravar(nome, gravacao, segs)
        extra = getattr(rodar, "extra", {}).get(gravacao, {})
        tempos["gravacoes"][gravacao] = {"audio_s": round(dur, 1), "gasto_s": round(gasto, 1),
                                         "xrt": round(dur / gasto, 1), **forma(segs), **extra}
        print(f"  {gravacao}  {dur / 60:5.1f} min  {gasto:7.1f} s  {dur / gasto:6.1f}x  "
              f"{extra or forma(segs)}", flush=True)

    tempos["vram_mb"] = max(tempos.get("vram_mb", 0), vram.parar())
    arq.parent.mkdir(parents=True, exist_ok=True)
    arq.write_text(json.dumps(tempos, indent=2), encoding="utf-8")
    print(f"  VRAM da rodada: ~{tempos['vram_mb']} MB", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
