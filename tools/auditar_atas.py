"""Audita as atas e transcrições já geradas, sem precisar de referência.

Por que existe: a comparação com uma segunda fonte (Notion, Gemini/Meet) é o que
decide **quem errou**, e ela é cara — depende de a reunião ter sido gravada em
paralelo. Mas boa parte dos defeitos encontrados em 20/08/2026 não precisou de
segunda fonte nenhuma: são contradições internas, que se veem olhando só o que já
está no disco.

Exemplos reais do que caiu aqui sem referência alguma:

* três atas em que o conteúdo gerado foi **descartado em silêncio** pelo redator,
  porque o modelo devolveu a seção canônica em ``secoes`` e o campo próprio
  ficou vazio;
* uma ata que compara o cliente da própria reunião contra uma sigla parecida e
  errada — o valor certo está no ``meta.json`` da mesma pasta;
* uma transcrição que diz dois valores diferentes para a mesma grandeza, com 75 s
  de distância, e a ata escolheu o errado;
* 19 de 28 itens duplicados numa ata só, por loop de repetição do modelo.

**Isto mede incidência, não decide conserto.** A régua da Fase 6 §5 continua
sendo a comparação com fontes paralelas: aqui se sabe *quantas vezes* um defeito
acontece, não *qual dos dois lados está certo* quando o defeito é de conteúdo.
Os achados marcados ``[heurística]`` erram para o lado de reportar demais.

Uso::

    python tools/auditar_atas.py                       # tabela por gravação
    python tools/auditar_atas.py --detalhe             # com os trechos
    python tools/auditar_atas.py --so secao_descartada # um achado só
    python tools/auditar_atas.py --json > auditoria.json
    python tools/auditar_atas.py --pasta /outro/lugar
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from difflib import SequenceMatcher
from pathlib import Path

# ---------------------------------------------------------------- infra

def pasta_das_gravacoes() -> Path:
    """Onde o app grava, procurado e não assumido.

    O app usa ``SpecialFolder.MyDocuments``, que no Windows segue o
    redirecionamento do OneDrive. Em 21/08/2026 a pasta Documentos foi
    redirecionada e o caminho fixo parou de existir de um dia para o outro —
    daí a busca em vez da constante. Do WSL, ``Path.home()`` é o home do Linux
    e nenhum dos dois serve, então os caminhos do Windows entram na lista.
    """
    candidatos = [Path.home() / "Documents" / "MeetingRecordings"]
    for usuarios in (Path("/mnt/c/Users"),):
        if usuarios.is_dir():
            for u in usuarios.iterdir():
                candidatos += [
                    u / "OneDrive" / "Documents" / "MeetingRecordings",
                    u / "Documents" / "MeetingRecordings",
                ]
    return next((c for c in candidatos if c.is_dir()), candidatos[0])


PASTA_PADRAO = pasta_das_gravacoes()

# Espelha RedatorDeAta.Canonizar. Se um título cai aqui, o redator escreve a
# seção a partir do campo próprio e **ignora** o texto que veio em `secoes`.
CANONICAS = {
    "resumo": "resumo",
    "decisoes": "decisoes",
    "pendencias": "acoes",
    "acoes": "acoes",
    "acao": "acoes",
    "acoes imediatas": "acoes",
    "action items": "acoes",
    "action item": "acoes",
    "pontos em aberto": "pontos_em_aberto",
    "riscos": "riscos",
    "riscos e alertas": "riscos",
    "riscos identificados": "riscos",
    "observacoes": "observacoes",
    "observacoes sobre a transcricao": "observacoes",
}

LISTAS = ["decisoes", "pontos_em_aberto", "riscos", "observacoes"]

VAZIAS = {
    "para", "como", "pelo", "pela", "isso", "esse", "essa", "esta", "está",
    "estao", "estão", "sobre", "quando", "porque", "então", "entao", "também",
    "tambem", "ainda", "todos", "todas", "deve", "pode", "fazer", "sendo",
    "cada", "mais", "menos", "muito", "após", "apos", "entre", "durante",
    "aqui", "onde", "qual", "quais", "seja", "sejam", "gente", "nesta",
    "neste", "dessa", "desse", "sera", "será", "foram", "havia",
}


def sem_acento(t: str) -> str:
    return "".join(
        c for c in unicodedata.normalize("NFD", t) if unicodedata.category(c) != "Mn"
    )


def normalizar_titulo(t: str) -> str:
    """Mesma normalização do redator: minúscula, sem acento, só alfanumérico."""
    limpo = sem_acento((t or "").lower())
    return "".join(c for c in limpo if c.isalnum() or c == " ").strip()


def conteudo(texto: str) -> set[str]:
    """Palavras de conteúdo, para comparar dois itens sem casar por 'para'."""
    palavras = re.findall(r"[\w]{4,}", sem_acento((texto or "").lower()))
    return {p for p in palavras if p not in VAZIAS}


def parecidos(a: str, b: str) -> float:
    """Jaccard das palavras de conteúdo, com desempate por sequência."""
    ca, cb = conteudo(a), conteudo(b)
    if not ca or not cb:
        return 0.0
    jaccard = len(ca & cb) / len(ca | cb)
    if jaccard < 0.4:
        return jaccard
    seq = SequenceMatcher(None, sem_acento(a.lower()), sem_acento(b.lower())).ratio()
    return max(jaccard, seq)


def itens_de(texto: str) -> list[str]:
    """Os bullets de um texto de seção, ou o texto inteiro se não houver."""
    linhas = [
        re.sub(r"^\s*(?:[-*]|\[\s*\]|\d+\.)\s*", "", l).strip()
        for l in (texto or "").splitlines()
        if l.strip()
    ]
    bullets = [
        l for l, orig in zip(linhas, (texto or "").splitlines())
        if orig.strip().startswith(("-", "*"))
    ]
    return bullets or ([texto.strip()] if (texto or "").strip() else [])


def primeiro_nome(n: str) -> str:
    partes = (n or "").split()
    return sem_acento(partes[0].lower()) if partes else ""


# ---------------------------------------------------------------- achados

@dataclass
class Achado:
    tipo: str
    gravidade: str          # "perda" | "ruido" | "suspeita"
    detalhe: str
    heuristica: bool = False


@dataclass
class Gravacao:
    nome: str
    achados: list[Achado] = field(default_factory=list)
    metricas: dict = field(default_factory=dict)

    def marcar(self, tipo, gravidade, detalhe, heuristica=False):
        self.achados.append(Achado(tipo, gravidade, detalhe, heuristica))


# ------------------------------------------------- auditorias sobre a ata

def auditar_secoes(ata: dict, g: Gravacao) -> None:
    """Seção canônica em `secoes`: ou some no redator, ou sai duplicada."""
    for s in ata.get("secoes") or []:
        titulo = s.get("titulo") or ""
        campo = CANONICAS.get(normalizar_titulo(titulo))
        texto = (s.get("texto") or "").strip()
        if not campo or not texto:
            continue
        n = len(itens_de(texto))
        if not ata.get(campo):
            g.marcar(
                "secao_descartada", "perda",
                f"'{titulo}' ({n} item(ns)) veio em secoes e o campo `{campo}` "
                f"está vazio — RedatorDeAta.cs:84 descarta, sem nota em Observações",
            )
        else:
            g.marcar(
                "secao_duplicada", "ruido",
                f"'{titulo}' veio em secoes e em `{campo}` — sai duas vezes no .md",
            )

        # O modelo preenche `situacao` com o próprio título da seção, e o
        # redator escreve "**Situação:** Decisoes" como se fosse um estado.
        if (s.get("situacao") or "").strip() and normalizar_titulo(
            s.get("situacao") or ""
        ) == normalizar_titulo(titulo):
            g.marcar(
                "situacao_e_o_titulo", "ruido",
                f"seção '{titulo}' com situacao='{s['situacao']}'",
            )


def auditar_bullets(ata: dict, g: Gravacao) -> None:
    """Item de lista que já vem com '- ' dentro do valor vira '- -' no .md."""
    for campo in LISTAS:
        for item in ata.get(campo) or []:
            if isinstance(item, str) and item.lstrip().startswith(("-", "*", "•")):
                g.marcar(
                    "bullet_no_valor", "ruido",
                    f"`{campo}` com marcador dentro do valor: {curto(item)}",
                )
                break   # um por campo basta para contar a gravação


def auditar_repeticao(ata: dict, g: Gravacao) -> None:
    """Loop de repetição do modelo: itens quase idênticos na mesma lista.

    Olha também dentro de `secoes`: a seção livre de uma ata de sessão de
    trabalho ("Descobertas") é onde o loop apareceu mais forte — 6 de 18 bullets
    eram duas frases alternadas três vezes.
    """
    blocos = [
        (campo, [(i.get("acao") if isinstance(i, dict) else i) or "" for i in
                 (ata.get(campo) or [])])
        for campo in LISTAS + ["acoes"]
    ]
    blocos += [
        (f"secoes/{s.get('titulo') or '?'}", itens_de(s.get("texto") or ""))
        for s in (ata.get("secoes") or [])
        if len(itens_de(s.get("texto") or "")) > 2
    ]

    for nome, textos in blocos:
        duplicados = 0
        vistos: list[str] = []
        for t in textos:
            if any(parecidos(t, v) >= 0.85 for v in vistos):
                duplicados += 1
            else:
                vistos.append(t)
        if duplicados:
            g.marcar(
                "repeticao", "ruido",
                f"`{nome}`: {duplicados} de {len(textos)} itens são repetição "
                "quase literal de outro",
            )


def auditar_eco(ata: dict, g: Gravacao) -> None:
    """Risco/ponto em aberto que é a pendência reescrita não acrescenta nada.

    O ConferirRiscos exige eco na transcrição e é indefeso contra isto: um risco
    construído a partir do texto da própria ata tem eco por construção.
    """
    pendencias = [
        (a.get("acao") or "") for a in (ata.get("acoes") or []) if isinstance(a, dict)
    ]
    if not pendencias:
        return
    # `decisoes` fica de fora de propósito: decidir fazer X e ter a pendência
    # de fazer X é a ata funcionando, não eco. Medido — com `decisoes` dentro,
    # 13 das 17 ocorrências eram esse par legítimo, e o achado virava ruído.
    for campo in ("riscos", "pontos_em_aberto"):
        itens = [i for i in (ata.get(campo) or []) if isinstance(i, str)]
        ecos = sum(1 for i in itens if any(parecidos(i, p) >= 0.55 for p in pendencias))
        if ecos and itens:
            g.marcar(
                "eco_de_pendencia", "ruido",
                f"`{campo}`: {ecos} de {len(itens)} itens são uma pendência "
                "reescrita",
                heuristica=True,
            )


def auditar_donos(ata: dict, conhecidos: list[str], g: Gravacao) -> None:
    """Dono que não é ninguém da reunião, e ata sem dono nenhum."""
    acoes = [a for a in (ata.get("acoes") or []) if isinstance(a, dict)]
    if not acoes:
        return
    primeiros = {primeiro_nome(n) for n in conhecidos if n}
    sem_dono = 0
    for a in acoes:
        dono = (a.get("responsavel") or "").strip()
        if not dono or dono.startswith("["):
            sem_dono += 1
            continue
        if primeiros and not any(
            primeiro_nome(dono) == p or p in sem_acento(dono.lower())
            for p in primeiros
        ):
            g.marcar(
                "dono_fora_da_agenda", "suspeita",
                f"responsável '{dono}' não casa com nenhum participante",
            )
    if sem_dono == len(acoes):
        g.marcar(
            "nenhuma_pendencia_com_dono", "suspeita",
            f"as {len(acoes)} pendências saíram sem responsável",
        )

    lados = Counter((a.get("lado") or "").lower() for a in acoes)
    if lados.get("cliente", 0) == len(acoes) and len(acoes) > 2:
        g.marcar(
            "todas_do_cliente", "suspeita",
            f"o modelo pôs as {len(acoes)} pendências do lado do cliente — "
            "o verificador corrige, mas a nota vira rodapé",
        )


SIGLA = re.compile(r"^[A-Z][A-Z0-9]{2,7}$")


def distancia(a: str, b: str) -> int:
    """Levenshtein, pequeno o bastante para não valer uma dependência."""
    anterior = list(range(len(b) + 1))
    for i, ca in enumerate(a, 1):
        atual = [i]
        for j, cb in enumerate(b, 1):
            atual.append(min(anterior[j] + 1, atual[j - 1] + 1,
                             anterior[j - 1] + (ca != cb)))
        anterior = atual
    return anterior[-1]


def auditar_entidades(ata_md: str, entidades: list[str], conhecidas: set[str],
                      g: Gravacao) -> None:
    """Sigla corrompida: a ata escreve 'G6CB' e o cliente da reunião é 'GCCB'.

    Três filtros, cada um medido contra o ruído que ele tirou:

    1. **sigla contra sigla, mesmo tamanho, 1–2 letras trocadas.** A versão larga
       (qualquer token parecido) disparava em 62% das gravações, quase tudo
       plural e flexão — 'agentes' vs 'Agente', 'usuários' vs 'Usuario'.
       Diferença só no sufixo é morfologia; a corrupção troca letra no meio;
    2. **a candidata não pode ser entidade conhecida em nenhuma reunião do
       acervo.** Sem isto, 'B2C' vira corrupção de 'B2B' e 'API' de 'APP' — são
       siglas diferentes e ambas reais. Se o corpus conhece as duas, o par é
       vocabulário, não erro;
    3. sobra o caso que interessa: uma sigla que só existe nesta ata, a uma
       letra de uma que o ``meta.json`` conhece.

    Mesmo assim é **lista de candidatos, não de achados** — quem decide é quem
    leu a reunião. O valor certo já está na mesma pasta, e às vezes no cabeçalho
    da mesma ata.
    """
    if not ata_md:
        return
    alvos = {e for e in entidades if SIGLA.match(e or "")}
    if not alvos:
        return
    tokens = {t for t in re.findall(r"\b[A-Za-z0-9]{3,8}\b", ata_md) if SIGLA.match(t)}
    for t in sorted(tokens - alvos - conhecidas):
        for alvo in sorted(alvos):
            if len(t) != len(alvo) or t == alvo:
                continue
            d = distancia(t, alvo)
            if 1 <= d <= 2:
                g.marcar(
                    "sigla_corrompida", "suspeita",
                    f"a ata escreve '{t}' e o meta.json conhece '{alvo}' "
                    f"({d} letra(s) de diferença)",
                    heuristica=True,
                )
                break


# ------------------------------------------- auditorias sobre a transcrição

PORCENTO = re.compile(r"(\d{1,3}(?:[.,]\d+)?)\s*(?:%|por\s?cento)", re.I)


def auditar_numeros_invisiveis(segs: list[dict], ata_md: str, g: Gravacao) -> None:
    """Porcentagem que o verificador de omissões nunca chega a conferir.

    RoteiroDeFatos.Normalizar tira o '%' e sobra "2"; NaoIncorporados pula
    chave com menos de 3 caracteres. Resultado: **toda porcentagem abaixo de
    100% é invisível** para a rede de omissões — que numa reunião de negócio é
    quase todas.
    """
    ditas = {m.group(1) for s in segs for m in PORCENTO.finditer(s.get("text") or "")}
    if not ditas:
        return
    cegas = {d for d in ditas if len(d.replace(",", "").replace(".", "")) < 3}
    ausentes = sorted(
        d for d in cegas if not re.search(rf"\b{re.escape(d)}\s*%", ata_md or "")
    )
    if ausentes:
        g.marcar(
            "porcentagem_invisivel", "perda",
            f"{len(ausentes)} porcentagem(ns) ditas, ausentes da ata e cegas "
            f"para o verificador: {', '.join(a + '%' for a in ausentes[:8])}"
            + (f" (e mais {len(ausentes) - 8})" if len(ausentes) > 8 else ""),
        )


def auditar_numero_contraditorio(segs: list[dict], g: Gravacao) -> None:
    """Mesma grandeza, dois valores, poucos segundos de distância. [heurística]

    Nasceu do caso 2% × 12%: os dois estão na nossa transcrição, a 75 s um do
    outro, e a ata escolheu o errado. Só olha porcentagem — é onde o padrão é
    reconhecível sem entender a frase.
    """
    ditos = [
        (s.get("start") or 0.0, m.group(1))
        for s in segs
        for m in PORCENTO.finditer(s.get("text") or "")
    ]
    pares = set()
    for i, (t1, v1) in enumerate(ditos):
        for t2, v2 in ditos[i + 1:]:
            if t2 - t1 > 180:
                break
            if v1 != v2:
                pares.add(tuple(sorted((v1, v2))))
    if pares:
        amostra = ", ".join(f"{a}%×{b}%" for a, b in sorted(pares)[:5])
        g.marcar(
            "porcentagem_contraditoria", "suspeita",
            f"{len(pares)} par(es) de porcentagem diferente em até 3 min: {amostra}",
            heuristica=True,
        )


def auditar_segmentacao(segs: list[dict], g: Gravacao) -> None:
    """Fragmentação, quebra no meio de frase e buraco de VAD."""
    if not segs:
        return
    palavras = [len((s.get("text") or "").split()) for s in segs]
    curtos = sum(1 for p in palavras if p <= 4)

    quebras = 0
    for a, b in zip(segs, segs[1:]):
        ta, tb = (a.get("text") or "").strip(), (b.get("text") or "").strip()
        if not ta or not tb:
            continue
        if (
            a.get("speaker") != b.get("speaker")
            and ta[-1] not in ".!?…"
            and tb[0].islower()
        ):
            quebras += 1

    lacunas = [
        (a.get("end") or 0, b.get("start") or 0)
        for a, b in zip(segs, segs[1:])
        if (b.get("start") or 0) - (a.get("end") or 0) > 8
    ]

    horas = max((segs[-1].get("end") or 0) / 3600, 0.01)
    g.metricas.update(
        segmentos=len(segs),
        curtos_pct=round(100 * curtos / len(segs)),
        quebras_pct=round(100 * quebras / len(segs)),
        lacunas=len(lacunas),
        # Contagem bruta cresce com a duração; por hora as reuniões comparam.
        lacunas_h=round(len(lacunas) / horas, 1),
        maior_lacuna_s=round(max((b - a for a, b in lacunas), default=0)),
    )

    if curtos / len(segs) >= 0.30:
        g.marcar(
            "fragmentacao", "suspeita",
            f"{curtos} de {len(segs)} segmentos ({100 * curtos // len(segs)}%) "
            "têm 4 palavras ou menos",
        )
    if quebras / len(segs) >= 0.10:
        g.marcar(
            "quebra_meio_frase", "suspeita",
            f"{quebras} quebras no meio de frase com troca de falante "
            f"({100 * quebras // len(segs)}%)",
        )
    if lacunas:
        maior = max(b - a for a, b in lacunas)
        g.marcar(
            "lacuna_de_vad", "suspeita",
            f"{len(lacunas)} buraco(s) acima de 8 s; o maior tem {maior:.0f} s "
            f"em {int(max(lacunas, key=lambda p: p[1] - p[0])[0]) // 60:02d}min",
        )


def auditar_vocativo(segs: list[dict], g: Gravacao) -> None:
    """Ninguém chama a si mesmo pelo nome. [heurística]

    "Alô, e aí Diego, tudo bem?" atribuído ao Diego são dois turnos fundidos
    num falante só — e é conferível sem áudio.
    """
    casos = []
    for s in segs:
        quem = primeiro_nome(s.get("speaker") or "")
        if len(quem) < 4:
            continue
        texto = sem_acento((s.get("text") or "").lower())
        # vocativo: nome precedido de vírgula/interjeição, ou "aí <nome>"
        if re.search(rf"(?:,\s*|\bai\s+|\bo\s+|\be\s+ai\s+){re.escape(quem)}\b", texto):
            casos.append((s.get("start") or 0, s.get("speaker"), s.get("text")))
    if casos:
        t, quem, txt = casos[0]
        g.marcar(
            "vocativo_do_proprio_falante", "suspeita",
            f"{len(casos)} segmento(s) em que o falante atribuído é chamado pelo "
            f"nome; ex. [{int(t) // 60:02d}:{int(t) % 60:02d}] {quem}: {curto(txt)}",
            heuristica=True,
        )


# ---------------------------------------------------------------- varredura

def curto(t: str, teto: int = 68) -> str:
    t = " ".join((t or "").split())
    return t if len(t) <= teto else t[:teto].rstrip() + "…"


def convidados_de(meta: dict) -> tuple[list[str], list[str]]:
    reuniao = meta.get("meeting") or {}
    nomes = [n for n in (reuniao.get("attendees") or []) if n]
    entidades: list[str] = []
    for chave in ("client", "project", "title"):
        v = reuniao.get(chave)
        if isinstance(v, str):
            entidades += re.findall(r"\b[\w.-]{3,12}\b", v)
    return nomes, entidades


def auditar(pasta: Path, conhecidas: set[str] | None = None) -> Gravacao | None:
    g = Gravacao(pasta.name)

    meta = ler_json(pasta / "meta.json") or {}
    convidados, entidades = convidados_de(meta)

    vinculo = ler_json(pasta / "reuniao.json") or {}
    for chave in ("cliente", "projeto"):
        v = vinculo.get(chave)
        if isinstance(v, str):
            entidades += re.findall(r"\b[\w.-]{3,12}\b", v)

    transcricao = ler_json(pasta / "transcricao.json")
    segs = (transcricao or {}).get("segments") or []
    falantes = sorted({s.get("speaker") for s in segs if s.get("speaker")})

    ata = ler_json(pasta / "ata.json")
    ata_md = (pasta / "ata.md").read_text(encoding="utf-8", errors="replace") \
        if (pasta / "ata.md").exists() else ""

    if not segs and not ata:
        return None

    if segs:
        auditar_segmentacao(segs, g)
        auditar_vocativo(segs, g)
        auditar_numero_contraditorio(segs, g)
        if ata_md:
            auditar_numeros_invisiveis(segs, ata_md, g)

    if ata:
        auditar_secoes(ata, g)
        auditar_bullets(ata, g)
        auditar_repeticao(ata, g)
        auditar_eco(ata, g)
        auditar_donos(ata, convidados + falantes, g)
        auditar_entidades(ata_md, entidades, conhecidas or set(), g)

    g.metricas["tem_ata"] = bool(ata)
    return g


def ler_json(caminho: Path):
    if not caminho.exists():
        return None
    try:
        return json.loads(caminho.read_text(encoding="utf-8", errors="replace"))
    except (json.JSONDecodeError, OSError) as e:
        print(f"  ! {caminho.name} ilegível: {e}", file=sys.stderr)
        return None


# ---------------------------------------------------------------- saída

GRAVIDADE = {"perda": "PERDA", "ruido": "ruído", "suspeita": "suspeita"}


def relatar(gravacoes: list[Gravacao], detalhe: bool, so: str | None) -> None:
    por_tipo: dict[str, list[Gravacao]] = defaultdict(list)
    for g in gravacoes:
        for a in g.achados:
            if so and a.tipo != so:
                continue
            if g not in por_tipo[a.tipo]:
                por_tipo[a.tipo].append(g)

    total = len(gravacoes)
    com_ata = sum(1 for g in gravacoes if g.metricas.get("tem_ata"))
    print(f"\n{total} gravação(ões) varrida(s), {com_ata} com ata.\n")

    def exemplo_de(tipo: str, gs: list[Gravacao]) -> Achado:
        """O achado deste tipo — não o primeiro da gravação, que é de outro."""
        return next(a for a in gs[0].achados if a.tipo == tipo)

    print(f"{'achado':32} {'gravidade':10} {'gravações':>9} {'ocorr.':>7}  incidência")
    print("-" * 76)
    ordem = sorted(
        por_tipo.items(),
        key=lambda kv: (
            {"perda": 0, "suspeita": 1, "ruido": 2}[exemplo_de(*kv).gravidade],
            -len(kv[1]),
        ),
    )
    for tipo, gs in ordem:
        exemplo = exemplo_de(tipo, gs)
        ocorrencias = sum(1 for g in gs for a in g.achados if a.tipo == tipo)
        base = com_ata if tipo in ATAS else total
        pct = f"{100 * len(gs) // base}% de {base}" if base else "—"
        marca = " [h]" if exemplo.heuristica else ""
        print(
            f"{tipo:32} {GRAVIDADE[exemplo.gravidade]:10} {len(gs):>9} "
            f"{ocorrencias:>7}  {pct}{marca}"
        )

    print("\n[h] = heurística: erra para o lado de reportar demais.\n")

    if detalhe:
        for tipo, gs in ordem:
            print(f"\n### {tipo}")
            for g in gs:
                for a in g.achados:
                    if a.tipo == tipo:
                        print(f"  {g.nome}  {a.detalhe}")

    print("\n## Métricas de transcrição\n")
    print(f"{'gravação':22} {'segs':>5} {'≤4pal':>6} {'quebras':>8} "
          f"{'lacunas':>8} {'/h':>6}")
    print("-" * 72)
    for g in sorted(gravacoes, key=lambda x: x.nome):
        m = g.metricas
        if "segmentos" not in m:
            continue
        print(
            f"{g.nome:22} {m['segmentos']:>5} {m['curtos_pct']:>5}% "
            f"{m['quebras_pct']:>7}% {m['lacunas']:>8} {m['lacunas_h']:>6}"
        )
    medias = [g.metricas for g in gravacoes if "segmentos" in g.metricas]
    if medias:
        print("-" * 72)
        print(
            f"{'mediana':22} {'':>5} "
            f"{mediana([m['curtos_pct'] for m in medias]):>5}% "
            f"{mediana([m['quebras_pct'] for m in medias]):>7}%"
        )


ATAS = {
    "secao_descartada", "secao_duplicada", "situacao_e_o_titulo", "bullet_no_valor",
    "repeticao", "eco_de_pendencia", "dono_fora_da_agenda", "todas_do_cliente",
    "nenhuma_pendencia_com_dono", "sigla_corrompida", "porcentagem_invisivel",
}


def mediana(xs: list[int]) -> int:
    xs = sorted(xs)
    return xs[len(xs) // 2] if xs else 0


def siglas_do_acervo(pastas: list[Path]) -> set[str]:
    """As siglas que alguma reunião do acervo declara como entidade sua.

    É o que separa "sigla diferente" de "sigla corrompida": se o corpus conhece
    B2B **e** B2C, o par é vocabulário do domínio, não erro de transcrição.
    """
    siglas: set[str] = set()
    for pasta in pastas:
        meta = ler_json(pasta / "meta.json") or {}
        _, entidades = convidados_de(meta)
        vinculo = ler_json(pasta / "reuniao.json") or {}
        for chave in ("cliente", "projeto"):
            v = vinculo.get(chave)
            if isinstance(v, str):
                entidades += re.findall(r"\b[\w.-]{3,12}\b", v)
        siglas |= {e for e in entidades if SIGLA.match(e or "")}
    return siglas


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--pasta", type=Path, default=PASTA_PADRAO,
                   help=f"pasta das gravações (padrão: {PASTA_PADRAO})")
    p.add_argument("--detalhe", action="store_true", help="lista cada ocorrência")
    p.add_argument("--so", help="mostra só um tipo de achado")
    p.add_argument("--json", action="store_true", help="saída legível por máquina")
    args = p.parse_args()

    if not args.pasta.is_dir():
        print(f"pasta não encontrada: {args.pasta}", file=sys.stderr)
        return 1

    pastas = sorted(p for p in args.pasta.iterdir() if p.is_dir())
    conhecidas = siglas_do_acervo(pastas)

    gravacoes = []
    for pasta in pastas:
        g = auditar(pasta, conhecidas)
        if g:
            gravacoes.append(g)

    if not gravacoes:
        print("nenhuma gravação com transcrição ou ata.", file=sys.stderr)
        return 1

    if args.json:
        json.dump(
            [
                {
                    "gravacao": g.nome,
                    "metricas": g.metricas,
                    "achados": [vars(a) for a in g.achados],
                }
                for g in gravacoes
            ],
            sys.stdout, ensure_ascii=False, indent=1,
        )
        print()
    else:
        relatar(gravacoes, args.detalhe, args.so)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
