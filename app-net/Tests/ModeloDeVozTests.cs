using System.Text.RegularExpressions;
using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A chave que liga cada voz aprendida ao modelo que a produziu (VOZES.md §7).
/// </summary>
/// <remarks>
/// Um vetor só significa alguma coisa dentro do modelo que o gerou. Comparar
/// entre modelos <b>não falha</b>: devolve um número plausível, e o sintoma
/// aparece meses depois como um nome trocado numa ata, sem nada no arquivo que
/// explique. Estes testes prendem as duas pontas: a marcação de cada amostra e
/// o nome do modelo, que está escrito em C# e em Python.
/// </remarks>
public sealed class ModeloDeVozTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("modelo-de-voz").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    /// <summary>
    /// Vetores pseudoaleatórios, como em <see cref="VozesTests"/>.
    /// </summary>
    /// <remarks>
    /// Sementes diferentes dão direções quase ortogonais, que é o que faz
    /// "parecido" e "diferente" significarem alguma coisa no cosseno. Uma rampa
    /// linear pareceria consigo mesma em qualquer semente.
    /// </remarks>
    private static float[] Vetor(int semente)
    {
        var r = new Random(semente);
        var v = new float[256];
        for (int i = 0; i < v.Length; i++) v[i] = (float)r.NextDouble() - 0.5f;
        return v;
    }

    private static AmostraDeVoz Amostra(float[] vetor, string? modelo = null) => new()
    {
        Vetor = vetor,
        CriadaEm = DateTimeOffset.UtcNow.ToString("o"),
        DuracaoS = 5,
        Modelo = modelo,
        Regras = Vozes.RegrasAtuais,
        Origem = new Origem
        {
            Gravacao = "2026-08-20_10-00-00", Faixa = "system", T0 = 0, T1 = 5,
        },
    };

    [Fact]
    public void AmostraSemModeloEDoModeloDeSempre()
    {
        // Tudo o que foi gravado antes de 20/08/2026 saiu do wespeaker, porque
        // ele nunca foi escolhível. Não é suposição — é a única possibilidade —,
        // e é o que impede a chave nova de invalidar a biblioteca existente.
        Assert.Equal(Vozes.ModeloDeVozPadrao, Vozes.ModeloDe(Amostra(Vetor(1))));
    }

    [Fact]
    public void QuemFoiAprendidoNoModeloAntigoNaoResponderNoNovo()
    {
        // O pedido do dono do produto: trocado o modelo, o app aprende as vozes
        // do zero. O caro seria o contrário — responder com um nome que saiu de
        // uma comparação que não vale.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(7)));

        Assert.Null(v.Reconhecer(Vetor(7), "outro/modelo-novo"));
    }

    [Fact]
    public void EQuemFoiAprendidoNoNovoNaoResponderNoAntigo()
    {
        // A recusa vale nos dois sentidos: não é "o antigo é pior", é "os dois
        // não se comparam".
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(7), "outro/modelo-novo"));

        Assert.Null(v.Reconhecer(Vetor(7)));
        Assert.NotNull(v.Reconhecer(Vetor(7), "outro/modelo-novo"));
    }

    [Fact]
    public void ADuasBibliotecasConvivemNoMesmoPerfil()
    {
        // Trocar de modelo não apaga nada: a mesma pessoa passa a ter amostras
        // dos dois, e cada uma responde no seu. É o que permite voltar atrás
        // sem ter reinscrito ninguém à toa.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(7)));
        v.Aprender("Dimi", Amostra(Vetor(300), "outro/modelo-novo"));

        Assert.Equal("Dimi", v.Reconhecer(Vetor(7))!.Value.Pessoa);
        Assert.Equal("Dimi", v.Reconhecer(Vetor(300), "outro/modelo-novo")!.Value.Pessoa);
        Assert.Equal(2, v.Perfil("Dimi")!.Amostras.Count);
    }

    [Fact]
    public void APrimeiraAmostraDeUmModeloNovoNaoCaiEmQuarentena()
    {
        // Ela destoa de tudo o que existe — porque o que existe é de outro
        // espaço vetorial. Marcá-la como suspeita mandaria para a fila de
        // revisão justamente a única voz limpa que o modelo novo tem.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(3)));

        Assert.False(v.Aprender("Dimi", Amostra(Vetor(900), "outro/modelo-novo")).Quarentena);
    }

    [Fact]
    public void AQuarentenaContinuaValendoDentroDoMesmoModelo()
    {
        // A guarda nova não pode ter afrouxado a antiga.
        var v = new Vozes(_pasta);
        v.Aprender("Dimi", Amostra(Vetor(3), "m"));

        Assert.True(v.Aprender("Dimi", Amostra(Vetor(500), "m")).Quarentena);
    }

    [Fact]
    public void OMotorDizOMesmoNomeDeModeloQueONucleo()
    {
        // Mesma ideia do MarcaTests: o nome está escrito em dois arquivos que
        // não se falam — Vozes.cs e motores/diarizacao/motor.py. Se eles
        // discordarem, toda voz já aprendida passa a ser de "outro modelo" e o
        // app deixa de reconhecer quem sempre reconheceu, em silêncio.
        string? motor = Achar(Path.Combine("motores", "diarizacao", "motor.py"));
        // Fora do repositório não há o que comparar: a suíte é net8.0 portátil.
        if (motor is null) return;

        var m = Regex.Match(File.ReadAllText(motor),
                            @"^MODELO_DE_VOZ\s*=\s*""([^""]*)""", RegexOptions.Multiline);

        Assert.True(m.Success, $"{motor} perdeu o MODELO_DE_VOZ.");
        Assert.Equal(Vozes.ModeloDeVozPadrao, m.Groups[1].Value);
    }

    private static string? Achar(string relativo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string tentativa = Path.Combine(dir.FullName, relativo);
            if (File.Exists(tentativa)) return tentativa;
            dir = dir.Parent;
        }
        return null;
    }
}
