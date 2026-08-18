using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O registro em disco — a diferença entre adivinhar e saber.
/// </summary>
/// <remarks>
/// Nasceu em 18/08/2026: a transcrição de um usuário derrubou o Windows dele e
/// não havia nada para olhar. O que estes testes protegem é a propriedade que
/// torna um log confiável — <b>ele nunca pode ser o motivo da falha</b>.
/// </remarks>
public sealed class RegistroTests
{
    [Fact]
    public void EscreverNuncaLevantaExcecao()
    {
        // Um log que derruba o app é pior que não ter log. Isto vale inclusive
        // para o caso em que o perfil do usuário não é gravável.
        Assert.Null(Record.Exception(() => Registro.Escrever("teste", "linha")));
        Assert.Null(Record.Exception(() => Registro.Escrever("", "")));
    }

    [Fact]
    public void OCaminhoFicaJuntoDosOutrosDadosDoApp()
    {
        // Junto do app.json e das vozes: quem for procurar acha, e o
        // desinstalador já sabe não apagar essa pasta.
        Assert.Contains(".meeting-transcription", Registro.Caminho);
        Assert.EndsWith("registro.log", Registro.Caminho);
    }

    [Fact]
    public void LerSemArquivoDevolveVazioENaoQuebra()
    {
        Assert.NotNull(Registro.Ultimas());
    }
}
