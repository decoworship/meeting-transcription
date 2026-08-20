using System.Diagnostics;
using MeetingApp.Nucleo;

namespace MeetingApp.Cli;

/// <summary>
/// O pipeline por linha de comando: gravação → mix → ASR → diarização →
/// <c>TranscriptionResult</c> JSON.
/// </summary>
/// <remarks>
/// A lógica vive no <see cref="Transcritor"/>, no núcleo, porque o app usa a
/// mesma. O que sobra aqui é apresentação: onde estão os motores nesta máquina,
/// e como mostrar o andamento num terminal.
/// </remarks>
internal static class Pipeline
{
    public static async Task<int> ExecutarAsync(
        string pasta, string python, string? vocabulario, string? idioma,
        string? destino, bool filtrarSilencio, bool usarHotwords, CancellationToken ct)
    {
        // No desenvolvimento os motores estão no repositório e o Python é o do
        // ambiente; no app instalado eles vêm numa pasta ao lado do executável.
        var motores = new Motores(python, "motores/asr/motor.py",
                                  "motores/diarizacao/motor.py",
                                  "motores/modelos/motor.py");
        if (motores.OQueFalta() is { } falta)
        {
            Console.Error.WriteLine(falta);
            return 2;
        }

        var relogio = Stopwatch.StartNew();
        var transcritor = new Transcritor(motores);

        var resultado = await transcritor.ExecutarAsync(
            pasta, vocabulario, idioma, filtrarSilencio,
            p => Console.Write($"\r  {p.Etapa}: {p.Fracao,6:P0} {p.Texto}          "),
            usarHotwords: usarHotwords, ct: ct);

        Console.WriteLine($"\n\n{resultado.Segments.Count} segmentos, "
                          + $"idioma {resultado.Language}, "
                          + $"em {relogio.Elapsed.TotalSeconds:F1} s");

        int meus = resultado.Segments.Count(s => s.Speaker == "You");
        Console.WriteLine($"  microfone: {meus}/{resultado.Segments.Count} segmentos são seus");

        // As três réguas da FASE6 §4.1, impressas porque a comparação com e sem
        // hotwords se faz por elas — e ter que abrir o JSON para contar é o que
        // faz a comparação não ser refeita. Segmento longo é o defeito: a
        // atribuição de falante é um rótulo por segmento, e um trecho de 40 s
        // com três pessoas dentro perde duas.
        int longos = resultado.Segments.Count(s => s.End - s.Start > 25);
        double falada = resultado.Segments.Sum(s => s.End - s.Start);
        int palavras = resultado.Segments.Sum(
            s => s.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        Console.WriteLine(
            $"  {palavras} palavras · {longos} segmentos acima de 25 s · "
            + $"{(resultado.Duration > 0 ? falada / resultado.Duration : 0):P1} da gravação "
            + $"tem fala · hotwords={(usarHotwords ? "ligado" : "desligado")}");

        // O Transcritor já grava transcricao.json ao lado da gravação; --saida
        // existe para comparar sem sobrescrever o que está lá.
        if (destino is { Length: > 0 })
        {
            await File.WriteAllTextAsync(destino, resultado.ParaJson(), ct);
            Console.WriteLine($"\n{destino}");
        }
        else
        {
            Console.WriteLine($"\n{Path.Combine(pasta, "transcricao.json")}");
        }
        return 0;
    }
}
