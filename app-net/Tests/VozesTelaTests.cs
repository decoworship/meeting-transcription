using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O que a tela de vozes (plano 6, UI-2) lê e edita no perfil guardado.
/// </summary>
/// <remarks>
/// Nada aqui muda como uma voz é reconhecida ou aprendida: são leituras
/// (semelhança de uma amostra, perfis parecidos) e edições pedidas por quem
/// ouviu o trecho (mover, apagar).
/// </remarks>
public sealed class VozesTelaTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("vozes-tela").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static float[] Vetor(int semente, float ruido = 0f, int base_ = -1)
    {
        var r = new Random(base_ >= 0 ? base_ : semente);
        var v = new float[256];
        for (int i = 0; i < v.Length; i++) v[i] = (float)r.NextDouble() - 0.5f;
        if (ruido > 0)
        {
            var q = new Random(semente * 7919);
            for (int i = 0; i < v.Length; i++) v[i] += ruido * ((float)q.NextDouble() - 0.5f);
        }
        return v;
    }

    private static int _n;
    private static AmostraDeVoz Amostra(float[] vetor, int? regras = Vozes.RegrasAtuais,
                                        string? trecho = null) => new()
    {
        Vetor = vetor,
        CriadaEm = $"2026-09-17T10:00:{Interlocked.Increment(ref _n) % 60:00}.{_n:000}Z",
        DuracaoS = 4.0,
        Regras = regras,
        Trecho = trecho,
        Origem = new Origem { Gravacao = "2026-09-17_10-00-00", Faixa = "system", T0 = 1, T1 = 5, Dispositivo = "Sala" },
    };

    [Fact]
    public void ASemelhancaDeUmaAmostraEhContraORestoDoPerfil()
    {
        var v = new Vozes(_pasta);
        v.Aprender("Carol", Amostra(Vetor(1, 0.1f, 1)));
        v.Aprender("Carol", Amostra(Vetor(2, 0.1f, 1)));
        v.Aprender("Carol", Amostra(Vetor(99)));

        double? parecida = v.SemelhancaNoPerfil("Carol", 0);
        double? estranha = v.SemelhancaNoPerfil("Carol", 2);
        Assert.NotNull(parecida);
        Assert.NotNull(estranha);
        Assert.True(estranha < Vozes.LimiarDeQuarentena, $"estranha {estranha}");
        Assert.True(parecida > 0.5, $"parecida {parecida}");
    }

    [Fact]
    public void SemComOQueCompararASemelhancaEhNula()
    {
        var v = new Vozes(_pasta);
        v.Aprender("Heitor", Amostra(Vetor(1)));
        v.Aprender("Heitor", Amostra(Vetor(1), regras: null));   // geração 1: inerte
        Assert.Null(v.SemelhancaNoPerfil("Heitor", 0));
        Assert.Null(v.SemelhancaNoPerfil("Heitor", 1));
        Assert.Null(v.SemelhancaNoPerfil("Ninguém", 0));
    }

    [Fact]
    public void MoverLevaAAmostraETiraDaQuarentena()
    {
        var v = new Vozes(_pasta);
        v.Aprender("Carol", Amostra(Vetor(1)));
        var suspeita = v.Aprender("Carol", Amostra(Vetor(50)));
        Assert.True(suspeita.Quarentena);

        Assert.True(v.Mover("Carol", 1, "Rafael", suspeita.CriadaEm));
        v = new Vozes(_pasta);
        Assert.Single(v.Perfil("Carol")!.Amostras);
        var movida = Assert.Single(v.Perfil("Rafael")!.Amostras);
        Assert.False(movida.Quarentena);
        Assert.Equal(suspeita.CriadaEm, movida.CriadaEm);
    }

    [Fact]
    public void MoverAUltimaAmostraApagaOPerfilVazio()
    {
        var v = new Vozes(_pasta);
        var a = v.Aprender("Elio", Amostra(Vetor(1)));
        Assert.True(v.Mover("Elio", 0, "Élio", a.CriadaEm));
        Assert.Equal(["Élio"], v.Pessoas());
    }

    [Fact]
    public void AsOpsRecusamUmaAmostraQueNaoEhAMostrada()
    {
        var v = new Vozes(_pasta);
        v.Aprender("Carol", Amostra(Vetor(1)));
        v.Aprender("Carol", Amostra(Vetor(2)));

        Assert.False(v.Mover("Carol", 0, "Rafael", "outra"));
        Assert.False(v.Esquecer("Carol", 0, "outra"));
        Assert.False(v.Aprovar("Carol", 0, "outra"));
        Assert.False(v.Mover("Carol", 0, "Carol", v.Perfil("Carol")!.Amostras[0].CriadaEm));
        Assert.Equal(2, new Vozes(_pasta).Perfil("Carol")!.Amostras.Count);

        // Sem o carimbo, vale o índice — como antes.
        Assert.True(v.Esquecer("Carol", 0));
    }

    [Fact]
    public void ApagarTiraOPerfilEOsTrechos()
    {
        Directory.CreateDirectory(Path.Combine(_pasta, "trechos"));
        string trecho = Path.Combine("trechos", "a.wav");
        File.WriteAllBytes(Path.Combine(_pasta, trecho), [1, 2, 3]);

        var v = new Vozes(_pasta);
        v.Aprender("Carol", Amostra(Vetor(1), trecho: trecho));
        v.Aprender("Marcos", Amostra(Vetor(2)));

        Assert.True(v.Apagar("Carol"));
        Assert.False(v.Apagar("Carol"));
        Assert.Equal(["Marcos"], new Vozes(_pasta).Pessoas());
        Assert.False(File.Exists(Path.Combine(_pasta, trecho)));
    }

    [Fact]
    public void ParecidosAchaAMesmaPessoaComDoisNomes()
    {
        var v = new Vozes(_pasta);
        v.Aprender("Élio", Amostra(Vetor(1, 0.1f, 1)));
        v.Aprender("Élio", Amostra(Vetor(2, 0.1f, 1)));
        v.Aprender("Elio", Amostra(Vetor(3, 0.1f, 1)));
        v.Aprender("Marcos", Amostra(Vetor(40)));
        // Inerte não conta: a geração 1 de alguém igual não sugere nada.
        v.Aprender("Antigo", Amostra(Vetor(4, 0.1f, 1), regras: null));

        var pares = v.Parecidos();
        var par = Assert.Single(pares);
        Assert.Equal(["Elio", "Élio"], new[] { par.A, par.B }.Order(StringComparer.Ordinal));
        Assert.True(par.Semelhanca >= Vozes.LimiarDeReconhecimento);
    }
}
