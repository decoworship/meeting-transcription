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

    [Fact]
    public void PalavraCorrigidaNoComecoDeUmTrechoQueNaoEOPrimeiroDaFalaAindaTroca()
    {
        // A palavra cabe inteira no segundo trecho, sem atravessar fronteira —
        // então a fusão não entra em ação. Mas rodar a fonética de novo só
        // sobre "Tania hoje" isolado veria texto vazio antes dela e recusaria
        // a troca (a regra exige "meio de frase"). O contexto vem da fala.
        var r = CorrecaoDaLegenda.Corrigir(
            [T(0, "a pedido da"), T(1120, "Tania hoje")], Vocabulario, Entidades);

        Assert.Equal(2, r.Trechos.Count);
        Assert.Equal(0, r.Fundidos);
        Assert.Null(r.Trechos[0].Swaps);
        Assert.Equal("a pedido da", r.Trechos[0].Texto);
        Assert.Equal("Tânia hoje", r.Trechos[1].Texto);
        Assert.Contains(r.Trechos[1].Swaps!, s => s is { De: "Tania", Para: "Tânia" });
    }

    [Fact]
    public void PalavraFundidaQueComecaOTrechoTambemTroca()
    {
        // "Ta" funde com "nia" (colado) e forma "Tania" no começo do trecho
        // fundido — o mesmo defeito acima, mas depois da fusão.
        var r = CorrecaoDaLegenda.Corrigir(
            [T(0, "a"), T(1120, "Ta"), T(2240, "nia pediu", colado: true)],
            Vocabulario, Entidades);

        Assert.Equal(2, r.Trechos.Count);
        Assert.Equal(1, r.Fundidos);
        Assert.Equal("a", r.Trechos[0].Texto);
        Assert.Null(r.Trechos[0].Swaps);
        Assert.Equal("Tânia pediu", r.Trechos[1].Texto);
    }
}
