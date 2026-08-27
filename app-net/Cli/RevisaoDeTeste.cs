using System.Diagnostics;
using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;

namespace MeetingApp.Cli;

/// <summary>
/// Roda a revisão de termos numa gravação, sem transcrever de novo.
/// </summary>
/// <remarks>
/// Existe pelo mesmo motivo dos outros modos do Sidecar: provar o caminho antes
/// de existir tela. Mostra o que a regra propõe e o que o modelo propõe, lado a
/// lado, sem aplicar nada.
/// </remarks>
public static class RevisaoDeTeste
{
    public static async Task<int> ExecutarAsync(string pasta, string? modelo, bool comModelo,
                                                CancellationToken ct)
    {
        string arquivo = Path.Combine(pasta, "transcricao.json");
        if (!File.Exists(arquivo)) { Console.Error.WriteLine("sem transcricao.json"); return 2; }

        var dados = ResultadoDaTranscricao.DeJson(await File.ReadAllTextAsync(arquivo, ct));
        if (dados is null) { Console.Error.WriteLine("transcrição ilegível"); return 2; }

        var vinculo = DadosDaReuniao.Ler(pasta);
        var (nomes, emails) = ConvidadosDaAgenda.Ler(pasta);
        var pessoas = Organizacoes.Classificar(nomes, emails, []);
        var entidades = new List<string>();

        // O vocabulário do projeto é a fonte mais rica, e é a que o Transcritor
        // usa. Sem ela este modo mediria um cenário que o app nunca vive.
        var prefs = new Projetos().Preferencias(vinculo.Cliente ?? "", vinculo.Projeto ?? "");
        if (prefs?.InitialPrompt is { Length: > 0 } voc) entidades.Add(voc);

        if (vinculo.Cliente is { Length: > 0 }) entidades.Add(vinculo.Cliente);
        if (vinculo.Projeto is { Length: > 0 }) entidades.Add(vinculo.Projeto);
        entidades.AddRange(pessoas.Select(p => p.Nome).Where(n => n.Contains(' ')));

        var textos = dados.Segments.Select(s => s.Text).ToList();
        Console.WriteLine($"{Path.GetFileName(pasta)} · {textos.Count} trechos · "
                          + $"{entidades.Count} entidades: {string.Join(", ", entidades)}");

        var daRegra = RevisaoDeTermos.Validar(
            RevisaoDeTermos.Propor(textos, entidades), entidades);
        Console.WriteLine($"\nregra  ({daRegra.Count}): "
                          + string.Join(", ", daRegra.Select(p => $"{p.De}→{p.Para}")));

        if (!comModelo) return 0;

        var relogio = Stopwatch.StartNew();
        var motor = new MotorDeAta(CaminhosDoMotorDeAta.AoLadoDoExecutavel(modelo));
        var cruas = await PropositorDeModelo.ProporAsync(
            motor, textos, entidades,
            e => Console.Write($"\r  {e.Texto}                    "), ct);
        Console.WriteLine();

        var doModelo = RevisaoDeTermos.Validar(cruas, entidades);
        Console.WriteLine($"\nmodelo ({doModelo.Count} de {cruas.Count} propostas, "
                          + $"{relogio.Elapsed.TotalSeconds:F0}s): "
                          + string.Join(", ", doModelo.Select(p => $"{p.De}→{p.Para}")));
        Console.WriteLine("  descartadas pela validação: "
                          + string.Join(", ", cruas.Where(c => !doModelo.Any(b => b.De == c.De))
                                                   .Select(p => $"{p.De}→{p.Para}")));
        return 0;
    }
}
