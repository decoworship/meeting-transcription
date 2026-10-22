using System.Runtime.CompilerServices;
using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O formato de hoje, congelado antes da ata por tema (ATA-5).
/// </summary>
/// <remarks>
/// <para>
/// A chave "De hoje" em Ajustes promete o app de antes, byte a byte: mesmo
/// prompt e mesmo <c>ata.md</c>. Esta é a promessa escrita como teste. As
/// referências foram gravadas pelo código anterior à mudança.
/// </para>
/// <para>
/// Para regravar, de propósito: <c>ATA_REFERENCIAS=gravar dotnet test …</c>.
/// Regravar para fazer um teste passar é desfazer a promessa.
/// </para>
/// </remarks>
public sealed class ReferenciasDaAtaTests
{
    private static string Pasta([CallerFilePath] string aqui = "") =>
        Path.Combine(Path.GetDirectoryName(aqui)!, "Referencias");

    public static TheoryData<string> Tipos => new() { "cliente-update", "trabalho", "kickoff", "daily" };

    private static void Conferir(string arquivo, string atual)
    {
        string caminho = Path.Combine(Pasta(), arquivo);
        if (Environment.GetEnvironmentVariable("ATA_REFERENCIAS") == "gravar")
        {
            Directory.CreateDirectory(Pasta());
            File.WriteAllText(caminho, atual);
        }
        Assert.True(File.Exists(caminho), $"falta a referência {arquivo}");
        Assert.Equal(File.ReadAllText(caminho), atual);
    }

    [Theory, MemberData(nameof(Tipos))]
    public void OAtaMdClassicoNaoMuda(string tipo)
    {
        string md = RedatorDeAta.Escrever(ReuniaoDeExemplo.AtaSemSecao(),
            ModelosDeAta.Buscar(tipo)!, ReuniaoDeExemplo.Contexto());
        Conferir($"ata-classica-{tipo}.md", md);
    }

    [Theory, MemberData(nameof(Tipos))]
    public void OPromptClassicoNaoMuda(string tipo)
    {
        var segmentos = ReuniaoDeExemplo.Segmentos();
        string prompt = PromptDeAta.Montar(ModelosDeAta.Buscar(tipo)!,
            ReuniaoDeExemplo.Contexto(), segmentos, RoteiroDeFatos.De(segmentos));
        Conferir($"prompt-classico-{tipo}.txt", prompt);
    }

    [Fact]
    public void OEsquemaClassicoNaoMuda() => Conferir("esquema-classico.json", AtaGerada.Esquema);
}
