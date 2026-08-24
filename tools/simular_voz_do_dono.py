"""Mede o que a trilha do dono muda, sem publicar o app.

Por que existe: `Nucleo/VozDoDono.cs` nasceu de um número medido (todos os erros
de atribuição do dono vinham de sobreposição), e a única forma honesta de saber
se o conserto funciona é medir de novo — contra a mesma referência, na mesma
gravação. Publicar, reinstalar e retranscrever para descobrir isso custa uma hora
por tentativa; esta simulação custa segundos e reproduz a mesma aritmética.

**Ela reimplementa três coisas do C#, e é dívida assumida.** `VozDoDono.Trilha`,
`Montagem.RepartirPorFalante` e `Montagem.AtribuirFalantes` estão aqui de novo,
em Python. Se divergirem, esta ferramenta mede outra coisa que não o pipeline —
por isso os limiares são importados por valor do arquivo C#, e não copiados à
mão: um `grep` no fonte, e o teste falha alto se não achar.

O que ela **não** reproduz: correção fonética, filtro de silêncio e
reconhecimento de vozes. Nenhum deles mexe em quem falou.

Entrada:

* ``transcricao-com-palavras.json`` — a saída do ASR com ``word_timestamps``,
  porque o ``transcricao.json`` final descarta as palavras (ver
  ``SegmentoFinal.Words``);
* ``gemini.md`` na pasta da gravação, como referência de falante;
* ``mic.wav`` e ``system.wav``.

A diarização do pyannote é reaproveitada do ``transcricao.json`` que já existe:
os rótulos de quem não é o dono, com os tempos deles, são a linha do tempo que o
pyannote produziu.

Uso::

    python tools/simular_voz_do_dono.py <pasta> --palavras asr.json
    python tools/simular_voz_do_dono.py <pasta> --palavras asr.json --limiar 0.02
"""

from __future__ import annotations

import argparse
import json
import math
import re
import sys
import wave
from array import array
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import comparar_com_gemini as C   # noqa: E402

FONTE_CS = Path(__file__).parent.parent / "app-net/Nucleo/VozDoDono.cs"
TAXA = 16_000


def constante(nome: str) -> float:
    """Lê um limiar do C#, para as duas implementações não divergirem em silêncio."""
    texto = FONTE_CS.read_text(encoding="utf-8")
    m = re.search(rf"const double {nome} = ([0-9.e-]+);", texto)
    if not m:
        raise SystemExit(f"não achei {nome} em {FONTE_CS.name} — o C# mudou?")
    return float(m.group(1))


# ---------------------------------------------------------------- áudio

def ler_wav(caminho: Path) -> array:
    with wave.open(str(caminho), "rb") as w:
        a = array("h")
        a.frombytes(w.readframes(w.getnframes()))
        return a


def rms_janelas(amostras: array, janela: int) -> list[float]:
    saida = []
    for i in range(0, len(amostras) - janela + 1, janela):
        soma = 0.0
        for x in amostras[i:i + janela]:
            v = x / 32768.0
            soma += v * v
        saida.append(math.sqrt(soma / janela))
    return saida


def trilha_do_dono(mic: array, sistema: array, limiar: float, janela_s: float,
                   pausa_s: float, minimo_s: float, rotulo: str) -> list[tuple]:
    """O mesmo algoritmo de VozDoDono.Trilha."""
    janela = int(janela_s * TAXA)
    jm = rms_janelas(mic, janela)
    js = rms_janelas(sistema, janela)

    # Vazamento: o quartil baixo do microfone nas janelas em que o sistema fala.
    com_sistema = sorted(m for m, s in zip(jm, js) if s >= limiar)
    vazamento = com_sistema[len(com_sistema) // 4] if com_sistema else 0.0
    if vazamento >= limiar / 5:
        return []

    trechos, inicio, ultima = [], -1.0, -1.0
    for i, r in enumerate(jm):
        t = i * janela_s
        if r >= limiar:
            if inicio < 0:
                inicio = t
            ultima = t + janela_s
        elif inicio >= 0 and t - ultima >= pausa_s:
            if ultima - inicio >= minimo_s:
                trechos.append((inicio, ultima, rotulo))
            inicio = -1.0
    if inicio >= 0 and ultima - inicio >= minimo_s:
        trechos.append((inicio, ultima, rotulo))
    return trechos


# ------------------------------------------------------- pipeline simulado

MINIMO_DO_PEDACO = 0.5   # Montagem.MinimoDoPedaco
MARGEM_DO_DONO = 2.0     # Montagem.MargemDoDono
RMS_MINIMO_DO_DONO = 5e-3


def atribuir_dono_hoje(segs, mic, sistema, rotulo):
    """A regra em produção até 21/08/2026, para o baseline ser o pipeline real.

    Sem isto, o "hoje" da comparação seria um pipeline que nunca existiu — e o
    ganho da trilha sairia inflado pelo que o AtribuirDono já acertava sozinho.
    """
    def rms(a, i0, i1):
        i, j = int(i0 * TAXA), min(int(i1 * TAXA), len(a))
        if j <= i:
            return 0.0
        soma = 0.0
        for x in a[i:j]:
            v = x / 32768.0
            soma += v * v
        return math.sqrt(soma / (j - i))

    for s in segs:
        rm, rs = rms(mic, s["start"], s["end"]), rms(sistema, s["start"], s["end"])
        if rm >= RMS_MINIMO_DO_DONO and rm > rs * MARGEM_DO_DONO:
            s["speaker"] = rotulo


def dono_do_intervalo(ini: float, fim: float, linha: list[tuple], rotulo: str):
    melhor, maior = None, 0.0
    for a, b, quem in linha:
        s = min(fim, b) - max(ini, a)
        if s <= 0:
            continue
        if s > maior or (abs(s - maior) < 1e-6 and quem == rotulo):
            maior, melhor = s, quem
    return melhor


def repartir(seg: dict, linha: list[tuple], rotulo: str) -> list[dict]:
    palavras = seg.get("words") or []
    if len(palavras) < 2:
        return [seg]

    donos = [dono_do_intervalo(p["start"], p["end"], linha, rotulo) for p in palavras]
    for i in range(len(donos)):
        if donos[i] is None:
            donos[i] = donos[i - 1] if i else next((d for d in donos if d), None)

    corridas, inicio = [], 0
    for i in range(1, len(donos) + 1):
        if i < len(donos) and donos[i] == donos[inicio]:
            continue
        dur = palavras[i - 1]["end"] - palavras[inicio]["start"]
        if corridas and dur < MINIMO_DO_PEDACO:
            corridas[-1] = (corridas[-1][0], i - 1)
        else:
            corridas.append((inicio, i - 1))
        inicio = i
    for i in range(len(corridas) - 1, 0, -1):
        if donos[corridas[i][0]] == donos[corridas[i - 1][0]]:
            corridas[i - 1] = (corridas[i - 1][0], corridas[i][1])
            corridas.pop(i)
    if len(corridas) <= 1:
        return [seg]

    pedacos = []
    for k, (de, ate) in enumerate(corridas):
        pedacos.append({
            "start": seg["start"] if k == 0 else palavras[de]["start"],
            "end": seg["end"] if k == len(corridas) - 1 else palavras[ate]["end"],
            "text": "".join(p["text"] for p in palavras[de:ate + 1]),
            "words": palavras[de:ate + 1],
        })
    return pedacos


def atribuir(segs: list[dict], linha: list[tuple], rotulo: str) -> None:
    crus = sorted({q for _, _, q in linha if q != rotulo})
    nomes = {c: f"Speaker {i + 1}" for i, c in enumerate(crus)}
    nomes[rotulo] = rotulo
    for s in segs:
        total: dict[str, float] = defaultdict(float)
        for a, b, quem in linha:
            ov = min(s["end"], b) - max(s["start"], a)
            if ov > 0:
                total[quem] += ov
        if not total:
            s["speaker"] = "Unknown"
            continue
        maior = max(total.values())
        s["speaker"] = (rotulo if abs(total.get(rotulo, -1) - maior) < 1e-6
                        else nomes[max(total, key=total.get)])


# ---------------------------------------------------------------- medição

def medir(segs: list[dict], gemini: list[tuple], rotulo_gemini: str) -> dict:
    r = C.comparar([(s.get("speaker") or "?", s.get("text") or "") for s in segs], gemini)
    ref = {p: e for p, _, _, e in r["erros"]}
    for p in r["certos"]:
        ref[p] = r["mapa"].get(r["a_quem"][p], r["a_quem"][p])

    lim, i = [], 0
    for s in segs:
        n = len(C.palavras(s.get("text") or ""))
        if n:
            lim.append((i, i + n - 1, s))
        i += n

    # Por palavra, e não só por segmento: o corte muda o número de segmentos, e
    # uma porcentagem cujo denominador se move não compara os dois lados.
    dono_palavra_ok = dono_palavra = 0
    for pos in r["certos"]:
        if r["mapa"].get(r["a_quem"][pos], r["a_quem"][pos]) == rotulo_gemini:
            dono_palavra_ok += 1
            dono_palavra += 1
    for _pos, _p, _n, esperado in r["erros"]:
        if esperado == rotulo_gemini:
            dono_palavra += 1

    certo = errado = 0
    dono_ok = dono_total = 0
    for ini, fim, s in lim:
        vistos = [ref[p] for p in range(ini, fim + 1) if p in ref]
        if not vistos:
            continue
        dom = Counter(vistos).most_common(1)[0][0]
        nosso = r["mapa"].get(s.get("speaker"), s.get("speaker"))
        if nosso == dom:
            certo += 1
        else:
            errado += 1
        if dom == rotulo_gemini:
            dono_total += 1
            dono_ok += nosso == dom
    return {
        "palavras_certas": r["acertos"], "palavras": r["alinhadas"],
        "seg_certo": certo, "seg_errado": errado,
        "dono_ok": dono_ok, "dono_total": dono_total, "segmentos": len(segs),
        "dono_palavra_ok": dono_palavra_ok, "dono_palavra": dono_palavra,
    }


def linha_do_pyannote(transcricao: Path, rotulo: str) -> list[tuple]:
    """A diarização de verdade, reaproveitada do resultado que já existe."""
    dados = json.loads(transcricao.read_text(encoding="utf-8"))
    return [(s["start"], s["end"], s["speaker"])
            for s in dados["segments"]
            if s.get("speaker") and s["speaker"] != rotulo]


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("pasta", type=Path)
    p.add_argument("--palavras", type=Path, required=True,
                   help="JSON do ASR com word_timestamps")
    p.add_argument("--dono-no-gemini", default="André Yuri")
    p.add_argument("--limiar", type=float, default=None, help="sobrepõe LimiarDeFala")
    args = p.parse_args()

    rotulo = "You"
    limiar = args.limiar if args.limiar is not None else constante("LimiarDeFala")
    janela = constante("JanelaS")
    pausa = constante("PausaS")
    minimo = constante("MinimoS")

    bruto = json.loads(args.palavras.read_text(encoding="utf-8"))
    segs_asr = next(iter(bruto.values()))["segments"]
    gemini = C.ler_gemini(args.pasta / "gemini.md")
    pyannote = linha_do_pyannote(args.pasta / "transcricao.json", rotulo)

    mic = ler_wav(args.pasta / "mic.wav")
    sistema = ler_wav(args.pasta / "system.wav")
    dono = trilha_do_dono(mic, sistema, limiar, janela, pausa, minimo, rotulo)

    print(f"\n{args.pasta.name}   limiar {limiar}")
    print(f"  trilha do dono: {len(dono)} trechos, "
          f"{sum(b - a for a, b, _ in dono):.0f}s de fala")

    resultados = {}
    for nome, linha, com_regra_velha in (
            ("hoje (AtribuirDono)", pyannote, True),
            ("com a trilha do dono", pyannote + dono, False)):
        segs = []
        for s in segs_asr:
            segs.extend(repartir(dict(s), linha, rotulo))
        atribuir(segs, linha, rotulo)
        if com_regra_velha:
            atribuir_dono_hoje(segs, mic, sistema, rotulo)
        resultados[nome] = medir(segs, gemini, args.dono_no_gemini)

    print(f"\n  {'':<22} {'segs':>6} {'palavra ok':>11} {'seg ok':>8} "
          f"{'dono/seg':>9} {'dono/palavra':>13}")
    for nome, m in resultados.items():
        tot = m["seg_certo"] + m["seg_errado"]
        print(f"  {nome:<22} {m['segmentos']:>6} "
              f"{100 * m['palavras_certas'] / max(m['palavras'], 1):>10.1f}% "
              f"{100 * m['seg_certo'] / max(tot, 1):>7.1f}% "
              f"{100 * m['dono_ok'] / max(m['dono_total'], 1):>8.1f}% "
              f"{100 * m['dono_palavra_ok'] / max(m['dono_palavra'], 1):>12.1f}%"
              f"   ({m['dono_palavra_ok']}/{m['dono_palavra']})")
    print()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
