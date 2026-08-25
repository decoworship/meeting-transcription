using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O descarte da geração de vozes aprendida antes da guarda de contaminação
/// (FASE6 §4.2, decisão do dono do produto em 20/08/2026).
/// </summary>
/// <remarks>
/// Não dá para saber <b>quais</b> amostras a regra velha estragou — o vetor via
/// o intervalo inteiro do segmento, e a contaminação não deixa marca no
/// arquivo. Por isso a geração inteira sai de circulação, e o app reaprende as
/// vozes à medida que as pessoas forem nomeadas de novo.
/// </remarks>
public sealed class GeracaoDeRegrasTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("geracao-de-regras").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static float[] Vetor(int semente)
    {
        var r = new Random(semente);
        var v = new float[256];
        for (int i = 0; i < v.Length; i++) v[i] = (float)r.NextDouble() - 0.5f;
        return v;
    }

    /// <param name="regras">Nulo reproduz o que está gravado em disco hoje.</param>
    private static AmostraDeVoz Amostra(float[] vetor, int? regras) => new()
    {
        Vetor = vetor,
        CriadaEm = DateTimeOffset.UtcNow.ToString("o"),
        DuracaoS = 5,
        Regras = regras,
        Origem = new Origem
        {
            Gravacao = "2026-08-11_08-02-40", Faixa = "system", T0 = 0, T1 = 5,
        },
    };

    [Fact]
    public void AmostraSemGeracaoEAPrimeira()
    {
        // As 48 amostras que existiam em 20/08/2026 não têm o campo. Ausente é
        // "geração 1", e não "atual": elas são exatamente as que passaram pelo
        // caminho sem guarda.
        Assert.Equal(1, Vozes.RegrasDe(Amostra(Vetor(1), null)));
    }

    [Fact]
    public void AGeracaoAntigaNaoReconheceNinguem()
    {
        // A decisão, em uma linha: o que foi aprendido antes não vale mais.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(7), regras: null));

        Assert.Null(v.Reconhecer(Vetor(7)));
    }

    [Fact]
    public void OAprendizadoNovoVoltaAReconhecer()
    {
        // E a outra metade: reinscrever funciona, e acontece sozinho quando a
        // pessoa é nomeada de novo numa reunião.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(7), regras: null));
        v.Aprender("Dimi", Amostra(Vetor(7), Vozes.RegrasAtuais));

        Assert.Equal("Dimi", v.Reconhecer(Vetor(7))!.Value.Pessoa);
    }

    [Fact]
    public void NadaEApagado()
    {
        // Descartar é não usar. O arquivo continua inteiro: a tela mostra as
        // amostras antigas apagadas, e apagá-las de verdade é decisão de quem
        // lê, pelo botão que já existe.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(7), regras: null));
        v.Aprender("Dimi", Amostra(Vetor(8), regras: null));

        Assert.Equal(2, v.Perfil("Dimi")!.Amostras.Count);
    }

    [Fact]
    public void APrimeiraAmostraDaGeracaoNovaNaoCaiEmQuarentena()
    {
        // Ela não tem com o que ser comparada — o que existe não conta mais.
        // Marcá-la mandaria para a fila de revisão a primeira voz limpa de cada
        // pessoa, ou seja, todas elas.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(3), regras: null));

        Assert.False(v.Aprender("Dimi", Amostra(Vetor(900), Vozes.RegrasAtuais)).Quarentena);
    }

    [Fact]
    public void UmaAmostraEmQuarentenaContinuaForaMesmoNaGeracaoNova()
    {
        // As três razões de não participar são independentes, e a guarda nova
        // não pode ter afrouxado nenhuma das outras.
        var a = Amostra(Vetor(5), Vozes.RegrasAtuais);
        a.Quarentena = true;

        Assert.False(Vozes.Conta(a, Vozes.ModeloDeVozPadrao));
    }
}
