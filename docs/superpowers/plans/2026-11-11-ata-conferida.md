# A ata conferida: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cada item da ata de formalização guarda a fala de onde saiu, a pessoa confere item por item ouvindo o trecho, e a ata passa a ter estado (rascunho, conferida, enviada), seja o rascunho escrito pelo modelo local ou pelo Claude.

**Architecture:** Um arquivo novo por reunião, `conferencia.json`, vira a fonte da ata de formalização; `ata.md`, `ata.json` e `ata.observacoes.md` passam a ser derivados dele a cada mudança. Toda a lógica mora no `Nucleo` (onde a suíte alcança) e a ponte só repassa. O Claude escreve por duas ferramentas novas do MCP, e o processo dele grava um arquivo só, `proposta-do-claude.json`, que o app adota por um gesto da pessoa.

**Tech Stack:** C#/.NET 8, xUnit, System.Text.Json com contexto gerado em compilação, `JsonNode` no MCP (trimming), JavaScript sem build no WebView2, `node --test` para as regras da tela, Python só na ferramenta de medida.

**Spec:** [docs/superpowers/specs/2026-11-11-ata-conferida-design.md](../specs/2026-11-11-ata-conferida-design.md). As seções citadas como §N são dele.

**É o passo 1 de quatro.** Os planos dos passos 2, 3 e 4 consomem os nomes que este define (bloco *Interfaces* de cada tarefa). Mudar um nome aqui é mudar lá.

## Global Constraints

- Comandos de teste sempre com `export PATH="$HOME/.dotnet:$PATH"` antes.
- Suítes: `dotnet test app-net/Tests/MeetingApp.Tests.csproj` e `node --test tools/web/*.test.mjs`. Toda tarefa termina com as duas verdes. Anote a contagem das duas antes da Tarefa 1.
- Arquivos novos na pasta da gravação, com estes nomes exatos: `conferencia.json`, `conferencia.anterior.json`, `proposta-do-claude.json`, `ata-email.md`, `ata-email.anterior.md`.
- **A conferência vale só para o tipo `formalizacao`** (§3). Os prompts e os esquemas da extração e da escrita locais (`SistemaDaExtracao`, `EsquemaDaExtracao`, `SistemaDaEscrita`, `EsquemaDaEscrita`) não mudam.
- **O `ata.md` de um rascunho local sai igual ao de hoje** para a mesma extração (§7). A Tarefa 1 congela isso antes de qualquer mudança.
- **Nenhuma ação da conferência muda a data do `ata.md`** (§10): aprovar, corrigir, tirar, repor, acrescentar, concluir e marcar como enviada preservam o `LastWriteTimeUtc`. Só gerar de novo e usar uma proposta escrevem data nova.
- **O processo MCP grava só `proposta-do-claude.json`.** Nunca `ata.md`, `ata.json`, `conferencia.json`.
- **Trimming:** no MCP, `(JsonNode)` em todo `Add` e `new JsonArray(a, b)` no lugar de `[a, b]`; tipos novos serializados só por contexto gerado em compilação. A prova é `tools/publicar.sh --so-build` sem IL2026 (Tarefa 16).
- **O stdout do MCP é o protocolo:** nada de `Console.Write` fora da resposta.
- **Na tela, nós e nunca `innerHTML`:** o texto vem de um modelo.
- **Nenhum dado de cliente real em fixture, teste ou commit.** O repositório é público. As fixtures usam a reunião inventada "Acme Bebidas".
- **Nunca rodar o motor de ata (GPU) sem o dono autorizar na hora**, e **nunca escrever nas pastas reais do acervo** fora da Tarefa 16, que começa perguntando.
- Não publicar (`tools/publicar.sh` sem `--so-build`), não subir versão, não dar push. O dono decide.
- Comentários e textos em português, no tom do código vizinho; `EmbeddedResource`/`None` com barra normal.

## Review Focus

1. **A tela aberta enquanto a ata muda por baixo** (refeita, ou uma proposta usada em outra janela): uma ação com o id de um item que não existe mais tem que ser recusada com mensagem, e nunca cair em outro item. Teste na Tarefa 5.
2. **`conferencia.json` ilegível, vazio ou de uma versão futura:** a aba Ata abre o `ata.md` como ata antiga, só leitura; a lista de reuniões não perde a reunião. Testes nas Tarefas 3 e 7.
3. **O Claude cita mal:** linha que não existe, linha como texto (`"12"`), número negativo, `lado` com valor de fora, lista que não é lista. O item cai ou fica com aviso, com a explicação na resposta, e nunca uma exceção. Teste na Tarefa 11.
4. **Falha ao escrever o `ata.md`** (arquivo aberto no Word, pasta do OneDrive travada): a ação da pessoa já está no `conferencia.json`, o erro aparece, e a ação seguinte deriva de novo. Teste na Tarefa 5.
5. **Fala citada de dez minutos** (uma pessoa falando sozinha vira um turno só): a tela mostra o começo e abre o resto a pedido; o registro guarda inteira. Teste na Tarefa 8.

---

## Mapa dos arquivos

| arquivo | o que muda |
|---|---|
| `app-net/Tests/FormalizacaoDeExemplo.cs` (novo) | a reunião inventada e as respostas falsas do motor |
| `app-net/Tests/Referencias/formalizacao.md` (novo) | o `ata.md` de hoje, congelado |
| `app-net/Nucleo/Atas/AtaDeFormalizacao.cs` | `Aviso`, `AConfirmar`, `Lado`, `Turno.Fim`; `Conferir` avisa em vez de apagar; `Montar` delega |
| `app-net/Nucleo/Atas/Conferencia.cs` (novo) | o registro, o JSON, ler e salvar, `ParaAta`, `DoLocal`, `DoClaude` |
| `app-net/Nucleo/Atas/ArquivosDaAta.cs` (novo) | derivar `ata.md`/`ata.json`/observações; arquivar a anterior |
| `app-net/Nucleo/Atas/RevisaoDaAta.cs` (novo) | as ações da pessoa, os bloqueios, a proposta |
| `app-net/Nucleo/Atas/GeracaoDeAta.cs`, `PromptDeAta.cs` | o caminho local escreve o registro; `ContextoDaReuniao.Casa` |
| `app-net/Nucleo/ConfiguracoesDoApp.cs` | `nome_da_empresa`, `NomeDaCasa()` |
| `app-net/Nucleo/EstadoDaAta.cs` | o estado da conferência, para a lista |
| `app-net/Nucleo/Claude/FerramentasDoMcp.cs`, `Acervo.cs`, `ServidorMcp.cs` | `preparar_ata`, `propor_ata`, `fazer-ata`, `ler_ata` com estado |
| `app-net/App/Ponte.cs`, `JanelaDoApp.cs`, `Nativo/Win32.cs` | as operações da conferência; o evento `janela` |
| `app-net/App/web/conferencia-regras.js`, `conferencia.js` (novos) | as regras e a tela |
| `app-net/App/web/atas.js`, `ata-regras.js`, `reuniao.js`, `configuracoes.js`, `app.css` | a aba Ata escolhe a tela; selo; Ajustes |
| `app-net/Cli/GeradorDeAta.cs` | passa a casa e o modelo |
| `tools/web/conferencia-regras.test.mjs` (novo), `tools/medir_conferencia.py` (novo) | os testes da tela; a régua |
| `docs/INTEGRACAO-CLAUDE.md`, `docs/RASTREABILIDADE.md`, `docs/BACKLOG.md`, `docs/ATA.md` | o que mudou |

**Dois cortes.** As Tarefas 1 a 9 e 13 entregam o caminho local com a tela, e já têm valor sozinhas. As Tarefas 10 a 12 entregam o Claude. 14 a 16 fecham. Sair em uma versão ou duas é decisão do dono.

---

### Task 1: Congelar a formalização de hoje

Nenhum comportamento muda. Grava o `ata.md` que o caminho local produz **hoje** para uma reunião inventada, e cria o teste que exige igualdade.

**Files:**
- Create: `app-net/Tests/FormalizacaoDeExemplo.cs`
- Modify: `app-net/Tests/ReferenciasDaAtaTests.cs` (um teste a mais)
- Create: `app-net/Tests/Referencias/formalizacao.md` (gerado pelo teste)

**Interfaces:**
- Produces: `FormalizacaoDeExemplo.Segmentos()`, `.Contexto()`, `.Extracao`, `.Escrita`, `.Abrir(List<string>? vistos = null)`. Todas as tarefas seguintes usam esta fixture.

- [ ] **Step 1: Escrever a fixture**

```csharp
using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;

namespace MeetingApp.Tests;

/// <summary>Uma reunião inventada com tudo o que a conferência mexe. Nenhum dado de cliente real.</summary>
internal static class FormalizacaoDeExemplo
{
    private static SegmentoFinal S(double t, string quem, string texto) =>
        new() { Start = t, End = t + 4, Speaker = quem, Text = " " + texto };

    public static List<SegmentoFinal> Segmentos() =>
    [
        S(0, "Bruno Teles", "Bom dia. Antes da pauta, o piloto precisa estar no ar até dia 20."),  // L1
        S(8, "Carla Nunes", "Certo. Sobre o corte, eu acho que 500 caixas."),                      // L2
        S(14, "Bruno Teles", "Fechado, 500 caixas por mês."),                                      // L3
        S(20, "Diego Prado", "Carla, consegue me mandar a margem por cliente até amanhã?"),        // L4
        S(26, "Carla Nunes", "Deixa eu ver aqui."),                                                // L5
        S(30, "Diego Prado", "É importante para o plano."),                                        // L6
        S(34, "Carla Nunes", "Mando isso hoje."),                                                  // L7
        S(38, "Bruno Teles", "Hoje o cálculo usa a média de três meses."),                         // L8
        S(44, "Carla Nunes", "A lista das lojas novas eu vejo com o comercial."),                  // L9
        S(50, "Bruno Teles", "Então a próxima fica para quinta, dia 23."),                         // L10
    ];

    public static ContextoDaReuniao Contexto() => new()
    {
        Titulo = "Semanal do Portal de Pedidos", Cliente = "Acme Bebidas", Projeto = "Portal de Pedidos",
        Data = "2026-10-21T11:30:00-03:00", DuracaoS = 54,
        Convidados = ["Bruno Teles", "Diego Prado", "Carla Nunes"],
        Pessoas =
        [
            new Pessoa("Bruno Teles", "casa.com", true), new Pessoa("Diego Prado", "casa.com", true),
            new Pessoa("Carla Nunes", "acme.com", false),
        ],
        Falantes = ["Bruno Teles", "Carla Nunes", "Diego Prado"],
    };

    /// <summary>Um item sem fala (cai), um prazo que ninguém disse, uma logística, uma próxima reunião.</summary>
    public const string Extracao = """
        {"resolvidos":[
          {"tema":"Corte","decisao":"500 caixas por mês","linhas":[3]},
          {"tema":"Cálculo","decisao":"Usa a média de três meses","linhas":[8]},
          {"tema":"Próxima reunião","decisao":"quinta, dia 23","linhas":[10]},
          {"tema":"Rateio","decisao":"Seguimos com o de 2025","linhas":[99]}],
         "pendentes":[
          {"o_que":"Margem por cliente","responsavel":"Carla","prazo":"hoje","linhas":[4]},
          {"o_que":"Lista das lojas novas","responsavel":"","prazo":"28/10/2026","linhas":[9]},
          {"o_que":"Enviar a ata da reunião","responsavel":"Bruno","prazo":"","linhas":[10]}]}
        """;

    /// <summary>A escrita deixa o "Cálculo" de fora, como o modelo de verdade faz ao juntar.</summary>
    public const string Escrita = """
        {"abertura":"","resolvidos":[
          {"n":1,"tema":"Corte de volume","texto":"500 caixas por mês."},
          {"n":3,"tema":"Próxima reunião","texto":"quinta, 23/10"}],
         "pendentes":[{"n":1,"texto":"Margem por cliente"},{"n":2,"texto":"Lista das lojas novas"}],
         "proxima_reuniao":""}
        """;

    private sealed class Sessao(string resposta, List<string>? vistos) : ISessaoDoMotor
    {
        public Task<string> PerguntarAsync(string prompt, CancellationToken ct)
        {
            vistos?.Add(prompt);
            return Task.FromResult(resposta);
        }
        public void Dispose() { }
    }

    public static GeracaoDeAta.AbrirSessao Abrir(List<string>? vistos = null) =>
        (sistema, _, _, _, _, _) => Task.FromResult<ISessaoDoMotor>(new Sessao(
            sistema == AtaDeFormalizacao.SistemaDaExtracao ? Extracao : Escrita, vistos));
}
```

- [ ] **Step 2: Escrever o teste de referência** em `ReferenciasDaAtaTests.cs`

```csharp
[Fact]
public async Task AFormalizacaoLocalEscreveOMesmoAtaMd()
{
    string pasta = Directory.CreateTempSubdirectory("ref-formal").FullName;
    try
    {
        var dados = new ResultadoDaTranscricao { Duration = 54, Segments = FormalizacaoDeExemplo.Segmentos() };
        await GeracaoDeAta.GerarAsync(pasta, ModelosDeAta.Buscar(AtaDeFormalizacao.Id)!, dados,
            FormalizacaoDeExemplo.Contexto(), (_, _, _, _, _) => throw new InvalidOperationException("caminho comum"),
            null, CancellationToken.None, FormatoDaAta.Classico, FormalizacaoDeExemplo.Abrir());
        Conferir("formalizacao.md", File.ReadAllText(Path.Combine(pasta, "ata.md")).Replace("\r\n", "\n"));
    }
    finally { Directory.Delete(pasta, recursive: true); }
}
```

- [ ] **Step 3: Gravar a referência com o código de hoje**

Run: `ATA_REFERENCIAS=gravar dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~AFormalizacaoLocalEscreveOMesmoAtaMd"`
Expected: PASS, e `app-net/Tests/Referencias/formalizacao.md` existe com este conteúdo (confira à mão; se diferir, a fixture foi copiada errada):

```
**Resolvidos em reunião**

- Corte de volume: 500 caixas por mês.

**Pendentes**

- Margem por cliente: Carla, hoje
- Lista das lojas novas

Próxima reunião: quinta, 23/10
```

- [ ] **Step 4: Rodar sem a variável**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ReferenciasDaAtaTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Tests/FormalizacaoDeExemplo.cs app-net/Tests/ReferenciasDaAtaTests.cs app-net/Tests/Referencias/formalizacao.md
git commit -m "test(ata): congela o ata.md da formalização antes da conferência"
```

---

### Task 2: A conferência automática avisa em vez de apagar

§5.1. O prazo que ninguém disse deixa de ser apagado: fica no item, com aviso. Número e responsável sem fonte viram aviso no item além da observação. Entra o tipo "a confirmar".

**Files:**
- Modify: `app-net/Nucleo/Atas/AtaDeFormalizacao.cs`
- Test: `app-net/Tests/AtaDeFormalizacaoTests.cs`

**Interfaces:**
- Produces, todos dentro de `AtaDeFormalizacao`:
  - `record Aviso(string Tipo, string Texto)`; `Tipo` é `"prazo"`, `"numero"`, `"responsavel"` ou `"escrita"`.
  - `record Turno(int N, string Falante, double Inicio, string Texto, double Fim = 0)`. `Turnos()` preenche `Fim` com o `End` do último segmento juntado.
  - `Resolvido` e `Pendente` ganham `IReadOnlyList<Aviso> Avisos { get; init; } = []`; `Pendente` ganha `string Lado { get; init; } = ""`.
  - `record AConfirmar(string Texto, string Motivo, IReadOnlyList<int> Linhas)` com o mesmo `Avisos`.
  - `Extracao` ganha `IReadOnlyList<AConfirmar> AConfirmar { get; init; } = []`.
  - `Conferir` mantém a assinatura.

- [ ] **Step 1: Trocar os testes do comportamento que muda, e escrever os novos**

`PrazoQueNinguemDisseEApagadoEViraObservacao` vira:

```csharp
[Fact]
public void PrazoQueNinguemDisseFicaComAvisoENaoEApagado()
{
    var ext = new Extracao([], [new Pendente("Margem por cliente", "Rafael", "28/10/2026", [4])]);

    var (conf, obs) = Conferir(ext, Turnos(Reuniao), Ctx());

    Assert.Equal("28/10/2026", conf.Pendentes[0].Prazo);
    Assert.Contains(conf.Pendentes[0].Avisos, a => a.Tipo == "prazo" && a.Texto.Contains("28/10/2026"));
    Assert.Contains(obs, o => o.StartsWith("Prazo fora da ata até ser conferido") && o.Contains("28/10/2026"));
}

[Fact]
public void NumeroEResponsavelSemFonteViramAvisoNoItem()
{
    var ext = new Extracao(
        [new Resolvido("Corte", "700 caixas por mês", [3])],
        [new Pendente("Margem por cliente", "Zuleica", "", [4])]);

    var (conf, _) = Conferir(ext, Turnos(Reuniao), Ctx());

    Assert.Contains(conf.Resolvidos[0].Avisos, a => a.Tipo == "numero" && a.Texto.Contains("700"));
    Assert.Contains(conf.Pendentes[0].Avisos, a => a.Tipo == "responsavel" && a.Texto.Contains("Zuleica"));
}

[Fact]
public void AConfirmarSemFalaCaiEComFalaFica()
{
    var ext = new Extracao([], [])
    {
        AConfirmar = [new AConfirmar("O corte vale para todas as regiões", "ninguém do cliente concordou", [3]),
                      new AConfirmar("Sem fala", "x", [99])],
    };

    var (conf, obs) = Conferir(ext, Turnos(Reuniao), Ctx());

    Assert.Single(conf.AConfirmar);
    Assert.Equal([3], conf.AConfirmar[0].Linhas);
    Assert.Contains(obs, o => o.StartsWith("Descartado por não citar a fala") && o.Contains("Sem fala"));
}

[Fact]
public void OTurnoGuardaOndeAFalaTermina()
{
    var t = Turnos(Reuniao);
    Assert.Equal(0, t[0].Inicio);
    Assert.Equal(4, t[0].Fim);   // "Bom dia." (0–2) + "Vamos fechar o corte." (2–4)
}
```

Nos outros testes do arquivo que afirmam `Prazo == ""` depois de `Conferir`, ou a frase "Prazo apagado": troque para o prazo mantido com aviso e para a frase nova. Os que afirmam sobre o `ata.md` ou o `AtaGerada` final continuam valendo como estão (o prazo com aviso não entra na ata do rascunho, Tarefa 3).

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~AtaDeFormalizacaoTests"`
Expected: FAIL de compilação (`Avisos`, `AConfirmar`, `Fim` não existem).

- [ ] **Step 3: Implementar em `AtaDeFormalizacao.cs`**

Os tipos do bloco *Interfaces*. Em `Conferir`:
- prazo que a régua reprova: o item fica com `Prazo` intacto e `Aviso("prazo", $"O prazo \"{prazo}\" não aparece perto das falas citadas.")`; a observação passa a ser `$"Prazo fora da ata até ser conferido, porque não foi dito ({prazo}): {p.OQue}"`;
- número sem fonte: `Aviso("numero", $"Número que não aparece na fala citada: {string.Join(", ", faltam)}.")`, e a observação de hoje fica;
- responsável sem fonte: `Aviso("responsavel", $"\"{resp}\" não aparece na fala citada nem entre os convidados.")`, e a observação de hoje fica;
- `AConfirmar`: sem linha que exista ou com texto vazio, cai com a mesma frase "Descartado por não citar a fala de onde saiu: …"; senão fica, com `Linhas` filtradas. Passa pela régua de número como o resolvido.
- `Pendente.Lado` atravessa sem mudança.

`Montar` e `Acao` ainda leem `p.Prazo`: nesta tarefa, faça `Acao` devolver `Prazo = ""` quando o pendente tem aviso de tipo `"prazo"`, para o `ata.md` não mudar. A Tarefa 4 troca `Montar` inteiro.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS, inclusive `AFormalizacaoLocalEscreveOMesmoAtaMd`.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Atas/AtaDeFormalizacao.cs app-net/Tests/AtaDeFormalizacaoTests.cs
git commit -m "feat(ata): a conferência automática avisa no item em vez de apagar o prazo"
```

---

### Task 3: O registro da conferência

§5. Os tipos do `conferencia.json`, ler e salvar, e a ata que sai dele.

**Files:**
- Create: `app-net/Nucleo/Atas/Conferencia.cs`
- Test: `app-net/Tests/ConferenciaTests.cs`

**Interfaces:**
- Produces (namespace `MeetingApp.Nucleo.Atas`; os nomes JSON entre aspas):

```csharp
public sealed class FalaCitada      // "inicio", "fim" (segundos), "falante", "texto"
public sealed class AvisoDoItem     // "tipo", "texto"
public sealed class EnvioDaAta      // "em" (ISO 8601), "modo" ("manual"; o passo 4 acrescenta "mailto" e "gmail"),
                                    // "para" (List<string>?), "id_da_mensagem" (string?)
public sealed class ItemDaConferencia
{
    public const string Resolvido = "resolvido", Pendente = "pendente", AConfirmar = "a_confirmar";
    public const string AConferir = "a_conferir", Aprovado = "aprovado", Corrigido = "corrigido", Retirado = "retirado";
    // "id" ("i1", "i2"… nunca reaproveitado), "tipo", "tema", "texto", "lado" ("nosso" | "cliente" | ""),
    // "responsavel", "prazo", "motivo", "falas", "avisos", "revisao" (padrão a_conferir), "origem" ("modelo" | "manual")
    public bool Visto => Revisao != AConferir;
    public bool Fora => Revisao == Retirado;
}
public sealed class Conferencia
{
    public const string NomeDoArquivo = "conferencia.json", ArquivoAnterior = "conferencia.anterior.json",
                        ArquivoDaProposta = "proposta-do-claude.json";
    public const string Rascunho = "rascunho", Conferida = "conferida";
    // "versao" (1), "escrita_por" ("local" | "claude"), "modelo", "criada_em", "estado" (padrão rascunho),
    // "comecou_em", "concluida_em" (string?), "envios", "abertura", "proxima_reuniao",
    // "itens", "observacoes" (List<string>), "proposta" (List<ItemDaConferencia>, a original, intocada)
    public static Conferencia? Ler(string pasta, string arquivo = NomeDoArquivo);   // nulo se não existe, ilegível, ou versao > 1. Nunca lança.
    public static Conferencia? LerProposta(string pasta);    // Ler(pasta, ArquivoDaProposta)
    public static string? EstadoNoDisco(string pasta);       // "rascunho" | "conferida" | null. Nunca lança.
    public void Salvar(string pasta);                        // .tmp e troca
    public void SalvarComoProposta(string pasta);            // idem, em proposta-do-claude.json
    public AtaGerada ParaAta(NomesDosLados nomes);
    public string Markdown(NomesDosLados nomes);             // AtaDeFormalizacao.Markdown(ParaAta(nomes))
    public List<ItemDaConferencia> CopiaDosItens();          // cópia funda, por ida e volta no JSON
}
public sealed record NomesDosLados(string? Cliente, string? Casa)
{
    public static readonly NomesDosLados Nenhum = new(null, null);
}
```

As propriedades C# são o nome JSON em PascalCase (`escrita_por` → `EscritaPor`, `proxima_reuniao` → `ProximaReuniao`, `id_da_mensagem` → `IdDaMensagem`), todas com `get; set;`, texto com padrão `""` e listas com padrão `[]`.

O contexto de serialização segue o molde do `ReuniaoJson` de `DadosDaReuniao.cs`: `ConferenciaJsonBase` com `[JsonSerializable(typeof(Conferencia))]` e um `ConferenciaJson.Default` com `WriteIndented`, `UnsafeRelaxedJsonEscaping` e `WhenWritingNull`.

**As regras de `ParaAta`** (o que decide o `ata.md`):
- `Resumo` = `Abertura`. Itens com `Fora` não entram.
- resolvido → `Decisoes.Add($"{tema}: {texto}")`; com tema vazio, só o texto.
- pendente → `AcaoDaAta`: `Acao` = texto; `Responsavel` = o responsável, ou, vazio, o nome do lado (`nomes.Cliente` para `"cliente"`, `nomes.Casa` para `"nosso"`, nada para lado vazio); `Prazo` = `""` **enquanto o item tiver aviso de tipo `"prazo"` e `Revisao == AConferir`**, senão o prazo; `Lado` = o lado, ou `"nosso"` quando vazio.
- a confirmar → `AcaoDaAta` com `Acao = "Confirmar: " + texto`, sem responsável nem prazo (§5.2: nunca sob "Resolvidos").
- `ProximaReuniao` não vazia → `Secoes` com título `"Próxima reunião"`.
- `Observacoes` = as do registro.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class ConferenciaTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("conf-testes").FullName;
    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static readonly NomesDosLados Nomes = new("Acme Bebidas", "Casa");

    internal static Conferencia Exemplo() => new()
    {
        EscritaPor = "local", Modelo = "qwen3-4b", CriadaEm = "2026-10-21T12:10:00-03:00",
        ProximaReuniao = "quinta, 23/10",
        Itens =
        [
            new() { Id = "i1", Tipo = "resolvido", Tema = "Corte de volume", Texto = "500 caixas por mês.",
                    Falas = [new() { Inicio = 14, Fim = 18, Falante = "Bruno Teles", Texto = "Fechado, 500 caixas por mês." }] },
            new() { Id = "i2", Tipo = "pendente", Texto = "Margem por cliente", Responsavel = "Carla",
                    Prazo = "hoje", Lado = "cliente" },
            new() { Id = "i3", Tipo = "pendente", Texto = "Lista das lojas novas", Prazo = "28/10/2026",
                    Avisos = [new() { Tipo = "prazo", Texto = "O prazo \"28/10/2026\" não aparece perto das falas citadas." }] },
            new() { Id = "i4", Tipo = "a_confirmar", Texto = "O cálculo usa a média de três meses",
                    Motivo = "é uma explicação de como funciona hoje" },
            new() { Id = "i5", Tipo = "resolvido", Tema = "Rateio", Texto = "Seguimos com o de 2025.", Revisao = "retirado" },
        ],
    };

    [Fact]
    public void SalvaELeDeVoltaComAcentoLiteral()
    {
        Exemplo().Salvar(_pasta);
        string bruto = File.ReadAllText(Path.Combine(_pasta, "conferencia.json"));
        var lida = Conferencia.Ler(_pasta)!;

        Assert.Contains("\"versao\": 1", bruto);
        Assert.Contains("500 caixas por mês.", bruto);          // sem \u00ea
        Assert.Equal("rascunho", lida.Estado);
        Assert.Equal(5, lida.Itens.Count);
        Assert.Equal(14, lida.Itens[0].Falas[0].Inicio);
        Assert.False(File.Exists(Path.Combine(_pasta, "conferencia.json.tmp")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ isto não é json")]
    [InlineData("{\"versao\": 2, \"itens\": []}")]
    public void ArquivoIlegivelOuDeVersaoFuturaNaoELido(string conteudo)
    {
        File.WriteAllText(Path.Combine(_pasta, "conferencia.json"), conteudo);
        Assert.Null(Conferencia.Ler(_pasta));
        Assert.Null(Conferencia.EstadoNoDisco(_pasta));
    }

    [Fact]
    public void AAtaDoRascunhoTemDuasListasESoDuas()
    {
        string md = Exemplo().Markdown(Nomes).Replace("\r\n", "\n");

        Assert.Equal("""
            **Resolvidos em reunião**

            - Corte de volume: 500 caixas por mês.

            **Pendentes**

            - Margem por cliente: Carla, hoje
            - Lista das lojas novas
            - Confirmar: O cálculo usa a média de três meses

            Próxima reunião: quinta, 23/10

            """.Replace("\r\n", "\n"), md);
    }

    [Fact]
    public void OPrazoComAvisoSoEntraDepoisDeVisto()
    {
        var c = Exemplo();
        c.Itens[2].Revisao = "aprovado";
        Assert.Contains("- Lista das lojas novas: 28/10/2026", c.Markdown(Nomes));
    }

    [Fact]
    public void SemNomeAAtaDizOLadoPelaEmpresa()
    {
        var c = Exemplo();
        c.Itens[2].Lado = "nosso";
        c.Itens[1].Responsavel = "";
        string md = c.Markdown(Nomes);

        Assert.Contains("- Margem por cliente: Acme Bebidas, hoje", md);
        Assert.Contains("- Lista das lojas novas: Casa", md);
        Assert.DoesNotContain("Casa", Exemplo().Markdown(NomesDosLados.Nenhum));
    }

    [Fact]
    public void ACopiaDosItensNaoDivideNadaComOsOriginais()
    {
        var c = Exemplo();
        var copia = c.CopiaDosItens();
        copia[0].Falas[0].Texto = "outro";
        Assert.Equal("Fechado, 500 caixas por mês.", c.Itens[0].Falas[0].Texto);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ConferenciaTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar `Conferencia.cs`** conforme o bloco *Interfaces* e as regras de `ParaAta`. `Ler` e `EstadoNoDisco` envolvem tudo em `try`: a lista de reuniões passa por aqui.

- [ ] **Step 4: Rodar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ConferenciaTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Atas/Conferencia.cs app-net/Tests/ConferenciaTests.cs
git commit -m "feat(ata): o registro da conferência e a ata derivada dele"
```

---

### Task 4: O caminho local escreve o registro

§7. A formalização gerada pelo app passa a escrever o `conferencia.json`, e os três arquivos de hoje saem dele.

**Files:**
- Modify: `app-net/Nucleo/Atas/Conferencia.cs` (`DoLocal`)
- Create: `app-net/Nucleo/Atas/ArquivosDaAta.cs`
- Modify: `app-net/Nucleo/Atas/AtaDeFormalizacao.cs` (`Montar` delega; some o `Acao` privado)
- Modify: `app-net/Nucleo/Atas/GeracaoDeAta.cs`, `app-net/Nucleo/Atas/PromptDeAta.cs`, `app-net/Nucleo/ConfiguracoesDoApp.cs`
- Modify: `app-net/App/Ponte.cs` (`GerarAta`), `app-net/Cli/GeradorDeAta.cs`
- Test: `app-net/Tests/ConferenciaTests.cs`, `app-net/Tests/ConfiguracoesTests.cs`

**Interfaces:**
- Consumes: `Conferencia`, `NomesDosLados` (Tarefa 3); `Aviso`, `Turno.Fim` (Tarefa 2).
- Produces:
  - `static Conferencia Conferencia.DoLocal(AtaDeFormalizacao.Extracao conferida, AtaDeFormalizacao.Escrita escrita, IReadOnlyList<string> obs, IReadOnlyList<AtaDeFormalizacao.Turno> turnos, ContextoDaReuniao ctx, string modelo, DateTimeOffset agora)`
  - `static class ArquivosDaAta`: `void Derivar(string pasta, Conferencia c, NomesDosLados nomes, bool manterData)` e `void Arquivar(string pasta)`.
  - `ContextoDaReuniao.Casa` (`string`, padrão `""`); `NomesDosLados.De(ContextoDaReuniao ctx)`.
  - `ConfiguracoesDoApp.NomeDaEmpresa` (`"nome_da_empresa"`, padrão `""`) e `string NomeDaCasa()`.
  - `GeracaoDeAta.Contexto(..., string casa = "")`; `GerarAsync(..., string modeloDoRascunho = "")` e `GerarFormalizacaoAsync(..., string modeloDoRascunho = "")`, os dois como último parâmetro.

**As regras de `DoLocal`** (o `Montar` de hoje, produzindo itens):
- a ordem dos itens: os resolvidos que a escrita devolveu, os pendentes que ela devolveu, e por fim **o que ela deixou de fora**, com `Revisao = Retirado`, o texto da extração e `AvisoDoItem("escrita", "Juntado a outro item ou deixado de fora na escrita.")`. A observação de hoje continua sendo acrescentada. *(Decisão deste plano: o item que a escrita engoliu fica à mão para a pessoa trazer de volta, sem mudar o `ata.md`.)*
- o resolvido de tema "Próxima reunião" não vira item: vai para `ProximaReuniao`, com as regras de hoje (inclusive a observação "Próxima reunião fora da ata…").
- `Id` = `"i" + posição`, a partir de 1. `Origem = "modelo"`.
- `Falas`: uma por linha citada que existe em `turnos`, com `Inicio`, `Fim`, `Falante` e `Texto` do turno.
- `Avisos`: os da conferência automática, um para um.
- `Lado` do pendente: o da `Pessoa` de `ctx.Pessoas` cujo nome começa com o primeiro nome do responsável; senão `"cliente"` quando o primeiro nome do responsável aparece em `ctx.Cliente` (sem acento); senão `""`.
- `Proposta = CopiaDosItens()`; `EscritaPor = "local"`; `CriadaEm = agora.ToString("o")`.

**`ArquivosDaAta.Derivar`:** escreve `ata.md` (`.tmp` e troca), `ata.json` e `ata.observacoes.md` (ou o apaga, sem observações), nesta ordem. Com `manterData` e um `ata.md` já existente, repõe o `LastWriteTimeUtc` dele depois de escrever. **`Arquivar`:** copia `ata.md` para `ata.anterior.md` e move `conferencia.json` para `conferencia.anterior.json`, o que existir.

**`NomeDaCasa()`:** `NomeDaEmpresa` aparado, se tiver; senão o primeiro de `DominiosDaCasa` até o primeiro ponto, com a inicial maiúscula (`"casa.com.br"` → `"Casa"`); senão `""`.

- [ ] **Step 1: Escrever os testes**

Em `ConferenciaTests.cs`:

```csharp
private static (AtaDeFormalizacao.Extracao, IReadOnlyList<string>, IReadOnlyList<AtaDeFormalizacao.Turno>) Conferida()
{
    var turnos = AtaDeFormalizacao.Turnos(FormalizacaoDeExemplo.Segmentos());
    var (conf, obs) = AtaDeFormalizacao.Conferir(
        AtaDeFormalizacao.LerExtracao(FormalizacaoDeExemplo.Extracao), turnos, FormalizacaoDeExemplo.Contexto());
    return (conf, obs, turnos);
}

[Fact]
public void ORascunhoLocalGuardaAFalaDeCadaItem()
{
    var (conf, obs, turnos) = Conferida();
    var c = Conferencia.DoLocal(conf, AtaDeFormalizacao.LerEscrita(FormalizacaoDeExemplo.Escrita), obs, turnos,
        FormalizacaoDeExemplo.Contexto(), "qwen3-4b", DateTimeOffset.Parse("2026-10-21T12:10:00-03:00"));

    Assert.Equal(["i1", "i2", "i3", "i4"], c.Itens.Select(i => i.Id));
    Assert.Equal(("resolvido", "Corte de volume"), (c.Itens[0].Tipo, c.Itens[0].Tema));
    Assert.Equal((14d, 18d, "Bruno Teles"), (c.Itens[0].Falas[0].Inicio, c.Itens[0].Falas[0].Fim, c.Itens[0].Falas[0].Falante));
    Assert.Equal(("Carla", "hoje", "cliente"), (c.Itens[1].Responsavel, c.Itens[1].Prazo, c.Itens[1].Lado));
    Assert.Equal(("28/10/2026", ""), (c.Itens[2].Prazo, c.Itens[2].Lado));
    Assert.Contains(c.Itens[2].Avisos, a => a.Tipo == "prazo");
    // O que a escrita engoliu fica no registro, fora da ata, para a pessoa trazer de volta.
    Assert.Equal(("Cálculo", "retirado"), (c.Itens[3].Tema, c.Itens[3].Revisao));
    Assert.Contains(c.Itens[3].Avisos, a => a.Tipo == "escrita");
    Assert.Equal("quinta, 23/10", c.ProximaReuniao);
    Assert.Equal(4, c.Proposta.Count);
    Assert.Equal("local", c.EscritaPor);
}

[Fact]
public async Task GerarAFormalizacaoEscreveORegistroEDerivaOsArquivos()
{
    File.WriteAllText(Path.Combine(_pasta, "ata.md"), "ata velha\n");
    Exemplo().Salvar(_pasta);
    var dados = new ResultadoDaTranscricao { Duration = 54, Segments = FormalizacaoDeExemplo.Segmentos() };

    await GeracaoDeAta.GerarAsync(_pasta, ModelosDeAta.Buscar(AtaDeFormalizacao.Id)!, dados,
        FormalizacaoDeExemplo.Contexto() with { Casa = "Casa" }, (_, _, _, _, _) => throw new InvalidOperationException(),
        null, CancellationToken.None, FormatoDaAta.Classico, FormalizacaoDeExemplo.Abrir(), "qwen3-4b");

    var c = Conferencia.Ler(_pasta)!;
    Assert.Equal(("local", "qwen3-4b", "rascunho"), (c.EscritaPor, c.Modelo, c.Estado));
    Assert.Equal("ata velha\n", File.ReadAllText(Path.Combine(_pasta, "ata.anterior.md")));
    Assert.Equal(5, Conferencia.Ler(_pasta, Conferencia.ArquivoAnterior)!.Itens.Count);
    Assert.Equal(c.Markdown(new NomesDosLados("Acme Bebidas", "Casa")), File.ReadAllText(Path.Combine(_pasta, "ata.md")));
    Assert.Contains("Prazo fora da ata", File.ReadAllText(Path.Combine(_pasta, "ata.observacoes.md")));
    Assert.True(File.Exists(Path.Combine(_pasta, "ata.json")));
}

[Fact]
public async Task GerarOutroTipoArquivaAConferencia()
{
    Exemplo().Salvar(_pasta);
    var dados = new ResultadoDaTranscricao { Duration = 54, Segments = FormalizacaoDeExemplo.Segmentos() };
    GeracaoDeAta.Motor motor = (_, _, _, _, _) => Task.FromResult(new AtaGerada { Resumo = "x" });

    await GeracaoDeAta.GerarAsync(_pasta, ModelosDeAta.Buscar("cliente-update")!, dados,
        FormalizacaoDeExemplo.Contexto(), motor, null, CancellationToken.None);

    Assert.False(File.Exists(Path.Combine(_pasta, "conferencia.json")));
    Assert.True(File.Exists(Path.Combine(_pasta, "conferencia.anterior.json")));
}

[Fact]
public void DerivarComManterDataNaoMexeNaDataDoAtaMd()
{
    string md = Path.Combine(_pasta, "ata.md");
    File.WriteAllText(md, "x");
    var antes = new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc);
    File.SetLastWriteTimeUtc(md, antes);

    ArquivosDaAta.Derivar(_pasta, Exemplo(), Nomes, manterData: true);

    Assert.Equal(antes, File.GetLastWriteTimeUtc(md));
    Assert.StartsWith("**Resolvidos em reunião**", File.ReadAllText(md));
}
```

Em `ConfiguracoesTests.cs`:

```csharp
[Theory]
[InlineData("Casa Analytics", "casa.com.br", "Casa Analytics")]
[InlineData("  ", "casa.com.br", "Casa")]
[InlineData("", "", "")]
public void ONomeDaCasaVemDoCampoOuDoPrimeiroDominio(string nome, string dominio, string esperado)
{
    var cfg = new ConfiguracoesDoApp { NomeDaEmpresa = nome, DominiosDaCasa = dominio.Length > 0 ? [dominio] : [] };
    Assert.Equal(esperado, cfg.NomeDaCasa());
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ConferenciaTests|FullyQualifiedName~ConfiguracoesTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar**

- `DoLocal`, `ArquivosDaAta`, `NomeDaCasa`, `ContextoDaReuniao.Casa` e `NomesDosLados.De` conforme acima.
- `AtaDeFormalizacao.Montar(conferida, escrita, obs, ctx)` passa a ser uma linha: `Conferencia.DoLocal(conferida, escrita, obs, [], ctx, "", default).ParaAta(NomesDosLados.Nenhum)`. Os testes de `Montar` que já existem são a regressão.
- `GerarFormalizacaoAsync`: depois da escrita, `DoLocal(...)` com `DateTimeOffset.Now`; `ArquivosDaAta.Arquivar(pasta)`; `conferencia.Salvar(pasta)`; `ArquivosDaAta.Derivar(pasta, conferencia, NomesDosLados.De(ctx), manterData: false)`; devolve `conferencia.ParaAta(...)`. Os `File.WriteAllText` de hoje saem.
- `GerarAsync`, caminho comum: logo antes de escrever o `ata.md`, se existir `conferencia.json`, mova para `conferencia.anterior.json`. Depois do motor, e não antes: uma geração que falha não pode custar a conferência.
- `Ponte.GerarAta` e `Cli/GeradorDeAta.cs`: passam `cfg.NomeDaCasa()` ao `Contexto` e `cfg.ModeloDeAta` como `modeloDoRascunho`.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS, inclusive `AFormalizacaoLocalEscreveOMesmoAtaMd` (a referência não foi regravada).

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo app-net/App/Ponte.cs app-net/Cli/GeradorDeAta.cs app-net/Tests
git commit -m "feat(ata): a formalização local escreve o conferencia.json e deriva a ata dele"
```

---

### Task 5: As ações da pessoa

§6. Aprovar, corrigir, tirar, repor, acrescentar, concluir e marcar como enviada, sem tela.

**Files:**
- Create: `app-net/Nucleo/Atas/RevisaoDaAta.cs`
- Test: `app-net/Tests/RevisaoDaAtaTests.cs`

**Interfaces:**
- Consumes: `Conferencia`, `ItemDaConferencia`, `EnvioDaAta`, `NomesDosLados`, `ArquivosDaAta.Derivar`.
- Produces:

```csharp
public sealed class MudancaDaConferencia
{
    // "acao": aprovar | corrigir | tirar | repor | adicionar | cabecalho | concluir | enviada
    // "item" (id), "tipo", "tema", "texto", "lado", "responsavel", "prazo", "abertura", "proxima_reuniao": todos string?
}
public static class RevisaoDaAta
{
    public const string ArquivoDoEmail = "ata-email.md", EmailAnterior = "ata-email.anterior.md";
    public static IReadOnlyList<string> Bloqueios(Conferencia c);     // vazio = pode concluir
    public static void Aplicar(Conferencia c, MudancaDaConferencia m, DateTimeOffset agora);
    public static Conferencia AplicarNaPasta(string pasta, MudancaDaConferencia m, NomesDosLados nomes, DateTimeOffset agora);
}
```

**As regras de `Aplicar`** (todo erro é `InvalidOperationException` com a frase que a tela mostra):
- toda ação preenche `ComecouEm` se estiver nulo (§5: a primeira ação da pessoa);
- `item` que não existe: `"este item não existe mais — a ata mudou; abra a ata de novo"`;
- **aprovar:** `Revisao = Aprovado`. Num item a confirmar, recusa: `"um item a confirmar precisa de destino: resolvido, pendente ou fora"`;
- **corrigir:** cada campo não nulo da mudança substitui o do item; `tipo` só aceita `resolvido` ou `pendente`; `lado` só aceita `nosso`, `cliente` ou vazio; `Revisao = Corrigido`. Texto vazio é recusado;
- **tirar:** `Revisao = Retirado`. **repor:** só de um retirado, e volta para `AConferir`;
- **adicionar:** `tipo` resolvido ou pendente, texto obrigatório; `Id` = `"i" + (maior número + 1)`; `Origem = "manual"`, `Revisao = Aprovado`, sem falas;
- **cabecalho:** `abertura` e `proxima_reuniao` não nulos substituem os da ata;
- **concluir:** recusa com a primeira frase de `Bloqueios` se houver; senão todo item `AConferir` vira `Aprovado`, `Estado = Conferida`, `ConcluidaEm = agora`;
- **enviada:** só com `Estado == Conferida` (`"só a ata conferida pode ser marcada como enviada"`); acrescenta `EnvioDaAta { Em = agora, Modo = "manual" }`;
- **depois de conferida, editar não volta o estado** (§6).

**`Bloqueios`** percorre, na ordem, os itens que não estão fora, e em cada um aplica as três regras nesta ordem (um item pode render mais de uma frase):
1. tipo a confirmar: `"«{texto}» ainda está a confirmar"`;
2. `AConferir` com aviso: `"«{texto}» tem aviso e ainda não foi visto"`;
3. pendente com lado vazio: `"«{texto}» está sem lado"`.

**`AplicarNaPasta`:** lê o registro (sem ele: `"esta ata não tem conferência"`), aplica, **salva o registro e só então deriva** com `manterData: true`. Na ação `enviada`, depois de derivar, copia um `ata-email.md` existente para `ata-email.anterior.md` e escreve o `ata.md` recém-derivado em `ata-email.md`.

- [ ] **Step 1: Escrever os testes**

```csharp
public sealed class RevisaoDaAtaTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("revisao-testes").FullName;
    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static readonly NomesDosLados Nomes = new("Acme Bebidas", "Casa");
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-21T12:15:00-03:00");
    private static MudancaDaConferencia M(string acao, string? item = null) => new() { Acao = acao, Item = item };

    [Fact]
    public void APrimeiraAcaoMarcaOComecoEAprovarMarcaOItem()
    {
        var c = ConferenciaTests.Exemplo();
        RevisaoDaAta.Aplicar(c, M("aprovar", "i1"), T0);
        RevisaoDaAta.Aplicar(c, M("aprovar", "i2"), T0.AddMinutes(1));

        Assert.Equal(T0.ToString("o"), c.ComecouEm);
        Assert.Equal("aprovado", c.Itens[0].Revisao);
    }

    [Fact]
    public void ItemQueNaoExisteMaisERecusadoENaoCaiEmOutro()
    {
        var c = ConferenciaTests.Exemplo();
        var e = Assert.Throws<InvalidOperationException>(() => RevisaoDaAta.Aplicar(c, M("tirar", "i9"), T0));

        Assert.Contains("não existe mais", e.Message);
        Assert.All(c.Itens.Take(4), i => Assert.Equal("a_conferir", i.Revisao));
    }

    [Fact]
    public void AConfirmarNaoSeAprovaGanhaDestino()
    {
        var c = ConferenciaTests.Exemplo();
        Assert.Throws<InvalidOperationException>(() => RevisaoDaAta.Aplicar(c, M("aprovar", "i4"), T0));

        RevisaoDaAta.Aplicar(c, new() { Acao = "corrigir", Item = "i4", Tipo = "pendente",
            Texto = "Confirmar a média de três meses", Lado = "cliente" }, T0);

        Assert.Equal(("pendente", "corrigido", "cliente"), (c.Itens[3].Tipo, c.Itens[3].Revisao, c.Itens[3].Lado));
    }

    [Fact]
    public void ConcluirPedeOsSuspeitosUmAUmEAprovaORestoEmBloco()
    {
        var c = ConferenciaTests.Exemplo();
        Assert.Equal(3, RevisaoDaAta.Bloqueios(c).Count);   // i3 com aviso, i3 sem lado, i4 a confirmar
        Assert.Throws<InvalidOperationException>(() => RevisaoDaAta.Aplicar(c, M("concluir"), T0));

        RevisaoDaAta.Aplicar(c, new() { Acao = "corrigir", Item = "i3", Lado = "nosso", Prazo = "" }, T0);
        RevisaoDaAta.Aplicar(c, M("tirar", "i4"), T0);
        RevisaoDaAta.Aplicar(c, M("concluir"), T0.AddMinutes(4));

        Assert.Equal("conferida", c.Estado);
        Assert.Equal(T0.AddMinutes(4).ToString("o"), c.ConcluidaEm);
        Assert.Equal(["aprovado", "aprovado", "corrigido", "retirado", "retirado"], c.Itens.Select(i => i.Revisao));
    }

    [Fact]
    public void AdicionarEntraAprovadoSemFalaEComIdNovo()
    {
        var c = ConferenciaTests.Exemplo();
        RevisaoDaAta.Aplicar(c, new() { Acao = "adicionar", Tipo = "pendente", Texto = "Enviar o material apresentado",
            Lado = "nosso", Responsavel = "Bruno" }, T0);

        var novo = c.Itens[^1];
        Assert.Equal(("i6", "manual", "aprovado"), (novo.Id, novo.Origem, novo.Revisao));
        Assert.Empty(novo.Falas);
        Assert.Empty(c.Proposta);   // a ação não escreve na proposta original
    }

    [Fact]
    public void EditarDepoisDeConferidaNaoVoltaParaRascunho()
    {
        var c = ConferenciaTests.Exemplo();
        c.Estado = "conferida";
        RevisaoDaAta.Aplicar(c, new() { Acao = "corrigir", Item = "i1", Texto = "600 caixas por mês." }, T0);
        Assert.Equal("conferida", c.Estado);
    }

    [Fact]
    public void NaPastaAAcaoGravaDerivaENaoMexeNaData()
    {
        var c = ConferenciaTests.Exemplo();
        c.Salvar(_pasta);
        ArquivosDaAta.Derivar(_pasta, c, Nomes, manterData: false);
        string md = Path.Combine(_pasta, "ata.md");
        var antes = new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(md, antes);

        RevisaoDaAta.AplicarNaPasta(_pasta, M("aprovar", "i3"), Nomes, T0);

        Assert.Contains("- Lista das lojas novas: 28/10/2026", File.ReadAllText(md));
        Assert.Equal(antes, File.GetLastWriteTimeUtc(md));
        Assert.Equal("aprovado", Conferencia.Ler(_pasta)!.Itens[2].Revisao);
    }

    [Fact]
    public void SeOAtaMdNaoPodeSerEscritoAAcaoFicaNoRegistro()
    {
        ConferenciaTests.Exemplo().Salvar(_pasta);
        Directory.CreateDirectory(Path.Combine(_pasta, "ata.md"));   // um ata.md que não se escreve

        Assert.ThrowsAny<Exception>(() => RevisaoDaAta.AplicarNaPasta(_pasta, M("aprovar", "i1"), Nomes, T0));
        Assert.Equal("aprovado", Conferencia.Ler(_pasta)!.Itens[0].Revisao);
    }

    [Fact]
    public void EnviadaSoDepoisDeConferidaEGuardaOTextoDaquelaHora()
    {
        var c = ConferenciaTests.Exemplo();
        c.Salvar(_pasta);
        Assert.Throws<InvalidOperationException>(() => RevisaoDaAta.AplicarNaPasta(_pasta, M("enviada"), Nomes, T0));

        c.Estado = "conferida";
        c.Salvar(_pasta);
        File.WriteAllText(Path.Combine(_pasta, "ata-email.md"), "a que foi antes\n");
        var depois = RevisaoDaAta.AplicarNaPasta(_pasta, M("enviada"), Nomes, T0);

        Assert.Equal(("manual", T0.ToString("o")), (depois.Envios[0].Modo, depois.Envios[0].Em));
        Assert.Equal(File.ReadAllText(Path.Combine(_pasta, "ata.md")), File.ReadAllText(Path.Combine(_pasta, "ata-email.md")));
        Assert.Equal("a que foi antes\n", File.ReadAllText(Path.Combine(_pasta, "ata-email.anterior.md")));
    }
}
```

`ConferenciaTests.Exemplo()` já é `internal static` (Tarefa 3), e não tem proposta.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~RevisaoDaAtaTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar `RevisaoDaAta.cs`** conforme as regras. `MudancaDaConferencia` leva `[JsonPropertyName]` em cada campo: a ponte a desserializa dentro do `Pedido` (Tarefa 7).

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Atas/RevisaoDaAta.cs app-net/Tests/RevisaoDaAtaTests.cs
git commit -m "feat(ata): aprovar, corrigir, tirar, acrescentar, concluir e marcar como enviada"
```

---

### Task 6: A proposta do Claude no disco

§8.2, do lado do app: montar a proposta a partir de itens conferidos, e o gesto que a transforma em rascunho.

**Files:**
- Modify: `app-net/Nucleo/Atas/Conferencia.cs` (`DoClaude`), `app-net/Nucleo/Atas/RevisaoDaAta.cs`
- Test: `app-net/Tests/RevisaoDaAtaTests.cs`

**Interfaces:**
- Produces:
  - `static Conferencia Conferencia.DoClaude(AtaDeFormalizacao.Extracao conferida, IReadOnlyList<string> obs, IReadOnlyList<AtaDeFormalizacao.Turno> turnos, string abertura, DateTimeOffset agora)`
  - `static Conferencia RevisaoDaAta.UsarProposta(string pasta, NomesDosLados nomes)`
  - `static void RevisaoDaAta.DescartarProposta(string pasta)`

**As regras de `DoClaude`:** os itens na ordem resolvidos, pendentes, a confirmar, com os textos como vieram; o resolvido de tema "Próxima reunião" (o primeiro) vai para `ProximaReuniao` e não vira item; `Lado` do pendente é o que veio, se for `nosso` ou `cliente`, senão vazio; `Motivo` no a confirmar; falas, avisos, ids e `Proposta` como no `DoLocal`; `EscritaPor = "claude"`, `Modelo = ""`.

**`UsarProposta`:** sem proposta, `"não há proposta do Claude para esta reunião"`. Com ela: `ArquivosDaAta.Arquivar(pasta)`, salva a proposta como `conferencia.json`, `Derivar(..., manterData: false)`, apaga `proposta-do-claude.json`. **`DescartarProposta`** apaga o arquivo, e não reclama se ele não existe.

- [ ] **Step 1: Escrever os testes**

```csharp
[Fact]
public void APropostaDoClaudeTrazOsTresGruposEOMotivo()
{
    var turnos = AtaDeFormalizacao.Turnos(FormalizacaoDeExemplo.Segmentos());
    var ext = new AtaDeFormalizacao.Extracao(
        [new("Corte de volume", "500 caixas por mês.", [3]), new("Próxima reunião", "quinta, 23/10", [10])],
        [new("Margem por cliente", "Carla", "hoje", [4, 7]) { Lado = "cliente" },
         new("Lista das lojas novas", "", "", [9]) { Lado = "talvez" }])
    {
        AConfirmar = [new("O cálculo usa a média de três meses", "é uma explicação de como funciona hoje", [8])],
    };

    var c = Conferencia.DoClaude(ext, [], turnos, "", T0);

    Assert.Equal(["resolvido", "pendente", "pendente", "a_confirmar"], c.Itens.Select(i => i.Tipo));
    Assert.Equal(2, c.Itens[1].Falas.Count);
    Assert.Equal("", c.Itens[2].Lado);                       // valor de fora não entra
    Assert.Equal("é uma explicação de como funciona hoje", c.Itens[3].Motivo);
    Assert.Equal(("quinta, 23/10", "claude"), (c.ProximaReuniao, c.EscritaPor));
}

[Fact]
public void UsarAPropostaArquivaOQueHaviaESomeComOArquivo()
{
    var atual = ConferenciaTests.Exemplo();
    atual.Estado = "conferida";
    atual.Salvar(_pasta);
    ArquivosDaAta.Derivar(_pasta, atual, Nomes, manterData: false);
    var proposta = ConferenciaTests.Exemplo();
    proposta.EscritaPor = "claude";
    proposta.Itens.RemoveAt(4);
    proposta.SalvarComoProposta(_pasta);

    var usada = RevisaoDaAta.UsarProposta(_pasta, Nomes);

    Assert.Equal(("claude", "rascunho", 4), (usada.EscritaPor, usada.Estado, usada.Itens.Count));
    Assert.Equal("conferida", Conferencia.Ler(_pasta, Conferencia.ArquivoAnterior)!.Estado);
    Assert.True(File.Exists(Path.Combine(_pasta, "ata.anterior.md")));
    Assert.False(File.Exists(Path.Combine(_pasta, Conferencia.ArquivoDaProposta)));
}

[Fact]
public void SemPropostaUsarRecusaEDescartarNaoReclama()
{
    Assert.Throws<InvalidOperationException>(() => RevisaoDaAta.UsarProposta(_pasta, Nomes));
    RevisaoDaAta.DescartarProposta(_pasta);
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~RevisaoDaAtaTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar** `DoClaude`, `UsarProposta` e `DescartarProposta`.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Atas app-net/Tests/RevisaoDaAtaTests.cs
git commit -m "feat(ata): a proposta do Claude vira rascunho por um gesto, e arquiva o que havia"
```

---

### Task 7: A ponte

A página passa a receber o registro e a mandar as ações. A `Ponte` é interna ao executável e a suíte não a alcança: ela só repassa ao `Nucleo`, e o que se testa aqui é o que o `Nucleo` devolve.

**Files:**
- Modify: `app-net/Nucleo/EstadoDaAta.cs`
- Modify: `app-net/App/Ponte.cs`, `app-net/App/JanelaDoApp.cs`, `app-net/App/Nativo/Win32.cs`
- Test: `app-net/Tests/EstadoDaAtaTests.cs`

**Interfaces:**
- Produces, para a página:
  - `EstadoDaAta.Conferencia` (`string?`, `init`): `"rascunho"`, `"conferida"` ou nulo. Propriedade com `init` e não parâmetro posicional, para não quebrar quem constrói o registro.
  - na resposta de `ata`: `conferencia` (o `Conferencia` inteiro, ou ausente) e `proposta` (`{ "itens": n, "criada_em": "…" }`, ou ausente);
  - op `conferencia` com `{ gravacao, mudanca }` (`mudanca` é a `MudancaDaConferencia`): devolve `ata`, `conferencia`, `ata_velha`, `observacoes`, como a op `ata`;
  - ops `usar-proposta` e `descartar-proposta` com `{ gravacao }`: devolvem o mesmo que `ata`;
  - em cada gravação da lista: `ata_estado` (o `EstadoDaAta.Conferencia`);
  - evento empurrado `{ id: 0, tipo: "janela" }` quando a janela do app é ativada.

- [ ] **Step 1: Escrever o teste do estado**

```csharp
[Fact]
public void OEstadoDaAtaDizSeAConferenciaERascunhoOuConferida()
{
    string pasta = Directory.CreateTempSubdirectory("estado-conf").FullName;
    try
    {
        File.WriteAllText(Path.Combine(pasta, "ata.md"), "**Pendentes**\n\n- x\n");
        Assert.Null(EstadoDaAta.Ler(pasta).Conferencia);

        var c = ConferenciaTests.Exemplo();
        c.Salvar(pasta);
        Assert.Equal("rascunho", EstadoDaAta.Ler(pasta).Conferencia);

        File.WriteAllText(Path.Combine(pasta, "conferencia.json"), "{ quebrado");
        Assert.True(EstadoDaAta.Ler(pasta).Existe);          // a reunião não some da lista
        Assert.Null(EstadoDaAta.Ler(pasta).Conferencia);
    }
    finally { Directory.Delete(pasta, recursive: true); }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~EstadoDaAtaTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implementar**

- `EstadoDaAta.Ler` preenche `Conferencia` com `Atas.Conferencia.EstadoNoDisco(pasta)` nos dois retornos em que a ata existe.
- `Ponte.cs`:
  - `Pedido` ganha `[JsonPropertyName("mudanca")] public MudancaDaConferencia? Mudanca`;
  - `Resposta` ganha `conferencia` (`Conferencia?`) e `proposta` (uma classe pequena `PropostaResumo` com `itens` e `criada_em`);
  - um método privado `RespostaDaAta(int id, string pasta)` monta a resposta de `ata` (o que a op faz hoje, mais os dois campos), e as quatro ops o usam;
  - `case "conferencia"`: `RevisaoDaAta.AplicarNaPasta(onde, p.Mudanca ?? throw …, Nomes(onde), DateTimeOffset.Now)` e responde com `RespostaDaAta`;
  - `case "usar-proposta"` e `case "descartar-proposta"`: idem com `UsarProposta` e `DescartarProposta`;
  - `Nomes(pasta)` = `new NomesDosLados(DadosDaReuniao.Ler(pasta).Cliente, ConfiguracoesDoApp.Carregar().NomeDaCasa())`;
  - `GravacaoResumo` ganha `ata_estado`;
  - um método público `JanelaAtivada()` que faz `Responder(new Resposta { Id = 0, Tipo = "janela" })`.
- `Win32.cs`: `internal const uint WM_ACTIVATE = 0x0006;`. `JanelaDoApp.Processar`: em `WM_ACTIVATE` com a palavra baixa de `wParam` diferente de zero, chama `_ponte?.JanelaAtivada()` e **deixa a mensagem seguir** para o tratamento padrão.

- [ ] **Step 4: Rodar a suíte e compilar o app**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj && dotnet build app-net/App -c Release 2>&1 | tail -3`
Expected: PASS, e `0 Erro(s)`.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/EstadoDaAta.cs app-net/App app-net/Tests/EstadoDaAtaTests.cs
git commit -m "feat(ponte): as operações da conferência e o aviso de janela ativada"
```

---

### Task 8: As regras da tela

O que a tela calcula, sem DOM, provado fora do navegador.

**Files:**
- Create: `app-net/App/web/conferencia-regras.js`
- Modify: `app-net/App/web/ata-regras.js` (`eFormalizacaoAntiga`)
- Test: `tools/web/conferencia-regras.test.mjs`, `tools/web/ata-regras.test.mjs`

**Interfaces:**
- Consumes: o `conferencia` da resposta de `ata` (Tarefa 7), com os nomes JSON da Tarefa 3.
- Produces, em `conferencia-regras.js`:
  - `GRUPOS`: `[["resolvido", "Resolvidos"], ["pendente", "Pendentes"], ["a_confirmar", "A confirmar"]]`
  - `visto(item)`, `fora(item)`, `pedeAtencao(item)` (não visto, e com aviso ou a confirmar)
  - `contagens(conferencia)` → `{ itens, vistos, atencao, faltam }`, sobre os que não estão fora
  - `filtrar(itens, filtro)`, com `filtro` em `"todos" | "atencao" | "faltam"`; os retirados só aparecem em `"todos"`
  - `bloqueios(conferencia)` → as mesmas frases de `RevisaoDaAta.Bloqueios`
  - `emBloco(conferencia)` → quantos itens o *Concluir* aprova de uma vez
  - `estadoDoTopo(conferencia)` → a frase do topo
  - `linhaDoItem(item, nomes)` → o texto como vai sair no `ata.md`, com `nomes = { cliente, casa }`
  - `resumoDaFala(texto, limite = 280)` → `{ texto, cortado }`, cortando na palavra
  - `minutosDaConferencia(conferencia)` → número, ou nulo sem começo ou sem fim
- Produces, em `ata-regras.js`: `eFormalizacaoAntiga(markdown)`: verdadeiro quando o texto não tem linha começando com `#` e tem `**Resolvidos em reunião**` ou `**Pendentes**`.

- [ ] **Step 1: Escrever os testes** em `tools/web/conferencia-regras.test.mjs`

```js
import { test } from "node:test";
import assert from "node:assert/strict";

const R = await import(new URL("../../app-net/App/web/conferencia-regras.js", import.meta.url));

const item = (id, tipo, extra = {}) => ({
  id, tipo, tema: "", texto: `texto ${id}`, lado: "", responsavel: "", prazo: "", motivo: "",
  falas: [], avisos: [], revisao: "a_conferir", origem: "modelo", ...extra,
});

const EXEMPLO = {
  escrita_por: "claude", estado: "rascunho", envios: [],
  itens: [
    item("i1", "resolvido", { tema: "Corte de volume", texto: "500 caixas por mês.", revisao: "aprovado" }),
    item("i2", "pendente", { texto: "Margem por cliente", responsavel: "Carla", prazo: "hoje", lado: "cliente" }),
    item("i3", "pendente", { texto: "Lista das lojas novas", prazo: "28/10/2026",
                             avisos: [{ tipo: "prazo", texto: "não foi dito" }] }),
    item("i4", "a_confirmar", { texto: "O cálculo usa a média de três meses" }),
    item("i5", "resolvido", { revisao: "retirado" }),
  ],
};

test("as contagens deixam o retirado de fora", () => {
  assert.deepEqual(R.contagens(EXEMPLO), { itens: 4, vistos: 1, atencao: 2, faltam: 3 });
});

test("o filtro de atenção traz só o que tem aviso ou está a confirmar, e ainda não foi visto", () => {
  assert.deepEqual(R.filtrar(EXEMPLO.itens, "atencao").map((i) => i.id), ["i3", "i4"]);
  assert.deepEqual(R.filtrar(EXEMPLO.itens, "faltam").map((i) => i.id), ["i2", "i3", "i4"]);
  assert.equal(R.filtrar(EXEMPLO.itens, "todos").length, 5);
});

test("os bloqueios são os do núcleo, e o resto vai em bloco", () => {
  assert.deepEqual(R.bloqueios(EXEMPLO), [
    "«Lista das lojas novas» tem aviso e ainda não foi visto",
    "«Lista das lojas novas» está sem lado",
    "«O cálculo usa a média de três meses» ainda está a confirmar",
  ]);
  assert.equal(R.emBloco(EXEMPLO), 1);   // só o i2: sem aviso, não visto
});

test("o topo diz quem escreveu e o que falta", () => {
  assert.equal(R.estadoDoTopo(EXEMPLO),
    "Rascunho · escrito pelo Claude · 4 itens · 1 visto · 2 pedem a sua atenção");
  assert.equal(R.estadoDoTopo({ ...EXEMPLO, escrita_por: "local", estado: "conferida",
    concluida_em: "2026-10-21T12:19:00-03:00" }), "Conferida em 21/10 · 4 itens");
  assert.equal(R.estadoDoTopo({ ...EXEMPLO, estado: "conferida", concluida_em: "2026-10-21T12:19:00-03:00",
    envios: [{ em: "2026-10-21T14:02:00-03:00", modo: "manual" }] }), "Enviada em 21/10 · 4 itens");
});

test("a linha do item é a do ata.md", () => {
  const nomes = { cliente: "Acme Bebidas", casa: "Casa" };
  assert.equal(R.linhaDoItem(EXEMPLO.itens[0], nomes), "Corte de volume: 500 caixas por mês.");
  assert.equal(R.linhaDoItem(EXEMPLO.itens[1], nomes), "Margem por cliente: Carla, hoje");
  assert.equal(R.linhaDoItem(EXEMPLO.itens[2], nomes), "Lista das lojas novas");   // prazo com aviso, não visto
  assert.equal(R.linhaDoItem({ ...EXEMPLO.itens[2], lado: "nosso", revisao: "aprovado" }, nomes),
    "Lista das lojas novas: Casa, 28/10/2026");
  assert.equal(R.linhaDoItem(EXEMPLO.itens[3], nomes), "Confirmar: O cálculo usa a média de três meses");
});

test("a fala longa é cortada na palavra, e a curta não", () => {
  const longa = "palavra ".repeat(100).trim();
  const r = R.resumoDaFala(longa, 280);
  assert.equal(r.cortado, true);
  assert.ok(r.texto.length <= 281 && r.texto.endsWith("…") && !r.texto.endsWith(" …"));
  assert.deepEqual(R.resumoDaFala("curta"), { texto: "curta", cortado: false });
});

test("os minutos saem do começo e do fim, ou não saem", () => {
  assert.equal(R.minutosDaConferencia({ comecou_em: "2026-10-21T12:15:00-03:00",
    concluida_em: "2026-10-21T12:19:30-03:00" }), 4.5);
  assert.equal(R.minutosDaConferencia({ comecou_em: "2026-10-21T12:15:00-03:00" }), null);
});
```

Em `tools/web/ata-regras.test.mjs`:

```js
test("a formalização de antes da conferência é reconhecida pelo formato", () => {
  assert.equal(R.eFormalizacaoAntiga("**Resolvidos em reunião**\n\n- a\n"), true);
  assert.equal(R.eFormalizacaoAntiga("**Pendentes**\n\n- a\n"), true);
  assert.equal(R.eFormalizacaoAntiga("# Ata\n\n## Pendências\n- [ ] a\n"), false);
  assert.equal(R.eFormalizacaoAntiga(""), false);
});
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `node --test tools/web/conferencia-regras.test.mjs tools/web/ata-regras.test.mjs`
Expected: FAIL (`Cannot find module`, e `eFormalizacaoAntiga is not a function`).

- [ ] **Step 3: Implementar** `conferencia-regras.js` e `eFormalizacaoAntiga`. A data do topo é dia e mês do texto ISO (`"2026-10-21T…"` → `"21/10"`), lida do texto e não por `new Date`, para o fuso de quem roda o teste não entrar. "1 visto" no singular, "N vistos" no plural; "1 pede a sua atenção" e "N pedem a sua atenção"; sem itens de atenção, o trecho some.

- [ ] **Step 4: Rodar**

Run: `node --test tools/web/*.test.mjs`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/App/web/conferencia-regras.js app-net/App/web/ata-regras.js tools/web
git commit -m "feat(web): as regras da tela de conferência"
```

---

### Task 9: A tela de conferência

§6. O comportamento é o do protótipo que o dono aprovou em 11/11/2026 (fora do repositório; o §6 o descreve). O desenho fino usa os componentes e os tokens do AA Design System que a aba Ata já usa.

**Files:**
- Create: `app-net/App/web/conferencia.js`
- Modify: `app-net/App/web/atas.js`, `app-net/App/web/reuniao.js`, `app-net/App/web/app.css`

**Interfaces:**
- Consumes: as ops da Tarefa 7; `conferencia-regras.js` e `eFormalizacaoAntiga` (Tarefa 8); `ouvir(g, segundos)` de `tocador.js`; `botoesDaAta` de `atas.js` (passa a ser exportado); `assinar("janela", fn)` de `ponte.js`; `confirmar` de `pecas.js`.
- Produces: `montarConferencia(corpo, g, resposta, { recarregar, aoMudar })` em `conferencia.js`. `aoMudar(conferencia)` é chamado a cada resposta do núcleo, para o selo da aba. `montarAta` passa a aceitar `{ aoContar, aoMudarEstado }`.

- [ ] **Step 1: `atas.js` escolhe a tela**

Em `carregarAta`, com a resposta `r` de `ata`:
- `r.conferencia` existe → `montarConferencia(corpo, g, r, …)` no lugar do texto renderizado; o botão do topo continua "Refazer ata";
- `!r.ata && r.proposta` → `pedir("usar-proposta", …)` e desenha o que voltar (§8.2: reunião sem ata, a proposta vira o rascunho);
- `r.ata && r.proposta` → um aviso acima da ata: "O Claude propôs uma ata para esta reunião (N itens; a atual tem M)." com *Usar a proposta* e *Descartar*. Sobre uma ata conferida, *Usar a proposta* pede antes a confirmação "Esta ata já foi conferida. Usar a proposta guarda a conferência atual como anterior e começa outra.";
- sem `conferencia` e com `eFormalizacaoAntiga(r.ata)` → a ata como hoje, e um aviso: "Esta ata é anterior à conferência item por item. Para conferir, refaça a ata.";
- senão, como hoje.

O clique em *Gerar ata*/*Refazer ata* sobre uma ata com `conferencia.estado === "conferida"` pede a confirmação "Refazer joga fora a conferência desta ata. A atual fica guardada como anterior." antes de pedir `gerar-ata`.

A aba procura a proposta de novo quando a janela volta: `assinar("janela", …)` chama `carregar()` se o painel está conectado e visível e não há geração em curso, e cancela a assinatura quando o painel sai do DOM.

- [ ] **Step 2: `conferencia.js`**

Uma função por parte, todas com nós:
- **o topo:** `estadoDoTopo`; os botões de `botoesDaAta` (Copiar e Exportar leem `r.ata`, que é o `ata.md` limpo, sem marca de rascunho); *Concluir conferência* (some na conferida); *Marcar como enviada* (só na conferida); a barra de andamento com `vistos / itens`; os três filtros com as contagens;
- **os grupos** de `GRUPOS`, cada um com os itens de `filtrar` e um *Adicionar item* (não no grupo "A confirmar");
- **o item:** `linhaDoItem`; o motivo do a confirmar, ou "Não há fala em que o outro lado concorda." quando vazio; os avisos; cada fala com falante, minuto (`relogioDoTocador` de `reuniao-regras.js`), o texto por `resumoDaFala` com "ver tudo" quando cortado, e *Ouvir* (`ouvir(g, fala.inicio)`); *Aprovar* (não no a confirmar), *Corrigir*, *Tirar*; no retirado, só *Trazer de volta*; o item manual diz "acrescentado à mão";
- **corrigir** abre no lugar do item: texto, tema (resolvido), lado, responsável e prazo (pendente), e o grupo (Resolvido ou Pendente). No a confirmar o grupo é obrigatório. Salvar manda `corrigir`; cancelar não manda nada;
- **concluir:** com `bloqueios` não vazio, mostra as frases e leva ao primeiro item; senão confirma "Aprovar os N itens que você ainda não abriu e concluir?" (sem a pergunta quando N é zero) e manda `concluir`;
- **toda ação** manda `pedir("conferencia", { gravacao, mudanca })`, redesenha com o que voltou e chama `aoMudar`; no erro, mostra a frase acima da lista e chama `recarregar()` quando a frase contém "não existe mais";
- **depois de concluir**, o topo mostra `minutosDaConferencia` ("conferida em 4 min").

- [ ] **Step 3: o selo da aba** em `reuniao.js`

O rótulo da aba Ata passa a ser `"rascunho"` quando `g.ata_estado === "rascunho"`, e a contagem de pendências de hoje nos outros casos. `montarAta` recebe `aoMudarEstado: (estado) => contar(botao, estado === "rascunho" ? "rascunho" : null)`, chamado pelo `aoMudar` da conferência.

- [ ] **Step 4: o estilo** em `app.css`

Classes `conferencia`, `conferencia__topo`, `conferencia__andamento`, `conferencia__filtros`, `conferencia__grupo`, `conferencia__item` (com os modificadores `--visto`, `--atencao`, `--retirado`), `conferencia__fala`, `conferencia__aviso`. Só tokens do tema (`assets/ds/colors_and_type.css`), nenhuma cor escrita à mão: a tela tem que ficar certa no claro e no escuro.

- [ ] **Step 5: Ver na interface do disco**

Run: `node --test tools/web/*.test.mjs && dotnet build app-net/App -c Release 2>&1 | tail -3`
Expected: PASS e `0 Erro(s)`.

Depois, numa **cópia** de uma pasta de gravação com `conferencia.json` (gere a cópia com o `Sidecar.exe --ata` sobre uma pasta de teste, ou escreva o JSON do `Exemplo()` à mão), abra o app com `PulseMeet.exe --web <app-net\App\web>` e confira nos dois temas: os três grupos, *Ouvir* toca no ponto, corrigir e tirar redesenham, concluir pede os suspeitos. Se o app instalado não puder ser trocado agora, esta conferência visual fica para a Tarefa 16 e isso é dito no relato da tarefa.

- [ ] **Step 6: Commit**

```bash
git add app-net/App/web
git commit -m "feat(web): a tela de conferência na aba Ata"
```

---

### Task 10: MCP — `preparar_ata`

§8.1. Tudo o que escrever a ata pede, numa chamada.

**Files:**
- Modify: `app-net/Nucleo/Claude/FerramentasDoMcp.cs`, `Acervo.cs`, `ServidorMcp.cs`
- Test: `app-net/Tests/IntegracaoComClaudeTests.cs`

**Interfaces:**
- Produces:
  - `static string? Acervo.VersaoDaTranscricao(ReuniaoNoAcervo r)`: o `LastWriteTimeUtc` do `transcricao.json` em `"o"`, ou nulo.
  - `public delegate ContextoDaReuniao FonteDoContexto(string pasta, ResultadoDaTranscricao dados);`
  - `FerramentasDoMcp(Acervo acervo, string? caminhoDaReuniaoAberta = null, FonteDoContexto? contexto = null)`; o padrão é `GeracaoDeAta.Contexto(pasta, dados, cfg.DominiosDaCasa, new Projetos(), cfg.NomeDaCasa())` com `cfg = ConfiguracoesDoApp.Carregar()`. `ServidorMcp` ganha o mesmo terceiro parâmetro opcional e o repassa.
  - ferramenta `preparar_ata` com `reuniao` (obrigatório) e `a_partir_de` (inteiro, opcional).

**O texto que ela devolve**, nesta ordem:
1. `Acervo.Cabecalho(r, dados)`;
2. `versao: <VersaoDaTranscricao>` numa linha só, e a frase "Mande esta versão de volta em propor_ata.";
3. `## Pessoas`, uma por linha com o lado ("da casa", "do cliente", ou "lado desconhecido"), e os nomes dos dois lados (cliente e casa);
4. `## Vocabulário do projeto`, quando houver;
5. `## Notas de quem gravou`, quando houver;
6. `## Regras deste tipo de ata`, com o texto de `ModelosDeAta.Buscar(AtaDeFormalizacao.Id)`;
7. `## Transcrição`, com `AtaDeFormalizacao.TextoNumerado(turnos)` começando na linha logo abaixo do título, sem linha em branco entre os dois.

O teto é `Acervo.TranscricaoMaxima` sobre a parte 7: ao passar, a saída para numa fala inteira e termina com `[a transcrição continua: chame preparar_ata de novo com a_partir_de = N]`. Com `a_partir_de`, saem só as partes 1, 2 e 7, da fala N em diante. Reunião sem transcrição é `ErroDeUso`.

- [ ] **Step 1: Escrever os testes**

No construtor de `ServidorMcpTests`, grave mais uma reunião, `"2026-10-21_11-30-00"`, título "Semanal do Portal de Pedidos", cliente "Aurora Bebidas", projeto "Portal de Pedidos", com `FormalizacaoDeExemplo.Segmentos()`, e passe ao `ServidorMcp` o contexto falso `(_, _) => FormalizacaoDeExemplo.Contexto() with { Casa = "Casa" }`. O cliente do acervo é "Aurora", e não "Acme", de propósito: os testes que já existem filtram por "acme", e o filtro casa por pedaço. Os nomes que aparecem no texto vêm do contexto falso. Os testes que contam todas as reuniões ou todos os clientes do acervo de teste sobem uma unidade.

```csharp
[Fact]
public void PrepararAtaDevolveAsFalasNumeradasAVersaoEAsRegras()
{
    string texto = Ferramenta("preparar_ata", new JsonObject { ["reuniao"] = "2026-10-21_11-30-00" }, out bool erro);

    Assert.False(erro);
    Assert.Matches(@"versao: \d{4}-\d{2}-\d{2}T", texto);
    Assert.Contains("[L3 00:14] Bruno Teles: Fechado, 500 caixas por mês.", texto);
    Assert.Contains("Carla Nunes — do cliente", texto);
    Assert.Contains("## Regras deste tipo de ata", texto);
}

[Fact]
public void PrepararAtaLongaParaNumaFalaEDizDeOndeContinuar()
{
    // 4.000 falas de 60 caracteres passam do teto de 150 mil.
    var longa = Enumerable.Range(0, 4000).Select(i =>
        AcervoTests.S(i * 3, i % 2 == 0 ? "Bruno Teles" : "Carla Nunes", new string('a', 60))).ToList();
    AcervoTests.Gravar(Path.Combine(_raiz, "acervo"), "2026-10-22_09-00-00", "Longa", segmentos: longa);

    string texto = Ferramenta("preparar_ata", new JsonObject { ["reuniao"] = "2026-10-22_09-00-00" }, out _);
    var m = System.Text.RegularExpressions.Regex.Match(texto, @"a_partir_de = (\d+)\]");
    Assert.True(m.Success);

    string resto = Ferramenta("preparar_ata", new JsonObject
        { ["reuniao"] = "2026-10-22_09-00-00", ["a_partir_de"] = int.Parse(m.Groups[1].Value) }, out _);
    Assert.StartsWith($"[L{m.Groups[1].Value} ", resto[resto.IndexOf("## Transcrição")..].Split('\n')[1]);
    Assert.DoesNotContain("## Regras deste tipo de ata", resto);
}

[Fact]
public void PrepararAtaSemTranscricaoEErroDeUso()
{
    AcervoTests.Gravar(Path.Combine(_raiz, "acervo"), "2026-10-23_09-00-00", "Só áudio");
    Ferramenta("preparar_ata", new JsonObject { ["reuniao"] = "2026-10-23_09-00-00" }, out bool erro);
    Assert.True(erro);
}
```

Ajuste `AsFerramentasSoLeem`: a lista ganha `preparar_ata` (e `propor_ata` na Tarefa 11); a asserção de `readOnlyHint` passa a valer para todas menos `propor_ata`.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ServidorMcpTests"`
Expected: FAIL (`ferramenta desconhecida: preparar_ata`).

- [ ] **Step 3: Implementar.** A definição entra em `Definicoes()` com a descrição: "Tudo o que escrever a ata de uma reunião pede: as pessoas com o lado de cada uma, o vocabulário, as notas, as regras da ata de formalização e a transcrição com as falas numeradas. É o primeiro passo de propor_ata." `Inteiro(args, "a_partir_de", 1, int.MaxValue)` lê o número.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Claude app-net/Tests/IntegracaoComClaudeTests.cs
git commit -m "feat(mcp): preparar_ata entrega a reunião com as falas numeradas"
```

---

### Task 11: MCP — `propor_ata`

§8.2. A única ferramenta que escreve, e só num arquivo.

**Files:**
- Modify: `app-net/Nucleo/Claude/FerramentasDoMcp.cs`
- Test: `app-net/Tests/IntegracaoComClaudeTests.cs`

**Interfaces:**
- Consumes: `Acervo.VersaoDaTranscricao`, `FonteDoContexto` (Tarefa 10); `AtaDeFormalizacao.Conferir`, `Conferencia.DoClaude`, `SalvarComoProposta` (Tarefas 2, 3 e 6).
- Produces: ferramenta `propor_ata`. Entrada:

```
reuniao      string, obrigatório          versao   string, obrigatório (a de preparar_ata)
abertura     string                       
resolvidos   [{ tema, decisao, linhas: [int] }]
pendentes    [{ o_que, lado: "nosso" | "cliente", responsavel, prazo, linhas: [int] }]
a_confirmar  [{ texto, motivo, linhas: [int] }]
```

`Ferramenta(...)` ganha um parâmetro `bool escreve = false`; com ele, as anotações são `readOnlyHint: false`, `destructiveHint: false`, `idempotentHint: true`, `openWorldHint: false`.

**O que ela faz, em ordem:** acha a reunião (`Reuniao(args)`, que passa pelo `Acervo.Achar`); exige transcrição; compara `versao` com `VersaoDaTranscricao` — diferente é `ErroDeUso("A transcrição mudou desde que você a leu. Chame preparar_ata de novo e refaça as citações.")`; lê as três listas com tolerância (o que não é lista vale lista vazia; item que não é objeto é pulado; `linhas` aceita número e texto com número, e joga fora o resto e os menores que 1); sem nenhum item nas três, `ErroDeUso("A proposta não tem nenhum item.")`; `Conferir`; `DoClaude`; `SalvarComoProposta`; responde.

**A resposta**, em texto:

```
Proposta gravada para a reunião <id>: N resolvidos, N pendentes, N a confirmar.

Ficaram com aviso, para a pessoa conferir:
- <texto do item> — <texto do aviso>

Ficaram de fora:
- <cada observação>

A proposta ainda não é a ata. Diga à pessoa para abrir a aba Ata desta reunião no PulseMeet e conferir item por item.
```

Os blocos de aviso e de fora só aparecem quando têm linha.

- [ ] **Step 1: Escrever os testes**

```csharp
private JsonObject Proposta(string versao) => new()
{
    ["reuniao"] = "2026-10-21_11-30-00", ["versao"] = versao,
    ["resolvidos"] = new JsonArray(
        new JsonObject { ["tema"] = "Corte de volume", ["decisao"] = "500 caixas por mês.", ["linhas"] = new JsonArray(3) },
        new JsonObject { ["tema"] = "Rateio", ["decisao"] = "Seguimos com o de 2025.", ["linhas"] = new JsonArray(99) }),
    ["pendentes"] = new JsonArray(
        new JsonObject { ["o_que"] = "Margem por cliente", ["lado"] = "cliente", ["responsavel"] = "Carla",
                         ["prazo"] = "hoje", ["linhas"] = new JsonArray(4, "7", -2, "x") },
        new JsonObject { ["o_que"] = "Lista das lojas novas", ["lado"] = "marte", ["responsavel"] = "",
                         ["prazo"] = "28/10/2026", ["linhas"] = new JsonArray(9) }),
    ["a_confirmar"] = new JsonArray(
        new JsonObject { ["texto"] = "O cálculo usa a média de três meses",
                         ["motivo"] = "é uma explicação de como funciona hoje", ["linhas"] = new JsonArray(8) }),
};

private string Versao() => System.Text.RegularExpressions.Regex.Match(
    Ferramenta("preparar_ata", new JsonObject { ["reuniao"] = "2026-10-21_11-30-00" }, out _),
    @"versao: (\S+)").Groups[1].Value;

[Fact]
public void ProporAtaGravaSoAPropostaEContaOQueCaiu()
{
    string pasta = Path.Combine(_raiz, "acervo", "2026-10-21_11-30-00");
    var antes = Directory.GetFiles(pasta).Select(Path.GetFileName).ToHashSet();

    string texto = Ferramenta("propor_ata", Proposta(Versao()), out bool erro);

    Assert.False(erro);
    Assert.Contains("1 resolvido, 2 pendentes, 1 a confirmar", texto);
    Assert.Contains("Lista das lojas novas — O prazo \"28/10/2026\"", texto);
    Assert.Contains("Descartado por não citar a fala de onde saiu: Rateio", texto);
    Assert.Equal(antes.Append("proposta-do-claude.json").Order(),
                 Directory.GetFiles(pasta).Select(Path.GetFileName).Order());

    var p = Conferencia.LerProposta(pasta)!;
    Assert.Equal("claude", p.EscritaPor);
    Assert.Equal(2, p.Itens[1].Falas.Count);            // 4 e "7"; -2 e "x" fora
    Assert.Equal("", p.Itens[2].Lado);                  // "marte" não é lado
    Assert.Equal(34, p.Itens[1].Falas[1].Inicio);       // a fala já resolvida em tempo
}

[Fact]
public void ProporAtaRecusaAVersaoVelhaENaoGravaNada()
{
    string pasta = Path.Combine(_raiz, "acervo", "2026-10-21_11-30-00");
    string versao = Versao();
    File.SetLastWriteTimeUtc(Path.Combine(pasta, "transcricao.json"), DateTime.UtcNow.AddMinutes(5));

    string texto = Ferramenta("propor_ata", Proposta(versao), out bool erro);

    Assert.True(erro);
    Assert.Contains("Chame preparar_ata de novo", texto);
    Assert.False(File.Exists(Path.Combine(pasta, "proposta-do-claude.json")));
}

[Theory]
[InlineData("../fora")]
[InlineData("C:\\Windows")]
public void ProporAtaNaoEscreveForaDoAcervo(string id)
{
    var args = Proposta("x");
    args["reuniao"] = id;
    Ferramenta("propor_ata", args, out bool erro);
    Assert.True(erro);
}

[Fact]
public void ProporAtaComListasQueNaoSaoListasEErroDeUsoENaoExcecao()
{
    var args = new JsonObject { ["reuniao"] = "2026-10-21_11-30-00", ["versao"] = Versao(),
                                ["resolvidos"] = "nenhum", ["pendentes"] = new JsonArray("solto", 3) };
    string texto = Ferramenta("propor_ata", args, out bool erro);
    Assert.True(erro);
    Assert.Contains("não tem nenhum item", texto);
}

[Fact]
public void UmaPropostaNovaSubstituiAAnterior()
{
    Ferramenta("propor_ata", Proposta(Versao()), out _);
    var menor = Proposta(Versao());
    menor["pendentes"] = new JsonArray();
    menor["a_confirmar"] = new JsonArray();
    Ferramenta("propor_ata", menor, out _);

    Assert.Single(Conferencia.LerProposta(Path.Combine(_raiz, "acervo", "2026-10-21_11-30-00"))!.Itens);
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ServidorMcpTests"`
Expected: FAIL (`ferramenta desconhecida: propor_ata`).

- [ ] **Step 3: Implementar.** Descrição da ferramenta: "Propõe a ata de formalização de uma reunião: resolvidos, pendentes e a confirmar, cada item com os números das falas (L) que o sustentam, lidos em preparar_ata. Grava uma proposta que a pessoa confere no PulseMeet; não substitui a ata que existe. Item sem fala citada não entra." Para ler número de `JsonNode` sem genérico perigoso, use o mesmo padrão do `Arg` (`TryGetValue<double>` e `TryGetValue<string>`), que já passa no trimming.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Claude/FerramentasDoMcp.cs app-net/Tests/IntegracaoComClaudeTests.cs
git commit -m "feat(mcp): propor_ata grava a proposta do Claude, e só ela"
```

---

### Task 12: MCP — o pedido `fazer-ata`, o estado em `ler_ata` e as instruções

§8.3 e §8.4.

**Files:**
- Modify: `app-net/Nucleo/Claude/FerramentasDoMcp.cs`
- Test: `app-net/Tests/IntegracaoComClaudeTests.cs`

**Interfaces:**
- Produces: prompt `fazer-ata` com o argumento opcional `reuniao`; a primeira linha de `ler_ata`; o texto novo de `Instrucoes`.

**A primeira linha de `ler_ata`**, antes do `id:`:
- com registro em rascunho: `Estado: rascunho, não conferida.`
- conferida: `Estado: conferida em dd/MM/yyyy.`, mais ` Enviada em dd/MM/yyyy.` quando há envio (o último);
- sem registro: `Estado: escrita por um modelo, sem conferência.`

**O fim de `Instrucoes`** troca a frase "As atas foram escritas por um modelo local e podem errar nomes e números; a transcrição também pode errar nomes próprios." por: "As atas são escritas por um modelo e podem errar nomes e números; ler_ata diz na primeira linha se a ata foi conferida por uma pessoa, e só a conferida é confiável. A transcrição também pode errar nomes próprios. Para escrever a ata de uma reunião, leia com preparar_ata e proponha com propor_ata: a proposta só vira ata depois que a pessoa a confere no PulseMeet."

**O texto de `fazer-ata`** (cópia exata; `{qual}` é `reunião <id> do PulseMeet` ou `reunião aberta no PulseMeet (use reuniao_aberta para saber qual é)`):

```
Escreva a ata de formalização da {qual}.

1. Leia a reunião com preparar_ata. Se a transcrição vier cortada, continue com a_partir_de até o fim antes de escrever qualquer coisa.
2. Leia a reunião inteira, inclusive a conversa antes da pauta: prazo e combinado também aparecem ali.
3. Quando alguém recapitular em voz alta o que ficou combinado, use o recap como lista de conferência do que você extraiu.
4. Separe em três grupos:
   - resolvidos: o que a reunião fechou e o outro lado aceitou;
   - pendentes: o que alguém ficou de fazer, enviar, decidir ou confirmar depois;
   - a_confirmar: o que parece decisão mas não tem a fala em que o outro lado concorda. Diga o motivo em uma frase.
5. Explicação de como algo funciona hoje não é decisão. Exemplo não é fato. O que alguém ficou de verificar é pendente ou a confirmar, nunca resolvido.
6. Prazo só quando foi dito, e como foi dito. Se a reunião disse dois, vale o último.
7. Todo pendente tem lado (nosso ou cliente). Responsável pelo nome quando a fala o diz; sem nome, deixe vazio.
8. Mudança de data ou a próxima reunião entra como um resolvido de tema "Próxima reunião".
9. Cada item cita em "linhas" os números L das falas que o sustentam. Sem fala, o item não existe.
10. Proponha com propor_ata, mandando de volta a versao que preparar_ata deu. Se a resposta disser que algo caiu por citação errada, corrija e proponha de novo.

Não entregue a ata no chat como se estivesse pronta. Termine dizendo quantos itens propôs em cada grupo, quais ficaram com aviso, e que a pessoa precisa conferir na aba Ata da reunião, no PulseMeet.
```

- [ ] **Step 1: Escrever os testes**

```csharp
[Fact]
public void LerAtaDizOEstadoNaPrimeiraLinha()
{
    string pasta = Path.Combine(_raiz, "acervo", "2026-09-01_10-00-00");
    Assert.StartsWith("Estado: escrita por um modelo, sem conferência.",
        Ferramenta("ler_ata", new JsonObject { ["reuniao"] = "2026-09-01_10-00-00" }, out _));

    var c = ConferenciaTests.Exemplo();
    c.Salvar(pasta);
    Assert.StartsWith("Estado: rascunho, não conferida.",
        Ferramenta("ler_ata", new JsonObject { ["reuniao"] = "2026-09-01_10-00-00" }, out _));

    c.Estado = "conferida";
    c.ConcluidaEm = "2026-09-01T11:00:00-03:00";
    c.Envios.Add(new EnvioDaAta { Em = "2026-09-02T09:00:00-03:00", Modo = "manual" });
    c.Salvar(pasta);
    Assert.StartsWith("Estado: conferida em 01/09/2026. Enviada em 02/09/2026.",
        Ferramenta("ler_ata", new JsonObject { ["reuniao"] = "2026-09-01_10-00-00" }, out _));
}

[Fact]
public void OPedidoFazerAtaMandaLerProporEConferirNoApp()
{
    var nomes = Pedir("prompts/list")["result"]!["prompts"]!.AsArray().Select(p => p!["name"]!.GetValue<string>());
    Assert.Contains("fazer-ata", nomes);

    string texto = Pedir("prompts/get", new JsonObject { ["name"] = "fazer-ata",
        ["arguments"] = new JsonObject { ["reuniao"] = "2026-10-21_11-30-00" } })
        ["result"]!["messages"]![0]!["content"]!["text"]!.GetValue<string>();

    Assert.Contains("reunião 2026-10-21_11-30-00 do PulseMeet", texto);
    Assert.Contains("preparar_ata", texto);
    Assert.Contains("propor_ata", texto);
    Assert.Contains("conversa antes da pauta", texto);
    Assert.Contains("nunca resolvido", texto);
    Assert.Contains("Não entregue a ata no chat", texto);
}

[Fact]
public void AsInstrucoesDistinguemAAtaConferida()
{
    Assert.Contains("só a conferida é confiável", FerramentasDoMcp.Instrucoes);
    Assert.Contains("propor_ata", FerramentasDoMcp.Instrucoes);
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj --filter "FullyQualifiedName~ServidorMcpTests"`
Expected: FAIL.

- [ ] **Step 3: Implementar.** A linha de estado entra só na ferramenta `ler_ata`, e não no recurso `…/ata`: o recurso é o que se anexa pelo "+", e continua sendo a ata como está no disco.

- [ ] **Step 4: Rodar a suíte**

Run: `dotnet test app-net/Tests/MeetingApp.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add app-net/Nucleo/Claude/FerramentasDoMcp.cs app-net/Tests/IntegracaoComClaudeTests.cs
git commit -m "feat(mcp): o pedido fazer-ata, e ler_ata diz se a ata foi conferida"
```

---

### Task 13: Ajustes — o nome da empresa e o ⓘ do Claude

§5.3 e §8.4.

**Files:**
- Modify: `app-net/App/web/configuracoes.js`

**Interfaces:**
- Consumes: `nome_da_empresa` do `app.json` (Tarefa 4).

- [ ] **Step 1: O campo** em `abaAtas`, logo depois do campo dos domínios da casa: rótulo "Nome da sua empresa", dica "Aparece na ata quando uma pendência é do seu lado e ninguém foi nomeado. Vazio, vale o primeiro domínio da casa.", gravando com `gravar({ nome_da_empresa: valor.trim() })` no `change`, do mesmo jeito que o campo `nome_do_dono`.

- [ ] **Step 2: O ⓘ** de `secaoDoClaude`: depois da frase que diz o que o Claude lê, acrescente "O Claude também pode propor a ata de uma reunião. A proposta fica à parte e só vira ata quando você a aceita e confere, na aba Ata." A linha "Conectado: …" não muda.

- [ ] **Step 3: Conferir**

Run: `node --test tools/web/*.test.mjs && dotnet build app-net/App -c Release 2>&1 | tail -3`
Expected: PASS e `0 Erro(s)`. A conferência na tela fica com a Tarefa 16.

- [ ] **Step 4: Commit**

```bash
git add app-net/App/web/configuracoes.js
git commit -m "feat(ajustes): o nome da empresa, e o ⓘ do Claude diz que ele também propõe"
```

---

### Task 14: A régua

§9. Uma ferramenta que lê os `conferencia.json` do acervo e imprime a tabela, por quem escreveu o rascunho.

**Files:**
- Create: `tools/medir_conferencia.py`

**Interfaces:**
- Consumes: o `conferencia.json` (Tarefa 3).
- Produces: `uv run python tools/medir_conferencia.py [--pasta <acervo>] [--todas]`. Sem `--pasta`, usa `~/Documents/MeetingRecordings` (a mesma do `PastaDasGravacoes.Padrao`). Só a biblioteca padrão.

**O que ela conta, por ata conferida** (as em rascunho só entram com `--todas`, marcadas): propostos (itens de `proposta`), aprovados sem mudança (`origem` modelo e `revisao` aprovado), corrigidos, tirados (`revisao` retirado **e** que não nasceram retirados: o item cujo par em `proposta`, pelo `id`, já era retirado não conta), acrescentados (`origem` manual), e os minutos (`concluida_em` − `comecou_em`).

**A saída:** uma linha por reunião (`id · quem · propostos · aprovados · corrigidos · tirados · acrescentados · min`) e um bloco final por quem escreveu, com as somas, a fração de aprovados sem mudança sobre os propostos, e a mediana dos minutos. Registro ilegível é pulado, com uma linha no stderr.

- [ ] **Step 1: Escrever a ferramenta.**

- [ ] **Step 2: Provar com um acervo de mentira**

```bash
T="$(mktemp -d)"; mkdir -p "$T/2026-10-21_11-30-00" "$T/2026-10-28_11-30-00" "$T/quebrada"
cat > "$T/2026-10-21_11-30-00/conferencia.json" <<'EOF'
{"versao":1,"escrita_por":"claude","estado":"conferida",
 "comecou_em":"2026-10-21T12:15:00-03:00","concluida_em":"2026-10-21T12:19:00-03:00",
 "proposta":[{"id":"i1","revisao":"a_conferir"},{"id":"i2","revisao":"a_conferir"},{"id":"i3","revisao":"retirado"}],
 "itens":[{"id":"i1","origem":"modelo","revisao":"aprovado"},{"id":"i2","origem":"modelo","revisao":"retirado"},
          {"id":"i3","origem":"modelo","revisao":"retirado"},{"id":"i4","origem":"manual","revisao":"aprovado"}]}
EOF
cat > "$T/2026-10-28_11-30-00/conferencia.json" <<'EOF'
{"versao":1,"escrita_por":"local","estado":"rascunho","proposta":[{"id":"i1","revisao":"a_conferir"}],
 "itens":[{"id":"i1","origem":"modelo","revisao":"a_conferir"}]}
EOF
echo "{ quebrado" > "$T/quebrada/conferencia.json"
uv run python tools/medir_conferencia.py --pasta "$T"
```

Expected: uma linha para `2026-10-21_11-30-00` com `claude · 3 · 1 · 0 · 1 · 1 · 4,0`; nenhuma para a de 28/10 (rascunho); o bloco `claude` com `1 de 3 aprovados sem mudança (33%)`; e no stderr `quebrada: registro ilegível`. Com `--todas`, a de 28/10 aparece marcada como rascunho.

- [ ] **Step 3: Commit**

```bash
git add tools/medir_conferencia.py
git commit -m "feat(tools): a régua da conferência, por quem escreveu o rascunho"
```

---

### Task 15: Os documentos

§13.

**Files:**
- Modify: `docs/INTEGRACAO-CLAUDE.md` (§2, §3, §9), `docs/RASTREABILIDADE.md`, `docs/BACKLOG.md`, `docs/ATA.md`

- [ ] **Step 1: `INTEGRACAO-CLAUDE.md`.** No §2, as duas ferramentas e o pedido novos, na tabela que já lista os outros. No §3, a decisão "Só lê" passa a "Lê, e propõe num arquivo à parte": o que o processo do Claude grava, por que ele nunca toca na ata, e a referência ao desenho. No §9, o que ficou para depois (o passo 2).

- [ ] **Step 2: `RASTREABILIDADE.md`.** Na tabela do topo, o estado do passo 1 passa a "construído, em validação", com a data. No passo 1, os itens que deixaram de ser plano.

- [ ] **Step 3: `BACKLOG.md` e `ATA.md`.** No backlog, o item do tema de atas que este passo fecha, com ponteiro para o desenho. No `ATA.md`, uma seção curta "A formalização conferida": os cinco arquivos novos da pasta e o que cada um é.

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "docs: a ata conferida no INTEGRACAO-CLAUDE, no RASTREABILIDADE e no ATA"
```

---

### Task 16: Validação com o dono

Os critérios do §11 que só se fecham com o app de verdade. **Esta tarefa começa perguntando**: ela usa a GPU, o app instalado e as pastas reais.

- [ ] **Step 1: O binário publica sem aviso de trimming** (critério 8)

Run: `tools/publicar.sh --so-build 2>&1 | tail -20`
Expected: publica em `dist/publicar`, sem `IL2026` nem `IL3050`. Um aviso aqui é falha: volte à tarefa que introduziu o tipo.

- [ ] **Step 2: Pedir ao dono** a janela para instalar (o `publicar.sh` exige o app fechado pela bandeja, e os processos `--mcp` do Claude Desktop também travam a troca) e a autorização para usar a GPU.

- [ ] **Step 3: O caminho local, numa reunião real** (critérios 1, 2 e 3): gerar a formalização, conferir que `conferencia.json` tem as falas, que *Ouvir* toca no ponto, e que aprovar, corrigir, tirar, acrescentar e concluir mudam o `ata.md` sem mudar a data dele.

- [ ] **Step 4: O caminho do Claude** (critérios 4 e 5): no Claude Desktop, "faça a ata da reunião de hoje"; a proposta aparece na aba Ata ao voltar para o PulseMeet, sem reabrir o app; sobre uma ata conferida, nada muda até aceitar.

- [ ] **Step 5: As referências.** Com a autorização do dono, copiar as nove atas de `ata-experimento/ref` para as pastas das reuniões correspondentes como `ata-email.md` (§9). A correspondência é do dono; nada disso entra no repositório.

- [ ] **Step 6: A medida** (critérios 6 e 7): o dono confere três reuniões com o rascunho do Claude e três com o local; `uv run python tools/medir_conferencia.py` imprime a tabela; os minutos e o que ele mudou à mão vão para o `RASTREABILIDADE.md`. Se o local passar muito dos cinco minutos, o achado é escrito, e não escondido.

- [ ] **Step 7: Commit do que a validação escreveu nos documentos**

```bash
git add docs/RASTREABILIDADE.md
git commit -m "docs: a primeira medida da conferência"
```
