using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A biblioteca de vozes nova — a que guarda de onde veio cada amostra.
/// </summary>
/// <remarks>
/// A anterior não foi migrada por decisão do dono do produto: lá cada amostra
/// é um vetor solto, e um vetor contaminado envenena o perfil sem que ninguém
/// consiga descobrir qual era. Estes testes prendem as regras que existem para
/// isso não se repetir.
/// </remarks>
public sealed class VozesTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("vozes-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    /// <summary>Um vetor determinístico, com uma direção dominante.</summary>
    private static float[] Vetor(int semente, float ruido = 0f)
    {
        var r = new Random(semente);
        var v = new float[256];
        for (int i = 0; i < v.Length; i++) v[i] = (float)r.NextDouble() - 0.5f;

        if (ruido > 0)
            for (int i = 0; i < v.Length; i++) v[i] += ruido * ((float)r.NextDouble() - 0.5f);
        return v;
    }

    private static AmostraDeVoz Amostra(float[] vetor, string dispositivo = "Headset",
                                        string faixa = "system") => new()
    {
        Vetor = vetor,
        CriadaEm = DateTimeOffset.UtcNow.ToString("o"),
        DuracaoS = 4.2,
        Origem = new Origem
        {
            Gravacao = "2026-08-10_11-50-26",
            Faixa = faixa,
            T0 = 312.4,
            T1 = 316.6,
            Dispositivo = dispositivo,
        },
    };

    [Fact]
    public void ABibliotecaComecaVazia()
    {
        // Decisão registrada: a do app Python não migra.
        var v = new Vozes(_pasta);
        Assert.Empty(v.Pessoas());
    }

    [Fact]
    public void AprenderGuardaAAmostraComProcedencia()
    {
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(1)));

        var perfil = new Vozes(_pasta).Perfil("Dimi");
        Assert.NotNull(perfil);
        var a = Assert.Single(perfil.Amostras);

        // O que a biblioteca antiga não tinha, e é o motivo de existir esta:
        Assert.Equal("2026-08-10_11-50-26", a.Origem.Gravacao);
        Assert.Equal("Headset", a.Origem.Dispositivo);
        Assert.Equal("system", a.Origem.Faixa);
        Assert.False(a.Quarentena);
    }

    [Fact]
    public void AVozConhecidaEhReconhecida()
    {
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(7)));

        // A mesma voz noutra reunião: o vetor não sai idêntico, mas perto.
        var r = v.Reconhecer(Vetor(7, ruido: 0.15f));
        Assert.NotNull(r);
        Assert.Equal("Dimi", r.Value.Pessoa);
        Assert.True(r.Value.Semelhanca >= Vozes.LimiarDeReconhecimento);
    }

    [Fact]
    public void VozDesconhecidaNaoEhAtribuidaAQualquerUm()
    {
        // O erro caro é o falso positivo: chamar de Dimi quem não é faz a ata
        // mentir, e quem lê não tem como desconfiar.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(7)));

        Assert.Null(v.Reconhecer(Vetor(99)));
    }

    [Fact]
    public void AmostraMuitoDistanteVaiParaQuarentenaEmVezDeEntrar()
    {
        // Distância grande tanto pode ser contaminação quanto condição nova
        // legítima. A máquina não distingue; por isso marca, e não descarta.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(3)));

        var suspeita = v.Aprender("Dimi", Amostra(Vetor(500)));
        Assert.True(suspeita.Quarentena);
    }

    [Fact]
    public void AmostraEmQuarentenaNaoParticipaDoReconhecimento()
    {
        // Usá-la seria deixar a contaminação agir justamente enquanto ela
        // espera julgamento.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(3)));
        v.Aprender("Dimi", Amostra(Vetor(500)));       // cai em quarentena

        Assert.Null(v.Reconhecer(Vetor(500, ruido: 0.05f)));
    }

    [Fact]
    public void APrimeiraAmostraNuncaCaiEmQuarentena()
    {
        // Não há com o que comparar; marcá-la seria pôr todo mundo na fila de
        // revisão logo ao ser nomeado pela primeira vez.
        var v = new Vozes(_pasta);
        Assert.False(v.Aprender("Alguém", Amostra(Vetor(42))).Quarentena);
    }

    [Fact]
    public void OReconhecimentoUsaOMelhorSubPerfilENaoUmaMediaDeTudo()
    {
        // Duas condições distintas da mesma pessoa — headset e sala. A média
        // das duas fica no meio do caminho e combina mal com ambas; o máximo
        // sobre os centróides de cada condição combina com a que estiver em uso.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(10), dispositivo: "Headset"));
        v.Aprender("Dimi", Amostra(Vetor(10, 0.1f), dispositivo: "Headset"));

        // A sala é uma condição nova, e por isso entra em quarentena: para a
        // máquina ela é indistinguível de contaminação. Quem ouviu o trecho
        // aprova, e só então ela passa a valer.
        v.Aprender("Dimi", Amostra(Vetor(900), dispositivo: "Sala"));
        var fila = v.EmQuarentena();
        Assert.Single(fila);
        Assert.True(v.Aprovar(fila[0].Pessoa, fila[0].Indice));

        // Chegando pela sala, é a condição "Sala" que tem de responder — e ela
        // responde porque o match é o máximo sobre os sub-perfis, não a média
        // de tudo, que ficaria no meio do caminho entre as duas condições.
        var r = v.Reconhecer(Vetor(900, ruido: 0.12f));
        Assert.NotNull(r);
        Assert.Equal("Dimi", r.Value.Pessoa);
    }

    [Fact]
    public void EsquecerTiraAAmostraEApagaOPerfilQuandoEsvazia()
    {
        var v = new Vozes(_pasta);
        v.Aprender("Temporário", Amostra(Vetor(5)));

        Assert.True(v.Esquecer("Temporário", 0));
        Assert.Empty(new Vozes(_pasta).Pessoas());
    }

    [Fact]
    public void BibliotecaIlegivelNaoDerrubaOApp()
    {
        // Reconhecer voz é um extra; a transcrição não pode depender dele.
        File.WriteAllText(Path.Combine(_pasta, "vozes.json"), "{ não é json");
        Assert.Empty(new Vozes(_pasta).Pessoas());
    }
}
