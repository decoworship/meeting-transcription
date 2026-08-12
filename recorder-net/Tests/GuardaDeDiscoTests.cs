using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>Requisito 3.3 e critério de aceite D.</summary>
public sealed class GuardaDeDiscoTests
{
    // 16 kHz × 2 bytes × 2 faixas = 64 KB/s.
    private const long PorSegundo = CrashSafeWavWriter.TaxaAlvo * 2 * 2;

    [Fact]
    public void DiscoFolgadoNaoAvisa()
    {
        var g = new GuardaDeDisco();
        Assert.Equal(EstadoDisco.Ok, g.Avaliar(PorSegundo * 3600));   // 1 h
        Assert.Null(g.Mensagem);
    }

    [Fact]
    public void PoucoEspacoAvisaSemImpedir()
    {
        var g = new GuardaDeDisco();
        Assert.Equal(EstadoDisco.Aviso, g.Avaliar(PorSegundo * 600));  // 10 min
        Assert.True(g.PodeComecar(PorSegundo * 600, out string? motivo));
        Assert.Contains("10 min", motivo);
    }

    [Fact]
    public void EspacoCriticoImpedeComecar()
    {
        // Recusar cedo é melhor que parar no meio da reunião: quem for avisado
        // agora libera espaço; quem descobrir aos 40 min perdeu a reunião.
        var g = new GuardaDeDisco();
        Assert.False(g.PodeComecar(PorSegundo * 60, out string? motivo));   // 1 min
        Assert.Contains("insuficiente", motivo);
    }

    [Fact]
    public void OTempoRestanteEhEmMinutosDeGravacao()
    {
        // Limiar em bytes não diz nada a quem está numa reunião; "13 minutos"
        // diz. É a razão de a régua ser tempo, não espaço.
        var g = new GuardaDeDisco();
        g.Avaliar(PorSegundo * 780);
        Assert.Equal(13, (int)g.TempoRestante.TotalMinutes);
    }

    [Fact]
    public void TaxaCustomizadaMudaOCalculo()
    {
        // Uma faixa só consome metade — o guarda tem que refletir a configuração
        // real, não um número fixo.
        var g = new GuardaDeDisco(bytesPorSegundo: PorSegundo / 2);
        g.Avaliar(PorSegundo * 600);
        Assert.Equal(20, (int)g.TempoRestante.TotalMinutes);
    }
}
