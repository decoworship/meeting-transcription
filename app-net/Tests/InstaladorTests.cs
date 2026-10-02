using System.Text.RegularExpressions;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O que o instalador decide e que só aparece na máquina de outra pessoa.
/// </summary>
/// <remarks>
/// O <c>instalador/MeetingApp.iss</c> é Inno Setup e não compila junto com o
/// app. Uma diretiva que some dele não quebra build nenhum: quebra o app de
/// quem instalou, e só lá.
/// </remarks>
public sealed class InstaladorTests
{
    /// <remarks>
    /// <para>
    /// <b>O Inno 6.7 liga o RedirectionGuard no próprio instalador, e o app que
    /// ele abre herda.</b> A ajuda diz que a mitigação não passa aos processos
    /// filhos; medido em 02/10/2026 com um instalador mínimo do 6.7.3, ela passa
    /// — aos do <c>[Run]</c> durante a instalação e ao "Abrir o PulseMeet" do
    /// fim. O app passa adiante aos motores Python, e aí nenhum deles atravessa
    /// link simbólico ou junção criados sem elevação.
    /// </para>
    /// <para>
    /// Com o Modo de Desenvolvedor ligado, o cache do HuggingFace é feito
    /// exatamente desses links. Na máquina da segunda usuária, toda transcrição
    /// aberta pelo instalador falhava com "Unable to open file 'model.bin'"
    /// (WinError 448 por baixo), e voltava a funcionar ao reabrir o app pelo
    /// menu Iniciar — até a atualização seguinte.
    /// </para>
    /// </remarks>
    [Fact]
    public void OInstaladorNaoPassaORedirectionGuardAoApp()
    {
        string? iss = Achar(Path.Combine("instalador", "MeetingApp.iss"));
        // Fora do repositório não há o que conferir — ver MarcaTests.
        if (iss is null) return;

        Assert.Matches(new Regex(@"^\s*RedirectionGuard\s*=\s*no\s*$",
                                 RegexOptions.Multiline | RegexOptions.IgnoreCase),
                       File.ReadAllText(iss));
    }

    /// <summary>Sobe do binário de teste até achar o arquivo, ou nulo.</summary>
    private static string? Achar(string relativo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string tentativa = Path.Combine(dir.FullName, relativo);
            if (File.Exists(tentativa)) return tentativa;
            dir = dir.Parent;
        }
        return null;
    }
}
