#!/usr/bin/env python3
"""O Granite Speech 4.1 2B, contra o motor de hoje, nas quatro com gabarito.

**Veredito de 29/09/2026: fora** (BACKLOG, `DIST-5`). Em reunião em pt ele
**traduz para o inglês** em vez de transcrever, alternando no meio do áudio, e
nenhum tamanho de bloco (30 s, 60 s, 3 min) nem pedido (cru, pontuado, em
português) corrigiu isso. O braço com termos abaixo usa o pedido de transcrição
crua com os termos colados; o card pede ``transcribe the speech to text.
Keywords: …``, e ele nunca foi medido de forma válida porque o modelo já caiu
antes.

**Por que ele está sendo medido.** É o primeiro candidato a passada final que
tem **vocabulário**. O Parakeet perdeu do ``large-v3`` na régua de texto
(CONVERGENCIA.md, 11/09/2026), e mesmo que ganhasse ficaria sem o mecanismo que
protege os termos — que é o que a ata mais sente. O Granite põe os termos no
próprio pedido (``Keywords: …``), e o card mede isso em pt: no CV-pt o F1 de
termo vai de 90,0 para 95,0.

**Dois braços, com e sem termos**, pela mesma razão do ``benchmark_vocab.py``:
sem o braço sem termos não se separa "o modelo já acertaria" de "o mecanismo
funcionou". Os termos são o ``initial_prompt`` do projeto, o mesmo que o
``large-v3`` recebe como ``hotwords``.

**É o modelo base, não o ``-plus``.** O ``-plus`` dá tempo por palavra e
falante, mas não pontua, e os tempos só valem até 3,5 min. A pergunta desta
rodada é a primeira que reprovou os outros: o texto dele é melhor? Se não for,
a estrutura de tempo nem entra em pauta.

**Os trechos são o bloco**, de :data:`BLOCO_S`. A régua
(``wer_contra_gemini.py``) só lê o texto, então isso não a afeta; mas quer dizer
que esta saída **não serve** para diarizar, e não deve ser lida como se servisse.

**bf16, e não fp16, mesmo na 2060.** A Turing não tem bf16 nativo, e o fp16
parecia a escolha óbvia — mas **em fp16 ele cospe lixo** em áudio de reunião
(*"i'm sorry i'm sorry…"* por 3 min inteiros), enquanto a amostra do próprio
repositório sai certa. Medido em 29/09/2026: em bf16 o mesmo bloco sai em
português, e até mais rápido (14,6 s contra 52,5 s para 3 min). A ferramenta
imprime o começo do primeiro bloco para um defeito desses aparecer na hora.

Uso::

    V=~/.cache/pulsemeet-medicoes/venv-granite
    $V/bin/python tools/medir_granite.py
    $V/bin/python tools/medir_granite.py --sem-termos

Depois::

    uv run python tools/wer_contra_gemini.py \\
        ~/.cache/pulsemeet-medicoes/varredura-granite/*/granite* \\
        ~/.cache/pulsemeet-medicoes/varredura-parakeet/*/app
"""

from __future__ import annotations

import argparse
import json
import shutil
import time
from pathlib import Path

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
PROJETOS = Path("/mnt/c/Users/andre/.meeting-transcription/projects.json")
SAIDA = Path.home() / ".cache" / "pulsemeet-medicoes" / "varredura-granite"
TAXA = 16000

#: O mesmo bloco do Parakeet, para as duas medições serem comparáveis. O card
#: diz que o ASR aguenta 9 min; 3 min fica longe do teto e do ``max_new_tokens``.
BLOCO_S = 180.0

COM_GABARITO = ("2026-08-20_15-59-20", "2026-08-21_11-00-33",
                "2026-08-25_08-59-22", "2026-08-27_15-28-37")

SISTEMA = ("Knowledge Cutoff Date: April 2024.\nToday's Date: December 19, 2024.\n"
           "You are Granite, developed by IBM. You are a helpful AI assistant")
PEDIDO = "<|audio|>can you transcribe the speech into a written format?"


def termos_do_projeto(gravacao: Path) -> list[str]:
    """O ``initial_prompt`` do projeto da gravação, ou nada."""
    t = json.loads((gravacao / "transcricao.json").read_text(encoding="utf-8"))
    try:
        p = json.loads(PROJETOS.read_text(encoding="utf-8"))["clients"]
        cfg = p[t.get("client", "")]["projects"][t.get("project", "")]
    except (KeyError, OSError):
        return []
    return [x.strip() for x in (cfg.get("initial_prompt") or "").split(",") if x.strip()]


def escrever(pasta: Path, trechos, gemini: Path) -> None:
    pasta.mkdir(parents=True, exist_ok=True)
    (pasta / "transcricao.json").write_text(json.dumps(
        {"segments": [{"start": a, "end": b, "text": t} for a, b, t in trechos]},
        ensure_ascii=False, indent=1), encoding="utf-8")
    if gemini.exists():
        shutil.copy(gemini, pasta / "gemini.md")


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--gravacoes", nargs="*", default=list(COM_GABARITO))
    p.add_argument("--sem-termos", action="store_true")
    p.add_argument("--modelo", default="ibm-granite/granite-speech-4.1-2b")
    p.add_argument("--json", type=Path)
    a = p.parse_args()

    import soundfile as sf
    import torch
    from transformers import AutoModelForSpeechSeq2Seq, AutoProcessor

    proc = AutoProcessor.from_pretrained(a.modelo)
    tok = proc.tokenizer
    m = AutoModelForSpeechSeq2Seq.from_pretrained(
        a.modelo, device_map="cuda", dtype=torch.bfloat16).eval()
    print(f"carregado: {a.modelo} · VRAM {torch.cuda.memory_allocated()/2**30:.2f} GiB",
          flush=True)

    @torch.inference_mode()
    def transcrever(onda, pedido: str) -> str:
        chat = [{"role": "system", "content": SISTEMA},
                {"role": "user", "content": pedido}]
        texto = tok.apply_chat_template(chat, tokenize=False, add_generation_prompt=True)
        ent = proc(texto, torch.from_numpy(onda), device="cuda",
                   return_tensors="pt").to("cuda")
        saida = m.generate(**ent, max_new_tokens=2000, do_sample=False, num_beams=1)
        return tok.decode(saida[0, ent["input_ids"].shape[-1]:],
                          add_special_tokens=False, skip_special_tokens=True).strip()

    resumo = {}
    for g in a.gravacoes:
        origem = ACERVO / g
        termos = [] if a.sem_termos else termos_do_projeto(origem)
        if not a.sem_termos and not termos:
            print(f"{g}: o projeto não tem termos — o braço com termos seria o sem")
            continue
        pedido = PEDIDO + (f" Keywords: {', '.join(termos)}" if termos else "")
        variante = "granite-" + ("com-termos" if termos else "sem-termos")

        onda, taxa = sf.read(str(origem / "mix.wav"), dtype="float32")
        if onda.ndim > 1:
            onda = onda[:, 0]
        assert taxa == TAXA, taxa
        passo = int(BLOCO_S * taxa)
        blocos = [onda[i:i + passo] for i in range(0, len(onda), passo)]
        blocos = [b for b in blocos if len(b) >= taxa]

        trechos, gasto, pico = [], 0.0, 0
        torch.cuda.reset_peak_memory_stats()
        for i, b in enumerate(blocos):
            t0 = time.perf_counter()
            texto = transcrever(b, pedido)
            gasto += time.perf_counter() - t0
            if i == 0:
                print(f"  {g} [{variante}] bloco 1: {texto[:160]!r}", flush=True)
            ini = i * BLOCO_S
            trechos.append((ini, ini + len(b) / taxa, texto))
            print(f"  {g} bloco {i+1}/{len(blocos)}", end="\r", flush=True)
        pico = torch.cuda.max_memory_allocated() / 2**30

        audio = len(onda) / taxa
        escrever(SAIDA / g / variante, trechos, origem / "gemini.md")
        palavras = len(" ".join(t[2] for t in trechos).split())
        print(f"  {g:<22} {audio/60:5.1f} min · {variante} {palavras} palavras, "
              f"{audio/gasto:.2f}x · pico {pico:.2f} GiB · {len(termos)} termos")
        resumo[g] = {"variante": variante, "minutos": audio / 60, "palavras": palavras,
                     "xrt": audio / gasto, "pico_gib": pico, "termos": termos}

    if a.json:
        a.json.write_text(json.dumps(resumo, indent=2, ensure_ascii=False),
                          encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
