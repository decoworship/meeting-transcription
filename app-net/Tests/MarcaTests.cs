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
    public void OExecutavelTemONomeDaMarca()
    {
        // O nome do .exe é o AssemblyName: desde 24/09/2026 ele é PulseMeet.exe
        // (docs/MARCA.md). O título e o produto são o que o Gerenciador de
        // Tarefas e as propriedades do arquivo mostram.
        string? csproj = Achar(Path.Combine("app-net", "App", "MeetingApp.App.csproj"));
        if (csproj is null) return;
        string texto = File.ReadAllText(csproj);

        Assert.Equal(Marca.Nome, Propriedade(texto, "AssemblyName"));
        Assert.Equal(Marca.Nome, Propriedade(texto, "AssemblyTitle"));
        Assert.Equal(Marca.Nome, Propriedade(texto, "Product"));
        // O RootNamespace fica no nome antigo, e escrito: sem ele, o padrão é o
        // AssemblyName, e o namespace padrão e os recursos sem LogicalName
        // mudariam de nome junto com o .exe.
        Assert.Equal("MeetingApp", Propriedade(texto, "RootNamespace"));
    }

    [Fact]
    public void OInstaladorInstalaOExecutavelDaMarca()
    {
        string? iss = Achar(Path.Combine("instalador", "MeetingApp.iss"));
        if (iss is null) return;
        string texto = File.ReadAllText(iss);

        // O que instala, atalha, inicia e desinstala o .exe usa o nome da marca.
        Assert.Contains(@"Source: ""{#Payload}\{#Marca}.exe""", texto);
        Assert.Contains(@"Name: ""{group}\{#Marca}""; Filename: ""{app}\{#Marca}.exe""", texto);
        Assert.Contains(@"ValueName: ""{#Marca}""; ValueData: """"""{app}\{#Marca}.exe""""""", texto);
        Assert.Contains(@"UninstallDisplayIcon={app}\{#Marca}.exe", texto);
        Assert.Contains("OutputBaseFilename={#Marca}-{#Versao}-instalador", texto);
        // O grupo do menu Iniciar de antes da marca não é reaproveitado.
        Assert.Contains("UsePreviousGroup=no", texto);
        // E os atalhos que a pessoa fez são repontados pelo mesmo script do publicar.sh.
        Assert.Contains(@"-File """"{app}\repontar_atalhos.ps1"""" -Pasta """"{app}"""" -Aplicar", texto);
        // Escondido e esperado: um PowerShell que parasse para perguntar
        // travaria o instalador sem ninguém ver.
        Assert.Contains("-NoProfile -NonInteractive -ExecutionPolicy Bypass", texto);

        // O .exe velho só aparece para ser apagado.
        var velhas = texto.Split('\n')
            .Where(l => l.Contains("MeetingApp.exe") && !l.TrimStart().StartsWith(';'))
            .Select(l => l.Trim()).ToList();
        Assert.Equal(new[] { @"Type: files; Name: ""{app}\MeetingApp.exe""" }, velhas);
    }

    [Fact]
    public void OsScriptsPublicamOExecutavelDaMarca()
    {
        // O publicar.sh (a máquina do dono) e o montar_instalador.sh (o
        // instalador) copiam o .exe pelo nome, por texto.
        foreach (string script in new[] { "publicar.sh", "montar_instalador.sh" })
        {
            string? caminho = Achar(Path.Combine("tools", script));
            if (caminho is null) continue;
            Assert.Contains($"{Marca.Nome}.exe", File.ReadAllText(caminho));
        }
        string? publicar = Achar(Path.Combine("tools", "publicar.sh"));
        if (publicar is not null)
            Assert.Contains("-NonInteractive", File.ReadAllText(publicar));
    }

    private static string? Propriedade(string csproj, string nome)
    {
        var m = Regex.Match(csproj, $@"<{nome}>([^<]*)</{nome}>");
        return m.Success ? m.Groups[1].Value : null;
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
