using System.Text.RegularExpressions;
using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A marca está escrita em dois lugares que não se falam.
/// </summary>
/// <remarks>
/// O C# tem o <see cref="Marca"/>; o instalador tem o <c>#define Marca</c> do
/// <c>instalador/MeetingApp.iss</c>, que é Inno Setup e não compila junto.
/// Uma troca de nome que esquece um dos dois produz um instalador que anuncia
/// um produto e instala outro — e isso só aparece na tela de quem instalou.
/// <para>
/// Este teste é o que torna a troca de marca uma edição de duas linhas em vez
/// de uma caçada. Se ele falhar, os dois arquivos discordam; acertar os dois é
/// o conserto.
/// </para>
/// </remarks>
public sealed class MarcaTests
{
    [Fact]
    public void OInstaladorDizOMesmoNomeQueOApp()
    {
        string? iss = Achar(Path.Combine("instalador", "MeetingApp.iss"));
        // Fora do repositório não há o que comparar. Não é falha: a suíte é
        // net8.0 portátil e roda também de um diretório publicado.
        if (iss is null) return;

        var m = Regex.Match(File.ReadAllText(iss),
                            @"^\s*#define\s+Marca\s+""([^""]*)""", RegexOptions.Multiline);

        Assert.True(m.Success,
            $"{iss} perdeu o #define Marca — o instalador voltou a ter o nome escrito à mão.");
        Assert.Equal(Marca.Nome, m.Groups[1].Value);
    }

    [Fact]
    public void OSimboloEOQueGeraOsIcones()
    {
        string? svg = Achar(Path.Combine("assets", "logo.svg"));
        if (svg is null) return;

        // Só a existência e o viewBox: o desenho muda, o quadro em que os
        // .ico são compostos não. Um logo.svg com outro viewBox sai cortado
        // nos 16 px da bandeja, e isso não aparece em tamanho grande.
        Assert.Contains("viewBox=\"0 0 496 496\"", File.ReadAllText(svg));
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
