using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>
/// Um segmento pronto: texto com tempo e falante.
/// </summary>
/// <remarks>
/// Os nomes em inglês e a ordem dos campos reproduzem o
/// <c>TranscriptionSegment</c> do app atual, porque o <c>history/</c> gravado
/// por ele precisa continuar legível — e porque a paridade da Fase 2 se mede
/// comparando este JSON com o dele.
/// </remarks>
public sealed class SegmentoFinal
{
    [JsonPropertyName("start")] public required double Start { get; init; }
    [JsonPropertyName("end")] public required double End { get; init; }
    // Mutável como o Speaker: a correção fonética reescreve o texto, e a edição
    // no lugar (E3 do FEATURES) vai reescrevê-lo de novo.
    [JsonPropertyName("text")] public required string Text { get; set; }
    [JsonPropertyName("speaker")] public string? Speaker { get; set; }
}

/// <summary>O resultado que a UI consome e o histórico persiste.</summary>
public sealed class ResultadoDaTranscricao
{
    [JsonPropertyName("language")] public string? Language { get; init; }
    [JsonPropertyName("duration")] public double? Duration { get; init; }

    /// <summary>
    /// De que reunião esta transcrição é.
    /// </summary>
    /// <remarks>
    /// Os mesmos nomes que o <c>history/</c> do app Python usa. Sem eles, o
    /// cliente e o projeto escolhidos antes de transcrever se perdiam ao sair
    /// da tela — e o cabeçalho do arquivo exportado saía sem dizer de quem era
    /// a reunião, que foi como o defeito apareceu.
    /// </remarks>
    [JsonPropertyName("client")] public string? Client { get; set; }
    [JsonPropertyName("project")] public string? Project { get; set; }

    /// <summary>Data e hora da reunião, em ISO. Vem da agenda quando existe.</summary>
    [JsonPropertyName("date")] public string? Date { get; set; }

    [JsonPropertyName("segments")] public required List<SegmentoFinal> Segments { get; init; }

    public string ParaJson() =>
        JsonSerializer.Serialize(this, TranscricaoJson.Default.ResultadoDaTranscricao);

    /// <summary>Lê o que <see cref="ParaJson"/> escreveu.</summary>
    public static ResultadoDaTranscricao? DeJson(string json) =>
        JsonSerializer.Deserialize(json, TranscricaoJson.Default.ResultadoDaTranscricao);
}

[JsonSourceGenerationOptions(WriteIndented = true,
                             DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(ResultadoDaTranscricao))]
internal sealed partial class TranscricaoJsonBase : JsonSerializerContext;

internal static class TranscricaoJson
{
    // UnsafeRelaxedJsonEscaping pelo mesmo motivo do meta.json: o Python grava
    // com ensure_ascii=False, e sem isto todo acento viraria escape unicode.
    public static readonly TranscricaoJsonBase Default = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>
/// Junta o que os motores devolveram: texto do ASR, falantes da diarização, e a
/// certeza que só as duas faixas separadas dão.
/// </summary>
public static class Montagem
{
    /// <summary>
    /// Quanto o microfone precisa superar o áudio do sistema para o segmento ser
    /// seu. 2,0 (~6 dB) tolera o vazamento de quem usa caixas em vez de fone.
    /// </summary>
    public const double MargemDoDono = 2.0;

    /// <summary>Abaixo disto o microfone é ruído de fundo, não fala.</summary>
    public const double RmsMinimoDoDono = 5e-3;

    /// <summary>
    /// Cada segmento transcrito recebe o falante com maior sobreposição
    /// temporal na diarização.
    /// </summary>
    /// <remarks>
    /// Os rótulos crus do motor (<c>SPEAKER_00</c>) viram "Speaker 1" aqui, e
    /// não no motor: nomear é apresentação, e o protocolo carrega o que o
    /// modelo produziu. A ordem é a alfabética dos rótulos crus, que é a do
    /// <c>_create_speaker_map</c> do app atual — trocá-la renomearia todo mundo
    /// e quebraria a comparação com o histórico já gravado.
    /// </remarks>
    public static void AtribuirFalantes(
        List<SegmentoFinal> segmentos, IReadOnlyList<SegmentoDeFalante> diarizacao)
    {
        var nomes = diarizacao.Select(d => d.Falante).Distinct().Order(StringComparer.Ordinal)
            .Select((cru, i) => (cru, nome: $"Speaker {i + 1}"))
            .ToDictionary(x => x.cru, x => x.nome);

        foreach (var seg in segmentos)
        {
            // Somar por falante, e não pegar o maior trecho isolado: um segmento
            // longo pode alternar entre duas pessoas, e quem fala três vezes por
            // um segundo domina quem falou uma vez por dois. Pegar o maior
            // trecho daria a resposta errada exatamente nas trocas de turno.
            var total = new Dictionary<string, double>();
            foreach (var d in diarizacao)
            {
                double sobreposicao = Math.Min(seg.End, d.Fim) - Math.Max(seg.Start, d.Inicio);
                if (sobreposicao > 0)
                    total[d.Falante] = total.GetValueOrDefault(d.Falante) + sobreposicao;
            }

            // "Unknown" e não nulo: é o que o app atual grava, e um rótulo
            // explícito diz "ninguém foi identificado aqui" onde a ausência
            // pareceria esquecimento.
            seg.Speaker = total.Count == 0
                ? "Unknown"
                : nomes[total.MaxBy(p => p.Value).Key];
        }
    }

    /// <summary>
    /// Marca como <paramref name="rotuloDoDono"/> os segmentos em que o
    /// microfone domina.
    /// </summary>
    /// <remarks>
    /// Roda <b>depois</b> da diarização e sobrescreve o palpite dela: onde o
    /// microfone tem energia claramente maior que o áudio do sistema, não se
    /// está estimando quem falou — se está sabendo.
    /// </remarks>
    /// <returns>Quantos segmentos foram atribuídos ao dono.</returns>
    public static int AtribuirDono(List<SegmentoFinal> segmentos, Faixas faixas,
                                   string rotuloDoDono = "You")
    {
        int meus = 0;
        foreach (var seg in segmentos)
        {
            double rmsMic = Faixas.Rms(faixas.Mic, seg.Start, seg.End);
            double rmsSistema = Faixas.Rms(faixas.Sistema, seg.Start, seg.End);

            if (rmsMic >= RmsMinimoDoDono && rmsMic > rmsSistema * MargemDoDono)
            {
                seg.Speaker = rotuloDoDono;
                meus++;
            }
        }
        return meus;
    }
}
