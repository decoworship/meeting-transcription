"""Compara a saída de dois motores de transcrição sobre o mesmo áudio.

Existe para responder à pergunta da fase 0 do plano: *migrar para whisper.cpp e
sherpa-onnx custa qualidade?* Não há verdade de referência — nenhuma dessas
reuniões foi transcrita à mão — então todas as métricas aqui são comparativas,
não absolutas. O que elas medem:

* **cobertura** — palavras, segmentos e a maior lacuna sem fala. Um motor que
  engole trechos aparece como menos palavras e lacuna maior.
* **vocabulário** — quantas vezes cada nome e jargão do ``initial_prompt``
  aparece. É a métrica que mais importa aqui: foi um "Dimi" virando "Jimmy" que
  originou toda a investigação.
* **vazamento de idioma** — trechos em inglês no meio do português, que é como
  o Whisper falha quando perde o fio.
* **divergência** — alinhado por tempo, onde os dois discordam de fato.

Uso::

    python tools/compare_engines.py \\
        --baseline data/meeting-transcription/history/1786024581252.json \\
        --candidate out/q8_11min.json --label q8_0 \\
        --config data/meeting-transcription/config.json
"""

from __future__ import annotations

import argparse
import json
import re
import unicodedata
from dataclasses import dataclass, field
from difflib import SequenceMatcher
from pathlib import Path


# Palavras funcionais que praticamente não existem em português e denunciam um
# trecho que escorregou para o inglês. Deliberadamente curtas e frequentes:
# a intenção é detectar a mudança de idioma, não classificar a frase.
EN_MARKERS = {
    "the", "and", "is", "are", "was", "were", "this", "that", "with", "have",
    "has", "you", "your", "they", "there", "what", "when", "which", "would",
    "about", "because", "people", "something", "going", "right",
}


def desacentuar(s: str) -> str:
    """Minúsculas sem acento — mesma normalização que o app usa em recordings.py."""
    s = unicodedata.normalize("NFKD", s.lower())
    return "".join(c for c in s if not unicodedata.combining(c))


@dataclass
class Transcript:
    """Uma transcrição, venha ela do histórico do app ou do whisper.cpp."""

    label: str
    segments: list[dict] = field(default_factory=list)

    @property
    def text(self) -> str:
        return " ".join(s["text"].strip() for s in self.segments)

    @property
    def words(self) -> list[str]:
        return re.findall(r"[\w'-]+", self.text, flags=re.UNICODE)

    @property
    def duration(self) -> float:
        return max((s["end"] for s in self.segments), default=0.0)

    def maior_lacuna(self) -> tuple[float, float]:
        """(duração, início) do maior intervalo sem nenhum segmento."""
        if not self.segments:
            return (0.0, 0.0)
        pior, onde, anterior = 0.0, 0.0, 0.0
        for s in sorted(self.segments, key=lambda x: x["start"]):
            gap = s["start"] - anterior
            if gap > pior:
                pior, onde = gap, anterior
            anterior = max(anterior, s["end"])
        return (pior, onde)

    def palavras_em_ingles(self) -> int:
        return sum(1 for w in self.words if desacentuar(w) in EN_MARKERS)


def carregar_historico(path: Path, ini: float = 0.0, fim: float = 1e9,
                       offset: float = 0.0) -> Transcript:
    """Lê uma entrada do histórico do app (baseline: faster-whisper + pyannote).

    ``ini``/``fim``/``offset`` existem para comparar contra um recorte do áudio:
    o histórico guarda tempos absolutos da gravação inteira, enquanto o
    whisper.cpp rodado sobre um trecho começa do zero.
    """
    d = json.loads(path.read_text(encoding="utf-8"))
    segs = []
    for s in d.get("segments", []):
        a, b = float(s["start"]), float(s["end"])
        if b <= ini or a >= fim:
            continue
        segs.append(
            {
                "start": max(a, ini) - offset,
                "end": min(b, fim) - offset,
                "text": s.get("text", ""),
                "speaker": s.get("speaker"),
            }
        )
    return Transcript(label=f"baseline ({path.name})", segments=segs)


def carregar_whisper_cpp(path: Path, label: str) -> Transcript:
    """Lê o JSON que o ``whisper-cli -oj`` produz."""
    d = json.loads(path.read_text(encoding="utf-8"))
    segs = []
    for s in d.get("transcription", []):
        off = s.get("offsets", {})
        segs.append(
            {
                # offsets vêm em milissegundos
                "start": off.get("from", 0) / 1000.0,
                "end": off.get("to", 0) / 1000.0,
                "text": s.get("text", ""),
                "speaker": None,
            }
        )
    return Transcript(label=label, segments=segs)


def termos_do_prompt(config_path: Path) -> list[str]:
    """Extrai do ``initial_prompt`` os termos que valem a pena conferir.

    Pega nomes próprios (maiúscula no meio da frase), identificadores com
    underscore e siglas em caixa alta. É heurística, mas o prompt foi escrito
    justamente para listar essas coisas, então acerta bem.
    """
    prompt = json.loads(config_path.read_text(encoding="utf-8")).get("initial_prompt", "")
    termos: set[str] = set()
    termos.update(re.findall(r"\b[a-z]+_[a-z_]+\b", prompt))          # tbeeg_cubo_...
    termos.update(re.findall(r"\b[A-Z]{2,}\b", prompt))                # CMF, IAM, ANATEL
    termos.update(re.findall(r"\b[A-Z][a-zà-ú]{2,}\b", prompt))        # Dimi, Vivo, NoBill
    # Ruído previsível: começos de frase e palavras comuns capitalizadas.
    ruido = {"Reunião", "Participam", "Falamos", "Sistemas", "Tabelas", "Campos", "Bases"}
    return sorted(t for t in termos if t not in ruido)


def contar_termos(t: Transcript, termos: list[str]) -> dict[str, int]:
    texto = desacentuar(t.text)
    saida = {}
    for termo in termos:
        alvo = desacentuar(termo)
        saida[termo] = len(re.findall(rf"\b{re.escape(alvo)}\b", texto))
    return saida


def divergencia(a: Transcript, b: Transcript) -> float:
    """Similaridade global entre os dois textos, 0..1.

    Comparar o texto inteiro (e não segmento a segmento) é proposital: os dois
    motores segmentam de forma diferente, e alinhar por segmento mediria a
    segmentação, não o conteúdo.

    Duas armadilhas evitadas aqui, ambas capazes de reportar 0,15 onde o valor
    real é 0,8:

    * comparar **palavras**, não caracteres — casar caractere a caractere pune
      qualquer diferença de pontuação como se fosse conteúdo perdido;
    * ``autojunk=False`` — com a heurística ligada (o padrão), o SequenceMatcher
      trata como "lixo" os elementos que aparecem em mais de 1% de uma sequência
      longa. Em texto natural isso inclui as palavras mais comuns, justamente as
      que ancoram o alinhamento.
    """
    pa = [desacentuar(w) for w in a.words]
    pb = [desacentuar(w) for w in b.words]
    return SequenceMatcher(None, pa, pb, autojunk=False).ratio()


def linha(rotulo: str, *valores: object) -> str:
    return f"{rotulo:<28}" + "".join(f"{str(v):>22}" for v in valores)


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--baseline", type=Path, required=True, help="JSON do histórico do app")
    p.add_argument("--candidate", type=Path, action="append", required=True,
                   help="JSON do whisper.cpp (pode repetir)")
    p.add_argument("--label", action="append", default=[], help="rótulo de cada candidato")
    p.add_argument("--config", type=Path, help="config.json, para extrair o vocabulário")
    p.add_argument("--vocab-detail", action="store_true", help="listar termo a termo")
    p.add_argument("--window", type=float, nargs=2, default=[0.0, 1e9],
                   help="recorte (s) do histórico a considerar")
    p.add_argument("--offset", type=float, default=0.0,
                   help="quanto subtrair dos tempos do histórico para casar com o recorte")
    args = p.parse_args()

    base = carregar_historico(args.baseline, args.window[0], args.window[1], args.offset)
    rotulos = args.label + [c.stem for c in args.candidate[len(args.label):]]
    cands = [carregar_whisper_cpp(c, r) for c, r in zip(args.candidate, rotulos)]
    todos = [base] + cands

    print("=" * (28 + 22 * len(todos)))
    print(linha("", *(t.label.split(" ")[0] for t in todos)))
    print("=" * (28 + 22 * len(todos)))

    print(linha("duração coberta (s)", *(f"{t.duration:.1f}" for t in todos)))
    print(linha("segmentos", *(len(t.segments) for t in todos)))
    print(linha("palavras", *(len(t.words) for t in todos)))
    print(linha("palavras/segmento", *(f"{len(t.words)/max(1,len(t.segments)):.1f}" for t in todos)))

    lacunas = [t.maior_lacuna() for t in todos]
    print(linha("maior lacuna (s)", *(f"{d:.1f} @ {o:.0f}s" for d, o in lacunas)))
    print(linha("marcadores de inglês", *(t.palavras_em_ingles() for t in todos)))

    if args.config and args.config.is_file():
        termos = termos_do_prompt(args.config)
        contagens = [contar_termos(t, termos) for t in todos]
        print()
        print(linha("termos do vocabulário", *(f"{sum(1 for v in c.values() if v)}/{len(termos)}"
                                               for c in contagens)))
        print(linha("ocorrências totais", *(sum(c.values()) for c in contagens)))

        if args.vocab_detail:
            print()
            print("  termo a termo (só onde há diferença):")
            for termo in termos:
                vals = [c[termo] for c in contagens]
                if len(set(vals)) > 1:
                    print("  " + linha(f"  {termo}", *vals))

    print()
    for c in cands:
        print(linha(f"similaridade vs baseline", f"{divergencia(base, c):.3f}") + f"  ({c.label})")


if __name__ == "__main__":
    main()
