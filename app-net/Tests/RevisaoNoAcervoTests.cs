using System.Text.Json;
using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
using Xunit;
using Xunit.Abstractions;

namespace MeetingApp.Tests;

/// <summary>
/// A revisão de termos contra as transcrições reais desta máquina.
/// </summary>
/// <remarks>
/// Pula quando o acervo não está presente — é máquina do dono do produto, não
/// da CI. Serve para ver o que a regra faria em dado de verdade antes de a
/// mudança chegar a uma transcrição nova.
/// </remarks>
public sealed class RevisaoNoAcervoTests(ITestOutputHelper saida)
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
    public void OQueARegraFariaNoAcervo()
    {
        if (Acervo() is not { } raiz) { saida.WriteLine("acervo ausente — pulado"); return; }

        int comProposta = 0, total = 0;
        foreach (string pasta in Directory.GetDirectories(raiz).OrderBy(x => x))
        {
            string tj = Path.Combine(pasta, "transcricao.json");
            string mj = Path.Combine(pasta, "meta.json");
            if (!File.Exists(tj)) continue;

            var dados = ResultadoDaTranscricao.DeJson(File.ReadAllText(tj));
            if (dados is null) continue;

            var (nomes, emails) = ConvidadosDaAgenda.Ler(pasta);
            var pessoas = Organizacoes.Classificar(nomes, emails, []);
            var entidades = new List<string>(pessoas.Select(p => p.Nome).Where(n => n.Contains(' ')));
            if (File.Exists(Path.Combine(pasta, "reuniao.json")))
            {
                var v = DadosDaReuniao.Ler(pasta);
                if (v.Cliente is { Length: > 0 }) entidades.Add(v.Cliente);
                if (v.Projeto is { Length: > 0 }) entidades.Add(v.Projeto);
            }
            if (entidades.Count == 0) continue;

            total++;
            var props = RevisaoDeTermos.Validar(
                RevisaoDeTermos.Propor(dados.Segments.Select(s => s.Text), entidades), entidades);
            if (props.Count == 0) continue;

            comProposta++;
            saida.WriteLine($"{Path.GetFileName(pasta)}: "
                + string.Join(", ", props.Select(p => $"{p.De}→{p.Para}")));
        }
        saida.WriteLine($"\n{comProposta} de {total} gravações teriam alguma troca.");
    }
}
