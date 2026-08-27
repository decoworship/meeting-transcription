using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A leitura do que o modelo devolve, e o corte em pedaços.
/// </summary>
/// <remarks>
/// O que o modelo <b>acerta</b> não se testa aqui — isso é medição contra
/// gravação real, e está registrada em <c>docs/</c>. Aqui está o que precisa
/// valer sempre: resposta ilegível não derruba a transcrição, e o pedaço nunca
/// corta uma fala ao meio.
/// </remarks>
public sealed class PropositorDeModeloTests
{
    [Fact]
    public void LeAsTrocasDoEsquema()
    {
        var ps = PropositorDeModelo.Ler(
            """{"trocas":[{"de":"G6CB","para":"GCCB"},{"de":"Cláudio","para":"Claude"}]}""");

        Assert.Equal(2, ps.Count);
        Assert.Equal("G6CB", ps[0].De);
        Assert.Equal("modelo", ps[0].Fonte);
    }

    [Fact]
    public void ListaVaziaEhRespostaValida()
    {
        // "não há nada a trocar" é o caso mais comum, e não é erro.
        Assert.Empty(PropositorDeModelo.Ler("""{"trocas":[]}"""));
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("""{"outra_coisa": 1}""")]
    [InlineData("""{"trocas": "não é lista"}""")]
    [InlineData("""{"trocas":[{"de":"","para":"GCCB"}]}""")]
    public void RespostaImprestavelNaoDerruba(string resposta)
    {
        // A revisão é acabamento: um pedaço que não voltou vale menos que a
        // reunião inteira.
        Assert.Empty(PropositorDeModelo.Ler(resposta));
    }

    [Fact]
    public void OQueOModeloProproPassaPelaMesmaPorta()
    {
        // O desenho inteiro em um teste: o modelo devolve identidade, alvo
        // desconhecido e uma troca boa; só a boa sobrevive.
        var doModelo = PropositorDeModelo.Ler("""
            {"trocas":[{"de":"Sorocaba","para":"Sorocaba"},
                       {"de":"webhooks","para":"webhook"},
                       {"de":"Cláudio","para":"claude"}]}
            """);

        var boas = RevisaoDeTermos.Validar(doModelo, ["Claude, Excel, Sorocaba"]);

        var p = Assert.Single(boas);
        Assert.Equal("Cláudio", p.De);
        Assert.Equal("Claude", p.Para);      // a grafia do vocabulário
        Assert.Equal("modelo", p.Fonte);
    }
}
