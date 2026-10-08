"""A ata por tema contra a clássica, com o modelo de verdade (ATA-5, spec §7.2).

Roda o Sidecar.exe --ata nos dois formatos sobre CÓPIAS das reuniões — o CLI
escreve ata.md na pasta que recebe, e a do acervo é a do dono — e mede os quatro
critérios da spec. Uso:

    uv run python tools/validar_ata_por_tema.py 2026-09-22_10-59-53:cliente-update ...
    uv run python tools/validar_ata_por_tema.py --medir <as mesmas>   # sem gerar de novo

Cada formato é uma geração independente, com temperatura 0,3 e sem semente:
o conteúdo muda um pouco além do formato, e o critério 4 (a daily igual) pode
reprovar por sorteio. A garantia determinística dele está nos testes de unidade
(AtaPorTemaTests.TipoSemMarcaIgnoraASecao); aqui ele é só o sinal de campo.
"""
import json
import os
import re
import shutil
import statistics
import subprocess
import sys
from pathlib import Path

ACERVO = Path("/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings")
BASE = Path("/mnt/c/Users/andre/ata-por-tema-validacao")
CLI = BASE / "cli" / "Sidecar.exe"
MOTOR = r"C:\Users\andre\AppData\Local\Programs\MeetingApp\motores\ata"
APP_JSON = Path("/mnt/c/Users/andre/.meeting-transcription/app.json")


def modelo_do_app() -> str | None:
    """O modelo de ata que o app usa: medir com outro mediria uma ata que ninguém vê."""
    try:
        return json.loads(APP_JSON.read_text(encoding="utf-8")).get("modelo_de_ata") or None
    except (OSError, ValueError):
        return None


def win(p: Path) -> str:
    return subprocess.run(["wslpath", "-w", str(p)], capture_output=True, text=True).stdout.strip()


def normalizar(t: str) -> str:
    """O RedatorDeAta.Normalizar, letra por letra: a mesma lista de acentos."""
    t = (t or "").lower()
    for de, para in zip("áãâéêíóõôúç", "aaaeeiooouc"):
        t = t.replace(de, para)
    return "".join(c for c in t if c.isalnum() or c == " ").strip()


def parecido(a: str, b: str) -> bool:
    a, b = normalizar(a), normalizar(b)
    return a == b or a.startswith(b) or b.startswith(a)


# O RedatorDeAta.Canonizar: os títulos que o redator escreve a partir dos campos.
CANONICAS = {"resumo", "decisoes", "pendencias", "acoes", "acao", "acoes imediatas",
             "action items", "action item", "pontos em aberto", "riscos",
             "riscos e alertas", "riscos identificados", "observacoes",
             "observacoes sobre a transcricao"}
ATAS = Path(__file__).resolve().parent.parent / "assets" / "atas"
MARCA = "<!-- ata: itens-por-secao -->"


def agrupa(tipo: str) -> bool:
    """O tipo tem a marca: a mesma régua do ModeloDeAta.ItensPorSecao."""
    return MARCA in (ATAS / f"{tipo}.md").read_text(encoding="utf-8")


def esqueleto(tipo: str) -> list[str]:
    """Os '## ' do bloco ```markdown do tipo, como o ModeloDeAta.Secoes."""
    texto = (ATAS / f"{tipo}.md").read_text(encoding="utf-8")
    ini = texto.index("```markdown") + len("```markdown")
    fim = texto.index("```", ini)
    return [l[3:].strip() for l in texto[ini:fim].splitlines() if l.startswith("## ")]


def rodape(tipo: str) -> list[str]:
    """O RedatorDeAta.Rodape: não canônicas depois da primeira canônica que não é o Resumo."""
    sec = esqueleto(tipo)
    for i, t in enumerate(sec):
        if normalizar(t) in CANONICAS and normalizar(t) != "resumo":
            return [x for x in sec[i:] if normalizar(x) not in CANONICAS]
    return []


def corpo(ata: dict, tipo: str) -> list[str]:
    """Os títulos onde o redator aninha: nem canônicos, nem de rodapé."""
    rod = rodape(tipo)
    return [s.get("titulo", "") for s in ata.get("secoes", [])
            if normalizar(s.get("titulo", "")) not in CANONICAS
            and not (normalizar(s.get("titulo", "")) and any(parecido(s.get("titulo", ""), r) for r in rod))]


def tema_do_item(titulos: list[str], secao: str | None) -> str | None:
    """O RedatorDeAta.TemaDoItem: igual antes de prefixo, o mais longo, nunca o vazio."""
    alvo = normalizar(secao or "")
    if not alvo:
        return None
    for t in titulos:
        if normalizar(t) == alvo:
            return t
    candidatos = [t for t in titulos if normalizar(t)
                  and (normalizar(t).startswith(alvo) or alvo.startswith(normalizar(t)))]
    return max(candidatos, key=lambda t: len(normalizar(t)), default=None)


def palavras(md: str) -> int:
    # O que vai ao cliente: sem a seção de observações, que no clássico está dentro.
    md = md.split("## Observações sobre a transcrição")[0]
    return len(re.findall(r"[\wÀ-ú%]+", md))


def guardado(nome: str, formato: str, saida: Path) -> dict:
    """O que uma rodada anterior gerou, para medir de novo sem chamar o modelo.

    No por tema vale o `por-tema-v2` quando existe: o ata.md redigido de novo,
    a partir do mesmo ata.json, por um redator corrigido depois da geração.
    """
    md = saida / f"{nome}__{formato}__ata.md"
    v2 = saida / f"{nome}__{formato}-v2__ata.md"
    return {"md": (v2 if v2.is_file() else md).read_text(encoding="utf-8"),
            "json": json.loads((saida / f"{nome}__{formato}__ata.json").read_text(encoding="utf-8"))}


def gerar(copia: Path, tipo: str, formato: str, saida: Path) -> dict:
    # Acrescenta ao WSLENV em vez de trocar: quem já repassa outra variável
    # para o Windows continua repassando.
    wslenv = ":".join(x for x in (os.environ.get("WSLENV", ""), "MEETINGAPP_MOTOR_ATA") if x)
    env = dict(os.environ, MEETINGAPP_MOTOR_ATA=MOTOR, WSLENV=wslenv)
    cmd = [str(CLI), "--ata", win(copia), "--tipo", tipo, "--formato", formato]
    if modelo := modelo_do_app():
        cmd += ["--modelo", modelo]
    proc = subprocess.run(cmd,
                          capture_output=True, text=True, encoding="utf-8", errors="replace", env=env)
    if proc.returncode != 0:
        raise RuntimeError(f"{copia.name} {formato}: {proc.stdout}{proc.stderr}")
    for nome in ("ata.md", "ata.json", "ata.observacoes.md"):
        if (copia / nome).is_file():
            shutil.copy(copia / nome, saida / f"{copia.name}__{formato}__{nome}")
    md = (copia / "ata.md").read_text(encoding="utf-8")
    return {"md": md, "json": json.loads((copia / "ata.json").read_text(encoding="utf-8"))}


def itens(ata: dict) -> list[str | None]:
    """O 'secao' de cada decisão, ação, ponto e risco."""
    out = []
    for campo in ("decisoes", "pontos_em_aberto", "riscos", "acoes"):
        for i in ata.get(campo, []):
            out.append(i.get("secao") if isinstance(i, dict) else None)
    return out


def main() -> int:
    so_medir = "--medir" in sys.argv
    pedidos = [a.split(":", 1) for a in sys.argv[1:] if not a.startswith("--")]
    if not pedidos:
        print(__doc__)
        return 2
    saida = BASE / "resultados"
    saida.mkdir(parents=True, exist_ok=True)

    medidas, casados, total, encolheu = [], 0, 0, []
    for nome, tipo in pedidos:
        if so_medir:
            classico, tema = guardado(nome, "classico", saida), guardado(nome, "por-tema", saida)
        else:
            copia = BASE / nome
            if not copia.exists():
                shutil.copytree(ACERVO / nome, copia, ignore=shutil.ignore_patterns("*.wav"))
            classico = gerar(copia, tipo, "classico", saida)
            tema = gerar(copia, tipo, "por-tema", saida)

        m = {"reuniao": nome, "tipo": tipo,
             "palavras_classico": palavras(classico["md"]),
             "palavras_tema": palavras(tema["md"])}
        m["proporcao"] = m["palavras_tema"] / max(1, m["palavras_classico"])
        secoes = tema["json"].get("secoes", [])
        m["secao_com_subtitulos"] = any("###" in (s.get("texto") or "") for s in secoes)
        if agrupa(tipo):
            titulos = corpo(tema["json"], tipo)
            ss = itens(tema["json"])
            ok = sum(1 for s in ss if tema_do_item(titulos, s))
            m["itens"], m["itens_no_tema"] = len(ss), ok
            casados, total = casados + ok, total + len(ss)
            encolheu.append(m["proporcao"])
        else:
            def h2(md: str) -> list[str]:
                return [l for l in md.splitlines() if l.startswith("## ")
                        and "Observações" not in l]
            m["mesma_estrutura"] = h2(classico["md"]) == h2(tema["md"])
        medidas.append(m)
        print(json.dumps(m, ensure_ascii=False))

    criterios = {
        "1_itens_no_tema": casados / max(1, total),
        "2_sem_subtitulos": not any(m["secao_com_subtitulos"] for m in medidas
                                    if agrupa(m["tipo"])),
        "3_mediana_proporcao": statistics.median(encolheu) if encolheu else None,
        "4_daily_igual": all(m.get("mesma_estrutura", True) for m in medidas),
    }
    passou = (criterios["1_itens_no_tema"] >= 0.8 and criterios["2_sem_subtitulos"]
              and (criterios["3_mediana_proporcao"] or 1) <= 0.75 and criterios["4_daily_igual"])
    (saida / "medidas.json").write_text(
        json.dumps({"medidas": medidas, "criterios": criterios, "passou": passou},
                   ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(criterios, ensure_ascii=False), "PASSOU" if passou else "FALHOU")
    return 0 if passou else 1


if __name__ == "__main__":
    sys.exit(main())
