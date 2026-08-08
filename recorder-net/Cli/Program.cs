using MeetingRecorder.Capture;
using MeetingRecorder.Core;
using NAudio.CoreAudioApi;

// CLI espelho do `python capture.py --seconds 30`: captura sem bandeja, para
// validar o núcleo contra o critério A antes de existir UI.
//
//   Capture.exe --list
//   Capture.exe --seconds 30 --track system
//   Capture.exe --seconds 30 --out ../data/recordings

var argumentos = ParseArgs(args);

if (argumentos.ContainsKey("list"))
{
    ListarDispositivos();
    return 0;
}

int segundos = int.TryParse(argumentos.GetValueOrDefault("seconds"), out int s) ? s : 30;
string faixa = argumentos.GetValueOrDefault("track") ?? "both";
string saida = argumentos.GetValueOrDefault("out")
    ?? Path.Combine(Directory.GetCurrentDirectory(), "gravacoes");

WasapiTrackCapture.Diagnostico = argumentos.ContainsKey("debug");

var pasta = Path.Combine(saida, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
Directory.CreateDirectory(pasta);

var enumerador = new MMDeviceEnumerator();
var capturas = new List<WasapiTrackCapture>();

try
{
    if (faixa is "system" or "both")
    {
        // Loopback do dispositivo de saída padrão: o que os outros participantes
        // dizem, do jeito que sai pelos alto-falantes.
        var alto = enumerador.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        capturas.Add(new WasapiTrackCapture(alto, loopback: true,
            Path.Combine(pasta, "system.wav"), "system"));
    }
    if (faixa is "mic" or "both")
    {
        var microfone = enumerador.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        capturas.Add(new WasapiTrackCapture(microfone, loopback: false,
            Path.Combine(pasta, "mic.wav"), "mic"));
    }

    // O cronômetro parte ANTES do Iniciar(): a inicialização do WASAPI leva
    // algumas centenas de milissegundos e o dispositivo já captura nesse
    // intervalo. Marcando depois, o áudio da inicialização entra no arquivo sem
    // contar no tempo pedido — medido: 10,48 s gravados para 10 s pedidos.
    var inicio = DateTime.UtcNow;
    // Origem única para as duas faixas: é o que faz o alinhamento entre elas ser
    // construção e não inferência.
    long origem = WasapiTrackCapture.QpcAgora();

    foreach (var c in capturas)
    {
        c.Iniciar(origem);
        Console.WriteLine($"  {c.Stats.Nome,-7} {c.NomeDispositivo} @ {c.TaxaNativa} Hz");
    }

    Console.WriteLine($"\ngravando {segundos}s em {pasta}");
    Console.WriteLine("Ctrl+C para parar antes\n");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    while (!cts.IsCancellationRequested &&
           (DateTime.UtcNow - inicio).TotalSeconds < segundos)
    {
        Thread.Sleep(500);
        var decorrido = (DateTime.UtcNow - inicio).TotalSeconds;
        Console.Write($"\r  {decorrido,5:F1}s  " + string.Join("  ", capturas.Select(c =>
            $"{c.Stats.Nome}={c.Stats.AmostrasEscritas / (double)CrashSafeWavWriter.TaxaAlvo,5:F1}s" +
            $"{(c.Stats.JaOuviu ? "" : " (mudo)")}")));
    }
    Console.WriteLine();

    foreach (var c in capturas) c.Parar();

    var system = capturas.FirstOrDefault(c => c.Stats.Nome == "system")?.Stats
                 ?? new TrackStats { Nome = "system" };
    var mic = capturas.FirstOrDefault(c => c.Stats.Nome == "mic")?.Stats
              ?? new TrackStats { Nome = "mic" };

    var meta = Meta.Montar(DateTimeOffset.Now, system, mic,
        capturas.FirstOrDefault(c => c.Stats.Nome == "system")?.NomeDispositivo ?? "-",
        capturas.FirstOrDefault(c => c.Stats.Nome == "mic")?.NomeDispositivo ?? "-",
        capturas.FirstOrDefault(c => c.Stats.Nome == "system")?.TaxaNativa ?? 0,
        capturas.FirstOrDefault(c => c.Stats.Nome == "mic")?.TaxaNativa ?? 0);

    File.WriteAllText(Path.Combine(pasta, "meta.json"), meta.ParaJson());

    Console.WriteLine($"\n{meta.DurationS:F1}s gravados\n");
    foreach (var c in capturas)
    {
        var st = c.Stats;
        var l = c.Linha;
        Console.WriteLine($"  {st.Nome,-7} {st.AmostrasEscritas / (double)CrashSafeWavWriter.TaxaAlvo,6:F1}s  " +
            $"pico_rms={st.PicoRms:F4}  deriva={st.CorrecoesDeriva}x ({st.DerivaLiquidaAmostras:+#;-#;0} amostras)  " +
            $"silencio_inserido={(l?.SilencioInserido ?? 0) / (double)CrashSafeWavWriter.TaxaAlvo:F1}s" +
            (st.SemAudio ? "  <- SEM AUDIO" : ""));
        if (l is { PacotesComDescontinuidade: > 0 } or { PacotesComErroDeTimestamp: > 0 })
            Console.WriteLine($"          anomalias: descontinuidade={l.PacotesComDescontinuidade} " +
                              $"timestamp={l.PacotesComErroDeTimestamp}");
    }

    if (capturas.Count == 2)
    {
        long d = Math.Abs(system.AmostrasEscritas - mic.AmostrasEscritas);
        Console.WriteLine($"\n  desalinhamento entre faixas: {d} amostras " +
                          $"({d * 1000.0 / CrashSafeWavWriter.TaxaAlvo:F1} ms)");
    }

    Console.WriteLine($"\n{pasta}");
    return 0;
}
finally
{
    foreach (var c in capturas) c.Dispose();
    enumerador.Dispose();
}

static void ListarDispositivos()
{
    using var e = new MMDeviceEnumerator();
    Console.WriteLine("saida (loopback disponivel):");
    foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        Console.WriteLine($"   {d.FriendlyName}  @ {d.AudioClient.MixFormat.SampleRate} Hz");
    Console.WriteLine("\nentrada:");
    foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        Console.WriteLine($"   {d.FriendlyName}  @ {d.AudioClient.MixFormat.SampleRate} Hz");
}

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
