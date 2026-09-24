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
        try
        {
            File.WriteAllText(Path.Combine(pasta, "meta.json"), """
                { "meeting": { "attendees": ["Felipeof", "Daniel Prada"] } }
                """);

            var entidades = CorrecaoDeTermos.Entidades(pasta, "KPI", "Agentes", "Interno");

            Assert.Contains("Daniel Prada", entidades);
            Assert.DoesNotContain("Felipeof", entidades);
            Assert.Equal(["KPI", "Agentes", "Interno"], entidades.Take(3));
        }
        finally { Directory.Delete(pasta, true); }
    }
}
