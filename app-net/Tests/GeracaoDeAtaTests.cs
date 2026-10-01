using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O caminho da ata que a ponte e o CLI percorrem, com um motor falso (DEB-1).
/// </summary>
public sealed class GeracaoDeAtaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "ata-" + Guid.NewGuid().ToString("N"));

    public GeracaoDeAtaTests()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Path.Combine(_pasta, "meta.json"), """
            {"meeting":{"title":"Semanal do Portal","attendees":["Ana Lima","dimi.randel"],
             "attendee_emails":["ana@casa.com","dimi.randel@cliente.com"],"calendar_event_id":"e"}}
            """);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static ResultadoDaTranscricao Dados() => new()
    {
        Duration = 600,
        Client = "Acme",
        Project = "Portal",
        Date = "2026-09-30T10:00:00-03:00",
        Segments =
        [
            new SegmentoFinal { Start = 0, End = 5, Text = " Vamos subir o Kubernetes na sexta.", Speaker = "Ana Lima" },
            new SegmentoFinal { Start = 5, End = 9, Text = " Fechado, eu cuido do deploy.", Speaker = "Pessoa 1" },
        ],
    };

    private Projetos ProjetosComVocabulario()
    {
        var p = new Projetos(Path.Combine(_pasta, "projetos.json"));
        p.Salvar("Acme", "Portal", new PreferenciasDoProjeto { InitialPrompt = "Kubernetes, Argo" });
        return p;
    }

    [Fact]
    public void OContextoLevaTituloVocabularioEONomeCanonico()
    {
        var ctx = GeracaoDeAta.Contexto(_pasta, Dados(), ["casa.com"], ProjetosComVocabulario());

        Assert.Equal("Semanal do Portal", ctx.Titulo);
        Assert.Equal("Kubernetes, Argo", ctx.Vocabulario);
        Assert.Equal("Acme", ctx.Cliente);
        Assert.Equal(600, ctx.DuracaoS);
        Assert.Equal(["Ana Lima", "Pessoa 1"], ctx.Falantes);
        // O local-part do e-mail não chega ao modelo: ver Organizacoes.Classificar.
        Assert.DoesNotContain("dimi.randel", ctx.Convidados);
        Assert.Equal(2, ctx.Pessoas.Count);
    }

    [Fact]
    public async Task OMotorRecebeOPromptEAAtaVaiParaODisco()
    {
        var dados = Dados();
        var tipo = ModelosDeAta.Todos()[0];
        var ctx = GeracaoDeAta.Contexto(_pasta, dados, ["casa.com"], ProjetosComVocabulario());
        string? promptVisto = null;
        double duracaoVista = 0;
        var etapas = new List<string>();

        GeracaoDeAta.Motor falso = (prompt, duracao, progresso, _) =>
        {
            promptVisto = prompt;
            duracaoVista = duracao;
            progresso?.Invoke(new ProgressoDaAta("gerando", 0.5, "meio"));
            return Task.FromResult(new AtaGerada
            {
                Resumo = "Decidiram subir o Kubernetes na sexta.",
                Decisoes = ["Subir o Kubernetes na sexta"],
            });
        };

        var ata = await GeracaoDeAta.GerarAsync(_pasta, tipo, dados, ctx, falso,
            p => etapas.Add(p.Etapa), CancellationToken.None);

        Assert.NotNull(promptVisto);
        Assert.Contains("Semanal do Portal", promptVisto);
        Assert.Contains("Kubernetes, Argo", promptVisto);
        Assert.Contains("Vamos subir o Kubernetes", promptVisto);
        Assert.Equal(600, duracaoVista);
        Assert.Equal(["gerando"], etapas);

        string md = File.ReadAllText(Path.Combine(_pasta, "ata.md"));
        Assert.Contains("Subir o Kubernetes na sexta", md);
        Assert.True(File.Exists(Path.Combine(_pasta, "ata.json")));
    }

    [Fact]
    public async Task MotorQueFalhaNaoDeixaAtaPelaMetade()
    {
        var dados = Dados();
        var ctx = GeracaoDeAta.Contexto(_pasta, dados, [], null);
        GeracaoDeAta.Motor quebra = (_, _, _, _) => throw new InvalidOperationException("sem memória");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GeracaoDeAta.GerarAsync(_pasta, ModelosDeAta.Todos()[0], dados, ctx, quebra, null, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(_pasta, "ata.md")));
        Assert.False(File.Exists(Path.Combine(_pasta, "ata.json")));
    }

    [Fact]
    public void SemMetaNemProjetoOContextoSaiVazioSemLancar()
    {
        File.Delete(Path.Combine(_pasta, "meta.json"));
        var ctx = GeracaoDeAta.Contexto(_pasta, Dados(), [], null);

        Assert.Null(ctx.Titulo);
        Assert.Equal("", ctx.Vocabulario);
        Assert.Empty(ctx.Pessoas);
    }
}
