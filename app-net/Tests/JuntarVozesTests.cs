using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Juntar dois perfis numa pessoa só.
/// </summary>
/// <remarks>
/// <para>
/// <b>O problema, relatado em campo em 09/09/2026.</b> O nome de uma pessoa é
/// digitado à mão, uma vez por reunião, e ninguém digita igual todas as vezes:
/// "Andre Yuri" e "André Yuri", "Diego" e "Diego Lacerda". Cada grafia vira um
/// perfil, e o reconhecimento passa a comparar contra dois centróides fracos em
/// vez de um forte — <b>quanto mais a pessoa é nomeada, pior ela é
/// reconhecida</b>. A única saída era esquecer um dos dois e perder as amostras.
/// </para>
/// <para>
/// O que estes testes guardam é que juntar <b>não perde nada</b>: os vetores, a
/// procedência, o modelo e o motor seguem intactos. É a procedência que torna a
/// auditoria possível depois — em particular a de desfazer em bloco o que veio
/// de um motor novo (<see cref="AmostraDeVoz.Motor"/>).
/// </para>
/// </remarks>
public sealed class JuntarVozesTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("juntar-vozes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private Vozes Banco() => new(_pasta);

    private static AmostraDeVoz Amostra(string gravacao, string? motor = null) => new()
    {
        Vetor = [1f, 0f, 0f],
        CriadaEm = DateTimeOffset.UtcNow.ToString("o"),
        DuracaoS = 4.5,
        Motor = motor,
        // Sem isto a amostra é da geração 1, e a geração 1 não conta em
        // comparação nenhuma (`Vozes.Conta`) — o teste do reconhecimento
        // passaria a medir a quarentena velha em vez da junção.
        Regras = Vozes.RegrasAtuais,
        Origem = new Origem
        {
            Gravacao = gravacao, Faixa = "system", T0 = 0, T1 = 4.5,
            Dispositivo = "headset",
        },
    };

    [Fact]
    public void AsAmostrasMudamDeDonoEOPerfilAntigoSome()
    {
        var v = Banco();
        v.Aprender("André Yuri", Amostra("a"));
        v.Aprender("André Yuri", Amostra("b"));
        v.Aprender("Andre Yuri", Amostra("c"));

        Assert.Equal(2, v.Juntar("André Yuri", "Andre Yuri"));

        Assert.DoesNotContain("André Yuri", v.Pessoas());
        Assert.Equal(3, v.Perfil("Andre Yuri")!.Amostras.Count);
    }

    [Fact]
    public void AProcedenciaAtravessaAJuncao()
    {
        // Sem isto, juntar seria uma forma silenciosa de apagar a auditoria — e
        // a auditoria é o que distingue esta biblioteca da anterior.
        var v = Banco();
        v.Aprender("Diego", Amostra("reuniao-x", "moss"));
        v.Aprender("Diego Lacerda", Amostra("reuniao-y"));

        v.Juntar("Diego", "Diego Lacerda");

        var perfil = v.Perfil("Diego Lacerda")!;
        Assert.Equal(2, perfil.Amostras.Count);
        Assert.Contains(perfil.Amostras, a => a.Origem.Gravacao == "reuniao-x");
        Assert.Contains(perfil.Amostras, a => Vozes.MotorDe(a) == "moss");
        Assert.Contains(perfil.Amostras, a => Vozes.MotorDe(a) == Vozes.MotorClassico);
    }

    [Fact]
    public void JuntarSobreviveAoArquivo()
    {
        // O que não é gravado não aconteceu: a próxima transcrição abre o banco
        // do zero, e é ela que precisa ver uma pessoa só.
        var v = Banco();
        v.Aprender("Ana", Amostra("a"));
        v.Aprender("Ana Silva", Amostra("b"));
        v.Juntar("Ana", "Ana Silva");

        var relido = Banco();
        Assert.Equal(["Ana Silva"], relido.Pessoas());
        Assert.Equal(2, relido.Perfil("Ana Silva")!.Amostras.Count);
    }

    [Fact]
    public void JuntarComQuemNaoExisteERenomear()
    {
        // O caso de ter digitado o nome torto UMA vez: não há com quem juntar,
        // e o que se quer é o nome certo. Cai no mesmo caminho de propósito —
        // uma operação a menos para explicar na tela.
        var v = Banco();
        v.Aprender("Alie Sena", Amostra("a"));

        Assert.Equal(1, v.Juntar("Alie Sena", "Aline Sena"));

        Assert.Equal(["Aline Sena"], v.Pessoas());
        Assert.Single(v.Perfil("Aline Sena")!.Amostras);
    }

    [Theory]
    [InlineData("", "Alguém")]
    [InlineData("Alguém", "")]
    [InlineData("Fantasma", "Alguém")]
    public void OQueNaoDaParaJuntarNaoMexeEmNada(string de, string para)
    {
        var v = Banco();
        v.Aprender("Alguém", Amostra("a"));

        Assert.Equal(0, v.Juntar(de, para));
        Assert.Equal(["Alguém"], v.Pessoas());
    }

    [Fact]
    public void JuntarAlguemComEleMesmoNaoDuplica()
    {
        // Um clique distraído no seletor não pode dobrar o perfil — e dobrar
        // enviesaria o centroide para as amostras repetidas.
        var v = Banco();
        v.Aprender("Paloma", Amostra("a"));

        Assert.Equal(0, v.Juntar("Paloma", "Paloma"));
        Assert.Single(v.Perfil("Paloma")!.Amostras);
    }

    [Fact]
    public void DepoisDeJuntarOReconhecimentoUsaAsDuasAmostras()
    {
        // O ponto inteiro da operação: um centroide forte em vez de dois fracos.
        var v = Banco();
        v.Aprender("Ellen", Amostra("a"));
        v.Aprender("Ellen R", Amostra("b"));
        v.Juntar("Ellen R", "Ellen");

        var quem = v.Reconhecer([1f, 0f, 0f]);

        Assert.NotNull(quem);
        Assert.Equal("Ellen", quem.Value.Pessoa);
    }
}
