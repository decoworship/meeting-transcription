using MeetingApp.Nucleo;
using MeetingApp.Sidecar;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>As regras puras dos nomes ao vivo (30/09/2026, em teste).</summary>
public sealed class FalantesAoVivoTests
{
    private static MotorSidecar.FalaAoVivo F(long de, long ate, int vaga) => new(de, ate, vaga);

    [Fact]
    public void AsVagasSaoNumeradasPelaOrdemEmQueFalam()
    {
        var falas = new List<MotorSidecar.FalaAoVivo>();
        var ordem = new Dictionary<int, int>();
        FalantesAoVivo.Acrescentar(falas, ordem, [F(0, 500, 3), F(600, 900, 1), F(1000, 1200, 3)], 60_000);

        Assert.Equal(1, ordem[3]);
        Assert.Equal(2, ordem[1]);
        Assert.Equal(3, falas.Count);
    }

    [Fact]
    public void AMemoriaNaoCresceComAReuniao()
    {
        var falas = new List<MotorSidecar.FalaAoVivo>();
        var ordem = new Dictionary<int, int>();
        FalantesAoVivo.Acrescentar(falas, ordem, [F(0, 1_000, 0)], 5_000);
        FalantesAoVivo.Acrescentar(falas, ordem, [F(9_000, 10_000, 1)], 5_000);

        Assert.Single(falas);
        // A vaga continua numerada: "Pessoa 1" não pode virar outra pessoa.
        Assert.Equal(1, ordem[0]);
    }

    [Fact]
    public void ComOsNomesDesligadosNaoLiga()
    {
        var motores = new Motores("python", "asr.py", "diar.py", "modelos.py");
        Assert.NotNull(FalantesAoVivo.OQueImpede(motores, new ConfiguracoesDoApp()));
        Assert.Contains("legenda", FalantesAoVivo.OQueImpede(motores,
            new ConfiguracoesDoApp { FalantesAoVivo = true })!);
    }
}
