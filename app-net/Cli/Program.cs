using System.Diagnostics;
using MeetingApp.Sidecar;

// Valida o contrato do sidecar por linha de comando, antes de existir UI — a
// ordem de trabalho da FASE2.md. É o equivalente, para a Fase 2, do que o
// `Capture.exe --list` foi para a Fase 1: a prova de que a peça funciona sem
// depender de nada visual.
//
//   Sidecar.exe --motor python3 --script motores/diarizacao/motor.py --audio mix.wav
//   Sidecar.exe ... --cancelar-em 2      (prova que cancelar mata o processo)
//
// O que vier depois de `--` é repassado ao motor, como manda o costume.

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Antes do `--` são as opções desta ferramenta; depois, os argumentos do motor.
int separador = Array.IndexOf(args, "--");
string[] doMotor = separador < 0 ? [] : args[(separador + 1)..];
var arg = ParseArgs(separador < 0 ? args : args[..separador]);
string motor = arg.GetValueOrDefault("motor") ?? "python3";
string script = arg.GetValueOrDefault("script") ?? "motores/diarizacao/motor.py";
string? audio = arg.GetValueOrDefault("audio");
double? cancelarEm = double.TryParse(arg.GetValueOrDefault("cancelar-em"), out double s) ? s : null;

if (audio is null)
{
    Console.Error.WriteLine("uso: --audio <arquivo.wav> [--motor python3] [--script motor.py] "
                            + "[--cancelar-em <segundos>]");
    return 2;
}

var relogio = Stopwatch.StartNew();

MotorSidecar sidecar;
try
{
    sidecar = await MotorSidecar.IniciarAsync(motor, [script, .. doMotor]);
}
catch (MotorException e)
{
    Console.Error.WriteLine($"o motor não subiu: {e.Message}");
    return 3;
}

using (sidecar)
{
    sidecar.AoRegistrar += linha => Console.Error.WriteLine($"  motor: {linha}");
    Console.WriteLine($"motor '{sidecar.Nome}' v{sidecar.Versao} pronto em {relogio.ElapsedMilliseconds} ms");

    using var cancelamento = new CancellationTokenSource();
    if (cancelarEm is { } quando)
    {
        Console.WriteLine($"cancelando em {quando} s, de propósito");
        cancelamento.CancelAfter(TimeSpan.FromSeconds(quando));
    }

    try
    {
        relogio.Restart();
        var segmentos = await sidecar.DiarizarAsync(audio,
            (pct, texto) => Console.WriteLine($"  [{pct,4:P0}] {texto}"),
            cancelamento.Token);

        Console.WriteLine($"\n{segmentos.Count} segmentos em {relogio.Elapsed.TotalSeconds:F1} s, "
                          + $"{segmentos.Select(x => x.Falante).Distinct().Count()} falantes");
        foreach (var seg in segmentos.Take(10))
            Console.WriteLine($"  {seg.Inicio,8:F2} → {seg.Fim,8:F2}  {seg.Falante}");
        if (segmentos.Count > 10) Console.WriteLine($"  ... e mais {segmentos.Count - 10}");
    }
    catch (OperationCanceledException)
    {
        // Cancelar é matar o processo — o critério B da Fase 2 mede exatamente
        // o tempo entre pedir e a VRAM voltar.
        Console.WriteLine($"cancelado, motor morto em {relogio.Elapsed.TotalSeconds:F2} s");
        return 4;
    }
    catch (MotorException e)
    {
        // O critério C: o motor falha, a mensagem é legível e o app continua.
        Console.Error.WriteLine($"o motor falhou: {e.Message}");
        return 5;
    }
}
return 0;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var d = new Dictionary<string, string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--")) continue;
        string chave = args[i][2..];
        d[chave] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "";
    }
    return d;
}
