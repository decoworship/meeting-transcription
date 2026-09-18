using System.Text.Json;
using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// De qual motor veio a identidade que alguém nomeou.
/// </summary>
/// <remarks>
/// <para>
/// <b>Transcrever com o MOSS não contamina o banco de vozes</b> — o pipeline só
/// chama o <c>ReconhecerAsync</c>, que lê e não escreve. <b>Nomear</b>, sim: um
/// nome posto num falante grava um vetor que atravessa para todas as reuniões
/// seguintes, e retranscrever não desfaz.
/// </para>
/// <para>
/// E o falante do MOSS não é um rótulo do modelo: é uma identidade
/// <b>costurada</b> por vetor de voz, e a costura divide gente demais e pode
/// fundir — 15 identidades para 8 pessoas na reunião de 122 min
/// (<c>docs/FASE7-RESULTADOS.md</c> §11.4). Um vetor inscrito a partir de uma
/// fusão sai com duas pessoas dentro, e o erro aparece meses depois como "o app
/// chamou a Vanessa de Carla".
/// </para>
/// <para>
/// A origem não impede a inscrição — ela permite <b>desfazer em bloco</b>. É a
/// mesma lição do <see cref="AmostraDeVoz.Regras"/>: quando não se sabe quais
/// amostras a regra velha estragou, o que salva é poder separar a geração
/// inteira. É barato agora e caro depois.
/// </para>
/// </remarks>
public sealed class OrigemDaVozTests
{
    private static AmostraDeVoz Amostra(string? motor) => new()
    {
        Vetor = [1f, 0f],
        CriadaEm = DateTimeOffset.UtcNow.ToString("o"),
        DuracaoS = 4.5,
        Motor = motor,
        Origem = new Origem
        {
            Gravacao = "2026-09-04_10-00-00", Faixa = "system", T0 = 0, T1 = 4.5,
        },
    };

    [Fact]
    public void AmostraSemMarcaEDoMotorClassico()
    {
        // Ausente não é "desconhecido": antes de 03/09/2026 não havia outro
        // motor, então a resposta é certa, não suposta.
        Assert.Equal(Vozes.MotorClassico, Vozes.MotorDe(Amostra(null)));
        Assert.Equal(Vozes.MotorClassico, Vozes.MotorDe(Amostra("")));
    }

    [Fact]
    public void AsDuasOrigensSaoDistinguiveis()
    {
        Assert.Equal("moss", Vozes.MotorDe(Amostra("moss")));
        Assert.NotEqual(Vozes.MotorDe(Amostra("moss")),
                        Vozes.MotorDe(Amostra(null)));
    }

    [Fact]
    public void OClassicoNaoCarimbaNada()
    {
        // Nulo e não "classico": o campo existe para poder desfazer o que veio de
        // um caminho novo, e escrevê-lo em toda amostra faria o arquivo de vozes
        // de todo mundo mudar numa atualização em que nada mudou.
        Assert.Null(Vozes.MotorAceitoNaAmostra(null));
        Assert.Null(Vozes.MotorAceitoNaAmostra("classico"));
        Assert.Null(Vozes.MotorAceitoNaAmostra("qualquer coisa"));
        // **E desde 17/09/2026, nem o "moss" carimba**: há um motor só. O
        // arquivo de quem experimentou continua com o campo escrito, e o
        // MotorDe sabe lê-lo — o que não acontece mais é escrever.
        Assert.Null(Vozes.MotorAceitoNaAmostra("moss"));
    }

    [Fact]
    public void AMarcaAtravessaOArquivo()
    {
        // O que não sobrevive à serialização não serve para desfazer em bloco
        // depois — que é a única razão de este campo existir.
        string json = JsonSerializer.Serialize(Amostra("moss"));
        Assert.Contains("\"motor\":\"moss\"", json);

        var lida = JsonSerializer.Deserialize<AmostraDeVoz>(json);
        Assert.Equal("moss", Vozes.MotorDe(lida!));
    }

    [Fact]
    public void AChaveDoAppEODaAmostraDizemAMesmaPalavra()
    {
        // **Desde 17/09/2026 há um motor só, e o que este teste guarda mudou
        // de objeto:** não é mais "as duas pontas escrevem a mesma palavra", é
        // "o app.json de quem experimentou o MOSS volta ao clássico sem
        // recusar a transcrição". Ver docs/CONVERGENCIA.md.
        Assert.Equal(Vozes.MotorClassico, new ConfiguracoesDoApp().MotorDeTranscricao);
        Assert.Equal(Vozes.MotorClassico, ConfiguracoesDoApp.MotorAceito("moss"));
        Assert.Equal(Vozes.MotorClassico, ConfiguracoesDoApp.MotorAceito("classico"));
    }

    [Fact]
    public void ChaveEstranhaCaiNoClassicoSemLevantarErro()
    {
        // Esta chave é editável à mão num arquivo, e um app.json com um typo não
        // pode impedir alguém de transcrever. Mesmo portão do TemaAceito.
        Assert.Equal(Vozes.MotorClassico, ConfiguracoesDoApp.MotorAceito("MOSSS"));
        Assert.Equal(Vozes.MotorClassico, ConfiguracoesDoApp.MotorAceito(null));
        Assert.Equal(Vozes.MotorClassico, ConfiguracoesDoApp.MotorAceito(""));
        Assert.Equal(Vozes.MotorClassico, ConfiguracoesDoApp.MotorAceito("MOSS"));
    }

    [Fact]
    public void AChaveSobreviveAoArquivo()
    {
        string caminho = Path.Combine(
            Directory.CreateTempSubdirectory("motor-chave").FullName, "app.json");
        try
        {
            new ConfiguracoesDoApp { MotorDeTranscricao = "moss" }.Salvar(caminho);
            Assert.Equal("moss",
                         ConfiguracoesDoApp.Carregar(caminho).MotorDeTranscricao);

            // E um app.json antigo, sem a chave, continua sendo o clássico.
            File.WriteAllText(caminho, "{ \"tema\": \"claro\" }");
            Assert.Equal(Vozes.MotorClassico,
                         ConfiguracoesDoApp.Carregar(caminho).MotorDeTranscricao);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(caminho)!, recursive: true);
        }
    }
}
