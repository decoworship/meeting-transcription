# Reuniões: achar a reunião e saber o que falta — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A lista de Reuniões vira uma lista que se procura — busca, filtros de cliente, data (atalhos, um dia ou uma semana) e estado, gravações agrupadas por dia — e que diz o que cada reunião precisa em seguida, num painel ao lado com o botão do próximo passo.

**Architecture:** O núcleo passa a mandar, na op `gravacoes`, o estado da ata de cada gravação (existe, velha, pendências, resumo) e os nomes dos convidados, lidos por um `Nucleo/EstadoDaAta.cs` novo e testado. Na página, as regras (filtrar, agrupar por dia, estado, próximo passo) moram num módulo puro `web/reunioes-regras.js`, testado com `node --test`; o desenho mora em `web/reunioes.js`, que sai do `app.js` pelo mesmo caminho que Gravador, Atas e Ajustes já saíram, e é provado num Chromium por `tools/provar_reunioes.py`.

**Tech Stack:** C# / .NET 8 (xUnit), JavaScript ES modules sem biblioteca, WebView2, AA Design System (CSS), Python + Playwright (prova de tela), Node 22 (`node --test`).

**Spec:** [docs/superpowers/specs/2026-09-23-ui-ux.md](../specs/2026-09-23-ui-ux.md) — §1 itens 2 e 3, §2 linhas "Busca e filtros", "Estados" e "Painel de detalhe", §3.3 "Lista" e "Painel", §4 inteiro. Mockup: https://claude.ai/artifact/BZLCSDP7cBLEhnwroZ5crS, pranchas "Reuniões · lista".

## Global Constraints

- CSP da página é `style-src 'self'` sem `'unsafe-inline'`: atributo `style` no HTML é ignorado em silêncio; estilo é classe no `app.css`, valor dinâmico é `elemento.style.x = …` (CSSOM).
- Nunca `innerHTML` com texto que veio do disco ou do modelo; nó com `textContent`.
- Só tokens do design system no `app.css` (`var(--cor-*)`, `--espaco-*`, `--raio-*`, `--texto-*`, `--fonte-*`); nenhum hexadecimal novo.
- O `data-tema="claro"` do `index.html` é procurado por texto exato pelo núcleo: não mexer.
- Anúncio (`anunciar()` de `pecas.js`) é de estado, nunca de conteúdo.
- Filtrar e buscar redesenham a lista, **nunca** a barra onde está o campo — o cursor não pode sair de quem digita (F-2 de `docs/FASE7-FRONTEND.md`).
- Arquivo novo em `app-net/App/web/` entra no executável sozinho (`web/*.*` no `.csproj`); **teste nunca mora lá** — mora em `tools/web/`.
- A página tem de aguentar um núcleo mais velho: com `MeetingApp.exe --web <pasta>` a interface nova roda contra o binário instalado, que não manda os campos novos. Campo novo ausente degrada (sem ata, lista vazia), nunca lança.
- Texto em português, na voz do app; "convidados", nunca "participantes".
- Publicar só pelo `tools/publicar.sh`, com o app fechado pela bandeja pelo dono — nunca `dotnet publish` na mão, nunca matar um `MeetingApp.exe` aberto.
- Comentários no estilo do código em volta: português, dizendo **por quê**, apontando o doc quando a razão mora num.
- `export PATH="$HOME/.dotnet:$PATH"` antes de qualquer `dotnet`.
- Tudo roda no worktree `/home/andre/projects/mt-reunioes` (Task 1, Step 1), nunca no checkout principal; `git add` sempre com os caminhos da tarefa, nunca `-A` nem `.`.

## Review Focus

1. **Digitar na busca não pode tirar o cursor do campo** — a pessoa digita "algar" de uma vez e espera ver as letras todas no campo. Provado em `prova_busca` (Task 4).
2. **Pasta com nome fora do padrão `AAAA-MM-DD_HH-MM-SS`** (reunião importada, pasta renomeada) aparece num grupo "Sem data" no fim da lista, e só some quando um período é escolhido. Node (Task 3) e `prova_grupos` (Task 4).
3. **Busca sem acento e com várias palavras** — "credito" acha "Crédito"; "algar rafael" acha a reunião da Algar em que Rafael foi convidado; "17/09" acha pela data. Node (Task 3).
4. **`ata.md` travado por outro programa, ou sem seção de Resumo**, não esconde a gravação da lista nem inventa resumo. C# (Task 1).
5. **Eventos de andamento chegando com o foco num botão do painel ou da lista** — a etiqueta muda, mas nem a linha nem o botão do painel são recriados. `prova_transcricao_em_curso` (Task 5).

---

## Mapa de arquivos

| arquivo | o quê |
|---|---|
| `app-net/Nucleo/Corte.cs` | **novo** — cortar texto numa palavra, com "…" |
| `app-net/Nucleo/EstadoDaAta.cs` | **novo** — existe, velha, pendências, primeiras pendências, resumo |
| `app-net/Nucleo/Notas.cs` | + `Notas.Inicio(pasta, limite)` |
| `app-net/Tests/EstadoDaAtaTests.cs` | **novo** |
| `app-net/Tests/NotasTests.cs` | + três testes do `Inicio` |
| `app-net/App/Ponte.cs` | `GravacaoResumo` ganha sete campos; `LerResumo` os preenche; a op `ata` usa `EstadoDaAta.EstaVelha` |
| `app-net/App/web/reunioes-regras.js` | **novo** — regras puras, sem DOM e sem import |
| `tools/web/reunioes-regras.test.mjs` | **novo** — `node --test` |
| `app-net/App/web/reunioes.js` | **novo** — a tela |
| `app-net/App/web/app.js` | a lista sai; `telaDeLista` vira moldura; `abrirAtas(opcoes)` |
| `app-net/App/web/atas.js` | `telaDeAtas(ctx, { foco })` abre e rola até a reunião pedida |
| `app-net/App/web/app.css` | bloco "reuniões" |
| `tools/provar_reunioes.py` | **novo** — a prova em Chromium |
| `docs/BACKLOG.md`, `docs/superpowers/specs/2026-09-23-ui-ux.md` | o estado do `UI-3` e do plano |

---

### Task 1: O estado da ata, lido do disco

**Files:**
- Create: `app-net/Nucleo/Corte.cs`
- Create: `app-net/Nucleo/EstadoDaAta.cs`
- Test: `app-net/Tests/EstadoDaAtaTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `MeetingApp.Nucleo.Corte.NumaPalavra(string texto, int limite) : string` (internal)
  - `MeetingApp.Nucleo.EstadoDaAta` — `record(bool Existe, bool Velha, int Pendencias, IReadOnlyList<string> PrimeirasPendencias, string? Resumo)`
  - `EstadoDaAta.Ler(string pasta) : EstadoDaAta` — nunca lança
  - `EstadoDaAta.EstaVelha(string pasta) : bool`
  - `EstadoDaAta.TamanhoDoResumo = 280`

- [ ] **Step 1: Criar o worktree**

Em 23/09/2026 outra sessão trabalhava no checkout principal, com um ramo dela
em curso. **Ramo não isola duas sessões na mesma pasta; worktree isola** —
já custou um commit misturado em agosto. Este plano roda inteiro no worktree,
partindo da `main`:

```bash
cd /home/andre/projects/meeting-transcription
git worktree add ../mt-reunioes -b ui/reunioes-lista main
mkdir -p ../mt-reunioes/docs/superpowers/specs ../mt-reunioes/docs/superpowers/plans
# O spec e este plano nasceram fora de ramo; entram no primeiro commit.
cp docs/superpowers/specs/2026-09-23-ui-ux.md ../mt-reunioes/docs/superpowers/specs/
cp docs/superpowers/plans/2026-09-23-ui-01-reunioes-lista.md ../mt-reunioes/docs/superpowers/plans/
cd ../mt-reunioes
```

Daqui em diante, **todo caminho relativo deste plano é a partir de
`/home/andre/projects/mt-reunioes`**.

- [ ] **Step 2: Escrever os testes que falham**

Criar `app-net/Tests/EstadoDaAtaTests.cs`:

```csharp
using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O estado da ata que a lista de reuniões mostra sem abri-la.
/// </summary>
/// <remarks>
/// O que dói errar aqui não dá erro: um contador de pendências que discorda da
/// ata, uma ata velha que a lista chama de pronta, um resumo inventado. E um
/// <c>ata.md</c> ilegível não pode esconder a gravação da lista.
/// </remarks>
public sealed class EstadoDaAtaTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("estado-ata-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    // O formato que o RedatorDeAta escreve, com os dois lados da pendência e um
    // item já fechado no meio.
    private const string Ata = """
        # Ata — Comunicação Beegol + App

        **Data:** 17/09/2026 · **Cliente:** Algar

        ## Resumo

        A reunião definiu o tom da comunicação
        por canal.

        ## Pendências

        ### Do nosso lado

        - [ ] Consolidar as referências — **Carol** — sexta
        - [x] Marcar a validação — **André** — [prazo a definir]

        ### Do lado do cliente

        - [ ] Revisar os textos de cobrança — **Jurídico** — [prazo a definir]
        """;

    private void Escrever(string arquivo, string texto) =>
        File.WriteAllText(Path.Combine(_pasta, arquivo), texto);

    [Fact]
    public void SemAtaNaoHaEstado()
    {
        var e = EstadoDaAta.Ler(_pasta);

        Assert.False(e.Existe);
        Assert.False(e.Velha);
        Assert.Equal(0, e.Pendencias);
        Assert.Empty(e.PrimeirasPendencias);
        Assert.Null(e.Resumo);
    }

    [Fact]
    public void ContaSoAsPendenciasAbertas()
    {
        Escrever("ata.md", Ata);

        Assert.Equal(2, EstadoDaAta.Ler(_pasta).Pendencias);
    }

    [Fact]
    public void AsPrimeirasPendenciasSaemSemAMarcacao()
    {
        Escrever("ata.md", Ata);

        Assert.Equal(
            ["Consolidar as referências — Carol — sexta",
             "Revisar os textos de cobrança — Jurídico — [prazo a definir]"],
            EstadoDaAta.Ler(_pasta).PrimeirasPendencias);
    }

    [Fact]
    public void OResumoEOPrimeiroParagrafoNumaLinhaSo()
    {
        Escrever("ata.md", Ata);

        Assert.Equal("A reunião definiu o tom da comunicação por canal.",
                     EstadoDaAta.Ler(_pasta).Resumo);
    }

    [Fact]
    public void AtaSemResumoNaoInventaUm()
    {
        Escrever("ata.md", "# Ata\n\n## Decisões\n\n- adiar o piloto\n");

        Assert.Null(EstadoDaAta.Ler(_pasta).Resumo);
    }

    [Fact]
    public void ResumoLongoECortadoNumaPalavra()
    {
        string longo = string.Join(" ", Enumerable.Repeat("palavra", 60));
        Escrever("ata.md", $"## Resumo\n\n{longo}\n");

        string resumo = EstadoDaAta.Ler(_pasta).Resumo!;

        Assert.True(resumo.Length <= EstadoDaAta.TamanhoDoResumo + 1);
        Assert.EndsWith("palavra…", resumo);
    }

    [Fact]
    public void AtaMaisVelhaQueATranscricaoEstaVelha()
    {
        Escrever("ata.md", Ata);
        Escrever("transcricao.json", "{}");
        File.SetLastWriteTimeUtc(Path.Combine(_pasta, "ata.md"), DateTime.UtcNow.AddMinutes(-10));

        Assert.True(EstadoDaAta.Ler(_pasta).Velha);
    }

    [Fact]
    public void AtaMaisNovaQueATranscricaoNaoEstaVelha()
    {
        Escrever("transcricao.json", "{}");
        Escrever("ata.md", Ata);
        File.SetLastWriteTimeUtc(Path.Combine(_pasta, "transcricao.json"),
                                 DateTime.UtcNow.AddMinutes(-10));

        Assert.False(EstadoDaAta.Ler(_pasta).Velha);
    }

    [Fact]
    public void AtaSemTranscricaoNaoEstaVelha()
    {
        Escrever("ata.md", Ata);

        Assert.False(EstadoDaAta.Ler(_pasta).Velha);
    }

    [Fact]
    public void AtaTravadaNaoLanca()
    {
        // Um editor com o arquivo aberto no Windows é o caso real. O contrato é
        // não lançar: a lista monta um resumo por gravação dentro de um try, e
        // uma exceção aqui esconderia a reunião inteira. Onde a trava não
        // impedir a leitura, o teste continua valendo — ele confere o contrato,
        // não o caminho.
        Escrever("ata.md", Ata);
        using var trava = new FileStream(Path.Combine(_pasta, "ata.md"),
                                         FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var e = EstadoDaAta.Ler(_pasta);

        Assert.True(e.Existe);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `export PATH="$HOME/.dotnet:$PATH"; dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~EstadoDaAtaTests"`
Expected: FAIL na compilação — `The name 'EstadoDaAta' does not exist in the current context`.

- [ ] **Step 4: Escrever o corte**

Criar `app-net/Nucleo/Corte.cs`:

```csharp
namespace MeetingApp.Nucleo;

/// <summary>Cortar um texto para caber numa linha de lista.</summary>
internal static class Corte
{
    /// <summary>
    /// Até <paramref name="limite"/> caracteres, sem partir palavra, com "…" no
    /// fim quando cortou.
    /// </summary>
    /// <remarks>
    /// Na palavra, e não no caractere: "o tom da comunica…" lê como erro; "o tom
    /// da…" lê como continuação.
    /// </remarks>
    public static string NumaPalavra(string texto, int limite)
    {
        if (texto.Length <= limite) return texto;
        int corte = texto.LastIndexOf(' ', limite);
        if (corte <= 0) corte = limite;
        return texto[..corte].TrimEnd(' ', ',', ';', ':') + "…";
    }
}
```

- [ ] **Step 5: Escrever o estado da ata**

Criar `app-net/Nucleo/EstadoDaAta.cs`:

```csharp
namespace MeetingApp.Nucleo;

/// <summary>
/// O que a lista de reuniões precisa saber da ata de uma gravação, sem abri-la.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lido do <c>ata.md</c>, e não guardado à parte.</b> O arquivo é escrito
/// pelo <c>RedatorDeAta</c>, que garante o formato: a pendência sai
/// <c>- [ ] Ação — **Responsável** — prazo</c>, e o resumo sai sob
/// <c>## Resumo</c>. Um segundo arquivo com os mesmos números envelheceria no
/// dia em que a ata fosse refeita por outro caminho.
/// </para>
/// <para>
/// <b>Nunca lança.</b> A lista monta um resumo por gravação dentro de um
/// <c>try</c> (<c>Ponte.Listar</c>), e uma exceção aqui esconderia a gravação
/// inteira — um <c>ata.md</c> travado custaria a reunião na lista, e não só a
/// ata.
/// </para>
/// </remarks>
public sealed record EstadoDaAta(bool Existe, bool Velha, int Pendencias,
                                 IReadOnlyList<string> PrimeirasPendencias, string? Resumo)
{
    /// <summary>Quanto do resumo a lista mostra. O resto está na ata.</summary>
    public const int TamanhoDoResumo = 280;

    /// <summary>Quantas pendências o painel da lista mostra.</summary>
    public const int PendenciasNoPainel = 3;

    public static readonly EstadoDaAta Nenhuma = new(false, false, 0, [], null);

    public static EstadoDaAta Ler(string pasta)
    {
        string caminho = Path.Combine(pasta, "ata.md");
        if (!File.Exists(caminho)) return Nenhuma;

        string texto;
        try
        {
            texto = File.ReadAllText(caminho);
        }
        catch (Exception)
        {
            // Existe e não se lê agora. A lista diz que há ata; abrir mostra o
            // erro de verdade, na hora em que ele importa.
            return new(true, EstaVelha(pasta), 0, [], null);
        }

        return new(true, EstaVelha(pasta), ContarPendencias(texto),
                   PrimeirasAbertas(texto), ExtrairResumo(texto));
    }

    /// <summary>
    /// A ata é mais velha que a transcrição: alguém corrigiu o texto depois que
    /// ela foi escrita, e ela não viu a correção.
    /// </summary>
    public static bool EstaVelha(string pasta)
    {
        try
        {
            string ata = Path.Combine(pasta, "ata.md");
            string transcricao = Path.Combine(pasta, "transcricao.json");
            return File.Exists(ata) && File.Exists(transcricao)
                   && File.GetLastWriteTimeUtc(ata) < File.GetLastWriteTimeUtc(transcricao);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Os itens abertos: <c>- [ ]</c> no começo da linha.</summary>
    /// <remarks>
    /// A mesma regra de <c>atas.js</c> (<c>/^- \[ \]/gm</c>), para a lista e a
    /// tela de Atas não discordarem do número.
    /// </remarks>
    private static int ContarPendencias(string markdown) =>
        markdown.Split('\n').Count(l => l.StartsWith("- [ ]", StringComparison.Ordinal));

    private static List<string> PrimeirasAbertas(string markdown) =>
        [.. markdown.Split('\n')
            .Where(l => l.StartsWith("- [ ]", StringComparison.Ordinal))
            .Select(l => l[5..].Replace("**", "").Trim())
            .Where(l => l.Length > 0)
            .Take(PendenciasNoPainel)];

    /// <summary>O primeiro parágrafo sob <c>## Resumo</c>, numa linha só e cortado.</summary>
    private static string? ExtrairResumo(string markdown)
    {
        string[] linhas = markdown.Replace("\r", "").Split('\n');
        int inicio = Array.FindIndex(linhas,
            l => l.Trim().Equals("## Resumo", StringComparison.OrdinalIgnoreCase));
        if (inicio < 0) return null;

        var paragrafo = new List<string>();
        for (int i = inicio + 1; i < linhas.Length; i++)
        {
            string l = linhas[i].Trim();
            if (l.StartsWith('#')) break;
            if (l.Length == 0)
            {
                if (paragrafo.Count > 0) break;
                continue;
            }
            paragrafo.Add(l);
        }

        return paragrafo.Count == 0
            ? null
            : Corte.NumaPalavra(string.Join(" ", paragrafo), TamanhoDoResumo);
    }
}
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~EstadoDaAtaTests"`
Expected: PASS, 10 testes.

- [ ] **Step 7: Commit**

```bash
git add docs/superpowers/specs/2026-09-23-ui-ux.md docs/superpowers/plans/2026-09-23-ui-01-reunioes-lista.md
git add app-net/Nucleo/Corte.cs app-net/Nucleo/EstadoDaAta.cs app-net/Tests/EstadoDaAtaTests.cs
git commit -m "feat(reunioes): o estado da ata lido do disco, para a lista

Existe, velha, pendências abertas, as três primeiras, e o resumo — do
ata.md que o RedatorDeAta escreve. Nunca lança: a lista monta um resumo
por gravação dentro de um try, e uma ata travada esconderia a reunião.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: A lista leva o estado da ata, os convidados e o começo das notas

**Files:**
- Modify: `app-net/Nucleo/Notas.cs` (acrescentar `Inicio` depois de `Existem`)
- Modify: `app-net/Tests/NotasTests.cs` (três testes no fim da classe)
- Modify: `app-net/App/Ponte.cs` — `GravacaoResumo` (~linha 487), `LerResumo` (~linha 2567), op `ata` (~linha 731)

**Interfaces:**
- Consumes: `EstadoDaAta.Ler`, `EstadoDaAta.EstaVelha`, `Corte.NumaPalavra` (Task 1).
- Produces: cada item de `pedir("gravacoes").gravacoes` ganha

  ```
  nomes: string[]              // convidados da agenda, por nome
  tem_ata: boolean
  ata_velha: boolean
  pendencias: number
  pendencias_inicio: string[]  // até 3, sem "- [ ]" e sem "**"
  resumo: string | null        // até 280 caracteres
  notas_inicio: string | null  // até 200 caracteres, linhas juntadas por " · "
  ```

- [ ] **Step 1: Escrever os testes do começo das notas**

Acrescentar ao fim da classe `NotasTests` em `app-net/Tests/NotasTests.cs`:

```csharp
    [Fact]
    public void OInicioDasNotasJuntaAsLinhas()
    {
        Notas.Salvar(_pasta, "Definir o tom antes do desenho.\n\n[00:30:38] Carol manda as referências.");

        Assert.Equal("Definir o tom antes do desenho. · [00:30:38] Carol manda as referências.",
                     Notas.Inicio(_pasta));
    }

    [Fact]
    public void SemNotasNaoHaInicio()
    {
        Assert.Null(Notas.Inicio(_pasta));
    }

    [Fact]
    public void NotaLongaECortadaNumaPalavra()
    {
        Notas.Salvar(_pasta, string.Join(" ", Enumerable.Repeat("pendência", 40)));

        string inicio = Notas.Inicio(_pasta, 50)!;

        Assert.True(inicio.Length <= 51);
        Assert.EndsWith("pendência…", inicio);
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~NotasTests"`
Expected: FAIL na compilação — `'Notas' does not contain a definition for 'Inicio'`.

- [ ] **Step 3: Escrever `Notas.Inicio`**

Em `app-net/Nucleo/Notas.cs`, logo depois do método `Existem`:

```csharp
    /// <summary>
    /// O começo das notas numa linha só, para o painel da lista. Nulo sem notas.
    /// </summary>
    /// <remarks>
    /// As linhas se juntam com " · " e não com espaço: uma nota é uma lista de
    /// coisas soltas, e juntá-las numa frase inventaria uma frase que ninguém
    /// escreveu.
    /// </remarks>
    public static string? Inicio(string pastaDaGravacao, int limite = 200)
    {
        string junto = string.Join(" · ", Ler(pastaDaGravacao)
            .Replace("\r", "")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0));
        return junto.Length == 0 ? null : Corte.NumaPalavra(junto, limite);
    }
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~NotasTests"`
Expected: PASS.

- [ ] **Step 5: Os campos novos no resumo da gravação**

Em `app-net/App/Ponte.cs`, na classe `GravacaoResumo`, trocar:

```csharp
    [JsonPropertyName("avisos")] public List<string> Avisos { get; init; } = [];
}
```

por:

```csharp
    [JsonPropertyName("avisos")] public List<string> Avisos { get; init; } = [];

    /// <summary>Os convidados da agenda por nome — é por eles que a busca acha a reunião.</summary>
    [JsonPropertyName("nomes")] public List<string> Nomes { get; init; } = [];

    /// <summary>
    /// O estado da ata, para a lista dizer o que a reunião precisa sem abri-la.
    /// Ver <c>Nucleo/EstadoDaAta.cs</c>.
    /// </summary>
    [JsonPropertyName("tem_ata")] public bool TemAta { get; init; }
    [JsonPropertyName("ata_velha")] public bool AtaVelha { get; init; }
    [JsonPropertyName("pendencias")] public int Pendencias { get; init; }
    [JsonPropertyName("pendencias_inicio")] public List<string> PendenciasInicio { get; init; } = [];
    [JsonPropertyName("resumo")] public string? Resumo { get; init; }

    /// <summary>O começo das notas, para o painel. Ver <c>Notas.Inicio</c>.</summary>
    [JsonPropertyName("notas_inicio")] public string? NotasInicio { get; init; }
}
```

- [ ] **Step 6: `LerResumo` preenche os campos**

Em `LerResumo`, trocar:

```csharp
        string? titulo = null;
        int convidados = 0;
        if (raiz.TryGetProperty("meeting", out var reuniao))
        {
            if (reuniao.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                titulo = t.GetString();
            if (reuniao.TryGetProperty("attendees", out var a) && a.ValueKind == JsonValueKind.Array)
                convidados = a.GetArrayLength();
        }
```

por:

```csharp
        string? titulo = null;
        int convidados = 0;
        var nomes = new List<string>();
        if (raiz.TryGetProperty("meeting", out var reuniao))
        {
            if (reuniao.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                titulo = t.GetString();
            if (reuniao.TryGetProperty("attendees", out var a) && a.ValueKind == JsonValueKind.Array)
            {
                convidados = a.GetArrayLength();
                foreach (var n in a.EnumerateArray())
                    if (n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } nome)
                        nomes.Add(nome);
            }
        }
```

E trocar o fim do método:

```csharp
            ComNotas = Notas.Existem(pasta),
            Avisos = avisos,
        };
    }
```

por:

```csharp
            ComNotas = Notas.Existem(pasta),
            Avisos = avisos,
            Nomes = nomes,
            TemAta = ata.Existe,
            AtaVelha = ata.Velha,
            Pendencias = ata.Pendencias,
            PendenciasInicio = [.. ata.PrimeirasPendencias],
            Resumo = ata.Resumo,
            NotasInicio = Notas.Inicio(pasta),
        };
    }
```

e, logo antes do `return new GravacaoResumo` desse método, acrescentar:

```csharp
        // Na lista, e não num pedido por linha: são poucos campos por gravação,
        // e um pedido por linha faria a lista piscar preenchendo-se aos poucos —
        // o mesmo argumento do vínculo, logo acima.
        var ata = EstadoDaAta.Ler(pasta);

```

- [ ] **Step 7: A op `ata` usa a mesma regra de "velha"**

No `case "ata":`, trocar:

```csharp
                        AtaVelha = File.Exists(caminho)
                                   && File.Exists(Path.Combine(onde, "transcricao.json"))
                                   && File.GetLastWriteTimeUtc(caminho)
                                      < File.GetLastWriteTimeUtc(Path.Combine(onde, "transcricao.json")),
```

por:

```csharp
                        AtaVelha = EstadoDaAta.EstaVelha(onde),
```

(o comentário de três linhas logo acima fica: ele explica o que "velha" quer dizer.)

- [ ] **Step 8: Compilar o app e rodar a suíte inteira**

Run: `dotnet build app-net/App/MeetingApp.App.csproj -v q && dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: build sem erro; todos os testes passam (os 628 de antes mais os 13 novos).

- [ ] **Step 9: Commit**

```bash
git add app-net/Nucleo/Notas.cs app-net/Tests/NotasTests.cs app-net/App/Ponte.cs
git commit -m "feat(reunioes): a lista leva o estado da ata, os convidados e as notas

A op gravacoes passa a mandar tem_ata, ata_velha, pendencias,
pendencias_inicio, resumo, nomes e notas_inicio. A op ata usa a mesma
regra de ata velha que a lista, para as duas não discordarem.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: As regras da lista, puras e testadas

**Files:**
- Create: `app-net/App/web/reunioes-regras.js`
- Test: `tools/web/reunioes-regras.test.mjs`

**Interfaces:**
- Consumes: o formato de gravação da Task 2.
- Produces (todas exportadas por `/reunioes-regras.js`):
  - `SEM_CLIENTE : string` — valor do filtro que pega gravação sem cliente
  - `PERIODOS : [valor, texto][]` — `tudo | hoje | esta-semana | semana-passada | 30 | dia | semana`; `ESTADOS : [valor, texto][]`
  - `intervaloDoPeriodo(periodo, data, hoje) : ["AAAA-MM-DD", "AAAA-MM-DD"] | null` — a semana vai de segunda a domingo; `dia`/`semana` usam `data`
  - `normalizar(texto) : string`
  - `diaDe(nome) : "AAAA-MM-DD" | null`, `horaDe(nome) : "HH:MM" | ""`
  - `hojeLocal(agora = new Date()) : "AAAA-MM-DD"`
  - `rotuloDoDia(dia, hoje) : string`
  - `agruparPorDia(gravacoes, hoje) : { dia, rotulo, itens }[]`
  - `estadoDe(g, rodando = null) : { chave, rotulo, tom }` — `chave ∈ transcrevendo | escrevendo-ata | nao-transcrita | sem-ata | ata-velha | ata-pronta`; `tom ∈ info | neutro | atencao | sucesso`
  - `proximoPasso(g, rodando = null) : { acao, rotulo }` — `acao ∈ transcrever | acompanhar-transcricao | acompanhar-ata | gerar-ata | refazer-ata | abrir-ata`
  - `textoDeBusca(g) : string`
  - `filtrar(gravacoes, { texto, cliente, periodo, data, estado }, { hoje, rodandoDe }) : gravacao[]`
  - `clientesDe(gravacoes) : { nome, n }[]`

  `rodando` é o que `emCurso(caminho)` de `transcricoes.js` devolve: `{ gravacao, tarefa, etapa, fracao, texto, … }` ou `null`.

- [ ] **Step 1: Escrever os testes que falham**

Criar `tools/web/reunioes-regras.test.mjs`:

```js
// As regras da lista de Reuniões, fora do navegador.
//
//     node --test tools/web/reunioes-regras.test.mjs
//
// O que dói errar aqui não dá erro: um filtro que esconde uma reunião só deixa
// a lista menor. Ver docs/superpowers/specs/2026-09-23-ui-ux.md §3.3.

import { test } from "node:test";
import assert from "node:assert/strict";

const R = await import(new URL("../../app-net/App/web/reunioes-regras.js", import.meta.url));

const G = (nome, extra = {}) => ({
  nome, caminho: `C:\\Rec\\${nome}`, titulo: null, cliente: null, projeto: null,
  transcrita: true, tem_ata: false, ata_velha: false, pendencias: 0, nomes: [], ...extra,
});

test("normalizar tira acento, maiúscula e espaço das pontas", () => {
  assert.equal(R.normalizar("  Agente de CRÉDITO "), "agente de credito");
  assert.equal(R.normalizar(null), "");
});

test("diaDe e horaDe leem o nome da pasta, e nome fora do padrão não tem dia", () => {
  assert.equal(R.diaDe("2026-09-23_14-00-12"), "2026-09-23");
  assert.equal(R.horaDe("2026-09-23_14-00-12"), "14:00");
  assert.equal(R.diaDe("reuniao-importada"), null);
  assert.equal(R.horaDe("reuniao-importada"), "");
});

test("hojeLocal escreve a data local com zeros", () => {
  assert.equal(R.hojeLocal(new Date(2026, 8, 3, 23, 59)), "2026-09-03");
});

test("rotuloDoDia diz hoje, ontem, o dia da semana, e o ano quando não é o de hoje", () => {
  assert.equal(R.rotuloDoDia("2026-09-23", "2026-09-23"), "Hoje · quarta, 23 set");
  assert.equal(R.rotuloDoDia("2026-09-22", "2026-09-23"), "Ontem · terça, 22 set");
  assert.equal(R.rotuloDoDia("2026-09-17", "2026-09-23"), "Quinta, 17 set");
  assert.equal(R.rotuloDoDia("2025-12-31", "2026-09-23"), "Quarta, 31 dez 2025");
  assert.equal(R.rotuloDoDia(null, "2026-09-23"), "Sem data");
});

test("agruparPorDia mantém a ordem e manda o que não tem data para o fim", () => {
  // O núcleo ordena pelo nome, decrescente — e "reuniao-…" vem ANTES dos
  // números nessa ordem. É o caso que a regra existe para consertar.
  const lista = [G("reuniao-importada"), G("2026-09-23_11-02-00"),
                 G("2026-09-23_09-00-00"), G("2026-09-17_13-59-57")];

  const grupos = R.agruparPorDia(lista, "2026-09-23");

  assert.deepEqual(grupos.map((g) => g.rotulo),
                   ["Hoje · quarta, 23 set", "Quinta, 17 set", "Sem data"]);
  assert.deepEqual(grupos[0].itens.map((g) => g.nome),
                   ["2026-09-23_11-02-00", "2026-09-23_09-00-00"]);
});

test("estadoDe segue a escada, e o que roda vem antes de tudo", () => {
  assert.equal(R.estadoDe(G("a", { transcrita: false })).chave, "nao-transcrita");
  assert.equal(R.estadoDe(G("a")).chave, "sem-ata");
  assert.equal(R.estadoDe(G("a", { tem_ata: true })).chave, "ata-pronta");
  assert.equal(R.estadoDe(G("a", { tem_ata: true, ata_velha: true })).chave, "ata-velha");
  assert.deepEqual(
    R.estadoDe(G("a", { transcrita: false }), { tarefa: "transcricao", etapa: "diarizacao" }),
    { chave: "transcrevendo", rotulo: "Separando falantes…", tom: "info" });
  assert.equal(R.estadoDe(G("a"), { tarefa: "ata", etapa: "lendo" }).rotulo, "Escrevendo a ata…");
});

test("estadoDe aguenta o núcleo antigo, que não manda os campos da ata", () => {
  const antiga = { nome: "2026-09-23_11-02-00", caminho: "x", transcrita: true };
  assert.equal(R.estadoDe(antiga).chave, "sem-ata");
});

test("proximoPasso oferece o que a reunião precisa", () => {
  assert.equal(R.proximoPasso(G("a", { transcrita: false })).acao, "transcrever");
  assert.equal(R.proximoPasso(G("a")).acao, "gerar-ata");
  assert.equal(R.proximoPasso(G("a", { tem_ata: true, ata_velha: true })).acao, "refazer-ata");
  assert.equal(R.proximoPasso(G("a", { tem_ata: true })).acao, "abrir-ata");
  assert.equal(R.proximoPasso(G("a"), { tarefa: "transcricao" }).acao, "acompanhar-transcricao");
  assert.equal(R.proximoPasso(G("a"), { tarefa: "ata" }).acao, "acompanhar-ata");
});

const acervo = [
  G("2026-09-23_11-02-00", { titulo: "Reunião de lideranças", cliente: "Beegol (interno)", projeto: "Gestão" }),
  G("2026-09-23_10-30-00", { titulo: "Semanal — Uberlândia", transcrita: false }),
  G("2026-09-17_13-59-57", { titulo: "Comunicação Beegol + App", cliente: "Algar", projeto: "Agentes",
                             nomes: ["Rafael Prado"], tem_ata: true, pendencias: 3 }),
  G("2026-09-16_15-29-00", { titulo: "Kickoff", cliente: "Algar", projeto: "Agente de Crédito", tem_ata: true }),
  G("2026-08-01_09-00-00", { titulo: "Antiga", cliente: "Vivo", projeto: "Sherlock" }),
  G("reuniao-importada", { titulo: "Importada" }),
];
const titulos = (l) => l.map((g) => g.titulo);
const opcoes = { hoje: "2026-09-23" };

test("sem critério, nada some", () => {
  assert.equal(R.filtrar(acervo, {}, opcoes).length, acervo.length);
});

test("a busca ignora acento, casa todas as palavras, e olha projeto, convidado e data", () => {
  assert.deepEqual(titulos(R.filtrar(acervo, { texto: "credito" }, opcoes)), ["Kickoff"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { texto: "algar rafael" }, opcoes)),
                   ["Comunicação Beegol + App"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { texto: "17/09" }, opcoes)),
                   ["Comunicação Beegol + App"]);
});

test("o filtro de cliente separa, e 'sem cliente' pega os que não têm", () => {
  assert.deepEqual(titulos(R.filtrar(acervo, { cliente: "Algar" }, opcoes)),
                   ["Comunicação Beegol + App", "Kickoff"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { cliente: R.SEM_CLIENTE }, opcoes)),
                   ["Semanal — Uberlândia", "Importada"]);
});

test("a semana vai de segunda a domingo, inclusive quando atravessa o mês", () => {
  // 23/09/2026 é uma quarta: esta semana vai de 21 a 27, a passada de 14 a 20.
  assert.deepEqual(R.intervaloDoPeriodo("esta-semana", "", "2026-09-23"), ["2026-09-21", "2026-09-27"]);
  assert.deepEqual(R.intervaloDoPeriodo("semana-passada", "", "2026-09-23"), ["2026-09-14", "2026-09-20"]);
  // 06/09 é domingo: a semana dele começou na segunda, 31 de agosto.
  assert.deepEqual(R.intervaloDoPeriodo("semana", "2026-09-06", "2026-09-23"), ["2026-08-31", "2026-09-06"]);
  assert.deepEqual(R.intervaloDoPeriodo("dia", "2026-09-17", "2026-09-23"), ["2026-09-17", "2026-09-17"]);
  assert.deepEqual(R.intervaloDoPeriodo("30", "", "2026-09-23"), ["2026-08-25", "2026-09-23"]);
  assert.equal(R.intervaloDoPeriodo("tudo", "", "2026-09-23"), null);
});

test("'um dia' sem o dia escolhido ainda não filtra nada", () => {
  assert.equal(R.intervaloDoPeriodo("dia", "", "2026-09-23"), null);
  assert.equal(R.filtrar(acervo, { periodo: "dia", data: "" }, opcoes).length, acervo.length);
});

test("a data filtra pelo dia da pasta, e o sem data só aparece em 'qualquer data'", () => {
  assert.deepEqual(titulos(R.filtrar(acervo, { periodo: "esta-semana" }, opcoes)),
                   ["Reunião de lideranças", "Semanal — Uberlândia"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { periodo: "semana-passada" }, opcoes)),
                   ["Comunicação Beegol + App", "Kickoff"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { periodo: "dia", data: "2026-09-17" }, opcoes)),
                   ["Comunicação Beegol + App"]);
  assert.equal(R.filtrar(acervo, { periodo: "30" }, opcoes).length, 4);
  assert.equal(R.filtrar(acervo, { periodo: "tudo" }, opcoes).length, acervo.length);
});

test("o estado filtra pela escada, e 'com pendências' é ata com item aberto", () => {
  assert.deepEqual(titulos(R.filtrar(acervo, { estado: "nao-transcrita" }, opcoes)),
                   ["Semanal — Uberlândia"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { estado: "com-pendencias" }, opcoes)),
                   ["Comunicação Beegol + App"]);
  assert.deepEqual(titulos(R.filtrar(acervo, { estado: "ata-pronta" }, opcoes)),
                   ["Comunicação Beegol + App", "Kickoff"]);
});

test("o estado usa o que está rodando: transcrição em curso não é 'não transcrita'", () => {
  const rodandoDe = (c) => (c === acervo[1].caminho ? { tarefa: "transcricao", etapa: "asr" } : null);
  assert.deepEqual(R.filtrar(acervo, { estado: "nao-transcrita" }, { ...opcoes, rodandoDe }), []);
});

test("clientesDe conta e ordena em português", () => {
  assert.deepEqual(R.clientesDe(acervo),
                   [{ nome: "Algar", n: 2 }, { nome: "Beegol (interno)", n: 1 }, { nome: "Vivo", n: 1 }]);
});
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `node --test tools/web/reunioes-regras.test.mjs`
Expected: FAIL — `Cannot find module '…/app-net/App/web/reunioes-regras.js'`.

- [ ] **Step 3: Escrever as regras**

Criar `app-net/App/web/reunioes-regras.js`:

```js
// As regras da lista de Reuniões: o que a busca vê, como se agrupa por dia, e o
// que cada gravação precisa em seguida.
//
// **Sem DOM e sem import**, de propósito. É a parte da tela que erra em
// silêncio — um filtro que esconde uma reunião não dá erro, só deixa a lista
// menor —, e por isso é a que se testa fora do navegador:
//
//     node --test tools/web/reunioes-regras.test.mjs
//
// O desenho está em docs/superpowers/specs/2026-09-23-ui-ux.md §3.3.

const SEMANA = ["domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado"];
const MESES = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

/** O valor do filtro de cliente que pega as gravações sem cliente. */
export const SEM_CLIENTE = "__sem_cliente__";

/** [valor, texto]. O texto diz o critério inteiro, porque o seletor não tem rótulo visível. */
export const PERIODOS = [
  ["tudo", "Qualquer data"],
  ["hoje", "Hoje"],
  ["esta-semana", "Esta semana"],
  ["semana-passada", "Semana passada"],
  ["30", "Últimos 30 dias"],
  ["dia", "Um dia…"],
  ["semana", "Uma semana…"],
];

export const ESTADOS = [
  ["", "Todos os estados"],
  ["nao-transcrita", "Não transcritas"],
  ["sem-ata", "Sem ata"],
  ["com-pendencias", "Com pendências"],
  ["ata-velha", "Ata desatualizada"],
  ["ata-pronta", "Ata pronta"],
];

/**
 * O andamento em palavras, pela etapa.
 *
 * Sem porcentagem: a fração do registro é da etapa, e não do pipeline
 * (Nucleo/RegistroDeTranscricoes.cs) — "46%" durante a separação de falantes
 * diria que falta metade quando falta um quarto.
 */
const ETAPAS = {
  mix: "Somando as faixas…",
  asr: "Transcrevendo…",
  diarizacao: "Separando falantes…",
  montagem: "Montando…",
};

/** Minúsculas e sem acento: "Crédito" e "credito" são a mesma busca. */
export function normalizar(texto) {
  return String(texto ?? "").normalize("NFD").replace(/[\u0300-\u036f]/g, "")
    .toLowerCase().trim();
}

/** "2026-09-23_14-00-12" → "2026-09-23". Nome fora do padrão → null. */
export function diaDe(nome) {
  const m = /^(\d{4})-(\d{2})-(\d{2})_/.exec(nome ?? "");
  return m ? `${m[1]}-${m[2]}-${m[3]}` : null;
}

/** "2026-09-23_14-00-12" → "14:00". Nome fora do padrão → "". */
export function horaDe(nome) {
  const m = /^\d{4}-\d{2}-\d{2}_(\d{2})-(\d{2})/.exec(nome ?? "");
  return m ? `${m[1]}:${m[2]}` : "";
}

/** A data de hoje no fuso da máquina — o mesmo em que o gravador nomeia as pastas. */
export function hojeLocal(agora = new Date()) {
  const p = (n) => String(n).padStart(2, "0");
  return `${agora.getFullYear()}-${p(agora.getMonth() + 1)}-${p(agora.getDate())}`;
}

/** "AAAA-MM-DD" como meia-noite UTC — conta de calendário, sem hora e sem fuso. */
function utc(dia) {
  const [a, m, d] = dia.split("-").map(Number);
  return new Date(Date.UTC(a, m - 1, d));
}

const iso = (data) => data.toISOString().slice(0, 10);

/** Dias de calendário entre duas datas "AAAA-MM-DD". */
function diasEntre(de, ate) {
  return Math.round((utc(ate) - utc(de)) / 86_400_000);
}

function somarDias(dia, n) {
  const d = utc(dia);
  d.setUTCDate(d.getUTCDate() + n);
  return iso(d);
}

/** A segunda-feira da semana de um dia. A semana vai de segunda a domingo. */
function segundaDe(dia) {
  return somarDias(dia, -((utc(dia).getUTCDay() + 6) % 7));
}

/**
 * O intervalo [de, até] de um período, as duas pontas incluídas, ou null
 * para "qualquer data" — e para "um dia" ou "uma semana" enquanto o dia não
 * foi escolhido: um filtro pela metade não pode esvaziar a lista.
 *
 * `data` é o dia escolhido no calendário ("AAAA-MM-DD"); só "dia" e "semana"
 * o usam.
 */
export function intervaloDoPeriodo(periodo, data, hoje) {
  if (periodo === "dia") return data ? [data, data] : null;
  if (periodo === "semana") {
    if (!data) return null;
    const s = segundaDe(data);
    return [s, somarDias(s, 6)];
  }
  if (!hoje) return null;
  if (periodo === "hoje") return [hoje, hoje];
  if (periodo === "esta-semana") {
    const s = segundaDe(hoje);
    return [s, somarDias(s, 6)];
  }
  if (periodo === "semana-passada") {
    const s = somarDias(segundaDe(hoje), -7);
    return [s, somarDias(s, 6)];
  }
  if (periodo === "30") return [somarDias(hoje, -29), hoje];
  return null;
}

/** "Hoje · quarta, 23 set", "Ontem · terça, 22 set", "Quinta, 17 set", "Quarta, 31 dez 2025". */
export function rotuloDoDia(dia, hoje) {
  if (!dia) return "Sem data";
  const [a, m, d] = dia.split("-").map(Number);
  const semana = SEMANA[new Date(Date.UTC(a, m - 1, d)).getUTCDay()];
  const ano = hoje && dia.slice(0, 4) !== hoje.slice(0, 4) ? ` ${a}` : "";
  const data = `${d} ${MESES[m - 1]}${ano}`;
  const distancia = hoje ? diasEntre(dia, hoje) : null;
  if (distancia === 0) return `Hoje · ${semana}, ${data}`;
  if (distancia === 1) return `Ontem · ${semana}, ${data}`;
  return `${semana[0].toUpperCase()}${semana.slice(1)}, ${data}`;
}

/**
 * A lista em grupos de um dia, na ordem em que chegou.
 *
 * O núcleo ordena pelo nome da pasta, decrescente, e nessa ordem um nome fora
 * do padrão ("reuniao-importada") vem ANTES dos números. O grupo "Sem data" vai
 * para o fim à mão.
 */
export function agruparPorDia(gravacoes, hoje) {
  const grupos = new Map();
  for (const g of gravacoes) {
    const dia = diaDe(g.nome);
    const chave = dia ?? "";
    if (!grupos.has(chave)) grupos.set(chave, { dia, rotulo: rotuloDoDia(dia, hoje), itens: [] });
    grupos.get(chave).itens.push(g);
  }
  const todos = [...grupos.values()];
  return [...todos.filter((x) => x.dia), ...todos.filter((x) => !x.dia)];
}

/**
 * O estado da gravação em poucas palavras, e o tom da etiqueta.
 *
 * O que está rodando vem antes de tudo: "Não transcrita" ao lado de uma
 * transcrição que está andando é mentira — a regra que a etiquetaDe do app.js
 * já seguia.
 *
 * `tem_ata` e `ata_velha` ausentes (núcleo antigo, --web) contam como falso.
 */
export function estadoDe(g, rodando = null) {
  if (rodando) {
    if (rodando.tarefa === "ata")
      return { chave: "escrevendo-ata", rotulo: "Escrevendo a ata…", tom: "info" };
    const rotulo = rodando.tarefa === "falantes" ? "Separando falantes…"
      : ETAPAS[rodando.etapa] ?? "Transcrevendo…";
    return { chave: "transcrevendo", rotulo, tom: "info" };
  }
  if (!g.transcrita) return { chave: "nao-transcrita", rotulo: "Não transcrita", tom: "neutro" };
  if (!g.tem_ata) return { chave: "sem-ata", rotulo: "Sem ata", tom: "neutro" };
  if (g.ata_velha) return { chave: "ata-velha", rotulo: "Ata desatualizada", tom: "atencao" };
  return { chave: "ata-pronta", rotulo: "Ata pronta", tom: "sucesso" };
}

/** O que a reunião precisa agora — o botão principal do painel. */
export function proximoPasso(g, rodando = null) {
  if (rodando)
    return rodando.tarefa === "ata"
      ? { acao: "acompanhar-ata", rotulo: "Acompanhar a ata" }
      : { acao: "acompanhar-transcricao", rotulo: "Acompanhar a transcrição" };
  if (!g.transcrita) return { acao: "transcrever", rotulo: "Transcrever" };
  if (!g.tem_ata) return { acao: "gerar-ata", rotulo: "Gerar a ata" };
  if (g.ata_velha) return { acao: "refazer-ata", rotulo: "Refazer a ata" };
  return { acao: "abrir-ata", rotulo: "Abrir a ata" };
}

/** "2026-09-17_…" → "17/09/2026", para achar a reunião sem título pela data. */
function dataCurta(nome) {
  const dia = diaDe(nome);
  if (!dia) return "";
  const [a, m, d] = dia.split("-");
  return `${d}/${m}/${a}`;
}

/** Tudo o que a busca olha numa gravação, normalizado. */
export function textoDeBusca(g) {
  return normalizar([g.titulo, g.cliente, g.projeto, dataCurta(g.nome), ...(g.nomes ?? [])]
    .filter(Boolean).join(" "));
}

/**
 * As gravações que passam em todos os critérios.
 *
 * A busca casa **todas** as palavras, em qualquer campo: "algar rafael" é a
 * reunião da Algar em que o Rafael foi convidado, não qualquer uma das duas.
 *
 * @param rodandoDe o `emCurso` de transcricoes.js — o estado depende do que
 *   está rodando agora.
 */
export function filtrar(gravacoes, criterios = {}, { hoje = null, rodandoDe = () => null } = {}) {
  const { texto = "", cliente = "", periodo = "tudo", data = "", estado = "" } = criterios;
  const palavras = normalizar(texto).split(/\s+/).filter(Boolean);
  const faixa = intervaloDoPeriodo(periodo, data, hoje);

  return gravacoes.filter((g) => {
    if (cliente === SEM_CLIENTE) {
      if (g.cliente) return false;
    } else if (cliente && g.cliente !== cliente) {
      return false;
    }

    // "AAAA-MM-DD" compara como texto na mesma ordem do calendário.
    if (faixa) {
      const dia = diaDe(g.nome);
      if (!dia || dia < faixa[0] || dia > faixa[1]) return false;
    }

    if (estado === "com-pendencias") {
      if (!(g.tem_ata && g.pendencias > 0)) return false;
    } else if (estado && estadoDe(g, rodandoDe(g.caminho)).chave !== estado) {
      return false;
    }

    if (palavras.length > 0) {
      const alvo = textoDeBusca(g);
      if (!palavras.every((p) => alvo.includes(p))) return false;
    }
    return true;
  });
}

/** Os clientes que aparecem no acervo, com quantas gravações cada um, em ordem alfabética. */
export function clientesDe(gravacoes) {
  const contagem = new Map();
  for (const g of gravacoes)
    if (g.cliente) contagem.set(g.cliente, (contagem.get(g.cliente) ?? 0) + 1);
  return [...contagem.entries()]
    .sort((a, b) => a[0].localeCompare(b[0], "pt-BR"))
    .map(([nome, n]) => ({ nome, n }));
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `node --test tools/web/reunioes-regras.test.mjs`
Expected: `# pass 17`, `# fail 0`.

- [ ] **Step 5: Commit**

```bash
git add app-net/App/web/reunioes-regras.js tools/web/reunioes-regras.test.mjs
git commit -m "feat(reunioes): as regras da lista, sem DOM e testadas com node

Busca sem acento e com todas as palavras, filtros de cliente, período e
estado, grupos por dia com o sem-data no fim, e a escada de estados com
o próximo passo de cada reunião.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: A lista que se procura

**Files:**
- Create: `app-net/App/web/reunioes.js`
- Create: `tools/provar_reunioes.py`
- Modify: `app-net/App/web/app.js` — o bloco `// ── lista` (hoje linhas 99–262: `cartao`, `etiquetaDe`, `avisoDeVersaoDispensado`, `avisarDeVersaoNova`, `telaDeLista`) e os imports
- Modify: `app-net/App/web/app.css` — bloco novo depois de `.vazio`

**Interfaces:**
- Consumes: tudo o que a Task 3 exporta; `duracao`, `tituloDe`, `abrirGravacao`, `abrirGravador` exportados por `/app.js`; `assinarTranscricoes`, `emCurso`, `ultimoResultado` de `/transcricoes.js`; `alerta`, `anunciar` de `/pecas.js`.
- Produces:
  - `telaDeReunioes({ cabecalho, tela }) : Promise<void>` exportada por `/reunioes.js`
  - DOM: `#busca-reunioes`, `#filtro-cliente`, `#filtro-periodo`, `#filtro-data` (o `<input type="date">` que aparece com "Um dia…" e "Uma semana…"), `#filtro-estado`, `.reunioes__dia`, `.reuniao-linha[data-gravacao]` com `.reuniao-linha__titulo`, `.reuniao-linha__meta` e a etiqueta como último filho (`span.aa-etiqueta[data-estado]`), `.reunioes__vazio`
  - `tools/provar_reunioes.py` com `PONTE_FALSA`, `abrir()`, `conferir()`, `linha_por_titulo()` e a lista `PROVAS`, que as Tasks 5 e 6 estendem

- [ ] **Step 1: Escrever a prova que falha**

Criar `tools/provar_reunioes.py`:

```python
#!/usr/bin/env python3
"""Prova a tela de Reuniões num Chromium de verdade.

**Por que existe.** Um filtro que esconde uma reunião não dá erro nenhum: a
lista só fica menor. E redesenhar a barra de busca a cada tecla tira o cursor
de quem digita — o F-2 de docs/FASE7-FRONTEND.md, que só aparece rodando.

Monta o app inteiro com uma ponte falsa, no molde de
tools/provar_painel_ao_vivo.py, e confere o que a tela promete
(docs/superpowers/specs/2026-09-23-ui-ux.md §3.3). Cada prova abre uma página
nova: os critérios moram no módulo, e uma prova não pode herdar os da outra.

Uso::

    uv run --with playwright python tools/provar_reunioes.py [--fotos PASTA]
"""

from __future__ import annotations

import argparse
import http.server
import socketserver
import sys
import threading
from datetime import date, timedelta
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
WEB = RAIZ / "app-net" / "App" / "web"
DS = RAIZ / "assets" / "ds"

# A ponte falsa. As datas nascem de "hoje", porque a lista agrupa por "Hoje" e
# "Ontem" — com datas fixas, a prova quebraria no dia seguinte.
PONTE_FALSA = r"""
(() => {
  const agora = new Date();
  const p = (n) => String(n).padStart(2, "0");
  const nome = (dias, h, m) => {
    const d = new Date(agora.getFullYear(), agora.getMonth(), agora.getDate() - dias);
    return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}_${p(h)}-${p(m)}-00`;
  };
  const G = (n, extra) => Object.assign({
    nome: n, caminho: "C:\\Rec\\" + n, duracao_s: 1800, titulo: null, convidados: 0,
    transcrita: true, cliente: null, projeto: null, com_notas: false, avisos: [],
    tem_ata: false, ata_velha: false, pendencias: 0, pendencias_inicio: [],
    resumo: null, nomes: [], notas_inicio: null,
  }, extra);

  // Na ordem do núcleo: nome da pasta, decrescente — o "reuniao-…" vem primeiro.
  const gravacoes = [
    G("reuniao-importada", { titulo: "Reunião importada", duracao_s: 600 }),
    G(nome(0, 11, 2), { titulo: "Reunião de lideranças", cliente: "Beegol (interno)",
                        projeto: "Gestão", convidados: 9, nomes: ["Heitor Lima"] }),
    G(nome(0, 10, 30), { titulo: "Semanal — Beegol · Uberlândia", transcrita: false, convidados: 6 }),
    G(nome(0, 9, 0), { titulo: "Sherlock Diário — Status e Ações", cliente: "Vivo", projeto: "Sherlock" }),
    G(nome(6, 13, 59), { titulo: "Comunicação Beegol + App", cliente: "Algar", projeto: "Agentes",
      convidados: 7, nomes: ["Carol Souza", "Rafael Prado"], tem_ata: true, pendencias: 3,
      resumo: "O tom da comunicação passa a ser separado por canal.",
      pendencias_inicio: ["Consolidar as referências — Carol — sexta",
                          "Marcar a validação — André — [prazo a definir]",
                          "Revisar os textos de cobrança — Jurídico — [prazo a definir]"],
      notas_inicio: "Definir o tom antes do novo desenho." }),
    G(nome(6, 8, 59), { titulo: "Pedido Sugerido — alinhamento", cliente: "Coca-Cola — CCIL",
                        projeto: "Pedido Sugerido", tem_ata: true, ata_velha: true }),
    G(nome(7, 15, 29), { titulo: "Agente de Crédito — kickoff", cliente: "Algar",
                         projeto: "Agente de Crédito", tem_ata: true }),
    G(nome(7, 10, 30), { avisos: ["O microfone não teve áudio nenhum."] }),
    G(nome(45, 14, 0), { titulo: "Planejamento trimestral", cliente: "Beegol (interno)", projeto: "App" }),
  ];

  const transcricao = JSON.stringify({ language: "pt", segments: [
    { start: 0, end: 4, text: " Bom dia.", speaker: "André" },
    { start: 4, end: 9, text: " Vamos começar.", speaker: "Carol" },
  ] });
  const ata = "# Ata — Comunicação Beegol + App\n\n## Resumo\n\nO tom passa a ser separado por canal.\n\n"
            + "## Pendências\n\n- [ ] Consolidar as referências — **Carol** — sexta\n";

  const responder = (q) => {
    switch (q.op) {
      case "gravacoes": return { gravacoes };
      case "transcricoes": return { transcricoes: { atual: null, ultimo: null } };
      case "catalogo": return { catalogo: [{ pacote: { id: "large-v3", nome: "Large v3", familia: "asr" },
                                             estado: "instalado", em_uso: true }], diarizadores: [] };
      case "atualizacao": return { atualizacao: { nova: null, versao_instalada: "0.7.1" } };
      case "config": return { config: {} };
      case "transcricao": return { transcricao };
      case "reuniao": return { cliente: "", projeto: "" };
      case "legenda-gravada": return { legenda_gravada: [] };
      case "clientes": return { clientes: {} };
      case "prefs": return { prefs: null };
      case "notas": return { notas: "" };
      case "modelos-de-ata": return { tipos: [{ id: "geral", nome: "Reunião geral" }] };
      case "ata": return q.gravacao.includes("13-59") ? { ata, ata_velha: false } : { ata: null };
      default: return {};
    }
  };

  window.chrome = { webview: {
    _ouvintes: [],
    addEventListener(_, f) { this._ouvintes.push(f); },
    postMessage(cru) {
      const q = JSON.parse(cru);
      const r = Object.assign({ id: q.id }, responder(q));
      setTimeout(() => { for (const f of this._ouvintes) f({ data: JSON.stringify(r) }); }, 0);
    },
  } };
  window.__gravacoes = gravacoes;
  window.__emitir = (ev) => {
    for (const f of window.chrome.webview._ouvintes)
      f({ data: JSON.stringify(Object.assign({ id: 0 }, ev)) });
  };
})();
"""

# Acha uma linha pelo começo do título e a guarda em window.__linha, para
# conferir depois se ela é o MESMO nó — é assim que se prova que a lista não
# foi redesenhada.
LINHA_POR_TITULO = """(inicio) => {
  const l = [...document.querySelectorAll('.reuniao-linha')]
    .find((b) => b.querySelector('.reuniao-linha__titulo').textContent.startsWith(inicio));
  window.__linha = l || null;
  return Boolean(l);
}"""


class Servidor(http.server.SimpleHTTPRequestHandler):
    """Mapeia /ds/ para o design system, como o app faz por recurso embutido."""

    def log_message(self, *args):
        pass

    def translate_path(self, path):
        path = path.split("?")[0]
        if path.startswith("/ds/"):
            return str(DS / path[4:])
        return str(WEB / path.lstrip("/"))


falhas: list[str] = []


def conferir(condicao: bool, o_que: str) -> None:
    print(("  ok     " if condicao else "  FALHA  ") + o_que)
    if not condicao:
        falhas.append(o_que)


def abrir(navegador, porta: int, largura: int = 1280, altura: int = 800, tema: str | None = None):
    pagina = navegador.new_page(viewport={"width": largura, "height": altura})
    erros: list[str] = []
    pagina.on("pageerror", lambda e: erros.append(str(e)))
    pagina.add_init_script(PONTE_FALSA)
    # Antes da primeira pintura, como o núcleo faz ao servir a página. Trocar
    # depois fotografa os botões no meio da transição de cor do design system.
    if tema:
        pagina.add_init_script(
            "document.addEventListener('DOMContentLoaded', () => "
            f"document.documentElement.dataset.tema = '{tema}')")
    pagina.goto(f"http://127.0.0.1:{porta}/index.html", wait_until="load")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    return pagina, erros


def titulos(pagina) -> list[str]:
    return pagina.eval_on_selector_all(
        ".reuniao-linha__titulo", "els => els.map((e) => e.textContent)")


def linha_por_titulo(pagina, inicio: str) -> bool:
    return pagina.evaluate(LINHA_POR_TITULO, inicio)


def buscar(pagina, texto: str) -> None:
    """Digita letra por letra, como gente — é o que pega o cursor saindo do campo."""
    pagina.fill("#busca-reunioes", "")
    pagina.locator("#busca-reunioes").press_sequentially(texto)
    pagina.wait_for_timeout(50)


# ─────────────────────────────────────────────────────────────── as provas

SEM_ROLAGEM_LATERAL = """() => {
  const c = document.querySelector('.conteudo');
  return c.scrollWidth <= c.clientWidth && document.documentElement.scrollWidth <= innerWidth;
}"""


def prova_grupos(pagina) -> None:
    conferir(pagina.evaluate(SEM_ROLAGEM_LATERAL), "a lista cabe na largura da janela, sem rolagem lateral")
    dias = pagina.eval_on_selector_all(".reunioes__dia", "els => els.map((e) => e.textContent)")
    conferir(dias[0].startswith("Hoje · "), f"o primeiro grupo é o de hoje ({dias[0]!r})")
    conferir(dias[-1] == "Sem data", f"o último grupo é 'Sem data' ({dias[-1]!r})")
    conferir(titulos(pagina)[-1] == "Reunião importada",
             "a gravação sem data vem no fim, embora o núcleo a mande primeiro")
    conferir(len(titulos(pagina)) == 9, "as nove gravações estão na lista")


def prova_busca(pagina) -> None:
    buscar(pagina, "credito")
    conferir(titulos(pagina) == ["Agente de Crédito — kickoff"], "'credito' acha 'Crédito'")
    ativo = pagina.evaluate("() => document.activeElement && document.activeElement.id")
    conferir(ativo == "busca-reunioes", f"o cursor ficou no campo enquanto se digitava ({ativo!r})")
    valor = pagina.input_value("#busca-reunioes")
    conferir(valor == "credito", f"as letras todas chegaram ao campo ({valor!r})")
    buscar(pagina, "algar rafael")
    conferir(titulos(pagina) == ["Comunicação Beegol + App"],
             "'algar rafael' casa cliente e convidado ao mesmo tempo")
    sub = pagina.text_content("#subtitulo")
    conferir(sub == "1 de 9 gravações", f"o subtítulo diz quantas sobraram ({sub!r})")


def prova_filtros(pagina) -> None:
    pagina.select_option("#filtro-cliente", "Algar")
    metas = pagina.eval_on_selector_all(".reuniao-linha__meta", "els => els.map((e) => e.textContent)")
    conferir(len(metas) == 2 and all(m.startswith("Algar") for m in metas),
             f"o filtro de cliente deixa só a Algar ({metas})")
    pagina.select_option("#filtro-cliente", "")
    pagina.select_option("#filtro-estado", "com-pendencias")
    conferir(titulos(pagina) == ["Comunicação Beegol + App"], "'Com pendências' deixa só a que tem item aberto")
    pagina.select_option("#filtro-estado", "nao-transcrita")
    conferir(titulos(pagina) == ["Semanal — Beegol · Uberlândia"], "'Não transcritas' deixa só a não transcrita")
    pagina.select_option("#filtro-estado", "")
    conferir(not pagina.is_visible("#filtro-data"), "sem 'Um dia…', o campo de data não aparece")
    pagina.select_option("#filtro-periodo", "dia")
    conferir(pagina.is_visible("#filtro-data"), "com 'Um dia…', o campo de data aparece")
    conferir(len(titulos(pagina)) == 9, "e, sem o dia escolhido, a lista continua inteira")
    ha_seis_dias = (date.today() - timedelta(days=6)).isoformat()
    pagina.fill("#filtro-data", ha_seis_dias)
    pagina.wait_for_timeout(50)
    conferir(titulos(pagina) == ["Comunicação Beegol + App", "Pedido Sugerido — alinhamento"],
             f"um dia escolhido deixa só as reuniões dele ({titulos(pagina)})")


def prova_sem_resultado(pagina) -> None:
    buscar(pagina, "zzzz")
    conferir(pagina.is_visible(".reunioes__vazio"), "sem resultado, a tela diz isso")
    pagina.click(".reunioes__vazio button")
    pagina.wait_for_timeout(50)
    conferir(len(titulos(pagina)) == 9, "'Limpar filtros' devolve as nove")
    conferir(pagina.input_value("#busca-reunioes") == "", "e esvazia o campo de busca")


def prova_criterios_sobrevivem(pagina) -> None:
    pagina.select_option("#filtro-cliente", "Vivo")
    pagina.click(".reuniao-linha")
    pagina.wait_for_selector(".revisao", timeout=5000)
    pagina.click("#voltar")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    conferir(pagina.input_value("#filtro-cliente") == "Vivo", "o filtro voltou como estava")
    conferir(titulos(pagina) == ["Sherlock Diário — Status e Ações"], "e a lista voltou filtrada")


PROVAS = [prova_grupos, prova_busca, prova_filtros, prova_sem_resultado, prova_criterios_sobrevivem]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--fotos", type=Path, help="pasta onde deixar as fotos das telas")
    args = ap.parse_args()

    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print("falta o playwright: uv run --with playwright python tools/provar_reunioes.py",
              file=sys.stderr)
        return 2

    socketserver.TCPServer.allow_reuse_address = True
    with socketserver.TCPServer(("127.0.0.1", 0), Servidor) as srv:
        porta = srv.server_address[1]
        threading.Thread(target=srv.serve_forever, daemon=True).start()

        with sync_playwright() as pw:
            navegador = pw.chromium.launch()
            for prova in PROVAS:
                print(f"── {prova.__name__}")
                largura, altura = getattr(prova, "janela", (1280, 800))
                pagina, erros = abrir(navegador, porta, largura, altura)
                prova(pagina)
                conferir(not erros, f"sem erro de JavaScript {erros[:2] if erros else ''}")
                pagina.close()

            if args.fotos:
                args.fotos.mkdir(parents=True, exist_ok=True)
                for tema in ["escuro", "claro"]:
                    for largura, altura in [(1280, 800), (1000, 700)]:
                        pagina, _ = abrir(navegador, porta, largura, altura, tema)
                        pagina.screenshot(path=str(args.fotos / f"reunioes-{tema}-{largura}.png"))
                        pagina.close()
            navegador.close()
        srv.shutdown()

    if falhas:
        print(f"\n{len(falhas)} falha(s).", file=sys.stderr)
        return 1
    print("\ntudo certo.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `uv run --with playwright python tools/provar_reunioes.py`
Expected: FAIL — `TimeoutError` esperando `.reuniao-linha` (a lista de hoje desenha `.gravacao`).

- [ ] **Step 3: Escrever a tela**

Criar `app-net/App/web/reunioes.js`:

```js
// A tela de Reuniões: achar a reunião e saber o que ela precisa em seguida.
//
// A lista de cartões não deixava procurar — seis por tela, e o acervo passou
// de 70 (docs/superpowers/specs/2026-09-23-ui-ux.md §1). Aqui cada gravação é
// uma linha, as linhas se agrupam por dia, e a busca e os filtros cortam a
// lista sem sair da tela.
//
// **As regras moram em reunioes-regras.js**, sem DOM e testadas fora do
// navegador. Este arquivo só desenha, e a prova dele é tools/provar_reunioes.py.

import { pedir } from "/ponte.js";
import { alerta, anunciar } from "/pecas.js";
import { assinarTranscricoes, emCurso, ultimoResultado } from "/transcricoes.js";
import { duracao, tituloDe, abrirGravacao, abrirGravador } from "/app.js";
import { agruparPorDia, clientesDe, estadoDe, filtrar, horaDe, hojeLocal,
         ESTADOS, PERIODOS, SEM_CLIENTE } from "/reunioes-regras.js";

/**
 * Os critérios sobrevivem a abrir uma reunião e voltar.
 *
 * No módulo, e não no disco: são o "onde eu estava", não uma preferência. Quem
 * filtrou por Algar, abriu uma reunião e clicou em ← Reuniões espera a lista
 * como a deixou.
 */
const criterios = { texto: "", cliente: "", periodo: "tudo", data: "", estado: "" };

const TOM = {
  info: "aa-etiqueta aa-etiqueta--info",
  atencao: "aa-etiqueta aa-etiqueta--atencao",
  sucesso: "aa-etiqueta aa-etiqueta--sucesso",
  neutro: "aa-etiqueta",
};

const contagem = (n) => (n === 1 ? "1 gravação" : `${n} gravações`);

/**
 * Uma linha, no alto da lista, quando saiu versão nova.
 *
 * **Por que aqui e não só nos Ajustes.** O aviso existe para chegar a quem não
 * é quem compila o app — e essa pessoa não abre Ajustes por esporte. A tela de
 * Reuniões é a que ela vê todo dia; um aviso que ninguém encontra é o mesmo que
 * aviso nenhum.
 *
 * Uma linha, dispensável com um clique, e nunca um diálogo por cima: quem abriu
 * o app queria ver as reuniões, não conversar sobre versões.
 *
 * Assíncrono e à prova de falha: sem rede, sem GitHub, sem nada — a lista
 * aparece igual e ninguém fica sabendo que houve uma tentativa.
 */
let avisoDeVersaoDispensado = false;

function avisarDeVersaoNova(tela) {
  if (avisoDeVersaoDispensado) return;

  pedir("atualizacao").then((r) => {
    const a = r.atualizacao;
    if (!a?.nova || avisoDeVersaoDispensado) return;

    const linha = document.createElement("div");
    linha.className = "aa-alerta aa-alerta--atencao";

    const ponto = document.createElement("span");
    ponto.className = "aa-alerta__ponto";

    const texto = document.createElement("span");
    texto.textContent = `Saiu a versão ${a.nova.versao}`
      + (a.nova.notas ? ` — ${a.nova.notas}` : ".")
      + " A sua é a " + a.versao_instalada + ".";

    const dispensar = document.createElement("button");
    dispensar.className = "aa-btn aa-btn-texto";
    dispensar.type = "button";
    dispensar.textContent = "Dispensar";
    dispensar.addEventListener("click", () => {
      avisoDeVersaoDispensado = true;
      linha.remove();
    });

    linha.append(ponto, texto, dispensar);
    tela.prepend(linha);
  }).catch(() => {
    // Sem rede não é assunto de quem só queria ver as reuniões.
  });
}

export async function telaDeReunioes({ cabecalho, tela }) {
  cabecalho("Reuniões", "", false);
  tela.setAttribute("aria-busy", "true");
  tela.replaceChildren();

  let gravacoes;
  try {
    ({ gravacoes } = await pedir("gravacoes"));
  } catch (e) {
    tela.setAttribute("aria-busy", "false");
    tela.replaceChildren(alerta(e.message, "erro"));
    return;
  }
  tela.setAttribute("aria-busy", "false");
  tela.replaceChildren();
  avisarDeVersaoNova(tela);

  if (gravacoes.length === 0) {
    cabecalho("Reuniões", "Nenhuma gravação encontrada", false);
    const vazio = document.createElement("p");
    vazio.className = "vazio";
    // Não fala mais em "o MeetingRecorder": desde a Fase 2.5 o gravador é
    // este mesmo app.
    vazio.textContent = "Nenhuma gravação ainda. Comece uma em Gravador.";
    const ir = document.createElement("button");
    ir.className = "aa-btn aa-btn-primario";
    ir.type = "button";
    ir.textContent = "Ir para o Gravador";
    ir.addEventListener("click", abrirGravador);
    tela.append(vazio, ir);
    return;
  }

  const hoje = hojeLocal();
  const raiz = document.createElement("div");
  raiz.className = "reunioes";

  // ---- a barra: desenhada uma vez só.
  //
  // Filtrar redesenha a lista, nunca isto. Redesenhar o campo a cada tecla
  // tiraria o cursor de quem digita — o F-2 de docs/FASE7-FRONTEND.md.
  const ferramentas = document.createElement("div");
  ferramentas.className = "reunioes__ferramentas";

  const busca = document.createElement("input");
  busca.type = "search";
  busca.id = "busca-reunioes";
  busca.className = "aa-entrada reunioes__busca";
  busca.placeholder = "Buscar por título, cliente, projeto, convidado ou data…";
  busca.setAttribute("aria-label", "Buscar reuniões");
  busca.value = criterios.texto;

  const filtroCliente = seletor("Cliente", "filtro-cliente", [
    ["", "Todos os clientes"],
    ...clientesDe(gravacoes).map((c) => [c.nome, `${c.nome} (${c.n})`]),
    [SEM_CLIENTE, "Sem cliente"],
  ], criterios.cliente);
  const filtroPeriodo = seletor("Data", "filtro-periodo", PERIODOS, criterios.periodo);

  // O dia de "Um dia…" e "Uma semana…", no calendário do próprio navegador.
  // Some quando o período não pede dia — um campo de data à toa na barra pede
  // um valor que não vai ser usado.
  const filtroData = document.createElement("input");
  filtroData.type = "date";
  filtroData.id = "filtro-data";
  filtroData.className = "aa-entrada reunioes__filtro";
  filtroData.value = criterios.data;
  function ajustarData() {
    const pede = criterios.periodo === "dia" || criterios.periodo === "semana";
    filtroData.hidden = !pede;
    filtroData.setAttribute("aria-label", criterios.periodo === "semana"
      ? "Um dia da semana que você procura" : "O dia que você procura");
  }
  ajustarData();

  const filtroEstado = seletor("Estado", "filtro-estado", ESTADOS, criterios.estado);

  // Um cliente que sumiu desde a última visita não pode continuar filtrando em
  // silêncio: o seletor caiu em "Todos", e o critério cai junto.
  criterios.cliente = filtroCliente.value;

  ferramentas.append(busca, filtroCliente, filtroPeriodo, filtroData, filtroEstado);

  const lista = document.createElement("section");
  lista.className = "reunioes__lista";
  lista.setAttribute("aria-label", "Gravações");

  raiz.append(ferramentas, lista);
  tela.appendChild(raiz);

  /** caminho → linha, para trocar a etiqueta sem redesenhar a lista. */
  const linhas = new Map();
  let visiveis = [];

  function desenhar() {
    visiveis = filtrar(gravacoes, criterios, { hoje, rodandoDe: emCurso });
    cabecalho("Reuniões", visiveis.length === gravacoes.length
      ? contagem(gravacoes.length)
      : `${visiveis.length} de ${contagem(gravacoes.length)}`, false);

    linhas.clear();
    lista.replaceChildren();
    if (visiveis.length === 0) {
      lista.appendChild(semResultado());
      return;
    }

    for (const grupo of agruparPorDia(visiveis, hoje)) {
      const dia = document.createElement("h2");
      dia.className = "reunioes__dia";
      dia.textContent = grupo.rotulo;
      lista.appendChild(dia);
      for (const g of grupo.itens) {
        const l = linha(g);
        linhas.set(g.caminho, l);
        lista.appendChild(l);
      }
    }
  }

  function linha(g) {
    const b = document.createElement("button");
    b.type = "button";
    b.className = "reuniao-linha";
    b.dataset.gravacao = g.caminho;
    // O texto inteiro dos avisos, que na linha viram só "1 aviso".
    if (g.avisos.length > 0) b.title = g.avisos.join("\n");

    const hora = document.createElement("span");
    hora.className = "reuniao-linha__hora";
    hora.textContent = horaDe(g.nome);

    const textos = document.createElement("span");
    textos.className = "reuniao-linha__textos";
    const titulo = document.createElement("span");
    titulo.className = "reuniao-linha__titulo";
    titulo.textContent = tituloDe(g);
    const meta = document.createElement("span");
    meta.className = "reuniao-linha__meta";
    meta.textContent = metaDe(g);
    textos.append(titulo, meta);

    b.append(hora, textos, etiqueta(g));
    b.addEventListener("click", () => abrirGravacao(g));
    return b;
  }

  function semResultado() {
    const caixa = document.createElement("div");
    caixa.className = "reunioes__vazio";
    const p = document.createElement("p");
    p.textContent = "Nenhuma reunião com esses filtros.";
    const limpar = document.createElement("button");
    limpar.type = "button";
    limpar.className = "aa-btn aa-btn-secundario";
    limpar.textContent = "Limpar filtros";
    limpar.addEventListener("click", () => {
      Object.assign(criterios, { texto: "", cliente: "", periodo: "tudo", data: "", estado: "" });
      busca.value = "";
      filtroCliente.value = "";
      filtroPeriodo.value = "tudo";
      filtroData.value = "";
      ajustarData();
      filtroEstado.value = "";
      aoMudar();
      busca.focus();
    });
    caixa.append(p, limpar);
    return caixa;
  }

  // Quantas sobraram, dito quando a pessoa para de digitar — e não a cada
  // letra, que faria o leitor de tela falar por cima da digitação.
  let anuncio = null;
  function aoMudar() {
    desenhar();
    clearTimeout(anuncio);
    anuncio = setTimeout(() => anunciar(
      visiveis.length === 1 ? "1 reunião" : `${visiveis.length} reuniões`), 600);
  }

  busca.addEventListener("input", () => { criterios.texto = busca.value; aoMudar(); });
  filtroCliente.addEventListener("change", () => { criterios.cliente = filtroCliente.value; aoMudar(); });
  filtroPeriodo.addEventListener("change", () => {
    criterios.periodo = filtroPeriodo.value;
    ajustarData();
    // Escolheu "Um dia…" e ainda não disse qual: o cursor vai para onde se diz.
    if (!filtroData.hidden && !filtroData.value) filtroData.focus();
    aoMudar();
  });
  filtroData.addEventListener("change", () => { criterios.data = filtroData.value; aoMudar(); });
  filtroEstado.addEventListener("change", () => { criterios.estado = filtroEstado.value; aoMudar(); });

  desenhar();

  // A etiqueta acompanha o trabalho da placa: quem fica parado na lista vê
  // "Transcrevendo…" virar "Sem ata" sozinho.
  //
  // **Só a etiqueta muda, e a lista não se refiltra.** Refiltrar a cada evento
  // de andamento recriaria as linhas, e quem estivesse com o Tab numa delas
  // perderia o lugar. Uma reunião que deixou de casar com o filtro de estado
  // sai no próximo filtro, não no meio da leitura.
  const cancelar = assinarTranscricoes(() => {
    if (!raiz.isConnected) { cancelar(); return; }
    for (const g of gravacoes) {
      const fim = ultimoResultado(g.caminho);
      if (!emCurso(g.caminho) && fim && !fim.erro && !fim.cancelada) {
        if (fim.tarefa === "ata") { g.tem_ata = true; g.ata_velha = false; }
        else g.transcrita = true;
      }
      const l = linhas.get(g.caminho);
      if (l && l.lastElementChild.textContent !== estadoDe(g, emCurso(g.caminho)).rotulo)
        l.lastElementChild.replaceWith(etiqueta(g));
    }
  });
}

function etiqueta(g) {
  const e = estadoDe(g, emCurso(g.caminho));
  const s = document.createElement("span");
  s.className = TOM[e.tom];
  s.dataset.estado = e.chave;
  s.textContent = e.rotulo;
  return s;
}

/** "Algar › Agentes · 31min 00s · 3 pendências · 1 aviso". */
function metaDe(g) {
  const partes = [g.cliente || g.projeto
    ? [g.cliente, g.projeto].filter(Boolean).join(" › ") : "sem cliente"];
  partes.push(duracao(g.duracao_s));
  if (g.tem_ata && g.pendencias > 0)
    partes.push(g.pendencias === 1 ? "1 pendência" : `${g.pendencias} pendências`);
  if (g.avisos.length > 0)
    partes.push(g.avisos.length === 1 ? "1 aviso" : `${g.avisos.length} avisos`);
  return partes.join(" · ");
}

/** Um seletor sem rótulo visível: o texto de cada opção diz o critério inteiro. */
function seletor(rotulo, id, opcoes, valor) {
  const s = document.createElement("select");
  s.id = id;
  s.className = "aa-entrada reunioes__filtro";
  s.setAttribute("aria-label", rotulo);
  for (const [v, texto] of opcoes) {
    const o = document.createElement("option");
    o.value = v;
    o.textContent = texto;
    s.appendChild(o);
  }
  s.value = opcoes.some(([v]) => v === valor) ? valor : opcoes[0][0];
  return s;
}
```

- [ ] **Step 4: Tirar a lista do `app.js`**

Em `app-net/App/web/app.js`:

1. Acrescentar aos imports, logo depois de `import { telaDeAtas } from "/atas.js";`:

   ```js
   import { telaDeReunioes } from "/reunioes.js";
   ```

2. Apagar tudo **de** `// ─────────────────────────────────────────────────────────── lista` **até o fim de** `export async function telaDeLista() { … }` — isto é, `cartao`, `etiquetaDe`, o comentário e o `let avisoDeVersaoDispensado`, `avisarDeVersaoNova` e `telaDeLista` —, e pôr no lugar:

   ```js
   // ─────────────────────────────────────────────────────────── lista

   /**
    * A lista mora em reunioes.js, pelo mesmo motivo dos outros destinos: o
    * app.js é o único lugar que sabe da moldura, e a tela recebe o que precisa
    * dela.
    */
   export function telaDeLista() {
     fecharGavetas();
     destino("ir-reunioes");
     return telaDeReunioes({ cabecalho, tela });
   }
   ```

   `botaoApagarGravacao`, logo abaixo, fica onde está.

3. Conferir que nada ficou pendurado:

   Run: `grep -n "etiquetaDe\|cartao(\|avisarDeVersaoNova\|avisoDeVersaoDispensado" app-net/App/web/app.js`
   Expected: nenhuma linha.

- [ ] **Step 5: O CSS da lista**

Em `app-net/App/web/app.css`, logo depois da linha `.vazio { color: var(--cor-texto-suave); }`:

```css
/* ─────────────────────────────────────────────────────── reuniões
 *
 * Uma linha por gravação, agrupadas por dia, com busca e filtros por cima.
 * O desenho está em docs/superpowers/specs/2026-09-23-ui-ux.md §3.3. */

/* A página de documento do design system centraliza em 980 px e respira 64 px
 * em cima; a lista com painel precisa da largura da janela. O width é o que
 * conta: a .aa-pagina tem margin auto dentro da grade do .conteudo, e item de
 * grade com margem automática encolhe até o conteúdo — tirar só o max-width
 * deixava a lista em 896 px. E o border-box é o que impede o padding da página
 * de somar aos 100% e empurrar os filtros para fora da janela. Mesmo remendo
 * que a revisão já usa; a correção de fundo é o UI-8. */
.aa-pagina:has(> .reunioes) {
  box-sizing: border-box;
  width: 100%;
  max-width: 90rem;
  padding-top: var(--espaco-5);
}

.reunioes { display: grid; gap: var(--espaco-4); }

.reunioes__ferramentas {
  display: flex;
  flex-wrap: wrap;
  gap: var(--espaco-2);
  align-items: center;
}
.reunioes__busca { flex: 1 1 18rem; }
.reunioes__filtro { flex: 0 0 auto; width: auto; min-width: 11rem; }

.reunioes__lista {
  background: var(--cor-superficie);
  border: 1px solid var(--cor-borda);
  border-radius: var(--raio-grande);
  overflow: hidden;
}

.reunioes__dia {
  margin: 0;
  padding: var(--espaco-3) var(--espaco-4) var(--espaco-2);
  font: 700 var(--texto-rotulo) / 1.2 var(--fonte-ui);
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--cor-texto-suave);
  border-bottom: 1px solid var(--cor-borda);
}

.reuniao-linha {
  display: grid;
  grid-template-columns: 3rem minmax(0, 1fr) auto;
  gap: var(--espaco-3);
  align-items: center;
  width: 100%;
  padding: var(--espaco-3) var(--espaco-4);
  border: 0;
  border-bottom: 1px solid var(--cor-borda);
  background: transparent;
  color: inherit;
  font: inherit;
  text-align: left;
  cursor: pointer;
}
.reuniao-linha:last-child { border-bottom: 0; }
.reuniao-linha:hover { background: var(--cor-linha-hover); }
/* Por dentro, e não por fora: a lista tem overflow hidden, e um anel por fora
 * seria cortado nas bordas. */
.reuniao-linha:focus-visible { outline: none; box-shadow: inset var(--anel-foco); }

.reuniao-linha__hora {
  font-family: var(--fonte-mono);
  font-size: var(--texto-rotulo);
  color: var(--cor-texto-suave);
}
.reuniao-linha__textos { display: grid; gap: 2px; min-width: 0; }
.reuniao-linha__titulo,
.reuniao-linha__meta { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.reuniao-linha__titulo { font-weight: 600; color: var(--cor-texto-forte); }
.reuniao-linha__meta { font-size: var(--texto-rotulo); color: var(--cor-texto-suave); }

.reunioes__vazio {
  display: grid;
  gap: var(--espaco-3);
  justify-items: start;
  padding: var(--espaco-5) var(--espaco-4);
  color: var(--cor-texto-suave);
}
.reunioes__vazio p { margin: 0; }
```

- [ ] **Step 6: Rodar a prova e ver passar**

Run: `uv run --with playwright python tools/provar_reunioes.py --fotos /tmp/claude-1000/provar-reunioes`
Expected: todas as linhas `ok`, `tudo certo.` no fim. Abrir as quatro fotos (escuro e claro, 1280 e 1000 px) e olhar: a lista ocupando a largura, linhas legíveis, etiquetas com contraste nos dois temas, nada transbordando a 1000 px.

- [ ] **Step 7: O que já existia continua de pé**

Run: `node --test tools/web/reunioes-regras.test.mjs && .venv/bin/python tools/medir_layout.py`
Expected: node `# fail 0`; `medir_layout.py` sem `PROBLEMA` (ele mede Ajustes e Gravador, que não mudaram — a moldura que a lista divide com eles não pode ter mudado).

Se o `.venv` não tiver o playwright: `uv run --with playwright python tools/medir_layout.py`.

- [ ] **Step 8: Commit**

```bash
git add app-net/App/web/reunioes.js app-net/App/web/app.js app-net/App/web/app.css tools/provar_reunioes.py
git commit -m "feat(reunioes): a lista se procura — busca, filtros e grupos por dia

Uma linha por gravação em vez de um cartão, agrupadas por dia, com busca
sem acento e filtros de cliente, período e estado. A barra não se
redesenha: o cursor fica em quem digita. Os critérios sobrevivem a abrir
uma reunião e voltar. A lista saiu do app.js para reunioes.js.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: O painel da reunião escolhida

**Files:**
- Modify: `app-net/App/web/reunioes.js`
- Modify: `app-net/App/web/app.css` — continuação do bloco "reuniões"
- Modify: `tools/provar_reunioes.py`

**Interfaces:**
- Consumes: `proximoPasso` (Task 3); `quando`, `abrirAtas` de `/app.js` (`abrirAtas` hoje ignora argumentos; a Task 6 o faz respeitar `{ foco }`).
- Produces:
  - DOM: `aside.reunioes__painel` com `.reunioes__painel-titulo` e o botão do próximo passo `button[data-acao]` (valores de `proximoPasso().acao`); `.reuniao-linha[aria-pressed="true"]` na escolhida
  - Comportamento: janela com 1100 px ou mais — clique escolhe, duplo clique abre; menos que 1100 px — o painel some e o clique abre

- [ ] **Step 1: Escrever as provas que falham**

Em `tools/provar_reunioes.py`, acrescentar antes da linha `PROVAS = [...]`:

```python
def prova_painel(pagina) -> None:
    # A primeira da TELA: o núcleo manda a "Reunião importada" na frente, e ela
    # é desenhada no fim, em "Sem data".
    conferir(pagina.get_attribute(".reuniao-linha", "aria-pressed") == "true",
             "sem escolha anterior, a escolhida é a primeira linha da tela")
    conferir(pagina.text_content(".reunioes__painel-titulo") == "Reunião de lideranças",
             "e o painel fala dela")
    linha_por_titulo(pagina, "Sherlock")
    pagina.evaluate("() => window.__linha.click()")
    pagina.wait_for_timeout(50)
    conferir(pagina.evaluate("() => window.__linha.getAttribute('aria-pressed')") == "true",
             "o clique escolhe a linha, e não abre a reunião")
    conferir(pagina.text_content(".reunioes__painel-titulo") == "Sherlock Diário — Status e Ações",
             "o painel mostra a escolhida")
    for inicio, acao in [("Sherlock", "gerar-ata"), ("Semanal", "transcrever"),
                         ("Pedido Sugerido", "refazer-ata"), ("Comunicação", "abrir-ata")]:
        linha_por_titulo(pagina, inicio)
        pagina.evaluate("() => window.__linha.click()")
        pagina.wait_for_timeout(30)
        achada = pagina.get_attribute(".reunioes__painel [data-acao]", "data-acao")
        conferir(achada == acao, f"'{inicio}' oferece {acao} ({achada})")
    pendencias = pagina.eval_on_selector_all(".reunioes__pendencias li", "els => els.length")
    conferir(pendencias == 3, f"o painel mostra as três primeiras pendências ({pendencias})")
    linha_por_titulo(pagina, "Reunião de lideranças")
    pagina.evaluate("() => window.__linha.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }))")
    pagina.wait_for_selector(".revisao", timeout=5000)
    conferir(True, "o duplo clique abre a reunião")


def prova_transcricao_em_curso(pagina) -> None:
    linha_por_titulo(pagina, "Semanal")
    pagina.evaluate("() => window.__linha.click()")
    pagina.wait_for_timeout(30)
    pagina.evaluate("() => { window.__botao = document.querySelector('.reunioes__painel [data-acao]'); "
                    "window.__botao.focus(); }")
    caminho = pagina.evaluate("() => window.__linha.dataset.gravacao")
    em_curso = ("(c, f) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: "
                "{ gravacao: c, nome: 'x', tarefa: 'transcricao', etapa: 'asr', fracao: f, texto: '', "
                "comecou_em: '', terminou: false, erro: null, cancelada: false }, ultimo: null } })")
    pagina.evaluate(f"([c, f]) => ({em_curso})(c, f)", [caminho, 0.2])
    pagina.wait_for_timeout(30)
    conferir(pagina.evaluate("() => window.__linha.isConnected"), "a lista não foi redesenhada")
    conferir(pagina.evaluate("() => window.__linha.lastElementChild.textContent") == "Transcrevendo…",
             "a etiqueta passou a 'Transcrevendo…'")
    conferir(pagina.get_attribute(".reunioes__painel [data-acao]", "data-acao") == "acompanhar-transcricao",
             "o painel passou a oferecer acompanhar")
    pagina.evaluate("() => { window.__botao = document.querySelector('.reunioes__painel [data-acao]'); "
                    "window.__botao.focus(); }")
    pagina.evaluate(f"([c, f]) => ({em_curso})(c, f)", [caminho, 0.6])
    pagina.wait_for_timeout(30)
    conferir(pagina.evaluate("() => window.__botao.isConnected && document.activeElement === window.__botao"),
             "andamento sem mudança de estado não recria o botão, e o foco fica nele")
    pagina.evaluate("(c) => window.__emitir({ tipo: 'transcricoes', transcricoes: { atual: null, ultimo: "
                    "{ gravacao: c, nome: 'x', tarefa: 'transcricao', etapa: 'montagem', fracao: 1, "
                    "texto: '', comecou_em: '', terminou: true, erro: null, cancelada: false } } })", caminho)
    pagina.wait_for_timeout(30)
    conferir(pagina.evaluate("() => window.__linha.lastElementChild.textContent") == "Sem ata",
             "terminada, a etiqueta diz 'Sem ata' sozinha")
    conferir(pagina.get_attribute(".reunioes__painel [data-acao]", "data-acao") == "gerar-ata",
             "e o painel passa a oferecer gerar a ata")


def prova_janela_estreita(pagina) -> None:
    conferir(pagina.evaluate(SEM_ROLAGEM_LATERAL), "a 1000 px a lista cabe, sem rolagem lateral")
    conferir(not pagina.is_visible(".reunioes__painel"), "em janela estreita o painel some")
    linha_por_titulo(pagina, "Reunião de lideranças")
    pagina.evaluate("() => window.__linha.click()")
    pagina.wait_for_selector(".revisao", timeout=5000)
    conferir(True, "e o clique abre a reunião direto")


prova_janela_estreita.janela = (1000, 700)
```

Na mesma arquivo, **trocar** a `prova_criterios_sobrevivem` inteira por esta — com o painel, o clique escolhe, e quem abre é o botão "Abrir":

```python
def prova_criterios_sobrevivem(pagina) -> None:
    pagina.select_option("#filtro-cliente", "Vivo")
    pagina.click(".reuniao-linha")
    pagina.click('.reunioes__painel >> text="Abrir"')
    pagina.wait_for_selector(".revisao", timeout=5000)
    pagina.click("#voltar")
    pagina.wait_for_selector(".reuniao-linha", timeout=5000)
    conferir(pagina.input_value("#filtro-cliente") == "Vivo", "o filtro voltou como estava")
    conferir(titulos(pagina) == ["Sherlock Diário — Status e Ações"], "e a lista voltou filtrada")
    conferir(pagina.get_attribute(".reuniao-linha", "aria-pressed") == "true",
             "e a mesma reunião continua escolhida")
```

E trocar a lista de provas:

```python
PROVAS = [prova_grupos, prova_busca, prova_filtros, prova_sem_resultado, prova_criterios_sobrevivem,
          prova_painel, prova_transcricao_em_curso, prova_janela_estreita]
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `uv run --with playwright python tools/provar_reunioes.py`
Expected: FALHA em `prova_criterios_sobrevivem` (não há `.reunioes__painel`), em `prova_painel` e em `prova_transcricao_em_curso`; `prova_janela_estreita` passa por acaso (o clique já abre) — ela passa a valer depois do Step 3.

- [ ] **Step 3: O painel**

Em `app-net/App/web/reunioes.js`:

1. Trocar os dois imports de `/app.js` e `/reunioes-regras.js` por:

   ```js
   import { duracao, quando, tituloDe, abrirGravacao, abrirGravador, abrirAtas } from "/app.js";
   import { agruparPorDia, clientesDe, estadoDe, filtrar, horaDe, hojeLocal, proximoPasso,
            ESTADOS, PERIODOS, SEM_CLIENTE } from "/reunioes-regras.js";
   ```

2. Logo depois de `const criterios = { … };`, acrescentar:

   ```js
   /** A gravação no painel, pelo caminho. Sobrevive a voltar, como os critérios. */
   let escolhida = null;

   /**
    * Abaixo disto o painel não cabe, o CSS o esconde, e o clique na linha volta
    * a abrir a reunião direto. O número é o mesmo do @media do app.css.
    */
   const ESTREITA = window.matchMedia("(max-width: 1099px)");
   ```

3. Trocar:

   ```js
     const lista = document.createElement("section");
     lista.className = "reunioes__lista";
     lista.setAttribute("aria-label", "Gravações");

     raiz.append(ferramentas, lista);
     tela.appendChild(raiz);
   ```

   por:

   ```js
     const lista = document.createElement("section");
     lista.className = "reunioes__lista";
     lista.setAttribute("aria-label", "Gravações");

     const painel = document.createElement("aside");
     painel.className = "reunioes__painel";
     painel.setAttribute("aria-label", "Reunião escolhida");

     const corpo = document.createElement("div");
     corpo.className = "reunioes__corpo";
     corpo.append(lista, painel);

     raiz.append(ferramentas, corpo);
     tela.appendChild(raiz);
   ```

4. Trocar a função `desenhar` inteira por:

   ```js
     function desenhar() {
       visiveis = filtrar(gravacoes, criterios, { hoje, rodandoDe: emCurso });
       cabecalho("Reuniões", visiveis.length === gravacoes.length
         ? contagem(gravacoes.length)
         : `${visiveis.length} de ${contagem(gravacoes.length)}`, false);

       linhas.clear();
       lista.replaceChildren();
       if (visiveis.length === 0) {
         lista.appendChild(semResultado());
         desenharPainel(null);
         return;
       }

       const grupos = agruparPorDia(visiveis, hoje);
       for (const grupo of grupos) {
         const dia = document.createElement("h2");
         dia.className = "reunioes__dia";
         dia.textContent = grupo.rotulo;
         lista.appendChild(dia);
         for (const g of grupo.itens) {
           const l = linha(g);
           linhas.set(g.caminho, l);
           lista.appendChild(l);
         }
       }

       // A escolhida que o filtro escondeu cede o lugar à primeira que sobrou:
       // um painel falando de uma reunião que não está na lista confunde.
       // **A primeira da tela, e não a primeira que o núcleo mandou**: o "Sem
       // data" chega na frente e é desenhado no fim.
       if (!visiveis.some((g) => g.caminho === escolhida)) escolhida = grupos[0].itens[0].caminho;
       marcarEscolhida();
     }
   ```

5. Em `linha(g)`, trocar:

   ```js
       b.append(hora, textos, etiqueta(g));
       b.addEventListener("click", () => abrirGravacao(g));
       return b;
   ```

   por:

   ```js
       b.append(hora, textos, etiqueta(g));
       b.setAttribute("aria-pressed", "false");
       // Escolher custa um clique a mais para abrir, e o duplo clique o devolve.
       // Sem largura para o painel, escolher não mostraria nada — o clique abre.
       b.addEventListener("click", () => (ESTREITA.matches ? abrirGravacao(g) : escolher(g)));
       b.addEventListener("dblclick", () => abrirGravacao(g));
       return b;
   ```

6. Logo depois do fim de `function linha(g) { … }`, acrescentar:

   ```js
     function escolher(g) {
       escolhida = g.caminho;
       marcarEscolhida();
     }

     function marcarEscolhida() {
       for (const [caminho, l] of linhas) l.setAttribute("aria-pressed", String(caminho === escolhida));
       desenharPainel(gravacoes.find((g) => g.caminho === escolhida) ?? null);
     }

     /** O que o painel desenhou por último, para não redesenhá-lo à toa. */
     let pintado = "";

     /**
      * O painel da escolhida: o que ela é, o que ela precisa, e o que a ata diz.
      *
      * @param seMudou redesenhar só se o estado mudou. É o caso dos eventos de
      *   andamento, que chegam várias vezes por etapa: redesenhar a cada um
      *   tiraria o foco de quem está com o Tab no botão do painel.
      */
     function desenharPainel(g, { seMudou = false } = {}) {
       const rodando = g ? emCurso(g.caminho) : null;
       const chave = g
         ? `${g.caminho}|${estadoDe(g, rodando).rotulo}|${proximoPasso(g, rodando).acao}` : "";
       if (seMudou && chave === pintado) return;
       pintado = chave;
       painel.replaceChildren();
       if (!g) return;

       const titulo = document.createElement("h2");
       titulo.className = "reunioes__painel-titulo";
       titulo.textContent = tituloDe(g);

       painel.append(
         titulo,
         // Sem data no nome da pasta, o quando() devolveria o próprio nome.
         texto("reunioes__painel-meta", [horaDe(g.nome) ? quando(g.nome) : null,
           duracao(g.duracao_s),
           g.convidados > 0 ? `${g.convidados} convidados` : null].filter(Boolean).join(" · ")),
         texto("reunioes__painel-vinculo", g.cliente || g.projeto
           ? [g.cliente, g.projeto].filter(Boolean).join(" › ")
           : "Sem cliente — escolha ao transcrever"),
       );
       for (const aviso of g.avisos) painel.appendChild(alerta(aviso));

       const passo = proximoPasso(g, rodando);
       const principal = botao(passo.rotulo, "aa-btn aa-btn-primario", () => seguir(g, passo.acao));
       principal.dataset.acao = passo.acao;
       const acoes = document.createElement("div");
       acoes.className = "reunioes__painel-acoes";
       acoes.append(principal, botao("Abrir", "aa-btn aa-btn-secundario", () => abrirGravacao(g)));
       painel.appendChild(acoes);

       painel.appendChild(secaoDoPainel("Ata", textoDaAta(g, rodando)));
       if (g.resumo) painel.appendChild(texto("reunioes__painel-resumo", g.resumo));

       const pendencias = g.pendencias_inicio ?? [];
       if (pendencias.length > 0) {
         const s = secaoDoPainel(g.pendencias === 1 ? "1 pendência" : `${g.pendencias} pendências`);
         const ul = document.createElement("ul");
         ul.className = "reunioes__pendencias";
         for (const p of pendencias) {
           const li = document.createElement("li");
           li.textContent = p;
           ul.appendChild(li);
         }
         s.appendChild(ul);
         painel.appendChild(s);
       }
       if (g.notas_inicio) painel.appendChild(secaoDoPainel("Notas", g.notas_inicio));
     }
   ```

7. No fim da `assinarTranscricoes`, logo antes do `});` que a fecha, acrescentar:

   ```js
       desenharPainel(gravacoes.find((x) => x.caminho === escolhida) ?? null, { seMudou: true });
   ```

8. No fim do arquivo, acrescentar:

   ```js
   function texto(classe, conteudo) {
     const p = document.createElement("p");
     if (classe) p.className = classe;
     p.textContent = conteudo;
     return p;
   }

   function botao(rotulo, classe, aoClicar) {
     const b = document.createElement("button");
     b.type = "button";
     b.className = classe;
     b.textContent = rotulo;
     b.addEventListener("click", aoClicar);
     return b;
   }

   function secaoDoPainel(rotulo, conteudo = null) {
     const s = document.createElement("section");
     s.className = "reunioes__painel-secao";
     const h = document.createElement("h3");
     h.className = "reunioes__rotulo";
     h.textContent = rotulo;
     s.appendChild(h);
     if (conteudo) s.appendChild(texto("", conteudo));
     return s;
   }

   function textoDaAta(g, rodando) {
     if (rodando?.tarefa === "ata") return "Sendo escrita agora.";
     if (!g.transcrita) return "A ata é escrita a partir da transcrição.";
     if (!g.tem_ata) return "Ainda não foi escrita.";
     if (g.ata_velha) return "A transcrição foi corrigida depois que esta ata foi escrita.";
     return "Pronta.";
   }

   /**
    * O botão do próximo passo leva aonde o passo se dá.
    *
    * A ata ainda mora em Atas; o plano 2 a traz para dentro da reunião, e aí
    * esta função é o único lugar a mudar.
    */
   function seguir(g, acao) {
     if (acao === "transcrever" || acao === "acompanhar-transcricao") return abrirGravacao(g);
     return abrirAtas({ foco: g.caminho });
   }
   ```

- [ ] **Step 4: O CSS do painel**

Em `app-net/App/web/app.css`, no fim do bloco "reuniões" (depois de `.reunioes__vazio p { margin: 0; }`):

```css
.reunioes__corpo {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 22rem;
  gap: var(--espaco-4);
  align-items: start;
}

.reuniao-linha[aria-pressed="true"],
.reuniao-linha[aria-pressed="true"]:hover { background: var(--cor-acao-suave); }

/* Gruda logo abaixo da barra do topo, que também é sticky — pela mesma
 * variável que as abas dos ajustes e os controles da revisão já usam. */
.reunioes__painel {
  position: sticky;
  top: calc(var(--altura-da-barra) + var(--espaco-4));
  display: grid;
  gap: var(--espaco-3);
  align-content: start;
  padding: var(--espaco-5);
  background: var(--cor-superficie);
  border: 1px solid var(--cor-borda);
  border-radius: var(--raio-grande);
}
.reunioes__painel:empty { display: none; }

.reunioes__painel-titulo {
  margin: 0;
  font-family: var(--fonte-display);
  font-size: 1.3rem;
  line-height: 1.2;
  color: var(--cor-texto-forte);
}
.reunioes__painel-meta,
.reunioes__painel-vinculo { margin: 0; font-size: var(--texto-pequeno); color: var(--cor-texto-suave); }
.reunioes__painel-resumo { margin: 0; color: var(--cor-texto-forte); }

.reunioes__painel-acoes {
  display: flex;
  flex-wrap: wrap;
  gap: var(--espaco-2);
  padding-bottom: var(--espaco-3);
  border-bottom: 1px solid var(--cor-borda);
}

.reunioes__painel-secao { display: grid; gap: var(--espaco-1); }
.reunioes__painel-secao p { margin: 0; }
.reunioes__rotulo {
  margin: 0;
  font: 700 var(--texto-rotulo) / 1.2 var(--fonte-ui);
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: var(--cor-texto-suave);
}
.reunioes__pendencias {
  margin: 0;
  padding-left: var(--espaco-4);
  display: grid;
  gap: var(--espaco-1);
  font-size: var(--texto-pequeno);
}

/* Sem largura para o painel, ele some, e o clique na linha abre a reunião
 * (ESTREITA, em reunioes.js). Os dois números andam juntos. */
@media (max-width: 1099px) {
  .reunioes__corpo { grid-template-columns: 1fr; }
  .reunioes__painel { display: none; }
}
```

- [ ] **Step 5: Rodar e ver passar**

Run: `uv run --with playwright python tools/provar_reunioes.py --fotos /tmp/claude-1000/provar-reunioes`
Expected: todas `ok`, `tudo certo.`. Olhar `reunioes-escuro-1280.png` e `reunioes-claro-1280.png`: painel à direita, título em Fraunces, botões com as cores do tema, pendências legíveis; e nas de 1000 px, sem painel.

- [ ] **Step 6: Commit**

```bash
git add app-net/App/web/reunioes.js app-net/App/web/app.css tools/provar_reunioes.py
git commit -m "feat(reunioes): o painel da escolhida, com o próximo passo

Clicar escolhe a reunião e o painel diz o que ela precisa — transcrever,
gerar, refazer ou abrir a ata —, com o resumo, as primeiras pendências e
o começo das notas. Duplo clique abre; em janela estreita o painel some e
o clique abre direto. O andamento troca a etiqueta sem recriar a linha
nem o botão em foco.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Atas abre na reunião pedida

**Files:**
- Modify: `app-net/App/web/atas.js` — `telaDeAtas` (linha 19) e `cartaoDeAta` (linha 62)
- Modify: `app-net/App/web/app.js` — `abrirAtas` e a ligação do `#ir-atas`
- Modify: `tools/provar_reunioes.py`

**Interfaces:**
- Consumes: o `abrirAtas({ foco: caminho })` que o painel já chama (Task 5).
- Produces: `telaDeAtas(ctx, { foco = null } = {})` e `abrirAtas(opcoes = {})`. Com `foco`, a tela de Atas rola até o cartão dessa gravação, abre a ata dele e põe o foco no botão de gerar/refazer (`button[data-acao="ata"]`).

- [ ] **Step 1: Escrever a prova que falha**

Em `tools/provar_reunioes.py`, antes de `PROVAS = [...]`:

```python
def prova_ata_na_reuniao_pedida(pagina) -> None:
    linha_por_titulo(pagina, "Comunicação")
    pagina.evaluate("() => window.__linha.click()")
    pagina.click(".reunioes__painel [data-acao='abrir-ata']")
    pagina.wait_for_selector(".ata", timeout=5000)
    pagina.wait_for_timeout(100)
    aberta = pagina.evaluate("""() => {
        const c = [...document.querySelectorAll('.ata')]
          .find((x) => x.dataset.gravacao.includes('13-59'));
        const d = c && c.querySelector('details.ata__dobra');
        return Boolean(d && d.open);
    }""")
    conferir(aberta, "'Abrir a ata' leva a Atas com a ata daquela reunião aberta")
    foco = pagina.evaluate("() => document.activeElement && document.activeElement.dataset.acao")
    conferir(foco == "ata", f"e o foco vai para o botão dela ({foco!r})")
```

E acrescentar `prova_ata_na_reuniao_pedida` ao fim da lista `PROVAS`.

- [ ] **Step 2: Rodar e ver falhar**

Run: `uv run --with playwright python tools/provar_reunioes.py`
Expected: FALHA em `prova_ata_na_reuniao_pedida` — a ata está fechada e o foco está no título.

- [ ] **Step 3: `telaDeAtas` aceita o foco**

Em `app-net/App/web/atas.js`:

1. Trocar `export async function telaDeAtas(ctx) {` por:

   ```js
   /**
    * @param foco o caminho de uma gravação. Vem do painel de Reuniões: quem
    *   pediu "Abrir a ata" de uma reunião cai no cartão dela, com a ata aberta,
    *   e não no topo de uma lista que teria de percorrer.
    */
   export async function telaDeAtas(ctx, { foco = null } = {}) {
   ```

2. Trocar `for (const g of prontas) tela.appendChild(cartaoDeAta(g, tipos, ctx));` por:

   ```js
     for (const g of prontas) tela.appendChild(cartaoDeAta(g, tipos, ctx, g.caminho === foco));

     if (foco) {
       const alvo = tela.querySelector(`[data-gravacao="${CSS.escape(foco)}"]`);
       alvo?.scrollIntoView({ block: "start" });
       alvo?.querySelector('[data-acao="ata"]')?.focus({ preventScroll: true });
     }
   ```

3. Trocar `function cartaoDeAta(g, tipos, ctx) {` por `function cartaoDeAta(g, tipos, ctx, emFoco = false) {`.

4. Dentro de `cartaoDeAta` (hoje linhas 92–95 — há um segundo `botao.textContent = "Gerar ata"` na linha 360, que não é este), trocar:

   ```js
     const botao = document.createElement("button");
     botao.className = "aa-btn aa-btn-primario";
     botao.type = "button";
     botao.textContent = "Gerar ata";
   ```

   por:

   ```js
     const botao = document.createElement("button");
     botao.className = "aa-btn aa-btn-primario";
     botao.type = "button";
     botao.textContent = "Gerar ata";
     botao.dataset.acao = "ata";
   ```

5. Trocar `mostrarAtaExistente(g, corpo, botao);` (a chamada dentro de `cartaoDeAta`, hoje linha 125) por:

   ```js
     mostrarAtaExistente(g, corpo, botao, emFoco);
   ```

- [ ] **Step 4: `abrirAtas` repassa as opções**

Em `app-net/App/web/app.js`, trocar:

```js
/** O destino Atas mora em atas.js, pelo mesmo motivo dos outros dois. */
export function abrirAtas() {
  fecharGavetas();
  destino("ir-atas");
  return telaDeAtas({ cabecalho, tela });
}
```

por:

```js
/** O destino Atas mora em atas.js, pelo mesmo motivo dos outros dois. */
export function abrirAtas(opcoes = {}) {
  fecharGavetas();
  destino("ir-atas");
  return telaDeAtas({ cabecalho, tela }, opcoes);
}
```

e trocar `document.getElementById("ir-atas").addEventListener("click", abrirAtas);` por:

```js
// Embrulhado: o clique mandaria o evento no lugar das opções.
document.getElementById("ir-atas").addEventListener("click", () => abrirAtas());
```

- [ ] **Step 5: Rodar e ver passar**

Run: `uv run --with playwright python tools/provar_reunioes.py`
Expected: todas `ok`, `tudo certo.`.

- [ ] **Step 6: Commit**

```bash
git add app-net/App/web/atas.js app-net/App/web/app.js tools/provar_reunioes.py
git commit -m "feat(atas): abrir na reunião pedida, com a ata aberta

O painel de Reuniões leva a Atas com o foco na reunião escolhida: o
cartão dela rola para o topo, a ata abre, e o foco vai para o botão de
gerar ou refazer.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Conferir no app de verdade e fechar o item

**Files:**
- Modify: `docs/BACKLOG.md` — seção `UI-3`
- Modify: `docs/superpowers/specs/2026-09-23-ui-ux.md` — tabela do §5

**Interfaces:**
- Consumes: tudo acima.
- Produces: o `UI-3` fechado no backlog, com o que ficou de fora dito.

- [ ] **Step 1: Tudo verde de uma vez**

Run:

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet test app-net/Tests/MeetingApp.Tests.csproj
node --test tools/web/reunioes-regras.test.mjs
uv run --with playwright python tools/provar_reunioes.py
uv run --with playwright python tools/medir_layout.py
```

Expected: dotnet sem falha; node `# fail 0`; as duas provas sem `FALHA`/`PROBLEMA`.

- [ ] **Step 2: O binário compila com as réguas**

Run: `tools/publicar.sh --so-build`
Expected: termina sem reprovar régua; o binário fica em `dist/publicar`.

- [ ] **Step 3: Instalar — só com o dono**

**Parar e pedir ao dono do produto** que feche o app pela bandeja (ele pode estar gravando ou transcrevendo; o `publicar.sh` recusa com o app aberto, e ninguém mata o processo). Com o sim dele:

Run: `tools/publicar.sh`
Expected: instala em `AppData\Local\Programs\MeetingApp` sem reprovar.

- [ ] **Step 4: Percorrer com o acervo real**

Com o app aberto pelo dono, no tema dele, conferir e anotar o que não bater:

1. A lista abre agrupada por dia, com "Hoje · …" em cima, e as ~70 gravações estão lá (o subtítulo diz o número).
2. Digitar o nome de um cliente de uma vez: as letras todas ficam no campo.
3. "Estado: Com pendências" mostra só reunião com item aberto; abrir uma e conferir que a ata tem mesmo o número de pendências da linha.
4. Uma reunião com ata refeita depois de corrigir a transcrição aparece como "Ata desatualizada".
5. O botão do painel de uma reunião sem ata leva a Atas com o cartão dela no topo.
6. Filtrar, abrir uma reunião, ← Reuniões: filtro e escolhida no lugar.
7. Estreitar a janela abaixo de ~1100 px: o painel some e o clique abre.
8. Etiquetas e painel legíveis no tema escuro (é o `UI-9` para esta tela).

Se algo não bater, é um defeito desta tarefa — abrir uma correção com a prova que falha antes do conserto, no mesmo ramo.

- [ ] **Step 5: Fechar o `UI-3` no backlog**

Em `docs/BACKLOG.md`, trocar o título `### UI-3 · Busca e filtro nas listas — \`feature\` · \`aberto\`` por `### UI-3 · Busca e filtro nas listas — \`feature\` · feito em <data de hoje, DD/MM/AAAA>` e acrescentar, no fim da seção:

```markdown
**Feito**, pelo plano
[2026-09-23-ui-01-reunioes-lista.md](superpowers/plans/2026-09-23-ui-01-reunioes-lista.md):
busca sem acento em título, cliente, projeto, convidados e data; filtros de
cliente, período e estado; grupos por dia; o estado da ata na linha e o
próximo passo num painel. **Ficou de fora, de propósito:** a busca no
**conteúdo** das transcrições (pede op nova no núcleo) e a lista de Atas, que
continua como está até o plano 2 trazer a ata para dentro da reunião.
```

- [ ] **Step 6: Marcar o plano como feito no spec**

Em `docs/superpowers/specs/2026-09-23-ui-ux.md`, na tabela do §5, trocar a célula de estado do plano 1, `**detalhado**`, por `**feito em <data de hoje, DD/MM/AAAA>**`.

- [ ] **Step 7: Commit**

```bash
git add docs/BACKLOG.md docs/superpowers/specs/2026-09-23-ui-ux.md
git commit -m "docs: o UI-3 fechado pela lista nova de Reuniões

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
