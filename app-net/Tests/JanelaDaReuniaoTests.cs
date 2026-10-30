using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>
/// Qual janela do navegador é a da reunião, e quando o vídeo a grava.
/// </summary>
/// <remarks>
/// O que dói errar aqui é gravar o que não é a reunião — o e-mail que entrou na
/// frente quando a pessoa trocou de aba. Os títulos são os que o Chrome, o Edge
/// e o app instalado do Meet escrevem.
/// </remarks>
public sealed class JanelaDaReuniaoTests
{
    private static readonly AbaDaReuniao NaFrente = new("Meet - abc-defg-hij", true);

    [Theory]
    [InlineData("Meet - abc-defg-hij - Google Chrome")]
    [InlineData("Meet - abc-defg-hij e mais 3 guias - Pessoal — Microsoft​ Edge")]
    [InlineData("Meet - abc-defg-hij")]                      // o app instalado (PWA)
    public void AJanelaQueMostraAAbaCasa(string tituloDaJanela) =>
        Assert.True(JanelaDaReuniao.Mostra(tituloDaJanela, NaFrente.Titulo));

    [Theory]
    [InlineData("Caixa de entrada (6) - Gmail - Google Chrome", "Meet - abc-defg-hij")]
    [InlineData("Meet - Google Chrome", "Meet")]                // título curto demais
    [InlineData("", "Meet - abc-defg-hij")]
    [InlineData("Meet - abc-defg-hij - Google Chrome", null)]
    public void OResto_NaoCasa(string tituloDaJanela, string? tituloDaAba) =>
        Assert.False(JanelaDaReuniao.Mostra(tituloDaJanela, tituloDaAba));

    [Fact]
    public void EscolheAJanelaDaReuniaoEntreAsDoNavegador()
    {
        string[] janelas =
        [
            "Caixa de entrada - Gmail - Google Chrome",
            "Meet - abc-defg-hij - Google Chrome",
            "Meet - abc-defg-hij",
        ];
        // As duas contêm; a de título mais curto é a que tem menos além da aba.
        Assert.Equal(2, JanelaDaReuniao.Escolher(janelas, NaFrente.Titulo));
        Assert.Equal(-1, JanelaDaReuniao.Escolher(janelas, "Reunião | Microsoft Teams"));
        Assert.Equal(-1, JanelaDaReuniao.Escolher([], NaFrente.Titulo));
    }

    [Fact]
    public void GravaSoComAAbaNaFrenteEOTituloDaJanelaAindaSendoODela()
    {
        const string janela = "Meet - abc-defg-hij - Google Chrome";
        Assert.Equal(SituacaoDaJanela.Gravando, JanelaDaReuniao.Decidir(NaFrente, true, false, janela));

        Assert.Equal(SituacaoDaJanela.Procurando, JanelaDaReuniao.Decidir(null, true, false, janela));
        Assert.Equal(SituacaoDaJanela.SemJanela, JanelaDaReuniao.Decidir(NaFrente, false, false, null));
        Assert.Equal(SituacaoDaJanela.Minimizada, JanelaDaReuniao.Decidir(NaFrente, true, true, janela));
        // A extensão já disse que a aba saiu da frente.
        Assert.Equal(SituacaoDaJanela.ForaDaFrente,
            JanelaDaReuniao.Decidir(NaFrente with { NaFrente = false }, true, false, janela));
    }

    [Fact]
    public void TrocarDeAbaPausaAntesDeAExtensaoAvisar()
    {
        // O título da janela muda no instante da troca; a extensão avisa depois.
        // Nesse meio, a aba ainda consta como na frente, e o título já é outro.
        Assert.Equal(SituacaoDaJanela.ForaDaFrente,
            JanelaDaReuniao.Decidir(NaFrente, true, false, "Caixa de entrada - Gmail - Google Chrome"));
    }

    [Fact]
    public void TodaPausaTemAvisoEGravarNaoTem()
    {
        foreach (var s in Enum.GetValues<SituacaoDaJanela>())
            Assert.Equal(s != SituacaoDaJanela.Gravando, JanelaDaReuniao.Aviso(s) is { Length: > 0 });
    }

    [Theory]
    [InlineData(1920, 1080, 1920, 1080, 0, 0, 1920, 1080)]   // do mesmo tamanho: inteira
    [InlineData(960, 540, 1920, 1080, 0, 0, 1920, 1080)]     // menor: amplia
    [InlineData(1000, 1000, 1920, 1080, 420, 0, 1080, 1080)] // quadrada: faixas dos lados
    [InlineData(2560, 1080, 1920, 1080, 0, 135, 1920, 810)]  // larga: faixas em cima e embaixo
    public void AJanelaCabeNoQuadroSemDistorcer(int l, int a, int ql, int qa, int x, int y, int el, int ea) =>
        Assert.Equal((x, y, el, ea), JanelaDaReuniao.Encaixar(l, a, ql, qa));

    [Fact]
    public void JanelaSemTamanhoNaoOcupaNada() =>
        Assert.Equal((0, 0, 0, 0), JanelaDaReuniao.Encaixar(0, 500, 1920, 1080));
}
