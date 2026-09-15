using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A retomada não atravessa a troca de motor.
/// </summary>
/// <remarks>
/// <para>
/// <b>É o defeito da 0.4.0 com outra roupa, e ele quase entrou.</b> O parcial é
/// conferido por modelo, idioma e vocabulário — os três que decidem a saída do
/// ASR clássico. O MOSS <b>não usa nenhum dos três</b>: ele não recebe modelo de
/// Whisper, recusa idioma, e nenhum runtime ggml expõe o <c>hotwords</c>. Um
/// parcial dele bateria nas três conferências e seria reaproveitado numa rodada
/// clássica — que pularia o ASR e devolveria o texto do outro motor, rotulado
/// como sendo deste, em silêncio.
/// </para>
/// <para>
/// "Não retomar é sempre seguro; retomar o parcial errado devolve o texto de
/// outro modelo sem dizer nada" — a regra do <c>CLAUDE.md</c>, aplicada ao eixo
/// que faltava.
/// </para>
/// </remarks>
public sealed class RetomadaPorMotorTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("retomada-motor").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    /// <summary>Escreve um parcial como o pipeline o escreveria, com o motor dado.</summary>
    private void Parcial(string? motor)
    {
        Retomada.Escrever(_pasta, new ResultadoDaTranscricao
        {
            Language = "pt",
            Duration = 12.0,
            Segments = [new SegmentoFinal { Start = 0, End = 5, Text = " bom dia" }],
        }, "large-v3", "pt", null, motor);
    }

    [Fact]
    public void OMesmoMotorRetoma()
    {
        Parcial(Vozes.MotorClassico);
        Assert.NotNull(Retomada.Ler(_pasta, "large-v3", "pt", null, Vozes.MotorClassico));
    }

    [Fact]
    public void OParcialDoMossNaoServeAUmaRodadaClassica()
    {
        // O caso que este arquivo existe para impedir: os três parâmetros de
        // sempre batem, e mesmo assim o ASR tem de rodar de novo.
        Parcial(Vozes.MotorMoss);
        Assert.Null(Retomada.Ler(_pasta, "large-v3", "pt", null, Vozes.MotorClassico));
    }

    [Fact]
    public void OParcialClassicoNaoServeAUmaRodadaDoMoss()
    {
        Parcial(Vozes.MotorClassico);
        Assert.Null(Retomada.Ler(_pasta, "large-v3", "pt", null, Vozes.MotorMoss));
    }

    [Fact]
    public void OParcialDoMossRetomaNoMoss()
    {
        // Ele continua valendo para o que é: no caminho do MOSS a etapa pendente
        // é a costura, e o parcial guarda os rótulos locais para ela refazer.
        Parcial(Vozes.MotorMoss);
        Assert.NotNull(Retomada.Ler(_pasta, "large-v3", "pt", null, Vozes.MotorMoss));
    }

    [Fact]
    public void ParcialSemMarcaEDoMotorClassico()
    {
        // **Ausente não é desconhecido.** Todo parcial escrito antes de
        // 03/09/2026 saiu do pipeline de dois motores, porque não havia outro —
        // recusá-los jogaria fora um ASR que deu certo por uma chave que ninguém
        // tinha como escrever. É o mesmo raciocínio do AmostraDeVoz.Motor.
        string json = """
        {
          "language": "pt",
          "duration": 12.0,
          "segments": [{ "start": 0, "end": 5, "text": " bom dia" }],
          "pending": {
            "steps": ["diarizacao"],
            "model": "large-v3",
            "language": "pt",
            "vocabulary": null
          }
        }
        """;
        File.WriteAllText(Path.Combine(_pasta, "transcricao.json"), json);

        Assert.NotNull(Retomada.Ler(_pasta, "large-v3", "pt", null, Vozes.MotorClassico));
        Assert.Null(Retomada.Ler(_pasta, "large-v3", "pt", null, Vozes.MotorMoss));
    }

    [Fact]
    public void NaoPassarMotorContinuaSendoOClassico()
    {
        // A assinatura antiga — quatro argumentos — continua valendo, e continua
        // significando a mesma coisa. O CLI e quem mais chamar não precisam saber
        // que existe um segundo motor.
        Parcial(null);
        Assert.NotNull(Retomada.Ler(_pasta, "large-v3", "pt", null));
    }

    [Fact]
    public void OArquivoProntoDoCaminhoClassicoSaiSemOCampoNovo()
    {
        // A paridade com o history/ do Python se mede byte a byte, e um campo a
        // mais em toda transcrição a quebraria. Só o caminho do MOSS o escreve.
        var classico = new ResultadoDaTranscricao
        {
            Language = "pt", Duration = 1, Segments = [], Engine = null,
        };
        Assert.DoesNotContain("engine", classico.ParaJson());

        var doMoss = new ResultadoDaTranscricao
        {
            Language = "pt", Duration = 1, Segments = [], Engine = Vozes.MotorMoss,
        };
        Assert.Contains("\"engine\": \"moss\"", doMoss.ParaJson());
    }
}
