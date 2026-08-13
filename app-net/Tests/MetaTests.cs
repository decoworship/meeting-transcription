using System.Text.Json;
using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>
/// O <c>meta.json</c> é o contrato com o app de transcrição, que continua em
/// Python durante toda a Fase 1. Estes testes existem para que o porte não
/// quebre esse contrato sem alguém perceber.
/// </summary>
public sealed class MetaTests
{
    /// <summary>Chaves que o `src/web/recordings.py` lê. Remover qualquer uma quebra o app.</summary>
    private static readonly string[] ChavesDeFaixa =
    [
        "file", "device", "native_rate", "frames", "drift_corrections",
        "drift_net_samples", "peak_rms", "ever_heard", "no_audio",
        "total_silent_s", "longest_silence_s", "muted_s", "usable_pct",
    ];

    private static Meta Exemplo(double silencioSystem = 0, double mudoMic = 0)
    {
        var system = new TrackStats
        {
            Nome = "system", AmostrasEscritas = 16_000 * 60, JaOuviu = true,
            PicoRms = 0.376925, CorrecoesDeriva = 240, DerivaLiquidaAmostras = 19_948,
            TotalSilencioS = silencioSystem,
        };
        var mic = new TrackStats
        {
            Nome = "mic", AmostrasEscritas = 16_000 * 60, JaOuviu = true,
            PicoRms = 0.174235, CorrecoesDeriva = 973, DerivaLiquidaAmostras = 1_325_104,
            MudoS = mudoMic,
        };
        return Meta.Montar(DateTimeOffset.UtcNow, system, mic,
                           "Headphones (AN01) [Loopback]", "Headset (AN01)",
                           48_000, 16_000);
    }

    [Fact]
    public void TodasAsChavesDoContratoEstaoPresentes()
    {
        using var doc = JsonDocument.Parse(Exemplo().ParaJson());
        foreach (var faixa in new[] { "system", "mic" })
        {
            var t = doc.RootElement.GetProperty("tracks").GetProperty(faixa);
            foreach (var chave in ChavesDeFaixa)
                Assert.True(t.TryGetProperty(chave, out _),
                    $"faixa '{faixa}' perdeu a chave '{chave}' — quebra o recordings.py");
        }

        foreach (var chave in new[] { "recorded_at", "duration_s", "sample_rate", "tracks", "meeting" })
            Assert.True(doc.RootElement.TryGetProperty(chave, out _), $"faltou '{chave}'");

        var reuniao = doc.RootElement.GetProperty("meeting");
        foreach (var chave in new[] { "title", "client", "project", "attendees", "calendar_event_id" })
            Assert.True(reuniao.TryGetProperty(chave, out _), $"meeting perdeu '{chave}'");
    }

    [Fact]
    public void DuracaoVemDaFaixaMaisLonga()
    {
        // O loopback WASAPI não entrega nada enquanto nada toca. Se a duração
        // saísse da faixa do sistema, uma reunião só de escuta com o microfone
        // mudo reportaria zero.
        var system = new TrackStats { Nome = "system", AmostrasEscritas = 0 };
        var mic = new TrackStats { Nome = "mic", AmostrasEscritas = 16_000 * 120 };
        var m = Meta.Montar(DateTimeOffset.UtcNow, system, mic, "s", "m", 48_000, 16_000);
        Assert.Equal(120.0, m.DurationS);
    }

    [Fact]
    public void PercentualUtilDescontaSilencioEMute()
    {
        var m = Exemplo(silencioSystem: 30, mudoMic: 15);   // faixas de 60 s
        using var doc = JsonDocument.Parse(m.ParaJson());
        var tracks = doc.RootElement.GetProperty("tracks");
        Assert.Equal(50.0, tracks.GetProperty("system").GetProperty("usable_pct").GetDouble());
        Assert.Equal(75.0, tracks.GetProperty("mic").GetProperty("usable_pct").GetDouble());
    }

    [Fact]
    public void SemAudioEhOInversoDeJaOuviu()
    {
        var system = new TrackStats { Nome = "system", AmostrasEscritas = 16_000, JaOuviu = false };
        var mic = new TrackStats { Nome = "mic", AmostrasEscritas = 16_000, JaOuviu = true };
        var m = Meta.Montar(DateTimeOffset.UtcNow, system, mic, "s", "m", 48_000, 16_000);
        using var doc = JsonDocument.Parse(m.ParaJson());
        var tracks = doc.RootElement.GetProperty("tracks");
        Assert.True(tracks.GetProperty("system").GetProperty("no_audio").GetBoolean());
        Assert.False(tracks.GetProperty("mic").GetProperty("no_audio").GetBoolean());
    }

    [Fact]
    public void AcentosSaemLiteraisComoNoPython()
    {
        // O Python grava com ensure_ascii=False. Se o .NET escapasse, o arquivo
        // deixaria de bater com o do gravador antigo numa comparação byte a byte.
        var system = new TrackStats { Nome = "system", AmostrasEscritas = 16_000 };
        var mic = new TrackStats { Nome = "mic", AmostrasEscritas = 16_000 };
        var m = Meta.Montar(DateTimeOffset.UtcNow, system, mic,
                            "Alto-falantes (Áudio Genérico)", "Microfone Único", 48_000, 16_000);
        string json = m.ParaJson();
        Assert.Contains("Áudio Genérico", json);
        Assert.DoesNotContain("\\u00c1", json);
    }

    [Fact]
    public void ContadorDeDescartesEhRegistrado()
    {
        var system = new TrackStats { Nome = "system", AmostrasEscritas = 16_000, AmostrasDescartadas = 4096 };
        var mic = new TrackStats { Nome = "mic", AmostrasEscritas = 16_000 };
        var m = Meta.Montar(DateTimeOffset.UtcNow, system, mic, "s", "m", 48_000, 16_000);
        using var doc = JsonDocument.Parse(m.ParaJson());
        Assert.Equal(4096,
            doc.RootElement.GetProperty("tracks").GetProperty("system")
               .GetProperty("dropped_samples").GetInt64());
    }

    [Fact]
    public void DesconexaoFicaRegistradaPorFaixa()
    {
        // Requisito 3.7. A bandeja avisa durante a reunião, e é justamente o
        // aviso que ninguém vê; quem transcreve depois precisa distinguir "a
        // faixa acabou aqui" de "o headset caiu aqui".
        var system = new TrackStats { Nome = "system", AmostrasEscritas = 16_000 };
        var mic = new TrackStats { Nome = "mic", AmostrasEscritas = 16_000, Desconectado = true };
        var m = Meta.Montar(DateTimeOffset.UtcNow, system, mic, "s", "m", 48_000, 16_000);

        using var doc = JsonDocument.Parse(m.ParaJson());
        var faixas = doc.RootElement.GetProperty("tracks");
        Assert.True(faixas.GetProperty("mic").GetProperty("disconnected").GetBoolean());
        Assert.False(faixas.GetProperty("system").GetProperty("disconnected").GetBoolean());
    }
}
