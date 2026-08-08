using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>
/// Requisito 3.6: o loopback não entrega pacote enquanto nada toca, e o laço de
/// escrita precisa preencher os buracos explicitamente.
/// </summary>
public sealed class PacketTimelineTests
{
    private const int Nativa = 48_000;

    [Fact]
    public void PrimeiroPacoteDefineAOrigem()
    {
        var t = new PacketTimeline(Nativa);
        // A posição absoluta do dispositivo é arbitrária — conta desde que o
        // endpoint iniciou, não desde que começamos a gravar. Tomá-la como
        // deslocamento inseriria horas de silêncio no começo do arquivo.
        var d = t.Chegou(posicaoDispositivo: 987_654_321, quadros: 480, AnomaliaPacote.Nenhuma);

        Assert.Equal(0, d.SilencioAntes);
        Assert.Equal(160, d.PosicaoAlvo);          // 480 a 48 kHz = 160 a 16 kHz
    }

    [Fact]
    public void PacotesContiguosNaoGeramSilencio()
    {
        var t = new PacketTimeline(Nativa);
        t.Chegou(1000, 480, AnomaliaPacote.Nenhuma);
        var d = t.Chegou(1480, 480, AnomaliaPacote.Nenhuma);

        Assert.Equal(0, d.SilencioAntes);
        Assert.Equal(320, d.PosicaoAlvo);
        Assert.Equal(0, t.SilencioInserido);
    }

    [Fact]
    public void BuracoDoLoopbackViraSilencioExato()
    {
        var t = new PacketTimeline(Nativa);
        t.Chegou(1000, 480, AnomaliaPacote.Nenhuma);       // termina em 1480

        // Ninguém falou por 30 s: o WASAPI não entregou nada, e o próximo pacote
        // chega com a posição 30 s à frente.
        long depois = 1480 + 30L * Nativa;
        var d = t.Chegou(depois, 480, AnomaliaPacote.Nenhuma);

        Assert.Equal(30 * CrashSafeWavWriter.TaxaAlvo, d.SilencioAntes);
        Assert.Equal(30 * CrashSafeWavWriter.TaxaAlvo, t.SilencioInserido);
    }

    [Fact]
    public void PosicaoQueRetrocedeNaoDescartaAudio()
    {
        var t = new PacketTimeline(Nativa);
        t.Chegou(1000, 480, AnomaliaPacote.Nenhuma);
        // Posição menor que o fim anterior é dado corrompido, não buraco.
        // Descartar áudio por causa disso seria pior que ignorar o retrocesso.
        var d = t.Chegou(1200, 480, AnomaliaPacote.ErroDeTimestamp);

        Assert.Equal(0, d.SilencioAntes);
        Assert.Equal(1, t.PacotesComErroDeTimestamp);
    }

    [Fact]
    public void FlagsViramContadoresDeAnomalia()
    {
        var t = new PacketTimeline(Nativa);
        t.Chegou(0, 480, AnomaliaPacote.Silencio);
        t.Chegou(480, 480, AnomaliaPacote.Descontinuidade | AnomaliaPacote.ErroDeTimestamp);
        t.Chegou(960, 480, AnomaliaPacote.Silencio);

        Assert.Equal(2, t.PacotesDeSilencio);
        Assert.Equal(1, t.PacotesComDescontinuidade);
        Assert.Equal(1, t.PacotesComErroDeTimestamp);
    }

    [Theory]
    [InlineData(48_000, 48_000, 16_000)]     // 1 s
    [InlineData(44_100, 44_100, 16_000)]     // taxa não múltipla
    [InlineData(16_000, 16_000, 16_000)]     // passthrough
    public void ConversaoDeTaxaPreservaDuracao(int taxaNativa, long nativas, long esperadoAlvo)
    {
        var t = new PacketTimeline(taxaNativa);
        Assert.Equal(esperadoAlvo, t.ParaAlvo(nativas));
    }

    [Fact]
    public void PosicaoAlvoAcumulaAoLongoDeMuitosPacotes()
    {
        var t = new PacketTimeline(Nativa);
        long pos = 5_000_000;
        DecisaoPacote d = default;
        // 1000 pacotes de 10 ms = 10 s
        for (int i = 0; i < 1000; i++)
        {
            d = t.Chegou(pos, 480, AnomaliaPacote.Nenhuma);
            pos += 480;
        }

        // O erro de arredondamento não pode acumular: 10 s têm que dar 160000
        // amostras exatas, senão a faixa deriva por conta da própria aritmética.
        Assert.Equal(10 * CrashSafeWavWriter.TaxaAlvo, d.PosicaoAlvo);
    }

    [Fact]
    public void TaxaNaoMultiplaNaoAcumulaErro()
    {
        var t = new PacketTimeline(44_100);
        long pos = 0;
        DecisaoPacote d = default;
        for (int i = 0; i < 1000; i++)
        {
            d = t.Chegou(pos, 441, AnomaliaPacote.Nenhuma);   // 10 ms a 44,1 kHz
            pos += 441;
        }

        // 441000 amostras a 44,1 kHz = 10 s = 160000 a 16 kHz.
        Assert.Equal(160_000, d.PosicaoAlvo);
    }
}
