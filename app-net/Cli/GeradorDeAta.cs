using System.Diagnostics;
using System.Text.Json;
using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;

namespace MeetingApp.Cli;

/// <summary>
/// Gera a ata de uma gravação já transcrita, pela linha de comando.
/// </summary>
/// <remarks>
/// Existe pelo mesmo motivo que o pipeline de transcrição tem um modo de linha
/// de comando: provar o caminho inteiro <b>antes de existir tela</b>. Foi assim
/// na Fase 2, e é o que permite medir o motor de ata contra reunião real sem
/// clicar em nada.
///
///   Sidecar.exe --ata "C:\...\2026-08-13_14-30-15" --tipo cliente-update
/// </remarks>
public static class GeradorDeAta
{
    public static async Task<int> ExecutarAsync(string pasta, string tipoId, string? modelo,
                                                CancellationToken ct)
    {
        string arquivo = Path.Combine(pasta, "transcricao.json");
        if (!File.Exists(arquivo))
        {
            Console.Error.WriteLine($"{pasta} não tem transcricao.json — transcreva primeiro.");
            return 2;
        }

        var tipo = ModelosDeAta.Buscar(tipoId);
        if (tipo is null)
        {
            Console.Error.WriteLine(
                $"tipo desconhecido: {tipoId}\ntipos: "
                + string.Join(", ", ModelosDeAta.Todos().Select(m => m.Id)));
            return 2;
        }

        var dados = ResultadoDaTranscricao.DeJson(await File.ReadAllTextAsync(arquivo, ct));
        if (dados is null)
        {
            Console.Error.WriteLine("transcrição ilegível.");
            return 2;
        }

        var vinculo = DadosDaReuniao.Ler(pasta);
        var ctx = new ContextoDaReuniao
        {
            Cliente = vinculo.Cliente ?? dados.Client,
            Projeto = vinculo.Projeto ?? dados.Project,
            Data = dados.Date ?? Transcritor.DataDaReuniao(pasta),
            DuracaoS = dados.Duration ?? 0,
            Falantes = [.. dados.Segments.Select(s => s.Speaker)
                .Where(s => s is { Length: > 0 }).Distinct()!],
            Notas = Notas.Ler(pasta),
        };

        var roteiro = RoteiroDeFatos.De(dados.Segments);
        string prompt = PromptDeAta.Montar(tipo, ctx, dados.Segments, roteiro);

        var caminhos = CaminhosDoMotorDeAta.AoLadoDoExecutavel(modelo);
        Console.WriteLine($"reunião  {Path.GetFileName(pasta)} · {ctx.DuracaoS / 60:F0} min · "
                          + $"{dados.Segments.Count} trechos");
        Console.WriteLine($"tipo     {tipo.Nome}{(tipo.DoUsuario ? " (do usuário)" : "")}");
        Console.WriteLine($"prompt   {prompt.Length:N0} chars · roteiro com {roteiro.Count} fatos");

        // O dimensionamento agora depende do modelo e da placa, e não só do
        // relógio — então imprimir aqui exige ler o GGUF, o que só vale se ele
        // estiver mesmo no lugar. Sem ele, o GerarAsync abaixo já diz o que falta.
        if (File.Exists(caminhos.Modelo))
        {
            var meta = MetadadosDoGguf.Ler(caminhos.Modelo);
            Console.WriteLine($"modelo   {meta.Nome} · {meta.Camadas} camadas · "
                              + $"contexto máximo {meta.ContextoMaximo:N0}");
        }

        var motor = new MotorDeAta(caminhos);
        var relogio = Stopwatch.StartNew();

        var ata = await motor.GerarAsync(prompt, ctx.DuracaoS,
            p => Console.WriteLine($"  {p.Etapa}: {p.Texto}"), ct);

        Console.WriteLine($"gerou em {relogio.Elapsed.TotalSeconds:F0} s: "
                          + $"{ata.Secoes.Count} seções, {ata.Decisoes.Count} decisões, "
                          + $"{ata.Acoes.Count} ações");

        int antes = ata.Observacoes.Count;
        VerificadorDeAta.Conferir(ata, dados.Segments,
            [.. ctx.Convidados.Concat(ctx.Falantes)], roteiro);
        int mexeu = ata.Observacoes.Count - antes;
        Console.WriteLine(mexeu > 0
            ? $"verificador: {mexeu} observação(ões) — ver o fim da ata"
            : "verificador: nada a corrigir");

        string md = RedatorDeAta.Escrever(ata, tipo, ctx);
        string destino = Path.Combine(pasta, "ata.md");
        await File.WriteAllTextAsync(destino, md, ct);
        await File.WriteAllTextAsync(Path.Combine(pasta, "ata.json"), ata.ParaJson(), ct);

        Console.WriteLine($"\n{destino}  ({md.Length:N0} chars)");
        return 0;
    }
}
