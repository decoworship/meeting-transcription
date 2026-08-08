using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>
/// Requisito 3.6: o loopback não entrega pacote enquanto nada toca, e o laço de
/// escrita precisa preencher os buracos explicitamente.
/// </summary>
public sealed class PacketTimelineTests
{
    private const long Qpc = PacketTimeline.QpcPorSegundo;
    private const int Alvo = CrashSafeWavWriter.TaxaAlvo;

    /// <summary>QPC de N milissegundos.</summary>
    private static long Ms(double ms) => (long)(ms * Qpc / 1000);

    [Fact]
    public void PrimeiroPacoteDefineAOrigem()
    {
        var t = new PacketTimeline();
        // O QPC é o relógio da máquina desde o boot. Tomá-lo como deslocamento
        // inseriria dias de silêncio no começo do arquivo.
        var d = t.Chegou(qpc: 987_654_321_000, quadrosAlvo: 160, AnomaliaPacote.Nenhuma);

        Assert.Equal(0, d.SilencioAntes);
        Assert.Equal(160, d.PosicaoAlvo);
    }

    [Fact]
    public void PacotesContiguosNaoGeramSilencio()
    {
        var t = new PacketTimeline();
        long inicio = Ms(1000);
        t.Chegou(inicio, 160, AnomaliaPacote.Nenhuma);              // 10 ms
        var d = t.Chegou(inicio + Ms(10), 160, AnomaliaPacote.Nenhuma);

        Assert.Equal(0, d.SilencioAntes);
        Assert.Equal(320, d.PosicaoAlvo);
        Assert.Equal(0, t.SilencioInserido);
    }

    [Fact]
    public void BuracoDoLoopbackViraSilencioExato()
    {
        var t = new PacketTimeline();
        long inicio = Ms(5000);
        t.Chegou(inicio, 160, AnomaliaPacote.Nenhuma);

        // Ninguém falou por 30 s: o WASAPI não entregou nada, e o próximo pacote
        // chega 30 s à frente no QPC.
        var d = t.Chegou(inicio + Ms(30_000), 160, AnomaliaPacote.Nenhuma);

        // 30 s menos os 10 ms do primeiro pacote já escritos.
        Assert.Equal(30 * Alvo - 160, d.SilencioAntes);
        Assert.Equal(30 * Alvo - 160, t.SilencioInserido);
    }

    [Fact]
    public void SemPacoteNenhumOSilencioAindaEhPreenchido()
    {
        // O caso que a captura real expôs: 20 s pedidos produziram 0 s na faixa
        // do sistema, porque sem pacote não há salto para detectar. A camada de
        // captura consulta o relógio e chama SilencioAte.
        var t = new PacketTimeline();
        long inicio = Ms(1000);
        t.Chegou(inicio, 160, AnomaliaPacote.Nenhuma);

        int silencio = t.SilencioAte(inicio + Ms(5000));

        Assert.Equal(5 * Alvo - 160, silencio);
    }

    [Fact]
    public void SilencioAteNaoDuplicaOQueJaFoiEscrito()
    {
        var t = new PacketTimeline();
        long inicio = Ms(1000);
        t.Chegou(inicio, 160, AnomaliaPacote.Nenhuma);
        t.SilencioAte(inicio + Ms(5000));

        // Chamada repetida no mesmo instante não pode inserir de novo.
        Assert.Equal(0, t.SilencioAte(inicio + Ms(5000)));
    }

    [Fact]
    public void PacoteDepoisDeSilencioAteNaoRegride()
    {
        var t = new PacketTimeline();
        long inicio = Ms(1000);
        t.Chegou(inicio, 160, AnomaliaPacote.Nenhuma);
        t.SilencioAte(inicio + Ms(5000));

        // O pacote seguinte chega logo após o preenchimento: não deve inserir
        // mais silêncio nem descartar o que já foi escrito.
        var d = t.Chegou(inicio + Ms(5000), 160, AnomaliaPacote.Nenhuma);
        Assert.Equal(0, d.SilencioAntes);
        Assert.Equal(5 * Alvo + 160, d.PosicaoAlvo);
    }

    [Fact]
    public void CarimboQueRetrocedeNaoDescartaAudio()
    {
        var t = new PacketTimeline();
        long inicio = Ms(1000);
        t.Chegou(inicio, 160, AnomaliaPacote.Nenhuma);
        // Jitter do carimbo, não áudio sobrando. Descartar seria pior.
        var d = t.Chegou(inicio + Ms(5), 160, AnomaliaPacote.ErroDeTimestamp);

        Assert.Equal(0, d.SilencioAntes);
        Assert.Equal(1, t.PacotesComErroDeTimestamp);
    }

    [Fact]
    public void FlagsViramContadoresDeAnomalia()
    {
        var t = new PacketTimeline();
        t.Chegou(0, 160, AnomaliaPacote.Silencio);
        t.Chegou(Ms(10), 160, AnomaliaPacote.Descontinuidade | AnomaliaPacote.ErroDeTimestamp);
        t.Chegou(Ms(20), 160, AnomaliaPacote.Silencio);

        Assert.Equal(2, t.PacotesDeSilencio);
        Assert.Equal(1, t.PacotesComDescontinuidade);
        Assert.Equal(1, t.PacotesComErroDeTimestamp);
    }

    [Fact]
    public void MilPacotesNaoAcumulamErroDeArredondamento()
    {
        var t = new PacketTimeline();
        DecisaoPacote d = default;
        for (int i = 0; i < 1000; i++)                 // 1000 × 10 ms = 10 s
            d = t.Chegou(Ms(10.0 * i), 160, AnomaliaPacote.Nenhuma);

        // 10 s têm que dar 160000 amostras exatas, senão a faixa deriva pela
        // própria aritmética — uma deriva inventada por nós, somada à do
        // hardware e indistinguível dela no critério A.
        Assert.Equal(10 * Alvo, d.PosicaoAlvo);
    }

    [Theory]
    [InlineData(1000.0, 16_000)]
    [InlineData(10.0, 160)]
    [InlineData(33.333, 533)]
    public void ConversaoDeQpcParaAmostras(double ms, long esperado)
    {
        var t = new PacketTimeline();
        Assert.Equal(esperado, t.QpcParaAmostras(Ms(ms)));
    }
}
