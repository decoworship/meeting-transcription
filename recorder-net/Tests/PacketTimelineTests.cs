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

        // O relógio vai cru: a linha do tempo desconta a margem que ela mede, e
        // preenche até "agora menos a margem" — nunca até agora.
        int silencio = t.SilencioAte(inicio + Ms(5000));

        long margemAmostras = t.QpcParaAmostras(t.MargemOciosa);
        Assert.Equal(5 * Alvo - 160 - margemAmostras, silencio);
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

        // O preenchimento parou uma margem antes de "agora", então o pacote
        // carimbado exatamente em 5 s encontra um buraco legítimo do tamanho da
        // margem — e não pode fazer a posição regredir.
        var d = t.Chegou(inicio + Ms(5000), 160, AnomaliaPacote.Nenhuma);
        Assert.Equal(t.QpcParaAmostras(t.MargemOciosa), d.SilencioAntes);
        Assert.Equal(5 * Alvo + 160, d.PosicaoAlvo);
    }

    [Fact]
    public void PreenchimentoOciosoNaoAgeEnquantoChegamPacotes()
    {
        // A interação que 70 s de captura Bluetooth expuseram: o dispositivo
        // entrega em rajadas, com pausas de centenas de ms. Tratar cada pausa
        // como silêncio real escrevia sobre o tempo que a rajada seguinte ia
        // ocupar, e a âncora descartava o áudio verdadeiro para caber —
        // 13,4 s de silêncio inserido e 11,3 s de áudio jogado fora em 70 s.
        var t = new PacketTimeline();
        long inicio = Ms(1000);
        t.Chegou(inicio, 160, AnomaliaPacote.Nenhuma, qpcAgora: inicio);

        // Pausa de 900 ms: é rajada, não silêncio. O preenchimento fica quieto.
        Assert.Equal(0, t.SilencioAte(inicio + Ms(900)));

        // E a rajada atrasada encontra a linha do tempo intacta: o buraco é o
        // tempo real decorrido, não um resto do que o preenchimento comeu.
        var d = t.Chegou(inicio + Ms(400), 160, AnomaliaPacote.Nenhuma,
                         qpcAgora: inicio + Ms(900));
        Assert.Equal(Alvo * 400 / 1000 - 160, d.SilencioAntes);
    }

    [Fact]
    public void SemPacoteNenhumPorMuitoTempoOPreenchimentoVolta()
    {
        // O requisito 3.6 continua valendo: loopback com nada tocando não
        // entrega pacote nenhum, e a faixa não pode ficar vazia.
        var t = new PacketTimeline();
        long inicio = Ms(1000);
        t.Chegou(inicio, 160, AnomaliaPacote.Nenhuma, qpcAgora: inicio);

        int silencio = t.SilencioAte(inicio + Ms(5000));
        Assert.True(silencio > 0, "sem pacote por 5 s, o silêncio tem que ser escrito");

        long margem = t.QpcParaAmostras(t.MargemOciosa);
        Assert.Equal(5 * Alvo - 160 - margem, silencio);
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

    [Fact]
    public void DispositivoMaisRapidoQueORelogioApareceComoDivergencia()
    {
        // O bug que 20 min de soak expuseram: se a linha do tempo seguisse o
        // acumulado de quadros, um dispositivo 1% rápido divergiria do tempo real
        // sem nunca ser detectado — a âncora compararia a escrita com um número
        // derivado das mesmas amostras.
        var t = new PacketTimeline();
        long escritas = 0;
        DecisaoPacote d = default;

        for (int i = 0; i < 600; i++)          // 6 s de pacotes de 10 ms
        {
            // O relógio anda 10 ms; o dispositivo entrega 1% de amostras a mais.
            d = t.Chegou(Ms(10.0 * i), 162, AnomaliaPacote.Nenhuma);
            escritas += 162;
        }

        // A posição alvo tem que seguir o RELÓGIO (6 s = 96000), não os quadros
        // entregues (600 × 162 = 97200).
        Assert.InRange(d.PosicaoAlvo, 96_000, 96_200);
        Assert.True(escritas - d.PosicaoAlvo > 1000,
            "a divergência entre o escrito e o tempo real tem que ficar visível " +
            "para a âncora poder corrigi-la");
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
