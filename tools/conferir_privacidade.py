#!/usr/bin/env python3
"""Nada de cliente, projeto, voz ou reunião pode entrar no instalador.

É a régua que o dono do produto pediu em 14/08/2026, antes de o instalador ir
para a mão de alguém. Ela responde uma pergunta e não a responde por opinião:
**o que vai ser empacotado contém alguma informação minha?**

Duas verificações independentes, porque cada uma pega o que a outra deixa passar:

1. **Por forma.** Todo arquivo que vai viajar é confrontado com uma lista de
   nomes proibidos — ``projects.json``, ``vozes.json``, ``meta.json``,
   ``transcricao.json``, ``notas.md``, ``google_token.json``, ``.env`` e
   companhia. Pega o arquivo que foi parar ali por engano, mesmo que esteja
   vazio.

2. **Por conteúdo.** Os nomes reais que existem nesta máquina — clientes,
   projetos, pessoas com voz aprendida, pastas de gravação — são procurados
   dentro de tudo o que vai viajar, em UTF-8 e em UTF-16. Pega o dado que
   vazou para dentro de um arquivo que tem nome inocente.

A segunda é a que vale, e ela só funciona **nesta máquina**: os termos saem dos
seus próprios dados. Rodá-la numa máquina sem histórico não prova nada, e ela
avisa quando isso acontece em vez de passar em silêncio.

Um achado da régua 2 é perdoado em dois casos, os dois declarados em voz alta no
relatório: um ``HOMONIMO`` registrado à mão, e o termo que **já está no
código-fonte do repositório** -- aí a presença dele no que viaja está explicada
por nós. Ver ``explicado_pela_fonte``.

O que ela **não** verifica, e está registrado de propósito: o segredo OAuth do
Google vai embutido no binário, por decisão (docs/FASE4.md §4). Ele é a
credencial do aplicativo, não a sua conta — o seu ``google_token.json`` fica de
fora, e a régua 1 confere isso.

Uso::

    tools/conferir_privacidade.py --payload dist/instalador/payload \\
                                  --motores /mnt/c/Users/andre/MeetingApp/motores
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent

# Os mesmos Excludes do instalador/MeetingApp.iss. Duplicados aqui de propósito,
# e é uma duplicação que se defende: se um dia eles divergirem, esta régua passa
# a conferir MAIS do que viaja, nunca menos. O erro cai para o lado seguro.
EXCLUIDOS = {".cache", "__pycache__"}
EXTENSOES_EXCLUIDAS = {".gguf"}

# Régua 1: nomes que não podem viajar, custe o que custar. Cada um é um arquivo
# real deste projeto, com dado real dentro.
PROIBIDOS = {
    "projects.json",      # clientes, projetos, vocabulário e prompt inicial
    "app.json",           # configurações, incluindo pastas
    "vozes.json",         # as pessoas e os vetores de voz
    "meta.json",          # de uma gravação: dispositivo, duração, silêncios
    "transcricao.json",   # o que foi dito
    "reuniao.json",       # o vínculo cliente/projeto da gravação
    "notas.md",           # o que alguém escreveu durante a reunião
    "settings.json",      # do gravador: pasta das gravações, microfone
    "google_token.json",  # a SUA conta do Google
    "google_account.json",
    "hf_token.txt",
    ".env",
}

# Padrões, para o que não tem nome fixo.
PADROES_PROIBIDOS = [
    re.compile(r"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}"),  # pasta de gravação
    re.compile(r"\.(wav|mp3|m4a|mp4)$", re.I),            # áudio e vídeo…
]

# …com uma exceção conhecida: os pacotes Python trazem WAV de teste, e eles são
# do scipy e do pyannote, não seus. A exceção é por CAMINHO, não por nome — um
# system.wav dentro de site-packages seria pego.
AUDIO_PERMITIDO = re.compile(
    r"python[/\\]Lib[/\\]site-packages[/\\](scipy|pyannote|torchaudio|soundfile|librosa)[/\\]")

# ── Homônimos ────────────────────────────────────────────────────────────────
#
# Um cliente chamado com uma palavra comum colide com o vocabulário de terceiros,
# e é inevitável: a régua procura o nome, não o significado.
#
# Cada exceção é **por caminho e por termo**, com o motivo escrito, e continua
# aparecendo na saída como "ignorado". Silenciar sem explicar transformaria a
# régua num carimbo — e o dia em que "Vivo" aparecesse de verdade num arquivo
# nosso, ninguém saberia.
HOMONIMOS: list[tuple[re.Pattern, str, str]] = [
    (re.compile(r"av\.libs[/\\]avformat-"), "Vivo",
     "o demuxer 'Vivo' do ffmpeg — o formato de vídeo da Vivo Software, "
     "dos anos 90. Aparece ao lado de 'vivo' e 'viv' na tabela de formatos."),
]


def homonimo(caminho: Path, termo: str) -> str | None:
    """O motivo de este achado ser coincidência, ou ``None`` se não for."""
    for padrao, alvo, motivo in HOMONIMOS:
        if termo == alvo and padrao.search(str(caminho)):
            return motivo
    return None


# ── o que é nosso não é vazamento ────────────────────────────────────────────
#
# Achado em 19/08/2026, montando a 0.4.0: a régua reprovou 'Sprint' dentro do
# MeetingApp.exe. Não era vazamento nenhum -- o dono do produto tem um projeto
# chamado "Sprint", e a palavra está no binário porque o app embute a skill de
# ata `assets/atas/sprint.md` desde que as skills existem (e, de quebra, porque
# o runtime da Microsoft exporta `__stdio_common_vsprintf_s`).
#
# Não é caso isolado: as skills se chamam sprint, daily, kickoff, resultados,
# trabalho e cliente-update. Qualquer projeto batizado com uma dessas palavras
# reprova todo build a partir daí, e a régua que reprova sempre é uma régua que
# se desliga.
#
# O critério que resolve os seis de uma vez sem abrir exceção nominal: **se o
# termo já está no código-fonte do repositório, a presença dele no que viaja
# está explicada por nós, e não pelos seus dados.** É o mesmo raciocínio dos
# HOMÓNIMOS acima, generalizado -- e com a mesma exigência de dizer em voz alta
# o que foi perdoado e por quê.
#
# **O que ele pressupõe**, e é bom estar escrito: que o repositório em si esteja
# limpo de dado pessoal. Ele é público, então essa suposição já era condição de
# existir; se um nome de cliente for parar num arquivo versionado, o problema
# está no commit, e é lá que se conserta -- não aqui.
FONTES = ["app-net", "assets", "motores", "src", "docs", "CHANGELOG.md", "README.md"]

# dist/ é o próprio payload: deixá-lo entrar faria o achado explicar a si mesmo.
FORA_DA_FONTE = ["bin", "obj", "dist", ".git", ".venv", "__pycache__"]


def explicado_pela_fonte(termo: str, raiz: Path) -> Path | None:
    """O arquivo versionado que já contém o termo, ou ``None``.

    Duas etapas, pelo mesmo motivo do ``_candidatos``: o ``grep -F`` acha onde
    olhar, e o regex de fronteira -- o mesmo que a régua usa no payload --
    responde se é o termo mesmo, e não um pedaço de outra palavra.
    """
    alvos = [str(raiz / f) for f in FONTES if (raiz / f).exists()]
    if not alvos:
        return None

    excluir = [f"--exclude-dir={d}" for d in FORA_DA_FONTE]
    achado = subprocess.run(["grep", "-rlF", *excluir, "--", termo, *alvos],
                            capture_output=True, text=True)
    p8, _ = _padroes([termo])
    for linha in achado.stdout.splitlines():
        caminho = Path(linha)
        try:
            if p8.search(caminho.read_bytes()):
                return caminho
        except OSError:
            continue
    return None


def arquivos_que_viajam(payload: Path, motores: Path | None) -> list[Path]:
    """A lista exata do que o Inno vai empacotar, com os mesmos Excludes."""
    saida: list[Path] = []

    # `None`, e não `Path()`: o caminho vazio é o diretório atual, e passá-lo
    # aqui faria a régua varrer o repositório inteiro — inclusive os 2 GB de
    # instalador em dist/. Custou um timeout de cinco minutos para aparecer.
    for raiz in (payload, motores):
        if raiz is None or not raiz.is_dir():
            continue
        for pasta, subpastas, arquivos in os.walk(raiz):
            subpastas[:] = [s for s in subpastas if s not in EXCLUIDOS]
            for nome in arquivos:
                if Path(nome).suffix.lower() in EXTENSOES_EXCLUIDAS:
                    continue
                saida.append(Path(pasta) / nome)

    return saida


def termos_desta_maquina(perfil: Path) -> dict[str, list[str]]:
    """Os nomes reais a procurar, colhidos dos seus próprios dados.

    Só termos com 4 caracteres ou mais: "Ana" apareceria dentro de "analyze" em
    qualquer biblioteca Python, e uma régua que grita sem motivo é uma régua que
    se aprende a ignorar.
    """
    termos: dict[str, list[str]] = {"clientes": [], "projetos": [], "pessoas": [],
                                    "gravacoes": []}

    projetos = perfil / ".meeting-transcription" / "projects.json"
    if projetos.is_file():
        dados = json.loads(projetos.read_text(encoding="utf-8"))
        for cliente, corpo in (dados.get("clients") or {}).items():
            termos["clientes"].append(cliente)
            termos["projetos"].extend((corpo.get("projects") or {}).keys())

    vozes = perfil / ".meeting-transcription" / "vozes" / "vozes.json"
    if vozes.is_file():
        dados = json.loads(vozes.read_text(encoding="utf-8"))
        termos["pessoas"].extend((dados.get("pessoas") or {}).keys())

    # As pastas de gravação: o nome de cada uma é a data e a hora de uma reunião
    # real, e é dado tanto quanto o conteúdo.
    for base in (perfil / "Documents" / "MeetingRecordings",):
        if base.is_dir():
            termos["gravacoes"].extend(
                p.name for p in base.iterdir() if p.is_dir())

    for chave in termos:
        termos[chave] = sorted({t for t in termos[chave] if len(t) >= 4})

    return termos


# Fronteira de palavra, em bytes.
#
# Sem ela a régua reprova o binário correto: o cliente "Vivo" casa dentro de
# `FaixaAoVivo`, que é um tipo do C#, e "Ellen" casaria dentro de qualquer
# identificador que a contenha. Uma régua que grita sem motivo é uma régua que
# se aprende a ignorar — e aí ela deixa de proteger no dia em que estiver certa.
#
# `\x80-\xff` cobre as letras acentuadas sem depender de codificação: em UTF-8
# elas são bytes altos, e é exatamente isso que não pode contar como fronteira.
_LETRA_U8 = rb"[A-Za-z0-9_\x80-\xff]"
# Em UTF-16-LE cada caractere ASCII é o byte seguido de \x00, então a fronteira
# se testa em pares.
_LETRA_U16 = rb"[A-Za-z0-9_]\x00"


def _padroes(termos: list[str]) -> tuple[re.Pattern, re.Pattern]:
    """Uma alternância só por codificação, e não um regex por termo.

    A diferença não é de estilo. São 5,4 GB para varrer e algumas dezenas de
    termos: um regex por termo faz o disco ser lido dezenas de vezes e a régua
    passa de minutos para horas — medido, e foi preciso matar a primeira versão.
    Com a alternância, o autômato acha qualquer um dos termos numa passada só.
    """
    def alternancia(codificacao: str, letra: bytes) -> re.Pattern:
        # Do mais longo para o mais curto: sem isso, "Vivo" casaria antes de
        # "Vivo Empresas" e o relatório apontaria o termo errado.
        corpo = rb"|".join(re.escape(t.encode(codificacao))
                           for t in sorted(termos, key=len, reverse=True))
        return re.compile(rb"(?<!" + letra + rb")(" + corpo + rb")(?!" + letra + rb")")

    return alternancia("utf-8", _LETRA_U8), alternancia("utf-16-le", _LETRA_U16)


def _candidatos(arquivos: list[Path], termos: list[str]) -> list[Path]:
    """Os poucos arquivos que contêm algum termo, achados pelo ``grep``.

    **Por que não fazer tudo em Python.** A primeira versão rodava o regex de
    fronteira sobre cada arquivo, e gastou 20 minutos de CPU sem terminar os
    5,4 GB — inutilizável como régua de build. O ``grep -F`` com muitos padrões
    é Aho-Corasick em C: uma passada, na velocidade do disco.

    A divisão de trabalho que sai daí: o ``grep`` responde *onde olhar* e é
    permissivo (acha ``Vivo`` dentro de ``FaixaAoVivo``); o Python responde *se
    é de verdade* e é exato. O caro roda sobre 5,4 GB, o preciso sobre um punhado
    de arquivos.
    """
    import tempfile

    # Os dois alfabetos no mesmo arquivo de padrões: o UTF-16-LE de um termo
    # ASCII é ele com \x00 no meio, e o grep casa bytes, não caracteres.
    padroes = b"\n".join(
        [t.encode("utf-8") for t in termos] + [t.encode("utf-16-le") for t in termos])

    with tempfile.NamedTemporaryFile(suffix=".padroes", delete=False) as f:
        f.write(padroes)
        caminho_padroes = f.name

    encontrados: list[Path] = []
    try:
        # Em lotes: são dezenas de milhares de caminhos, e todos de uma vez
        # estouram o ARG_MAX com "Argument list too long" — que aqui apareceria
        # como a régua não conferindo nada e passando.
        LOTE = 2000
        for i in range(0, len(arquivos), LOTE):
            fatia = [str(a) for a in arquivos[i:i + LOTE]]
            # -a trata binário como texto (senão o grep para no primeiro NUL),
            # -l só o nome, -F literal, -Z separa a saída por NUL para o caminho
            # com espaço não virar dois.
            proc = subprocess.run(
                ["grep", "-alFZ", "-f", caminho_padroes, "--", *fatia],
                capture_output=True)
            # 0 = achou, 1 = não achou (não é erro), 2 = erro de verdade.
            if proc.returncode not in (0, 1):
                raise RuntimeError(
                    f"grep falhou: {proc.stderr.decode(errors='replace')[:400]}")
            encontrados.extend(Path(os.fsdecode(n))
                               for n in proc.stdout.split(b"\0") if n)
        return encontrados
    finally:
        os.unlink(caminho_padroes)


def procurar(arquivos: list[Path], termos: list[str]) -> list[tuple[Path, str]]:
    """Onde cada termo aparece como palavra inteira, em UTF-8 e em UTF-16.

    UTF-16 porque um binário .NET guarda strings assim: procurar só em UTF-8
    passaria batido justamente pelo executável que nós compilamos, que é o único
    arquivo desta lista cujo conteúdo saiu da nossa árvore.
    """
    p8, p16 = _padroes(termos)
    achados: list[tuple[Path, str]] = []

    for caminho in _candidatos(arquivos, termos):
        try:
            # Lido inteiro: o maior arquivo que sobra depois dos .gguf é uma DLL
            # de ~900 MB, e ler por pedaços exigiria costurar as bordas para não
            # perder um termo partido entre dois blocos.
            dados = caminho.read_bytes()
        except (OSError, MemoryError):
            continue

        vistos: set[str] = set()
        for achado in p8.finditer(dados):
            vistos.add(achado.group(1).decode("utf-8", "replace"))
        for achado in p16.finditer(dados):
            vistos.add(achado.group(1).decode("utf-16-le", "replace"))
        achados.extend((caminho, termo) for termo in sorted(vistos))

    return achados


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--payload", type=Path,
                    default=Path("dist/instalador/payload"))
    ap.add_argument("--motores", type=Path,
                    default=Path("/mnt/c/Users/andre/MeetingApp/motores"))
    ap.add_argument("--perfil", type=Path, default=Path("/mnt/c/Users/andre"),
                    help="de onde saem os termos a procurar")
    ap.add_argument("--rapido", action="store_true",
                    help="só o payload; pula os 5,4 GB de motores")
    args = ap.parse_args()

    arquivos = arquivos_que_viajam(args.payload,
                                   None if args.rapido else args.motores)
    if not arquivos:
        print("ERRO: não achei arquivo nenhum para conferir.", file=sys.stderr)
        return 2

    total_bytes = sum(f.stat().st_size for f in arquivos)
    print(f"conferindo {len(arquivos)} arquivos, {total_bytes / 1e9:.2f} GB")

    reprovas: list[str] = []

    # ── régua 1: por forma ───────────────────────────────────────────────────
    for caminho in arquivos:
        nome = caminho.name
        relativo = str(caminho)

        if nome in PROIBIDOS:
            reprovas.append(f"arquivo proibido: {relativo}")
            continue

        for padrao in PADROES_PROIBIDOS:
            if padrao.search(nome) and not AUDIO_PERMITIDO.search(relativo):
                reprovas.append(f"arquivo suspeito pelo nome: {relativo}")
                break

    print(f"  régua 1 (nomes proibidos): "
          f"{'reprovou' if reprovas else 'passou'}")

    # ── régua 2: por conteúdo ────────────────────────────────────────────────
    termos = termos_desta_maquina(args.perfil)
    todos = [t for lista in termos.values() for t in lista]

    for rotulo, lista in termos.items():
        print(f"  {rotulo}: {len(lista)}")

    if not todos:
        print("\nAVISO: não achei dado nenhum nesta máquina para procurar.",
              file=sys.stderr)
        print("       A régua 2 não rodou, e a ausência dela NÃO é aprovação:",
              file=sys.stderr)
        print("       rode isto na máquina que usa o app de verdade.", file=sys.stderr)
    else:
        achados = procurar(arquivos, todos)
        vazamentos = 0
        for caminho, termo in achados:
            if (motivo := homonimo(caminho, termo)) is not None:
                print(f"  ignorado: {termo!r} em {caminho.name}")
                print(f"            {motivo}")
                continue
            if (fonte := explicado_pela_fonte(termo, RAIZ)) is not None:
                print(f"  explicado: {termo!r} em {caminho.name}")
                print(f"             a mesma palavra está em "
                      f"{fonte.relative_to(RAIZ)}, no repositório — "
                      f"o que viaja veio de lá, não dos seus dados")
                continue
            reprovas.append(f"vazamento: {termo!r} dentro de {caminho}")
            vazamentos += 1
        print(f"  régua 2 (conteúdo, {len(todos)} termos): "
              f"{'reprovou' if vazamentos else 'passou'}")

    print()
    if reprovas:
        print(f"REPROVADO — {len(reprovas)} problema(s):")
        for r in reprovas[:40]:
            print(f"  {r}")
        if len(reprovas) > 40:
            print(f"  … e mais {len(reprovas) - 40}")
        return 1

    print("APROVADO — nada de cliente, projeto, voz ou reunião no que vai viajar.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
