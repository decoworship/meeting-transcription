using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A correção fonética, com os casos que a FASE0 mediu.
/// </summary>
/// <remarks>
/// Não há testes no <c>tools/correcao_fonetica.py</c> para portar — o que existe
/// lá são <b>decisões medidas</b>, registradas em prosa: regras que foram
/// testadas e rejeitadas, e o falso positivo que quase passou. É isso que estes
/// testes prendem, para ninguém "melhorar" o corretor de volta ao que já se
/// provou pior.
/// </remarks>
public sealed class CorrecaoFoneticaTests
{
    [Theory]
    [InlineData("Jimmy", "Dimi")]     // o caso central: 10 ocorrências na FASE0
    [InlineData("Dimmy", "Dimi")]     // o que o whisper.cpp produzia com prompt
    [InlineData("Jimi", "Dimi")]
    public void GrafiasDeSomIgualConvergem(string errado, string certo)
    {
        Assert.Equal(CorrecaoFonetica.Foneticar(certo), CorrecaoFonetica.Foneticar(errado));
        Assert.True(CorrecaoFonetica.Casa(errado, certo));
    }

    [Fact]
    public void OTetoDeDistanciaNaoPodeApertarAPontoDeMatarOCasoCentral()
    {
        // Escalar o teto pelo tamanho do termo foi testado e rejeitado: com
        // teto 1 para termos de até 4 letras, as 10 correções Jimmy->Dimi
        // desapareciam e sobrava 1 troca em todo o corpus.
        Assert.Equal(3, CorrecaoFonetica.Levenshtein("jimmy", "dimi"));
        Assert.Equal(3, CorrecaoFonetica.DistanciaMaxima("Dimi"));
    }

    [Fact]
    public void PalavraJaCertaNaoEhTrocada()
    {
        Assert.False(CorrecaoFonetica.Casa("Dimi", "Dimi"));
        var (texto, trocas) = CorrecaoFonetica.Corrigir("falei com o Dimi ontem", ["Dimi"]);
        Assert.Equal("falei com o Dimi ontem", texto);
        Assert.Empty(trocas);
    }

    [Fact]
    public void MinusculaNaoEhCandidata()
    {
        // O falso positivo que quase passou: "Do IP fixo" (português legítimo
        // numa reunião de telecom) virava "Do IP Fixa". O guarda é a
        // capitalização — o Whisper capitaliza nome próprio.
        var (texto, trocas) = CorrecaoFonetica.Corrigir("Do IP fixo para o cliente", ["Fixa"]);
        Assert.Equal("Do IP fixo para o cliente", texto);
        Assert.Empty(trocas);
    }

    [Fact]
    public void MaiusculaAbrindoFraseNaoEhSinalDeNomeProprio()
    {
        // Início de frase é maiúsculo por regra ortográfica, não por ser nome.
        var (texto, _) = CorrecaoFonetica.Corrigir("Fixo é o que combinamos.", ["Fixa"]);
        Assert.Equal("Fixo é o que combinamos.", texto);

        // Depois de ponto, idem — mesmo com texto antes.
        var (t2, _) = CorrecaoFonetica.Corrigir("Combinamos assim. Fixo então.", ["Fixa"]);
        Assert.Equal("Combinamos assim. Fixo então.", t2);
    }

    [Fact]
    public void TrocaNoMeioDaFraseEhAplicadaERegistrada()
    {
        var (texto, trocas) = CorrecaoFonetica.Corrigir("ontem o Jimmy falou disso", ["Dimi"]);

        Assert.Equal("ontem o Dimi falou disso", texto);
        var t = Assert.Single(trocas);
        Assert.Equal("Jimmy", t.De);
        Assert.Equal("Dimi", t.Para);
        // A posição existe para a UI poder marcar a substituição: se quem lê a
        // ata não tem como desconfiar, tem que poder inspecionar.
        Assert.Equal("ontem o ".Length, t.Posicao);
    }

    [Fact]
    public void ExcecoesSaoRespeitadas()
    {
        var (texto, trocas) = CorrecaoFonetica.Corrigir(
            "ontem o Jimmy falou disso", ["Dimi"],
            excecoes: new HashSet<string> { "jimmy" });

        Assert.Equal("ontem o Jimmy falou disso", texto);
        Assert.Empty(trocas);
    }

    [Fact]
    public void PalavrasDeSomDiferenteNaoSaoTocadas()
    {
        // O risco real não é errar pouco: é reescrever o que a pessoa disse.
        var (texto, _) = CorrecaoFonetica.Corrigir("ontem o Marcelo falou disso", ["Dimi"]);
        Assert.Equal("ontem o Marcelo falou disso", texto);
    }

    [Fact]
    public void AcentoNaoImpedeOCasamento()
    {
        Assert.Equal(CorrecaoFonetica.Foneticar("Elio"), CorrecaoFonetica.Foneticar("Élio"));
    }
}
