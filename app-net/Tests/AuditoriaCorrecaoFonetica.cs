using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O que a correção fonética recupera, e o que ela não recupera.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> O <c>hotwords</c> saiu do caminho do ASR em
/// 19/08/2026, e a justificativa registrada é boa — 787 segmentos em vez de 207
/// no mesmo áudio, e 4,6× no tempo. A frase que fecha o argumento é <i>"o
/// vocabulário continua inteiro na correção fonética"</i>. Este arquivo mede
/// essa frase.
/// </para>
/// <para>
/// <b>Resultado, medido em 25/08/2026:</b> a correção recupera o caso para o
/// qual foi desenhada — "Jimmy" quando o vocabulário tem "Dimi", som igual e
/// grafia diferente — e <b>nenhum</b> dos dez erros de nome próprio catalogados
/// nas gravações reais. Eles não são erros de grafia de som parecido; são de
/// três outros tipos:
/// </para>
/// <list type="bullet">
/// <item>sigla corrompida — <c>G6CB</c> por <c>GCCB</c>, <c>PDB</c> por
/// <c>PDV</c>: dígito no lugar de letra não tem fonética;</item>
/// <item>troca por palavra mais comum — <c>Algarve</c> por <c>Algar</c>,
/// <c>Tim</c> por <c>Teams</c>, <c>sexta</c> por <c>cesta</c>: o modelo escolheu
/// outra palavra, e ela soa diferente;</item>
/// <item>estrangeirismo aportuguesado — <c>Cláudio</c> por <c>Claude</c>,
/// <c>pooling</c> por <c>polling</c>.</item>
/// </list>
/// <para>
/// <b>Isto não é argumento para religar o <c>hotwords</c>.</b> O ganho de
/// segmentação é real e a gravação de 25/08, a primeira medida com ele
/// desligado, teve os melhores números de diarização do acervo. É argumento
/// para a lacuna ter uma rede própria, que compare contra as entidades que o
/// <c>meta.json</c> já conhece — cliente, projeto e convidados — por distância
/// de edição, e não por som.
/// </para>
/// <para>
/// Os testes abaixo afirmam o estado <b>de hoje</b>. Quando a rede existir, eles
/// falham — e é assim que se sabe que ela funcionou.
/// </para>
/// </remarks>
public sealed class AuditoriaCorrecaoFonetica
{
    [Theory]
    [InlineData("comparado inclusive com o G6CB", "GCCB")]
    [InlineData("o pessoal da Algarve tem problema", "Algar")]
    [InlineData("perguntar pro Cláudio, né", "Claude")]
    [InlineData("a agenda do Tim, sabe", "Teams")]
    [InlineData("com as outras engalafadoras", "engarrafadoras")]
    [InlineData("diferenciar a nível de PDB", "PDV")]
    [InlineData("a sexta é a mesma, cara", "cesta")]
    [InlineData("utilizando o sistema de pooling", "polling")]
    [InlineData("o cliente Moevade pediu", "Monlevade")]
    [InlineData("armazenados nos secrets da Tulsa API", "Tools")]
    public void AindaNaoRecupera(string frase, string termo)
    {
        var (texto, trocas) = CorrecaoFonetica.Corrigir(frase, [termo]);

        Assert.Empty(trocas);
        Assert.Equal(frase, texto);
    }

    [Fact]
    public void MasRecuperaOCasoParaOQualFoiDesenhada()
    {
        // FASE0, resultado 5: "Dimi" saía como "Jimmy" dez vezes nos dois
        // motores. Som igual, grafia diferente — é o alvo da classe.
        var (texto, trocas) = CorrecaoFonetica.Corrigir(
            "o Jimmy vai mandar o arquivo", ["Dimi"]);

        Assert.Single(trocas);
        Assert.Contains("Dimi", texto);
    }
}
