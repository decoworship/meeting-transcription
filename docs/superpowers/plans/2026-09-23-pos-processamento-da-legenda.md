# Pós-processamento da legenda — plano de implementação

> **Para quem executa:** SUB-SKILL OBRIGATÓRIA — use
> `superpowers:subagent-driven-development` (recomendada) ou
> `superpowers:executing-plans` para implementar tarefa a tarefa. Os passos usam
> caixas (`- [ ]`) para acompanhamento.

**Objetivo:** a legenda ao vivo passa pela mesma correção de termos da passada
final (fonética + revisão por regra + grafia) **no fim da reunião, junto da
separação de falantes**, fechando o `VIVO-3` do [BACKLOG.md](../../BACKLOG.md).

**Arquitetura:** a correção de texto sai de dentro do
`Transcritor.ExecutarInternoAsync` para uma classe do `Nucleo`
(`CorrecaoDeTermos`), chamada pelos dois caminhos — é uma cadeia só, e não uma
cópia. Uma segunda classe (`CorrecaoDaLegenda`) adapta essa cadeia aos trechos
de 1,1 s da legenda: corrige por **fala** (trechos seguidos do mesmo falante),
**funde** os trechos que uma troca atravessaria, e grava as trocas em cada
trecho. A `Ponte.SepararFalantes` chama isso antes do `LegendaAoVivo.Gravar` —
inclusive quando a diarização é recusada ou falha, porque corrigir texto não
usa GPU.

**Tech stack:** C# / .NET 8, xUnit. Nada de Python, nada de sidecar.

**Spec:** não há documento de desenho separado; a decisão está no `VIVO-3` do
[docs/BACKLOG.md](../../BACKLOG.md) e na medição de 23/09/2026 abaixo. Leia os
dois antes de começar.

### A medição que justifica isto — 23/09/2026

Reunião `2026-09-23_10-00-17` (101 min, projeto *Agentes (Interno)*), contagem
dos termos do vocabulário, com um runner descartável que chamou exatamente as
funções do `Transcritor` sobre a legenda juntada por fala:

| termo | Whisper | legenda crua | legenda + pós |
|---|---:|---:|---:|
| Wifi | 14 | 2 | **11** |
| Tânia | 2 | 0 | **2** |
| V.TAL | 2 | 0 | **2** |
| Webhook | 4 | 3 | **4** |
| Prada | 13 | 7 | 8 |
| API · Cloud | 11 · 6 | 6 · 1 | 6 · 1 |

**15 trocas, 4 propostas validadas** (`Wi Fi→Wifi`, `Vital→V.TAL`,
`pra da→Prada`, `web hook→Webhook`) + `Tania→Tânia` da fonética. Recupera o que
o Nemotron ouviu e escreveu diferente; **não** recupera o que ele não ouviu —
isso não tem conserto depois (o Nemotron não tem gancho de vocabulário na
entrada, ver `VIVO-3`), e não é objetivo deste plano.

**Três das propostas são de duas palavras** (`Wi Fi`, `pra da`, `web hook`).
Com trechos de 1,1 s, a metade de uma cai num trecho e a outra no seguinte — é
o motivo de existir a fusão da Tarefa 3.

---

## Restrições globais

- **`Gravacao/`, `Captura/` e o `DriftAnchor` não são tocados.** Ver o
  [CLAUDE.md](../../../CLAUDE.md), "O que não se reabre".
- **A passada final não muda de comportamento.** A Tarefa 1 é refatoração: os
  628 testes de hoje passam **sem alteração**. Se um precisar mudar, a extração
  mudou o comportamento — pare e reveja.
- **A chave é a mesma da passada final**: `ConfiguracoesDaTranscricao.CorrecaoFonetica`
  (`correcao_fonetica` no `app.json`, padrão `true`). Desligada, a legenda sai
  como sai hoje. Nenhuma chave nova.
- **O propositor por modelo (`PropositorDeModelo`) fica fora da legenda.** Ele
  sobe o motor de ata na GPU, e o fim da reunião já tem diarização e
  reconhecimento de vozes disputando a placa (`SUP-1`). A cadeia da legenda é
  só a síncrona.
- **Nunca derruba a separação de falantes.** Uma exceção na correção é
  registrada (`Registro.Escrever("legenda", …)`) e a legenda é gravada sem ela.
- **`legenda.json` antigo continua legível.** Sete arquivos do acervo não têm
  `trechos`, e nenhum tem `swaps`. O campo novo é opcional e omitido quando nulo.
- **Comandos** (de `/home/andre/projects/meeting-transcription`):
  `export PATH="$HOME/.dotnet:$PATH"` antes de tudo;
  `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter <Classe>` por
  tarefa e a suíte inteira no fim de cada uma. **Nunca `--no-build` depois de
  editar** — mede o binário velho.
- **Commits em português, no estilo do repositório** (`feat(legenda): …`,
  `refactor(correcao): …`), terminando com
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Nada de `dotnet publish` na mão.** Publicar é `tools/publicar.sh`, e só na
  Tarefa 5, com o app fechado pela bandeja.

## Foco da revisão

1. **Troca atravessando a fronteira de trechos** (`"problema de Wi"` |
   `"Fi e canal"`): tem de sair `Wifi` num trecho só, com início do primeiro e
   fim do último — e nunca juntar trechos de falantes diferentes. Teste na Tarefa 3.
2. **Palavra partida pelo commit (`Colado = true`)**: `"a Palo"` + `"ma tem"` é
   `"a Paloma tem"` na fala, não `"a Palo ma tem"`; a fusão preserva a colagem.
   Teste na Tarefa 3.
3. **O reconhecimento de vozes apaga o `Colado`** hoje — o `new TrechoDaLegenda`
   de `Ponte.cs:1720` copia início, fim, dono, texto e falante, e esquece o
   `Colado`. Depois do reconhecimento, "Paloma" volta a aparecer "Palo ma".
   Conserto e teste na Tarefa 2.
4. **Vocabulário vazio e reunião sem agenda**: a cadeia tem de devolver os
   trechos intactos, sem `swaps`, sem exceção. Teste na Tarefa 3.
5. **Diarização recusada ("já há uma transcrição em curso") ou falha**: a
   legenda sai corrigida mesmo sem falante. Hoje esses dois caminhos gravam os
   trechos crus. Coberto na Tarefa 4.

---

## Mapa de arquivos

| arquivo | o quê |
|---|---|
| `app-net/Nucleo/CorrecaoDeTermos.cs` | **novo** — a cadeia extraída do `Transcritor`: entidades, fonética, revisão |
| `app-net/Nucleo/CorrecaoDaLegenda.cs` | **novo** — a cadeia aplicada aos trechos: agrupar por fala, fundir, anotar |
| `app-net/Nucleo/Transcritor.cs` | passa a chamar `CorrecaoDeTermos`; perde o `EntidadesConhecidas` privado |
| `app-net/Nucleo/LegendaAoVivo.cs` | `TrechoDaLegenda` ganha `Swaps` |
| `app-net/App/Ponte.cs` | `SepararFalantes` chama a correção; o reconhecimento preserva `Colado` e `Swaps` |
| `app-net/Tests/CorrecaoDeTermosTests.cs` | **novo** |
| `app-net/Tests/CorrecaoDaLegendaTests.cs` | **novo** |
| `app-net/Tests/LegendaAoVivoTests.cs` | ida e volta do campo novo |
| `docs/BACKLOG.md` | `VIVO-3` passa a feito, com a medição |

### Dependências entre tarefas

```
T1 (CorrecaoDeTermos) ──┐
                        ├──> T3 (CorrecaoDaLegenda) ──> T4 (Ponte) ──> T5 (medir e docs)
T2 (Swaps + Colado) ────┘
```

**T1 e T2 são independentes e podem correr em paralelo**, em worktrees
separados: T1 mexe em `Transcritor.cs` + arquivo novo, T2 em `LegendaAoVivo.cs`
+ `Ponte.cs:1715-1725`. Nenhum arquivo em comum. T3, T4 e T5 são em série.

---

### Tarefa 1: extrair a cadeia de correção do `Transcritor`

**Arquivos:**
- Criar: `app-net/Nucleo/CorrecaoDeTermos.cs`
- Modificar: `app-net/Nucleo/Transcritor.cs` (o bloco `if (vocabulario … && corrigirFonetica)` até o fim do `if (corrigirFonetica) { … }`, hoje ~linhas 545–625, e o `EntidadesConhecidas` privado, ~linhas 708–742)
- Testar: `app-net/Tests/CorrecaoDeTermosTests.cs`

**Interfaces:**
- Consome: `CorrecaoFonetica.Corrigir(string, IReadOnlyList<string>)`,
  `RevisaoDeTermos.Propor/ProporGrafia/Validar/Aplicar`,
  `ConvidadosDaAgenda.Ler(string)`, `Atas.Organizacoes.Classificar(nomes, emails, [])`.
- Produz (usado pelas Tarefas 3 e 4):

```csharp
public static class CorrecaoDeTermos
{
    /// Vocabulário, cliente, projeto e convidados com nome e sobrenome.
    public static IReadOnlyList<string> Entidades(
        string pastaDaGravacao, string? vocabulario, string? cliente, string? projeto);

    /// A fonética, texto a texto. Vocabulário vazio devolve tudo intacto.
    public static List<(string Texto, List<Troca> Trocas)> Fonetica(
        IReadOnlyList<string> textos, string? vocabulario);

    /// As propostas validadas: regra + grafia + as extras (as do modelo, na passada final).
    public static IReadOnlyList<Proposta> Propostas(
        IReadOnlyList<string> textos, IReadOnlyList<string> entidades,
        IEnumerable<Proposta>? extras = null);
}
```

A aplicação das propostas continua sendo `RevisaoDeTermos.Aplicar(texto, propostas)`
— não se embrulha o que já é uma linha.

- [ ] **Passo 1: escrever os testes que falham**

```csharp
using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A cadeia de correção de termos, fora do Transcritor.
/// </summary>
/// <remarks>
/// Existe para que a legenda e a passada final corrijam com a mesma regra. O
/// comportamento é o que o Transcritor já tinha — estes testes o fixam do lado
/// de fora, antes de a legenda passar a depender dele.
/// </remarks>
public sealed class CorrecaoDeTermosTests
{
    [Fact]
    public void AFoneticaSemVocabularioNaoMexeEmNada()
    {
        var saida = CorrecaoDeTermos.Fonetica(["o Tania falou"], vocabulario: "");

        Assert.Equal("o Tania falou", saida[0].Texto);
        Assert.Empty(saida[0].Trocas);
    }

    [Fact]
    public void AFoneticaUsaOVocabulario()
    {
        var saida = CorrecaoDeTermos.Fonetica(["a Tania pediu"], "KPI, Tânia, Prada");

        Assert.Equal("a Tânia pediu", saida[0].Texto);
        Assert.Contains(saida[0].Trocas, t => t is { De: "Tania", Para: "Tânia" });
    }

    [Fact]
    public void AsPropostasJuntamPalavrasQueOVocabularioEscreveJuntas()
    {
        // Medido na legenda de 23/09/2026: o Nemotron escreve "Wi Fi" e
        // "web hook"; o vocabulário diz "Wifi" e "Webhook".
        var entidades = CorrecaoDeTermos.Entidades(
            Path.GetTempPath(), "Wifi, Webhook", cliente: null, projeto: null);

        var propostas = CorrecaoDeTermos.Propostas(
            ["o problema de Wi Fi", "chega pelo web hook"], entidades);

        Assert.Contains(propostas, p => p is { De: "Wi Fi", Para: "Wifi" });
        Assert.Contains(propostas, p => p is { De: "web hook", Para: "Webhook" });
    }

    [Fact]
    public void AsEntidadesDescartamNomeDeUmaPalavraSo()
    {
        // O corte do EntidadesConhecidas, de 25/08: "Felipeof" (local-part de
        // e-mail) não pode virar alvo.
        string pasta = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(pasta, "meta.json"), """
            { "meeting": { "attendees": ["Felipeof", "Daniel Prada"] } }
            """);

        var entidades = CorrecaoDeTermos.Entidades(pasta, "KPI", "Agentes", "Interno");

        Assert.Contains("Daniel Prada", entidades);
        Assert.DoesNotContain("Felipeof", entidades);
        Assert.Equal(["KPI", "Agentes", "Interno"], entidades.Take(3));
    }
}
```

> Se o formato de `meta.json` que o `ConvidadosDaAgenda.Ler` espera for outro,
> abra `app-net/Nucleo/ConvidadosDaAgenda.cs` e ajuste **o teste** ao formato
> real — o `meta.json` de uma gravação de verdade tem `meeting.attendees` como
> lista de nomes. Não mude o `ConvidadosDaAgenda`.

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter CorrecaoDeTermosTests`
Esperado: erro de compilação, `CorrecaoDeTermos` não existe.

- [ ] **Passo 3: criar `app-net/Nucleo/CorrecaoDeTermos.cs`**

Mover — não copiar — o corpo do `EntidadesConhecidas` do `Transcritor`, **com os
comentários** (o do "nome de uma palavra só" é o registro da decisão de 25/08).

```csharp
using MeetingApp.Nucleo.Atas;

namespace MeetingApp.Nucleo;

/// <summary>
/// A correção de termos: fonética, regra e grafia, sobre texto já transcrito.
/// </summary>
/// <remarks>
/// <b>Uma cadeia só para os dois caminhos.</b> Morava dentro do
/// <see cref="Transcritor"/>; saiu em 23/09/2026 para a legenda ao vivo usar a
/// mesma regra no fim da reunião (<c>VIVO-3</c>). Duas cópias divergiriam na
/// primeira mudança de regra, e a divergência só apareceria como "a legenda
/// corrige diferente da ata".
/// <para>
/// <b>A ordem importa e é a do Transcritor:</b> fonética primeiro, depois as
/// propostas sobre o texto já corrigido — o que a fonética consertou vira termo
/// conhecido e a regra nem olha. Ver o comentário em <see cref="RevisaoDeTermos"/>.
/// </para>
/// </remarks>
public static class CorrecaoDeTermos
{
    public static IReadOnlyList<string> Entidades(
        string pastaDaGravacao, string? vocabulario, string? cliente, string? projeto)
    {
        var (nomes, emails) = ConvidadosDaAgenda.Ler(pastaDaGravacao);
        var pessoas = Organizacoes.Classificar(nomes, emails, []);

        // (mover aqui, intacto, o comentário "Nome de uma palavra só não vira alvo")
        var comNomeDeVerdade = pessoas
            .Select(p => p.Nome)
            .Where(n => n.Contains(' '));

        return [.. new[] { vocabulario, cliente, projeto }
            .Where(x => x is { Length: > 0 })
            .Concat(comNomeDeVerdade)!];
    }

    public static List<(string Texto, List<Troca> Trocas)> Fonetica(
        IReadOnlyList<string> textos, string? vocabulario)
    {
        if (vocabulario is not { Length: > 0 })
            return [.. textos.Select(t => (t, new List<Troca>()))];

        var termos = vocabulario.Split(',', StringSplitOptions.TrimEntries
                                            | StringSplitOptions.RemoveEmptyEntries);
        return [.. textos.Select(t => CorrecaoFonetica.Corrigir(t, termos))];
    }

    public static IReadOnlyList<Proposta> Propostas(
        IReadOnlyList<string> textos, IReadOnlyList<string> entidades,
        IEnumerable<Proposta>? extras = null)
    {
        // (mover aqui os dois comentários do Transcritor: "Depois da fonética,
        // e não no lugar dela" e "A grafia entra junto, e não no lugar")
        var cruas = new List<Proposta>(RevisaoDeTermos.Propor(textos, entidades));
        cruas.AddRange(RevisaoDeTermos.ProporGrafia(textos, entidades));
        if (extras is not null) cruas.AddRange(extras);
        return RevisaoDeTermos.Validar(cruas, entidades);
    }
}
```

- [ ] **Passo 4: o `Transcritor` passa a chamar a classe**

Substituir o bloco inteiro (da linha `if (vocabulario is { Length: > 0 } && corrigirFonetica)`
até o fecho do `if (corrigirFonetica) { … }`) por:

```csharp
        if (corrigirFonetica)
        {
            var foneticos = CorrecaoDeTermos.Fonetica(
                [.. segmentos.Select(s => s.Text)], vocabulario);
            for (int i = 0; i < segmentos.Count; i++)
            {
                if (foneticos[i].Trocas.Count == 0) continue;
                segmentos[i].Text = foneticos[i].Texto;
                // (manter o comentário "A lista vai junto para o arquivo")
                Anotar(segmentos[i], foneticos[i].Trocas);
            }

            var entidades = CorrecaoDeTermos.Entidades(
                pastaDaGravacao, vocabulario, cliente, projeto);
            var textos = segmentos.Select(s => s.Text).ToList();

            // (manter o comentário "O segundo propositor, quando ligado")
            var doModelo = new List<Proposta>();
            if (revisarComModelo && motorDeAta is not null && entidades.Count > 0)
            {
                try
                {
                    progresso?.Invoke(new Progresso("montagem", 0.7, "revisando os termos"));
                    doModelo.AddRange(await PropositorDeModelo.ProporAsync(
                        new MotorDeAta(motorDeAta), textos, entidades, ct: ct));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    Registro.Escrever("pipeline",
                        $"a revisão pelo modelo falhou e foi ignorada: {e.Message}");
                }
            }

            var propostas = CorrecaoDeTermos.Propostas(textos, entidades, doModelo);
            if (propostas.Count > 0)
            {
                int mexidos = 0;
                foreach (var seg in segmentos)
                {
                    var (texto, trocas) = RevisaoDeTermos.Aplicar(seg.Text, propostas);
                    if (trocas.Count == 0) continue;
                    seg.Text = texto;
                    Anotar(seg, trocas);
                    mexidos++;
                }
                Registro.Escrever("pipeline",
                    $"revisão de termos: {propostas.Count} troca(s) em {mexidos} trecho(s) — "
                    + string.Join(", ", propostas.Take(6).Select(p => $"{p.De}→{p.Para}")));
            }
        }
```

**Atenção à equivalência**: hoje a fonética só roda com `vocabulario` não vazio,
e a revisão roda mesmo sem vocabulário (as entidades da agenda bastam). O código
acima preserva as duas coisas: `Fonetica` com vocabulário vazio devolve tudo
intacto. Apagar o `EntidadesConhecidas` privado do `Transcritor`.

- [ ] **Passo 5: rodar os testes novos e a suíte inteira**

Rodar: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter CorrecaoDeTermosTests`
Esperado: 4 PASS.
Rodar: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Esperado: todos passam, **nenhum teste existente editado**.

- [ ] **Passo 6: commit**

```bash
git add app-net/Nucleo/CorrecaoDeTermos.cs app-net/Nucleo/Transcritor.cs app-net/Tests/CorrecaoDeTermosTests.cs
git commit -m "refactor(correcao): a cadeia de termos sai do Transcritor para a legenda poder usá-la"
```

---

### Tarefa 2: o trecho da legenda guarda as trocas, e o reconhecimento para de apagar a colagem

**Arquivos:**
- Modificar: `app-net/Nucleo/LegendaAoVivo.cs` (classe `TrechoDaLegenda`, ~linha 62)
- Modificar: `app-net/App/Ponte.cs` (o `new TrechoDaLegenda { … Falante = nome }` dentro do `SepararFalantes`, ~linha 1720)
- Testar: `app-net/Tests/LegendaAoVivoTests.cs`

**Interfaces:**
- Consome: `TrocaFeita` (`Nucleo/Transcricao.cs:82`, JSON `from`/`to`).
- Produz: `TrechoDaLegenda.Swaps` (`List<TrocaFeita>?`, JSON `swaps`, omitido
  quando nulo) e o método `TrechoDaLegenda ComFalante(string? falante)`, que
  copia **todos** os campos trocando só o falante.

- [ ] **Passo 1: testes que falham** — acrescentar a `LegendaAoVivoTests`:

```csharp
    [Fact]
    public void AsTrocasDoTrechoSobrevivemAoArquivo()
    {
        string pasta = Directory.CreateTempSubdirectory().FullName;
        var trecho = new TrechoDaLegenda
        {
            InicioMs = 0, FimMs = 1120, Dono = false, Texto = "Wifi",
            Swaps = [new TrocaFeita { De = "Wi Fi", Para = "Wifi" }],
        };

        LegendaAoVivo.Gravar(pasta, [], [trecho], prontos: true);
        var lida = LegendaAoVivo.Ler(pasta)!;

        var troca = Assert.Single(lida.Trechos[0].Swaps!);
        Assert.Equal(("Wi Fi", "Wifi"), (troca.De, troca.Para));
    }

    [Fact]
    public void TrechoSemTrocaNaoEscreveOCampo()
    {
        // Os sete legenda.json antigos do acervo não têm o campo, e o arquivo
        // novo sem troca tem de continuar igual a eles.
        string pasta = Directory.CreateTempSubdirectory().FullName;
        LegendaAoVivo.Gravar(pasta, [],
            [new TrechoDaLegenda { InicioMs = 0, FimMs = 1, Dono = false, Texto = "oi" }],
            prontos: false);

        Assert.DoesNotContain("swaps", File.ReadAllText(Path.Combine(pasta, "legenda.json")));
    }

    [Fact]
    public void TrocarOFalantePreservaAColagemEAsTrocas()
    {
        // Até 23/09/2026 o reconhecimento de vozes recriava o trecho campo a
        // campo e esquecia o Colado: "Palo" + "ma" voltava a sair "Palo ma".
        var t = new TrechoDaLegenda
        {
            InicioMs = 10, FimMs = 20, Dono = false, Texto = "ma", Colado = true,
            Falante = "SPEAKER_00", Swaps = [new TrocaFeita { De = "a", Para = "b" }],
        };

        var n = t.ComFalante("Paloma Santos");

        Assert.Equal("Paloma Santos", n.Falante);
        Assert.True(n.Colado);
        Assert.Same(t.Swaps, n.Swaps);
        Assert.Equal((10L, 20L, "ma"), (n.InicioMs, n.FimMs, n.Texto));
    }
```

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter LegendaAoVivoTests`
Esperado: não compila — `Swaps` e `ComFalante` não existem.

- [ ] **Passo 3: implementar** — em `TrechoDaLegenda`, depois do `Colado`:

```csharp
    /// <summary>
    /// O que a correção de termos trocou neste trecho. Nulo quando nada.
    /// </summary>
    /// <remarks>
    /// Mesmo formato do <see cref="SegmentoFinal.Swaps"/> da passada final, e
    /// pelo mesmo motivo: correção que não deixa rastro não se desfaz. Ver
    /// <see cref="CorrecaoDaLegenda"/>.
    /// </remarks>
    [JsonPropertyName("swaps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TrocaFeita>? Swaps { get; init; }

    /// <summary>O mesmo trecho, com outro falante.</summary>
    /// <remarks>
    /// <b>Um lugar só para copiar o trecho.</b> Recriá-lo campo a campo no
    /// chamador esqueceu o <see cref="Colado"/> uma vez, e esqueceria o
    /// próximo campo também.
    /// </remarks>
    public TrechoDaLegenda ComFalante(string? falante) => new()
    {
        InicioMs = InicioMs, FimMs = FimMs, Dono = Dono, Texto = Texto,
        Falante = falante, Colado = Colado, Swaps = Swaps,
    };
```

(A `<see cref="CorrecaoDaLegenda"/>` só resolve depois da Tarefa 3; se o
compilador reclamar do cref com `TreatWarningsAsErrors`, use `<c>CorrecaoDaLegenda</c>`
aqui e a Tarefa 3 troca.)

Em `Ponte.SepararFalantes`, trocar o `new TrechoDaLegenda { InicioMs = t.InicioMs, … Falante = nome }` por `t.ComFalante(nome)`.
Conferir também `Nucleo/FalantesDaLegenda.Atribuir`: se ele recria trechos com
`new TrechoDaLegenda { … }`, trocar por `ComFalante` do mesmo jeito.

- [ ] **Passo 4: rodar** `--filter "LegendaAoVivoTests|FalantesDaLegendaTests"` e a suíte inteira. Esperado: PASS.

- [ ] **Passo 5: commit**

```bash
git add app-net/Nucleo/LegendaAoVivo.cs app-net/Nucleo/FalantesDaLegenda.cs app-net/App/Ponte.cs app-net/Tests/LegendaAoVivoTests.cs
git commit -m "feat(legenda): o trecho guarda as trocas, e nomear o falante para de apagar a colagem"
```

---

### Tarefa 3: a correção aplicada aos trechos da legenda

**Arquivos:**
- Criar: `app-net/Nucleo/CorrecaoDaLegenda.cs`
- Testar: `app-net/Tests/CorrecaoDaLegendaTests.cs`

**Interfaces:**
- Consome: `CorrecaoDeTermos.Fonetica/Propostas` (Tarefa 1),
  `TrechoDaLegenda.Swaps` e `ComFalante` (Tarefa 2), `RevisaoDeTermos.Aplicar`.
- Produz (usado pela Tarefa 4):

```csharp
public static class CorrecaoDaLegenda
{
    public sealed record Resultado(List<TrechoDaLegenda> Trechos, int Trocas, int Fundidos);

    public static Resultado Corrigir(
        IReadOnlyList<TrechoDaLegenda> trechos, string? vocabulario,
        IReadOnlyList<string> entidades);
}
```

**O algoritmo, em quatro passos — é o que os testes fixam:**

1. **Falas**: trechos seguidos com o mesmo `Falante` e o mesmo `Dono` formam
   uma fala. O texto da fala junta os trechos com `" "`, ou com `""` quando o
   trecho seguinte é `Colado` — a mesma regra do `Ponte.FalasDaLegenda`.
2. **Descobrir as trocas na fala**: `Fonetica` sobre os textos das falas, depois
   `Propostas` sobre o resultado. As propostas são por reunião (um conjunto só);
   da fonética, guardam-se as `Troca.De` de cada fala.
3. **Fundir**: para cada `De` (das propostas e da fonética daquela fala), achar
   as ocorrências no texto da fala **com fronteira de palavra**. Se uma
   ocorrência cobre caracteres de dois ou mais trechos, esses trechos viram um
   só: `InicioMs` do primeiro, `FimMs` do último, texto juntado pela regra do
   passo 1, `Colado` do primeiro, mesmo `Falante`/`Dono`. Nunca se funde entre
   falas diferentes.
4. **Aplicar por trecho**: sobre cada trecho (já fundido), `Fonetica` com o
   vocabulário e `RevisaoDeTermos.Aplicar` com as propostas do passo 2. As
   trocas das duas vão para `Swaps`. Trecho sem troca sai **a mesma instância**
   de entrada.

- [ ] **Passo 1: testes que falham**

```csharp
using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A correção de termos sobre a legenda — o VIVO-3.
/// </summary>
/// <remarks>
/// Os trechos têm 1,1 s, e três das quatro propostas medidas em 23/09/2026 eram
/// de duas palavras. O que se prova aqui é que a troca acontece mesmo quando as
/// duas palavras caem em trechos diferentes.
/// </remarks>
public sealed class CorrecaoDaLegendaTests
{
    private static readonly string[] Entidades = ["Wifi, Webhook, Tânia"];
    private const string Vocabulario = "Wifi, Webhook, Tânia";

    private static TrechoDaLegenda T(long de, string texto, string? quem = "Daniel Prada",
                                     bool colado = false) => new()
    {
        InicioMs = de, FimMs = de + 1120, Dono = false, Texto = texto,
        Falante = quem, Colado = colado,
    };

    [Fact]
    public void TrocaDentroDeUmTrechoSoFicaNele()
    {
        var r = CorrecaoDaLegenda.Corrigir(
            [T(0, "problema de Wi Fi"), T(1120, "e canal")], Vocabulario, Entidades);

        Assert.Equal(["problema de Wifi", "e canal"], r.Trechos.Select(t => t.Texto));
        Assert.Contains(r.Trechos[0].Swaps!, s => s is { De: "Wi Fi", Para: "Wifi" });
        Assert.Null(r.Trechos[1].Swaps);
    }

    [Fact]
    public void TrocaQueAtravessaAFronteiraFundeOsTrechos()
    {
        var r = CorrecaoDaLegenda.Corrigir(
            [T(0, "problema de Wi"), T(1120, "Fi e canal"), T(2240, "ruim")],
            Vocabulario, Entidades);

        Assert.Equal(2, r.Trechos.Count);
        Assert.Equal("problema de Wifi e canal", r.Trechos[0].Texto);
        Assert.Equal((0L, 2240L), (r.Trechos[0].InicioMs, r.Trechos[0].FimMs));
        Assert.Equal("ruim", r.Trechos[1].Texto);
        Assert.Equal(1, r.Fundidos);
    }

    [Fact]
    public void NuncaFundeFalantesDiferentes()
    {
        // "Wi" do fim da fala de um e "Fi" do começo da do outro não são uma
        // palavra — são duas pessoas.
        var r = CorrecaoDaLegenda.Corrigir(
            [T(0, "problema de Wi"), T(1120, "Fi e canal", quem: "Paloma Santos")],
            Vocabulario, Entidades);

        Assert.Equal(2, r.Trechos.Count);
        Assert.Equal(0, r.Fundidos);
        Assert.Equal("Daniel Prada", r.Trechos[0].Falante);
        Assert.Equal("Paloma Santos", r.Trechos[1].Falante);
    }

    [Fact]
    public void AColagemEntraNaFalaENaFusao()
    {
        // "Ta" + "nia" é "Tania" (colado), e a fonética a corrige para "Tânia".
        var r = CorrecaoDaLegenda.Corrigir(
            [T(0, "a Ta"), T(1120, "nia pediu", colado: true)], Vocabulario, Entidades);

        var t = Assert.Single(r.Trechos);
        Assert.Equal("a Tânia pediu", t.Texto);
        Assert.False(t.Colado);
    }

    [Fact]
    public void SemVocabularioNemAgendaOsTrechosSaemIntactos()
    {
        var entrada = new[] { T(0, "problema de Wi Fi"), T(1120, "e canal") };

        var r = CorrecaoDaLegenda.Corrigir(entrada, vocabulario: "", entidades: []);

        Assert.Equal(0, r.Trocas);
        Assert.Same(entrada[0], r.Trechos[0]);
        Assert.Same(entrada[1], r.Trechos[1]);
    }

    [Fact]
    public void LegendaSemFalanteAgrupaPeloDono()
    {
        // O caminho da diarização recusada: os trechos chegam sem falante.
        var r = CorrecaoDaLegenda.Corrigir(
            [T(0, "chega pelo web", quem: null), T(1120, "hook", quem: null)],
            Vocabulario, Entidades);

        Assert.Equal("chega pelo Webhook", Assert.Single(r.Trechos).Texto);
    }
}
```

> **Se um teste falhar porque a regra não propõe a troca** (e não por causa da
> fusão), não afrouxe a `RevisaoDeTermos`: ajuste o exemplo do teste para um que
> a regra já propõe, conferindo em `Tests/RevisaoDeTermosTests.cs` e
> `Tests/GrafiaDeTermosTests.cs`. `Wi Fi→Wifi`, `web hook→Webhook` e
> `Tania→Tânia` foram propostos na medição de 23/09/2026 com essas entidades,
> mas as entidades lá tinham também o resto do vocabulário e os convidados.

- [ ] **Passo 2: rodar e ver falhar**

Rodar: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter CorrecaoDaLegendaTests`
Esperado: não compila — `CorrecaoDaLegenda` não existe.

- [ ] **Passo 3: implementar `app-net/Nucleo/CorrecaoDaLegenda.cs`**

```csharp
using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo;

/// <summary>
/// A correção de termos da passada final, aplicada à legenda ao vivo.
/// </summary>
/// <remarks>
/// <para>
/// <b>É o <c>VIVO-3</c>.</b> O Nemotron não tem gancho de vocabulário na
/// entrada, então o que dá para fazer é corrigir depois — e roda no fim da
/// reunião, junto da separação de falantes. Medido em 23/09/2026: "Wifi" sai
/// de 2 para 11 ocorrências (a passada final tem 14).
/// </para>
/// <para>
/// <b>Corrige por fala e aplica por trecho.</b> O trecho tem 1,1 s — a
/// granularidade do commit do motor —, e três das quatro propostas medidas
/// eram de duas palavras. Corrigir trecho a trecho perderia justamente essas:
/// "Wi" num, "Fi" no seguinte. A fala (trechos seguidos do mesmo falante) é
/// onde a troca se enxerga; o trecho é onde ela se grava.
/// </para>
/// <para>
/// <b>Funde só o que a troca atravessa.</b> Os trechos carregam o tempo que a
/// diarização usa; fundir tudo apagaria esse tempo. Dois trechos viram um
/// quando, e só quando, uma troca cobre os dois.
/// </para>
/// </remarks>
public static class CorrecaoDaLegenda
{
    public sealed record Resultado(List<TrechoDaLegenda> Trechos, int Trocas, int Fundidos);

    public static Resultado Corrigir(
        IReadOnlyList<TrechoDaLegenda> trechos, string? vocabulario,
        IReadOnlyList<string> entidades)
    {
        var falas = Falas(trechos);
        var textos = falas.Select(f => Juntar(f)).ToList();

        var foneticos = CorrecaoDeTermos.Fonetica(textos, vocabulario);
        var propostas = CorrecaoDeTermos.Propostas(
            [.. foneticos.Select(f => f.Texto)], entidades);

        var saida = new List<TrechoDaLegenda>(trechos.Count);
        int trocas = 0, fundidos = 0;
        for (int i = 0; i < falas.Count; i++)
        {
            var alvos = foneticos[i].Trocas.Select(t => t.De)
                .Concat(propostas.Select(p => p.De))
                .Distinct().ToList();
            var fundida = Fundir(falas[i], alvos, ref fundidos);

            foreach (var t in fundida)
            {
                var (texto, dela) = CorrecaoDeTermos.Fonetica([t.Texto], vocabulario)[0];
                var (final, daRegra) = RevisaoDeTermos.Aplicar(texto, propostas);
                var todas = dela.Concat(daRegra).ToList();
                if (todas.Count == 0) { saida.Add(t); continue; }

                trocas += todas.Count;
                saida.Add(new TrechoDaLegenda
                {
                    InicioMs = t.InicioMs, FimMs = t.FimMs, Dono = t.Dono,
                    Falante = t.Falante, Colado = t.Colado, Texto = final,
                    Swaps = [.. (t.Swaps ?? []),
                             .. todas.Select(x => new TrocaFeita { De = x.De, Para = x.Para })],
                });
            }
        }
        return new Resultado(saida, trocas, fundidos);
    }

    /// <summary>Trechos seguidos do mesmo falante e do mesmo dono.</summary>
    private static List<List<TrechoDaLegenda>> Falas(IReadOnlyList<TrechoDaLegenda> trechos)
    {
        var falas = new List<List<TrechoDaLegenda>>();
        foreach (var t in trechos)
        {
            if (falas.Count > 0 && falas[^1][^1].Falante == t.Falante
                                && falas[^1][^1].Dono == t.Dono)
                falas[^1].Add(t);
            else
                falas.Add([t]);
        }
        return falas;
    }

    /// <summary>A regra do <c>Ponte.FalasDaLegenda</c>: sem espaço quando a palavra continua.</summary>
    private static string Juntar(IEnumerable<TrechoDaLegenda> fala)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in fala)
        {
            if (sb.Length > 0 && !t.Colado) sb.Append(' ');
            sb.Append(t.Texto);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Os trechos da fala, com os que uma ocorrência de <paramref name="alvos"/>
    /// atravessa fundidos num só.
    /// </summary>
    private static List<TrechoDaLegenda> Fundir(
        List<TrechoDaLegenda> fala, IReadOnlyList<string> alvos, ref int fundidos)
    {
        if (fala.Count < 2 || alvos.Count == 0) return fala;

        // Onde cada trecho começa no texto da fala.
        var inicios = new int[fala.Count];
        int pos = 0;
        for (int i = 0; i < fala.Count; i++)
        {
            if (i > 0 && !fala[i].Colado) pos++;
            inicios[i] = pos;
            pos += fala[i].Texto.Length;
        }
        int TrechoDe(int c) => Array.FindLastIndex(inicios, x => x <= c);

        // grupo[i] = índice do primeiro trecho do bloco fundido a que i pertence
        var grupo = Enumerable.Range(0, fala.Count).ToArray();
        string texto = Juntar(fala);
        foreach (var alvo in alvos)
        {
            var re = new Regex($@"(?<!\w){Regex.Escape(alvo)}(?!\w)", RegexOptions.IgnoreCase);
            foreach (Match m in re.Matches(texto))
            {
                int a = TrechoDe(m.Index), b = TrechoDe(m.Index + m.Length - 1);
                for (int k = a + 1; k <= b; k++) grupo[k] = grupo[a];
            }
        }

        var saida = new List<TrechoDaLegenda>();
        for (int i = 0; i < fala.Count; i++)
        {
            // grupo[k] sempre aponta para o primeiro de um bloco contíguo, então
            // "diferente de si" é exatamente "continua o trecho anterior".
            if (grupo[i] != i)
            {
                var antes = saida[^1];
                saida[^1] = new TrechoDaLegenda
                {
                    InicioMs = antes.InicioMs, FimMs = fala[i].FimMs, Dono = antes.Dono,
                    Falante = antes.Falante, Colado = antes.Colado,
                    Texto = antes.Texto + (fala[i].Colado ? "" : " ") + fala[i].Texto,
                    Swaps = antes.Swaps is null && fala[i].Swaps is null ? null
                        : [.. antes.Swaps ?? [], .. fala[i].Swaps ?? []],
                };
                fundidos++;
            }
            else saida.Add(fala[i]);
        }
        return saida;
    }
}
```

> **A fusão é transitiva** (uma troca junta 2 e 3, outra junta 3 e 4 → 2, 3 e 4
> viram um). `Fundidos` conta **junções** — dois trechos que viram um contam 1.
> Se um teste de transitividade for acrescentado, é esta a contagem que ele fixa.

> **A fonética por trecho, no passo 4, não enxerga a palavra que estava
> partida** — mas depois da fusão ela não está mais partida. É por isso que a
> fusão vem antes da aplicação, e o teste `AColagemEntraNaFalaENaFusao` é o que
> garante a ordem.

- [ ] **Passo 4: rodar** `--filter CorrecaoDaLegendaTests`. Esperado: 6 PASS. Depois a suíte inteira.

- [ ] **Passo 5: commit**

```bash
git add app-net/Nucleo/CorrecaoDaLegenda.cs app-net/Tests/CorrecaoDaLegendaTests.cs
git commit -m "feat(legenda): a correção de termos da passada final, aplicada por fala"
```

---

### Tarefa 4: ligar no fim da reunião

**Arquivos:**
- Modificar: `app-net/App/Ponte.cs`, método `SepararFalantes` (~linha 1636) e os três pontos em que ele chama `LegendaAoVivo.Gravar`.

**Interfaces:**
- Consome: `CorrecaoDaLegenda.Corrigir` (T3), `CorrecaoDeTermos.Entidades` (T1),
  `DadosDaReuniao.Ler(pasta)` (`.Cliente`, `.Projeto`),
  `_projetos.Preferencias(cliente, projeto)?.InitialPrompt`,
  `ConfiguracoesDoApp.Carregar()` e a chave de correção fonética da transcrição
  (a mesma que a linha `corrigirFonetica: cfgDaTranscricao.CorrecaoFonetica`
  usa em `IniciarTranscricao` — copie o caminho exato de acesso de lá).
- Produz: nada que outra tarefa consuma.

- [ ] **Passo 1: um método privado na `Ponte`**, junto do `SepararFalantes`:

```csharp
    /// <summary>
    /// A correção de termos sobre a legenda, com o vocabulário do projeto da reunião.
    /// </summary>
    /// <remarks>
    /// <b>Nunca levanta.</b> É acabamento: se falhar, a legenda sai como saía.
    /// Roda nos três desfechos da separação — feita, recusada e falha —,
    /// porque corrigir texto não usa placa e não depende de haver falante.
    /// </remarks>
    private List<TrechoDaLegenda> CorrigirTermos(string pasta, List<TrechoDaLegenda> trechos)
    {
        try
        {
            // (o mesmo acesso que IniciarTranscricao usa para corrigirFonetica)
            if (!/* cfg da transcrição */.CorrecaoFonetica) return trechos;

            var vinculo = DadosDaReuniao.Ler(pasta);
            string? vocabulario = _projetos.Preferencias(
                vinculo.Cliente ?? "", vinculo.Projeto ?? "")?.InitialPrompt;
            var entidades = CorrecaoDeTermos.Entidades(
                pasta, vocabulario, vinculo.Cliente, vinculo.Projeto);

            var r = CorrecaoDaLegenda.Corrigir(trechos, vocabulario, entidades);
            Registro.Escrever("legenda",
                $"termos corrigidos: {r.Trocas} troca(s), {r.Fundidos} trecho(s) fundido(s)");
            return r.Trechos;
        }
        catch (Exception e)
        {
            Registro.Escrever("legenda", $"correção de termos ignorada: {e.Message}");
            return trechos;
        }
    }
```

- [ ] **Passo 2: chamar nos três `Gravar` do `SepararFalantes`**

- recusado (`catch (InvalidOperationException e)`):
  `LegendaAoVivo.Gravar(pasta, legenda.Turnos, CorrigirTermos(pasta, legenda.Trechos), prontos: true);`
- sucesso, **depois** do bloco de vozes conhecidas:
  `comFalante = CorrigirTermos(pasta, comFalante);` logo antes do `Gravar`.
  Depois do reconhecimento, e não antes: a fala é agrupada por falante, e o
  falante com nome é o definitivo.
- falha (`catch (Exception e)` externo):
  `LegendaAoVivo.Gravar(pasta, legenda.Turnos, CorrigirTermos(pasta, legenda.Trechos), prontos: true);`

**Não** chamar no `catch (OperationCanceledException)`: cancelado não grava nada hoje, e continua não gravando.

- [ ] **Passo 3: a tela recebe o texto corrigido sem mudança** — conferir que
  `FalasDaLegenda` lê `t.Texto` e `t.Colado` dos trechos (lê). Mostrar as trocas
  na tela da legenda, com desfazer, **fica fora deste plano** (é o que a tela da
  revisão faz com o `swaps` da passada final; ver "Fora do escopo").

- [ ] **Passo 4: compilar e rodar a suíte inteira**

Rodar: `dotnet build app-net/App/MeetingApp.App.csproj` — a `Ponte` é do App,
que pode ter alvo Windows; se não compilar no Linux, conferir que ao menos
`dotnet test app-net/Tests/MeetingApp.Tests.csproj` passa (o `PonteTests`
compila a Ponte) e registrar isso no relatório da tarefa.
Esperado: tudo passa.

- [ ] **Passo 5: commit**

```bash
git add app-net/App/Ponte.cs
git commit -m "feat(legenda): o fim da reunião corrige os termos junto da separação de falantes"
```

---

### Tarefa 5: medir sobre a reunião de 23/09 e registrar

**Arquivos:**
- Modificar: `docs/BACKLOG.md` (o `VIVO-3`)
- Criar: `app-net/Tests/CorrecaoDaLegendaNoAcervoTests.cs`

- [ ] **Passo 1: o teste de acervo**, no molde do `RevisaoNoAcervoTests`
  (pula quando o acervo não existe — copie de lá o método `Acervo()`):

```csharp
    [Fact]
    public void AReuniaoDe23De09RecuperaOWifi()
    {
        if (Acervo() is not { } raiz) { saida.WriteLine("acervo ausente — pulado"); return; }
        string pasta = Path.Combine(raiz, "2026-09-23_10-00-17");
        if (LegendaAoVivo.Ler(pasta) is not { Trechos.Count: > 0 } legenda)
        { saida.WriteLine("reunião ausente — pulado"); return; }

        const string voc = "KPI, SDK, S3, life cycle, persona, Tânia, Prada, Knauth, API, "
            + "Cloud, Wellington, César, Ipera, Miro, Massiva, Wifi, Rede, APP, Diego, NPS, "
            + "modem, versionamento, white label, Beegol, Service, V.TAL, Beta, NIO, lambda, "
            + "ECS, PR, Webhook";
        var entidades = CorrecaoDeTermos.Entidades(pasta, voc, "Agentes", "Agentes (Interno)");

        var r = CorrecaoDaLegenda.Corrigir(legenda.Trechos, voc, entidades);
        int wifi = r.Trechos.Sum(t => Regex.Matches(t.Texto, @"(?<!\w)Wifi(?!\w)").Count);
        saida.WriteLine($"{r.Trocas} trocas, {r.Fundidos} fundidos, Wifi = {wifi}");

        // O runner de 23/09, que juntava por fala sem fundir, chegou a 11.
        // A fusão só pode igualar ou superar isso.
        Assert.True(wifi >= 11, $"Wifi = {wifi}");
    }
```

- [ ] **Passo 2: rodar** `--filter CorrecaoDaLegendaNoAcervoTests` e anotar a linha de saída (`--logger "console;verbosity=detailed"`).

- [ ] **Passo 3: atualizar o `VIVO-3` no `docs/BACKLOG.md`** — de `bug · espera`
  para feito, com: a data, a tabela de termos da seção "A medição" deste plano,
  o número do Passo 2, e a frase de limite: *"recupera o que o motor ouviu e
  escreveu diferente; o que ele não ouviu continua faltando, e só a passada final
  recupera"*. Mencionar que a tela ainda não mostra as trocas da legenda.

- [ ] **Passo 4: commit**

```bash
git add app-net/Tests/CorrecaoDaLegendaNoAcervoTests.cs docs/BACKLOG.md
git commit -m "docs(legenda): o VIVO-3 fechado, com a medição sobre a reunião de 23/09"
```

- [ ] **Passo 5: entrega ao dono do produto** — **não** publicar sozinho. Dizer
  que o próximo passo é `tools/publicar.sh` com o app fechado pela bandeja, e
  uma reunião real com a legenda ligada; a conferência é abrir o `legenda.json`
  depois da separação e ver `swaps` nos trechos, e a linha `termos corrigidos:`
  no `registro.log`.

---

## Fora do escopo — cada um é um plano próprio

- **Mostrar e desfazer as trocas na tela da legenda.** O dado passa a existir
  (`swaps`); a tela da revisão já sabe desenhá-lo para a passada final, e a da
  legenda pode herdar depois.
- **Corrigir legendas antigas** (`falantes_prontos: true` já gravado). O
  `SepararFalantes` só roda em legenda pendente; reprocessar o acervo é uma
  ferramenta à parte.
- **Dividir a fala pelo carimbo de palavra (`VIVO-1`).** A legenda abre a
  sessão com `timestamps: "none"` (`motores/legenda/motor.py:187`), e o modelo
  declara `max_timestamp_kind = token`. Ligar isso permitiria cortar na
  fronteira de palavra como o `RepartirPorFalante` faz na passada final — mas o
  custo numa sessão ao vivo precisa ser medido antes, e mexe no sidecar.
- **O propositor por modelo na legenda** — ver as restrições globais.
