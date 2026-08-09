using System.Diagnostics;
using MeetingApp.Nucleo;
using MeetingApp.Sidecar;

namespace MeetingApp.Cli;

/// <summary>
/// O pipeline inteiro por linha de comando: gravação → mix → ASR → diarização →
/// <c>TranscriptionResult</c> JSON.
/// </summary>
/// <remarks>
/// Passo 2 da ordem de trabalho da FASE2.md. Existe para provar o caminho
/// completo antes de existir UI — e para a paridade com o app Gradio poder ser
/// medida comparando dois arquivos, em vez de duas telas.
/// </remarks>
internal static class Pipeline
{
    public static async Task<int> ExecutarAsync(
        string pasta, string python, string? vocabulario, string? idioma,
        string? destino, CancellationToken ct)
    {
        string mic = Path.Combine(pasta, "mic.wav");
        string sistema = Path.Combine(pasta, "system.wav");
        foreach (string f in new[] { mic, sistema })
        {
            if (!File.Exists(f))
            {
                Console.Error.WriteLine($"faixa ausente: {f}");
                return 2;
            }
        }

        var relogio = Stopwatch.StartNew();
        Console.WriteLine($"lendo as faixas de {Path.GetFileName(pasta)}...");
        var faixas = Faixas.Ler(mic, sistema);

        // O mix vai para junto da gravação: é derivado, refazível, e ninguém
        // precisa dele depois — mas enquanto o pipeline roda ele tem que existir
        // num caminho que o motor Python consiga abrir.
        string caminhoDoMix = Path.Combine(pasta, "mix.wav");
        faixas.EscreverMix(caminhoDoMix);
        Console.WriteLine($"  mix: {faixas.Mic.Length / (double)Faixas.TaxaDeAmostragem:F1} s "
                          + $"em {relogio.ElapsedMilliseconds} ms");

        // ASR primeiro, diarização depois, cada um no seu processo: numa placa
        // de 6 GB os dois modelos não cabem juntos, e processos separados fazem
        // a VRAM do primeiro voltar antes do segundo subir.
        Transcricao transcricao;
        relogio.Restart();
        await using (var asr = await Subir(python, "motores/asr/motor.py", ct))
        {
            transcricao = await asr.Motor.TranscreverAsync(caminhoDoMix, vocabulario, idioma,
                (pct, texto) => Progresso("asr", pct, texto), ct);
        }
        Console.WriteLine($"\n  asr: {transcricao.Segmentos.Count} segmentos, "
                          + $"idioma {transcricao.Idioma}, em {relogio.Elapsed.TotalSeconds:F1} s");

        // A diarização roda só no system.wav: o que o microfone captou já se
        // sabe de quem é, e dar o mix ao pyannote faria ele tentar separar você
        // de você mesmo.
        IReadOnlyList<SegmentoDeFalante> diarizacao;
        relogio.Restart();
        await using (var diar = await Subir(python, "motores/diarizacao/motor.py", ct))
        {
            diarizacao = await diar.Motor.DiarizarAsync(sistema,
                (pct, texto) => Progresso("diarizacao", pct, texto), ct);
        }
        Console.WriteLine($"\n  diarizacao: {diarizacao.Count} trechos, "
                          + $"{diarizacao.Select(d => d.Falante).Distinct().Count()} falantes, "
                          + $"em {relogio.Elapsed.TotalSeconds:F1} s");

        var segmentos = transcricao.Segmentos
            .Select(s => new SegmentoFinal { Start = s.Inicio, End = s.Fim, Text = s.Texto })
            .ToList();

        Montagem.AtribuirFalantes(segmentos, diarizacao);
        int meus = Montagem.AtribuirDono(segmentos, faixas);
        Console.WriteLine($"  microfone: {meus}/{segmentos.Count} segmentos são seus");

        var resultado = new ResultadoDaTranscricao
        {
            Language = transcricao.Idioma,
            Duration = transcricao.Duracao,
            Segments = segmentos,
        };

        string saida = destino ?? Path.Combine(pasta, "transcricao.json");
        await File.WriteAllTextAsync(saida, resultado.ParaJson(), ct);
        Console.WriteLine($"\n{saida}");
        return 0;
    }

    private static void Progresso(string motor, double pct, string texto) =>
        Console.Write($"\r  {motor}: {pct,6:P0} {texto}          ");

    private static async Task<Descartavel> Subir(string python, string script, CancellationToken ct)
        => new(await MotorSidecar.IniciarAsync(python, [script], ct));

    /// <summary>
    /// Só para poder usar <c>await using</c>: o motor morre ao sair do bloco,
    /// inclusive por exceção, e é isso que impede processo órfão.
    /// </summary>
    private sealed class Descartavel(MotorSidecar motor) : IAsyncDisposable
    {
        public MotorSidecar Motor { get; } = motor;

        public ValueTask DisposeAsync()
        {
            Motor.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
