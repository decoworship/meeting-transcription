using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

public sealed class EstadoDaBandejaTests
{
    private static readonly DateTime T0 = new(2026, 8, 8, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OCliqueNuncaPara()
    {
        // Um clique acidental que encerra a gravação perde a reunião inteira; um
        // que muta você percebe na hora e desfaz.
        var e = new EstadoDaBandeja();
        Assert.Equal(AcaoDoClique.Iniciar, e.AcaoDoCliqueAtual);
        e.Iniciou();
        Assert.Equal(AcaoDoClique.AlternarMudo, e.AcaoDoCliqueAtual);
    }

    [Theory]
    [InlineData(false, false, false, CorDaBandeja.Cinza)]
    [InlineData(true, false, false, CorDaBandeja.Vermelho)]
    [InlineData(true, true, false, CorDaBandeja.Laranja)]
    [InlineData(true, false, true, CorDaBandeja.Amarelo)]
    public void CoresRefletemOEstado(bool gravando, bool mudo, bool semAudio, CorDaBandeja esperada)
    {
        var e = new EstadoDaBandeja();
        if (gravando) e.Iniciou();
        if (mudo) e.DefinirMudo(true, T0);
        e.CanalSemAudio = semAudio;
        Assert.Equal(esperada, e.Cor);
    }

    [Fact]
    public void FalhaDeEscritaVenceOMudo()
    {
        // Requisito 3.3. O laranja é um estado que você escolheu e reconhece; se
        // ele escondesse a falha de disco, a única gravação em que o aviso mais
        // importa — a que está indo embora — seria a que não avisaria.
        var e = new EstadoDaBandeja();
        e.Iniciou();
        e.DefinirMudo(true, T0);
        Assert.Equal(CorDaBandeja.Laranja, e.Cor);

        e.FalhaDeEscrita = true;
        Assert.Equal(CorDaBandeja.Amarelo, e.Cor);
        Assert.Contains("FALHA AO GRAVAR", e.TextoDeStatus(10, null));
    }

    [Fact]
    public void MudoDeliberadoTemPrecedenciaSobreCanalSemAudio()
    {
        // Se você mutou, "canal sem áudio" é consequência esperada e não merece
        // alarme. Amarelo tem que significar "algo errado que você não pediu".
        var e = new EstadoDaBandeja();
        e.Iniciou();
        e.DefinirMudo(true, T0);
        e.CanalSemAudio = true;
        Assert.Equal(CorDaBandeja.Laranja, e.Cor);
    }

    [Fact]
    public void LembretesSaemNosMarcosEUmaVezCada()
    {
        var e = new EstadoDaBandeja();
        e.Iniciou();
        e.DefinirMudo(true, T0);

        Assert.Null(e.LembreteDeMute(T0.AddMinutes(1)));
        Assert.Contains("2 min", e.LembreteDeMute(T0.AddMinutes(2)));
        Assert.Null(e.LembreteDeMute(T0.AddMinutes(3)));       // não repete
        Assert.Contains("5 min", e.LembreteDeMute(T0.AddMinutes(5)));
        Assert.Contains("15 min", e.LembreteDeMute(T0.AddMinutes(16)));
        Assert.Contains("30 min", e.LembreteDeMute(T0.AddMinutes(45)));
        Assert.Null(e.LembreteDeMute(T0.AddMinutes(90)));      // acabaram os marcos
    }

    [Fact]
    public void DesmutarReiniciaOsLembretes()
    {
        var e = new EstadoDaBandeja();
        e.Iniciou();
        e.DefinirMudo(true, T0);
        e.LembreteDeMute(T0.AddMinutes(5));

        e.DefinirMudo(false, T0.AddMinutes(6));
        e.DefinirMudo(true, T0.AddMinutes(7));

        // Mutou de novo: a contagem recomeça, senão um mute novo herdaria os
        // marcos já gastos e ficaria sem aviso.
        Assert.Contains("2 min", e.LembreteDeMute(T0.AddMinutes(9)));
    }

    [Fact]
    public void A14DesligaOLembreteMasNaoOAmarelo()
    {
        // São dois mecanismos independentes: o lembrete avisa sobre mute que VOCÊ
        // pediu; o amarelo avisa sobre canal morto que ninguém pediu — e é este
        // que existe por causa da gravação de 06/08.
        var e = new EstadoDaBandeja { NotificacoesLigadas = false };
        e.Iniciou();
        e.DefinirMudo(true, T0);
        Assert.Null(e.LembreteDeMute(T0.AddMinutes(30)));

        e.DefinirMudo(false, T0.AddMinutes(31));
        e.CanalSemAudio = true;
        Assert.Equal(CorDaBandeja.Amarelo, e.Cor);
    }

    [Fact]
    public void MudarMudoSemGravarNaoFazNada()
    {
        var e = new EstadoDaBandeja();
        e.DefinirMudo(true, T0);
        Assert.False(e.Mudo);
        Assert.Equal(CorDaBandeja.Cinza, e.Cor);
    }

    [Fact]
    public void PararLimpaOMudo()
    {
        var e = new EstadoDaBandeja();
        e.Iniciou();
        e.DefinirMudo(true, T0);
        e.Parou();
        Assert.False(e.Mudo);
        Assert.Equal(CorDaBandeja.Cinza, e.Cor);
    }

    /// <remarks>
    /// A janela mostra o mute continuamente, e "mudo" sem dizer há quanto tempo
    /// é a informação que menos ajuda quem esqueceu. A bandeja não precisava
    /// disto porque avisa por balão nos marcos e some.
    /// </remarks>
    [Fact]
    public void MudoHaSContaDesdeQuandoFoiMutado()
    {
        var e = new EstadoDaBandeja();
        e.Iniciou();
        Assert.Equal(0, e.MudoHaS(T0));

        e.DefinirMudo(true, T0);
        Assert.Equal(300, e.MudoHaS(T0.AddMinutes(5)));

        e.DefinirMudo(false, T0.AddMinutes(5));
        Assert.Equal(0, e.MudoHaS(T0.AddMinutes(6)));
    }

    [Fact]
    public void StatusMostraDuracaoEDispositivo()
    {
        var e = new EstadoDaBandeja();
        e.Iniciou();
        string s = e.TextoDeStatus(3725, "Headset (AN01)");
        Assert.Contains("01:02:05", s);
        Assert.Contains("Headset (AN01)", s);
    }
}
