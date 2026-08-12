"""Mede WER e CER contra transcrição de referência humana.

Existe porque a fase 0 comparou dois palpites entre si, sem verdade de
referência: quando os motores discordavam, não havia como saber qual errava.
Com um corpus anotado por gente, a pergunta vira mensurável.

Corpus sugerido: **FLEURS pt_br** (`google/fleurs`, split de teste, 919
enunciados). É fala **lida**, não espontânea — mede fidelidade do motor, não
desempenho em reunião. É exatamente o que se quer aqui: isolar a variável
"motor" do ruído da gravação real.

Uso::

    # 1. preparar o corpus (parquet -> wavs + referências)
    python tools/benchmark_wer.py preparar --parquet fleurs_pt_test.parquet \\
        --saida bench/ --n 100

    # 2. rodar o motor de hoje (faster-whisper, GPU)
    python tools/benchmark_wer.py faster-whisper --corpus bench/ --modelo large-v3

    # 3. pontuar qualquer saída contra a referência
    python tools/benchmark_wer.py pontuar --corpus bench/ \\
        --hipoteses bench/hip_faster-whisper.json bench/hip_whispercpp.json
"""

from __future__ import annotations

import argparse
import json
import re
import time
import unicodedata
from pathlib import Path


# ── Normalização ────────────────────────────────────────────────────────
#
# WER só é comparável se os dois lados passarem pelo mesmo normalizador. Este é
# deliberadamente simples e explícito: minúsculas, sem pontuação, sem acento,
# espaços colapsados.
#
# O que ele NÃO faz, e que infla o WER de todos os motores por igual (portanto
# não distorce a *comparação*, só o valor absoluto): não expande números por
# extenso ("2" vs "dois"), não desfaz abreviações, não trata siglas soletradas.

_PONTUACAO = re.compile(r"[^\w\s]", re.UNICODE)
_ESPACOS = re.compile(r"\s+")


def normalizar(texto: str) -> str:
    t = unicodedata.normalize("NFKD", texto.lower())
    t = "".join(c for c in t if not unicodedata.combining(c))
    t = _PONTUACAO.sub(" ", t)
    return _ESPACOS.sub(" ", t).strip()


# ── Distância de edição ─────────────────────────────────────────────────


def _levenshtein(a: list, b: list) -> int:
    """Distância de edição entre duas sequências, em O(min(n,m)) de memória."""
    if len(a) < len(b):
        a, b = b, a
    anterior = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        atual = [i]
        for j, y in enumerate(b, 1):
            atual.append(min(
                anterior[j] + 1,        # remoção
                atual[j - 1] + 1,       # inserção
                anterior[j - 1] + (x != y),  # substituição
            ))
        anterior = atual
    return anterior[-1]


def taxa_de_erro(referencias: list[str], hipoteses: list[str], por_caractere=False) -> dict:
    """WER (ou CER) agregado: soma os erros e divide pelo total de unidades.

    Agregar antes de dividir — e não tirar média das taxas por enunciado — é o
    padrão da área, e evita que um enunciado curto com um erro pese o mesmo que
    um longo e correto.
    """
    erros = unidades = 0
    piores = []
    for ref, hip in zip(referencias, hipoteses):
        r = list(normalizar(ref)) if por_caractere else normalizar(ref).split()
        h = list(normalizar(hip)) if por_caractere else normalizar(hip).split()
        d = _levenshtein(r, h)
        erros += d
        unidades += len(r)
        if r:
            piores.append((d / len(r), ref, hip))
    piores.sort(reverse=True)
    return {
        "taxa": erros / unidades if unidades else 0.0,
        "erros": erros,
        "unidades": unidades,
        "piores": piores[:5],
    }


# ── Preparo do corpus ───────────────────────────────────────────────────


def preparar(args) -> None:
    """Converte o parquet do FLEURS em wavs + um manifesto JSON."""
    import io
    import pandas as pd
    import soundfile as sf

    saida = args.saida
    (saida / "audio").mkdir(parents=True, exist_ok=True)

    df = pd.read_parquet(args.parquet)
    print(f"parquet: {len(df)} linhas, colunas: {list(df.columns)}")

    # FLEURS traz `transcription` (normalizada) e `raw_transcription` (com
    # pontuação e maiúsculas). Usamos a crua: nosso normalizador cuida do resto,
    # e assim o mesmo tratamento cai sobre referência e hipótese.
    col_texto = "raw_transcription" if "raw_transcription" in df.columns else "transcription"

    if args.n and args.n < len(df):
        # Amostra determinística, para a comparação ser repetível.
        df = df.sample(n=args.n, random_state=42).reset_index(drop=True)

    manifesto = []
    total_s = 0.0
    for i, linha in df.iterrows():
        audio = linha["audio"]
        dados = audio["bytes"] if isinstance(audio, dict) else audio
        sinal, sr = sf.read(io.BytesIO(dados))
        caminho = saida / "audio" / f"{i:04d}.wav"
        sf.write(caminho, sinal, sr, subtype="PCM_16")
        dur = len(sinal) / sr
        total_s += dur
        manifesto.append({
            "id": f"{i:04d}",
            "audio": str(caminho),
            "referencia": str(linha[col_texto]),
            "duracao_s": round(dur, 2),
        })

    (saida / "manifesto.json").write_text(
        json.dumps(manifesto, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(f"{len(manifesto)} enunciados, {total_s/60:.1f} min de áudio -> {saida}")


def preparar_coraa(args) -> None:
    """Extrai do CORAA um corpus de fala espontânea em pt-BR.

    Por que CORAA e não FLEURS: o FLEURS é fala **lida** em estúdio, e mede
    fidelidade do motor em condição ideal. O CORAA tem **fala espontânea** de
    conversa e entrevista — hesitação, sobreposição, gente se interrompendo —
    que é o registro de uma reunião. As transcrições são validadas à mão.

    Filtro aplicado: `pt_br`, estilo espontâneo, e ao menos um voto de "nenhum
    problema identificado". Não se filtra ruído de propósito: reunião tem ruído,
    e a referência continua confiável porque foi feita por gente.
    """
    import zipfile
    import pandas as pd
    import soundfile as sf

    saida = args.saida
    (saida / "audio").mkdir(parents=True, exist_ok=True)

    df = pd.read_csv(args.metadata)
    m = (
        df["variety"].eq("pt_br")
        & df["speech_style"].str.contains("Spontaneous", na=False)
        & df["votes_for_no_identified_problem"].ge(1)
        & df["text"].notna()
    )
    cand = df[m].copy()
    # Enunciados de uma ou duas palavras ("aham", "né") não medem nada e
    # inflariam a contagem sem trazer conteúdo.
    cand = cand[cand["text"].str.split().str.len() >= args.min_palavras]
    # Embaralho determinístico: a amostra sai proporcional aos subcorpora (e
    # portanto aos sotaques) sem precisar estratificar à mão, e é repetível.
    cand = cand.sample(frac=1.0, random_state=42).reset_index(drop=True)
    print(f"{len(cand)} candidatos após o filtro; alvo: {args.segundos}s de áudio")

    zf = zipfile.ZipFile(args.zip)
    nomes = {n.split("/")[-1]: n for n in zf.namelist() if n.endswith(".wav")}

    manifesto, total = [], 0.0
    for _, linha in cand.iterrows():
        if total >= args.segundos:
            break
        base = str(linha["file_path"]).split("/")[-1]
        dentro = nomes.get(base)
        if dentro is None:
            continue
        destino = saida / "audio" / base
        with zf.open(dentro) as origem, open(destino, "wb") as fh:
            fh.write(origem.read())
        info = sf.info(destino)
        if info.duration < args.min_duracao:
            destino.unlink()
            continue
        total += info.duration
        manifesto.append({
            "id": base.replace(".wav", ""),
            "audio": str(destino.resolve()),
            "referencia": str(linha["text"]),
            "duracao_s": round(info.duration, 2),
            "subcorpus": str(linha["dataset"]),
        })

    (saida / "manifesto.json").write_text(
        json.dumps(manifesto, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    from collections import Counter
    print(f"{len(manifesto)} enunciados, {total/60:.1f} min -> {saida}")
    print("  por subcorpus:", dict(Counter(m['subcorpus'] for m in manifesto)))


def preparar_coraa_longo(args) -> None:
    """Monta passagens longas concatenando clipes consecutivos da MESMA gravação.

    Motivo: o CORAA vem picotado em enunciados de 2 a 4 segundos, e medir sobre
    eles mede o regime errado. Clipe curto é entrada patológica para o Whisper —
    sem contexto, o modelo cai em frases de alta probabilidade. Medimos WER de
    15,7% na faixa de 0-3s contra 21,8% na de 5-8s, com os motores trocando de
    posição entre as faixas.

    Reunião é fala longa. Concatenar clipes consecutivos da mesma gravação
    reconstrói esse regime **sem perder a referência**: o texto da passagem é a
    junção dos textos dos clipes, todos validados à mão.

    Bônus: passagens longas exercitam a **segmentação**, que é onde a fase 0
    achou o bloqueante (segmentos de 73 s). Clipe a clipe isso fica invisível.
    """
    import io
    import re as _re
    import zipfile
    import numpy as np
    import pandas as pd
    import soundfile as sf

    saida = args.saida
    (saida / "audio").mkdir(parents=True, exist_ok=True)

    df = pd.read_csv(args.metadata)
    df = df[df["variety"].eq("pt_br")
            & df["speech_style"].str.contains("Spontaneous", na=False)
            & df["text"].notna()].copy()

    # A pasta do arquivo identifica a gravação original; o número no nome dá a
    # ordem dentro dela. É o que permite reconstruir a conversa.
    df["gravacao"] = df["file_path"].str.rsplit("/", n=1).str[0]
    df["ordem"] = df["file_path"].str.rsplit("/", n=1).str[1].str.extract(r"^(\d+)").astype(float)
    df = df.dropna(subset=["ordem"]).sort_values(["gravacao", "ordem"])

    zf = zipfile.ZipFile(args.zip)
    dentro = {n.split("/")[-1]: n for n in zf.namelist() if n.endswith(".wav")}

    # Gravações com mais material primeiro: passagem longa exige clipes seguidos.
    grupos = sorted(df.groupby("gravacao"), key=lambda kv: -len(kv[1]))

    manifesto, total = [], 0.0

    def gravar(nome, quadros, textos, dur, subcorpus):
        nonlocal total
        destino = saida / "audio" / f"{nome}.wav"
        sf.write(destino, np.concatenate(quadros), 16000, subtype="PCM_16")
        total += dur
        manifesto.append({
            "id": nome,
            "audio": str(destino.resolve()),
            "referencia": " ".join(textos),
            "duracao_s": round(dur, 2),
            "clipes": len(textos),
            "subcorpus": subcorpus,
        })
        print(f"  {nome[:46]:46s} {dur/60:4.1f} min  {len(textos):3d} clipes")

    for gravacao, g in grupos:
        if total >= args.segundos:
            break
        raiz = _re.sub(r"[^A-Za-z0-9]+", "_", gravacao).strip("_")
        # Teto por gravação: sem ele, o maior subcorpus preenche a cota inteira
        # e o benchmark mede um sotaque só. `dev/sp` tem 1768 clipes e sozinho
        # cobriria os 15 minutos.
        feitas_aqui = 0
        quadros, textos, dur, anterior, n = [], [], 0.0, None, 0

        for _, linha in g.iterrows():
            if total + dur >= args.segundos:
                break
            base = str(linha["file_path"]).split("/")[-1]
            if base not in dentro:
                continue
            # Saltos na numeração são o CORAA tendo descartado clipes (fala
            # ininteligível, outro locutor). Um salto pequeno mantém a passagem
            # localmente coerente — mesmos falantes, mesmo assunto; um salto
            # grande já é outra conversa, e aí a passagem é fechada.
            salto = None if anterior is None else int(linha["ordem"] - anterior)
            if salto is not None and salto > args.max_salto:
                if dur >= args.duracao_passagem * 0.6:
                    gravar(f"{raiz}_{n:02d}", quadros, textos, dur, str(linha["dataset"]))
                    n += 1
                    feitas_aqui += 1
                quadros, textos, dur = [], [], 0.0
                if feitas_aqui >= args.max_por_gravacao:
                    break
            anterior = linha["ordem"]

            # Os wavs do CORAA são float32; `soundfile` lê e devolve em float,
            # e a gravação final sai em PCM16, que é o que o Whisper consome.
            sinal, sr = sf.read(io.BytesIO(zf.read(dentro[base])), dtype="float32")
            if sr != 16000 or sinal.ndim != 1:
                continue
            quadros.append(sinal)
            dur += len(sinal) / 16000
            textos.append(str(linha["text"]).strip())

            if dur >= args.duracao_passagem:
                gravar(f"{raiz}_{n:02d}", quadros, textos, dur, str(linha["dataset"]))
                n += 1
                feitas_aqui += 1
                quadros, textos, dur = [], [], 0.0
                if feitas_aqui >= args.max_por_gravacao:
                    break

        if feitas_aqui < args.max_por_gravacao and dur >= args.duracao_passagem * 0.6:
            gravar(f"{raiz}_{n:02d}", quadros, textos, dur, str(g.iloc[0]["dataset"]))

    (saida / "manifesto.json").write_text(
        json.dumps(manifesto, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    palavras = sum(len(m["referencia"].split()) for m in manifesto)
    print(f"\n{len(manifesto)} passagens, {total/60:.1f} min, {palavras} palavras -> {saida}")


def rodar_whispercpp(args) -> None:
    """Roda o whisper-cli sobre o corpus inteiro numa única invocação.

    O `whisper-cli` aceita vários arquivos posicionalmente e **carrega o modelo
    uma vez só**. Chamar o binário por enunciado recarregaria 1,1 GB a cada
    clipe, e o tempo medido viraria tempo de carga, não de inferência.

    Com `-oj`, cada entrada produz um `<audio>.json` ao lado do wav.
    """
    import os
    import subprocess

    manifesto = json.loads((args.corpus / "manifesto.json").read_text(encoding="utf-8"))
    env = {**os.environ,
           "LD_LIBRARY_PATH": str(args.bin.resolve().parent) + ":" +
                              os.environ.get("LD_LIBRARY_PATH", "")}

    cmd = [str(args.bin.resolve()), "-m", str(args.modelo.resolve()),
           "-l", args.idioma, "-t", str(args.threads), "-bs", "5", "-nt", "-oj"]
    cmd += [item["audio"] for item in manifesto]

    t0 = time.time()
    r = subprocess.run(cmd, capture_output=True, text=True, env=env)
    gasto = time.time() - t0
    if r.returncode != 0:
        raise SystemExit(f"whisper-cli falhou ({r.returncode}):\n{r.stderr[-2000:]}")

    hip = []
    for item in manifesto:
        saida_json = Path(item["audio"]).with_suffix(".wav.json")
        if not saida_json.is_file():
            saida_json = Path(item["audio"] + ".json")
        texto = ""
        if saida_json.is_file():
            d = json.loads(saida_json.read_text(encoding="utf-8"))
            texto = " ".join(s.get("text", "").strip()
                             for s in d.get("transcription", []))
        hip.append({"id": item["id"], "texto": texto.strip()})

    vazios = sum(1 for h in hip if not h["texto"])
    if vazios:
        print(f"  aviso: {vazios} de {len(hip)} enunciados sem texto")

    destino = args.corpus / f"hip_whispercpp-{args.modelo.stem}.json"
    destino.write_text(json.dumps(
        {"motor": f"whisper.cpp {args.modelo.stem}", "segundos": round(gasto, 1),
         "hipoteses": hip}, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{gasto:.0f}s -> {destino}")


# ── Motor de hoje ───────────────────────────────────────────────────────


def rodar_faster_whisper(args) -> None:
    """Roda o transcritor que o app usa hoje, para produzir o baseline real."""
    import sys
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
    from src.transcription.faster_whisper_transcriber import FasterWhisperTranscriber

    manifesto = json.loads((args.corpus / "manifesto.json").read_text(encoding="utf-8"))
    t = FasterWhisperTranscriber(model_size=args.modelo)
    t.load_model()

    hip, t0 = [], time.time()
    for i, item in enumerate(manifesto, 1):
        r = t.transcribe(item["audio"], language=args.idioma)
        hip.append({"id": item["id"], "texto": " ".join(s.text for s in r.segments)})
        if i % 10 == 0:
            print(f"  {i}/{len(manifesto)}")
    gasto = time.time() - t0

    destino = args.corpus / f"hip_faster-whisper-{args.modelo}.json"
    destino.write_text(json.dumps(
        {"motor": f"faster-whisper {args.modelo}", "segundos": round(gasto, 1),
         "hipoteses": hip}, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{gasto:.0f}s -> {destino}")


def coletar(args) -> None:
    """Junta os `<id>.json` que o `whisper-cli -oj` deixou numa pasta.

    Serve para trazer de volta o resultado de uma rodada feita fora daqui — na
    prática, a rodada com GPU no Windows, que é onde o app vai viver. O tempo
    total vem de um `_tempo.txt` ao lado, escrito pelo script PowerShell.
    """
    manifesto = json.loads((args.corpus / "manifesto.json").read_text(encoding="utf-8"))

    hip = []
    for item in manifesto:
        arq = args.saida / f"{item['id']}.json"
        texto = ""
        if arq.is_file():
            d = json.loads(arq.read_text(encoding="utf-8"))
            texto = " ".join(s.get("text", "").strip()
                             for s in d.get("transcription", []))
        hip.append({"id": item["id"], "texto": texto.strip()})

    vazios = sum(1 for h in hip if not h["texto"])
    if vazios:
        print(f"aviso: {vazios} de {len(hip)} passagens sem texto")

    seg = None
    marca = args.saida / "_tempo.txt"
    if marca.is_file():
        try:
            seg = float(marca.read_text().strip())
        except ValueError:
            pass

    destino = args.corpus / f"hip_{args.rotulo}.json"
    destino.write_text(json.dumps(
        {"motor": args.rotulo, "segundos": seg, "hipoteses": hip},
        ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{len(hip)} passagens -> {destino}")


def rodar_fw_vad(args) -> None:
    """Roda o faster-whisper variando os parâmetros de VAD e de alucinação.

    O app fixa ``vad_filter=True`` com ``threshold=0.35``,
    ``min_silence_duration_ms=500`` e ``hallucination_silence_threshold=2.0``.
    Esses valores nunca foram variados contra uma referência — foram escolhidos
    por raciocínio.

    A suspeita a testar: nas passagens muito emendadas o faster-whisper perdeu
    70% do conteúdo (386 palavras de referência viraram 111) enquanto o
    whisper.cpp não perdeu. Se a causa for o VAD descartando o que não reconhece
    como fala contínua, **isso é melhoria disponível no app de hoje**, sem trocar
    motor nenhum.

    Chama o ``WhisperModel`` direto em vez de passar pelo transcritor do app,
    que não expõe esses parâmetros.
    """
    from faster_whisper import WhisperModel
    from src.utils.gpu_detector import is_cuda_available, get_optimal_compute_type

    manifesto = json.loads((args.corpus / "manifesto.json").read_text(encoding="utf-8"))
    dispositivo = "cuda" if is_cuda_available() else "cpu"
    modelo = WhisperModel(args.modelo, device=dispositivo,
                          compute_type=get_optimal_compute_type()
                          if dispositivo == "cuda" else "int8")

    kwargs = dict(language=args.idioma, beam_size=5,
                  condition_on_previous_text=False, word_timestamps=True)
    if args.sem_vad:
        kwargs["vad_filter"] = False
    else:
        kwargs["vad_filter"] = True
        kwargs["vad_parameters"] = dict(min_silence_duration_ms=args.min_silencio,
                                        max_speech_duration_s=25,
                                        threshold=args.vad_threshold)
    if not args.sem_filtro_alucinacao:
        kwargs["hallucination_silence_threshold"] = 2.0

    hip, t0 = [], time.time()
    for item in manifesto:
        segs, _ = modelo.transcribe(item["audio"], **kwargs)
        hip.append({"id": item["id"], "texto": " ".join(s.text for s in segs)})
        print(f"  {item['id']}")
    gasto = time.time() - t0

    partes = ["sem-vad" if args.sem_vad else f"vad{args.vad_threshold}"]
    if args.min_silencio != 500:
        partes.append(f"sil{args.min_silencio}")
    if args.sem_filtro_alucinacao:
        partes.append("sem-filtro-aluc")
    tag = "-".join(partes)

    destino = args.corpus / f"hip_fw-{tag}.json"
    destino.write_text(json.dumps(
        {"motor": f"faster-whisper {tag}", "segundos": round(gasto, 1), "hipoteses": hip},
        ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"{gasto:.0f}s -> {destino}")


# ── Pontuação ───────────────────────────────────────────────────────────


def pontuar(args) -> None:
    manifesto = json.loads((args.corpus / "manifesto.json").read_text(encoding="utf-8"))
    refs = {m["id"]: m["referencia"] for m in manifesto}
    total_audio = sum(m["duracao_s"] for m in manifesto)

    print(f"corpus: {len(manifesto)} enunciados, {total_audio/60:.1f} min\n")
    print(f"{'motor':38s} {'WER':>8s} {'CER':>8s} {'tempo':>9s} {'xRT':>7s}")
    print("-" * 74)

    resultados = []
    for caminho in args.hipoteses:
        d = json.loads(caminho.read_text(encoding="utf-8"))
        pares = [(refs[h["id"]], h["texto"]) for h in d["hipoteses"] if h["id"] in refs]
        r = [p[0] for p in pares]
        h = [p[1] for p in pares]
        wer = taxa_de_erro(r, h)
        cer = taxa_de_erro(r, h, por_caractere=True)
        seg = d.get("segundos")
        xrt = f"{total_audio/seg:.1f}x" if seg else "—"
        print(f"{d['motor'][:38]:38s} {100*wer['taxa']:7.2f}% {100*cer['taxa']:7.2f}% "
              f"{(f'{seg:.0f}s' if seg else '—'):>9s} {xrt:>7s}")
        resultados.append((d["motor"], wer))

    if args.por_item:
        # Média agregada esconde passagem patológica. Numa primeira montagem do
        # corpus longo, duas passagens do NURC-Recife (emenda a cada 1,6 s, por
        # os clipes originais serem curtíssimos) puxaram o WER de ~14% para 29%
        # sozinhas. Sem esta visão, a conclusão teria sido sobre o motor quando
        # o defeito era do corpus.
        print()
        larg = max(len(m["id"]) for m in manifesto)
        cab = f"{'passagem':{larg}s} {'dur':>6s} {'ref':>6s}"
        for c in args.hipoteses:
            d = json.loads(c.read_text(encoding="utf-8"))
            cab += f" {d['motor'][:14]:>15s}"
        print(cab)
        cargas = [json.loads(c.read_text(encoding="utf-8")) for c in args.hipoteses]
        mapas = [{h["id"]: h["texto"] for h in d["hipoteses"]} for d in cargas]
        for m in manifesto:
            linha = f"{m['id']:{larg}s} {m['duracao_s']:5.0f}s"
            r = normalizar(m["referencia"]).split()
            linha += f" {len(r):6d}"
            for mp in mapas:
                h = normalizar(mp.get(m["id"], "")).split()
                linha += f" {100*_levenshtein(r, h)/max(1,len(r)):14.1f}%"
            print(linha)

    if args.piores:
        for motor, wer in resultados:
            print(f"\n--- piores enunciados: {motor} ---")
            for taxa, ref, hip in wer["piores"]:
                print(f"  WER {100*taxa:5.1f}%")
                print(f"    ref: {ref[:100]}")
                print(f"    hip: {hip[:100]}")


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest="cmd", required=True)

    a = sub.add_parser("preparar", help="parquet do FLEURS -> wavs + manifesto")
    a.add_argument("--parquet", type=Path, required=True)
    a.add_argument("--saida", type=Path, required=True)
    a.add_argument("--n", type=int, default=0, help="0 = tudo")
    a.set_defaults(func=preparar)

    ac = sub.add_parser("preparar-coraa", help="CORAA (fala espontânea pt-BR) -> corpus")
    ac.add_argument("--zip", type=Path, required=True, help="dev.zip ou test.zip do CORAA")
    ac.add_argument("--metadata", type=Path, required=True, help="metadata_*_final.csv")
    ac.add_argument("--saida", type=Path, required=True)
    ac.add_argument("--segundos", type=float, default=300.0, help="alvo de áudio")
    ac.add_argument("--min-palavras", type=int, default=5)
    ac.add_argument("--min-duracao", type=float, default=2.0)
    ac.set_defaults(func=preparar_coraa)

    al = sub.add_parser("preparar-coraa-longo",
                        help="CORAA -> passagens longas (regime de reunião)")
    al.add_argument("--zip", type=Path, required=True)
    al.add_argument("--metadata", type=Path, required=True)
    al.add_argument("--saida", type=Path, required=True)
    al.add_argument("--segundos", type=float, default=900.0, help="alvo total")
    al.add_argument("--duracao-passagem", type=float, default=180.0)
    al.add_argument("--max-por-gravacao", type=int, default=2,
                    help="teto de passagens por gravação, para diversificar sotaques")
    al.add_argument("--max-salto", type=int, default=3,
                    help="maior salto de numeração tolerado dentro de uma passagem")
    al.set_defaults(func=preparar_coraa_longo)

    w = sub.add_parser("whispercpp", help="roda o whisper-cli sobre o corpus")
    w.add_argument("--corpus", type=Path, required=True)
    w.add_argument("--bin", type=Path, required=True, help="caminho do whisper-cli")
    w.add_argument("--modelo", type=Path, required=True)
    w.add_argument("--idioma", default="pt")
    w.add_argument("--threads", type=int, default=8)
    w.set_defaults(func=rodar_whispercpp)

    b = sub.add_parser("faster-whisper", help="roda o motor de hoje")
    b.add_argument("--corpus", type=Path, required=True)
    b.add_argument("--modelo", default="large-v3")
    b.add_argument("--idioma", default="pt")
    b.set_defaults(func=rodar_faster_whisper)

    v = sub.add_parser("fw-vad", help="faster-whisper variando VAD e filtro de alucinação")
    v.add_argument("--corpus", type=Path, required=True)
    v.add_argument("--modelo", default="large-v3")
    v.add_argument("--idioma", default="pt")
    v.add_argument("--sem-vad", action="store_true")
    v.add_argument("--vad-threshold", type=float, default=0.35)
    v.add_argument("--min-silencio", type=int, default=500)
    v.add_argument("--sem-filtro-alucinacao", action="store_true")
    v.set_defaults(func=rodar_fw_vad)

    co = sub.add_parser("coletar", help="junta os <id>.json de uma rodada externa (ex.: GPU no Windows)")
    co.add_argument("--corpus", type=Path, required=True)
    co.add_argument("--saida", type=Path, required=True, help="pasta com os <id>.json")
    co.add_argument("--rotulo", required=True, help="nome do motor no relatório")
    co.set_defaults(func=coletar)

    c = sub.add_parser("pontuar", help="WER/CER de uma ou mais hipóteses")
    c.add_argument("--corpus", type=Path, required=True)
    c.add_argument("--hipoteses", type=Path, nargs="+", required=True)
    c.add_argument("--piores", action="store_true")
    c.add_argument("--por-item", action="store_true",
                   help="WER por passagem — revela item patológico que a média esconde")
    c.set_defaults(func=pontuar)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
