using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingRecorder.Core;

/// <summary>
/// O <c>meta.json</c> que acompanha cada gravação.
/// </summary>
/// <remarks>
/// <b>O schema não pode mudar.</b> O app de transcrição lê estes campos em
/// <c>src/web/recordings.py</c>, e a Fase 1 troca o gravador sem que ele saiba.
/// Nomes em snake_case e ordem idênticos aos do gravador Python — os testes
/// comparam o JSON gerado contra um arquivo real produzido por ele.
/// </remarks>
public sealed class Meta
{
    [JsonPropertyName("recorded_at")] public required string RecordedAt { get; init; }
    [JsonPropertyName("duration_s")] public required double DurationS { get; init; }
    [JsonPropertyName("sample_rate")] public int SampleRate { get; init; } = CrashSafeWavWriter.TaxaAlvo;
    [JsonPropertyName("tracks")] public required Dictionary<string, MetaTrack> Tracks { get; init; }
    [JsonPropertyName("meeting")] public required MetaMeeting Meeting { get; init; }

    public string ParaJson() => JsonSerializer.Serialize(this, MetaJson.Default.Meta);

    public static Meta Montar(DateTimeOffset quando, TrackStats system, TrackStats mic,
                              string dispositivoSystem, string dispositivoMic,
                              int taxaNativaSystem, int taxaNativaMic,
                              MetaMeeting? reuniao = null)
    {
        // Pela faixa mais longa, não pela do sistema: o loopback WASAPI não
        // entrega nada enquanto nada toca, então uma reunião só de escuta (ou um
        // trecho em silêncio) reportaria duração zero.
        double duracao = Math.Max(system.AmostrasEscritas, mic.AmostrasEscritas)
                         / (double)CrashSafeWavWriter.TaxaAlvo;

        return new Meta
        {
            RecordedAt = quando.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz"),
            DurationS = Math.Round(duracao, 2),
            Tracks = new Dictionary<string, MetaTrack>
            {
                ["system"] = MetaTrack.De(system, "system.wav", dispositivoSystem,
                                          taxaNativaSystem, duracao),
                ["mic"] = MetaTrack.De(mic, "mic.wav", dispositivoMic,
                                       taxaNativaMic, duracao),
            },
            // Vem do Google Calendar quando a integração está ativa; os campos
            // existem sempre, para o transcritor não precisar checar.
            Meeting = reuniao ?? new MetaMeeting(),
        };
    }
}

public sealed class MetaTrack
{
    [JsonPropertyName("file")] public required string File { get; init; }
    [JsonPropertyName("device")] public required string Device { get; init; }
    [JsonPropertyName("native_rate")] public required int NativeRate { get; init; }
    [JsonPropertyName("frames")] public required long Frames { get; init; }
    [JsonPropertyName("drift_corrections")] public required int DriftCorrections { get; init; }
    [JsonPropertyName("drift_net_samples")] public required long DriftNetSamples { get; init; }
    [JsonPropertyName("peak_rms")] public required double PeakRms { get; init; }
    [JsonPropertyName("ever_heard")] public required bool EverHeard { get; init; }
    [JsonPropertyName("no_audio")] public required bool NoAudio { get; init; }
    [JsonPropertyName("total_silent_s")] public required double TotalSilentS { get; init; }
    [JsonPropertyName("longest_silence_s")] public required double LongestSilenceS { get; init; }
    [JsonPropertyName("muted_s")] public required double MutedS { get; init; }
    [JsonPropertyName("usable_pct")] public required double UsablePct { get; init; }

    /// <summary>
    /// Campo novo no porte (requisito 3.5). O transcritor ignora chaves que não
    /// conhece, então acrescentar é seguro — o que não se pode é remover ou
    /// renomear as existentes.
    /// </summary>
    [JsonPropertyName("dropped_samples")] public required long DroppedSamples { get; init; }

    public static MetaTrack De(TrackStats s, string arquivo, string dispositivo,
                               int taxaNativa, double duracao) => new()
    {
        File = arquivo,
        Device = dispositivo,
        NativeRate = taxaNativa,
        Frames = s.AmostrasEscritas,
        DriftCorrections = s.CorrecoesDeriva,
        DriftNetSamples = s.DerivaLiquidaAmostras,
        PeakRms = Math.Round(s.PicoRms, 6),
        EverHeard = s.JaOuviu,
        NoAudio = s.SemAudio,
        TotalSilentS = Math.Round(s.TotalSilencioS, 1),
        LongestSilenceS = Math.Round(s.MaiorSilencioS, 1),
        MutedS = Math.Round(s.MudoS, 1),
        UsablePct = s.PercentualUtil(duracao),
        DroppedSamples = s.AmostrasDescartadas,
    };
}

/// <summary>
/// Contexto de serialização gerado em tempo de compilação.
/// </summary>
/// <remarks>
/// O <c>JsonSerializer.Serialize</c> por reflexão não sobrevive ao
/// <c>PublishTrimmed</c> — o trimmer não consegue provar quais tipos são
/// necessários e o build falha com IL2026. O gerador resolve isso e, de quebra,
/// deixa o caminho pronto para NativeAOT, que a Fase 4 pode querer.
///
/// O <c>UnsafeRelaxedJsonEscaping</c> continua indispensável: o Python grava com
/// <c>ensure_ascii=False</c>, e sem ele "Áudio Genérico" viraria escape unicode,
/// quebrando a comparação byte a byte com o gravador antigo.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(Meta))]
internal sealed partial class MetaJsonBase : JsonSerializerContext;

internal static class MetaJson
{
    public static readonly MetaJsonBase Default = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

public sealed class MetaMeeting
{
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("client")] public string? Client { get; init; }
    [JsonPropertyName("project")] public string? Project { get; init; }
    [JsonPropertyName("attendees")] public List<object> Attendees { get; init; } = [];
    [JsonPropertyName("calendar_event_id")] public string? CalendarEventId { get; init; }
}
