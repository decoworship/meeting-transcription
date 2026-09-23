using System.Text.RegularExpressions;
using MeetingApp.Nucleo;
using Xunit;
using Xunit.Abstractions;

namespace MeetingApp.Tests;

/// <summary>
/// A correção da legenda contra a reunião real que a justificou.
/// </summary>
/// <remarks>
/// Pula quando o acervo não está presente — é máquina do dono do produto, não
/// da CI. Mede sobre <c>2026-09-23_10-00-17</c>, a reunião da tabela em
/// "A medição" do plano do <c>VIVO-3</c>.
/// </remarks>
public sealed class CorrecaoDaLegendaNoAcervoTests(ITestOutputHelper saida)
{
    private static string? Acervo()
    {
        foreach (string c in new[]
        {
            "/mnt/c/Users/andre/OneDrive/Documents/MeetingRecordings",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                         "MeetingRecordings"),
        })
            if (Directory.Exists(c)) return c;
        return null;
    }

    [Fact]
    public void AReuniaoDe23De09RecuperaOWifi()
    {
        if (Acervo() is not { } raiz) { saida.WriteLine("acervo ausente — pulado"); return; }
        string pasta = Path.Combine(raiz, "2026-09-23_10-00-17");
        if (LegendaAoVivo.Ler(pasta) is not { Trechos.Count: > 0 } legenda)
        { saida.WriteLine("reunião ausente — pulado"); return; }

        const string voc = "KPI, SDK, S3, life cycle, persona, Tânia, Prada, Knauth, API, "
            + "Cloud, Wellington, César, Ipera, Miro, Massiva, Wifi, Rede, APP, Diego, NPS, "
            + "modem, versionamento, white label, Beegol, Service, V.TAL, Beta, NIO, lambda, "
            + "ECS, PR, Webhook";
        var entidades = CorrecaoDeTermos.Entidades(pasta, voc, "Agentes", "Agentes (Interno)");

        var r = CorrecaoDaLegenda.Corrigir(legenda.Trechos, voc, entidades);

        // Contagem sem diferenciar caixa, como o runner de 23/09: das onze
        // ocorrências de "wifi" na reunião, uma já estava certa na legenda
        // crua em minúscula ("wifi") — a cadeia não normaliza caixa de uma
        // palavra já bem grafada, só corrige o que ouviu errado. As outras
        // nove eram "Wi Fi" separado, e a cadeia converteu as nove.
        int wifi = r.Trechos.Sum(t =>
            Regex.Matches(t.Texto, @"(?<!\w)Wifi(?!\w)", RegexOptions.IgnoreCase).Count);
        saida.WriteLine($"{r.Trocas} trocas, {r.Fundidos} fundidos, Wifi = {wifi}");

        // O runner de 23/09, que juntava por fala sem fundir, chegou a 11.
        // A fusão só pode igualar ou superar isso.
        Assert.True(wifi >= 11, $"Wifi = {wifi}");
    }
}
