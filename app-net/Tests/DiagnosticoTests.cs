using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O bloco de diagnóstico da Fase 4.
/// </summary>
/// <remarks>
/// O que estes testes protegem não é a coleta — ela é leitura de ambiente, e
/// numa suíte portátil o ambiente é o do CI, não o da máquina de quem usa. O que
/// eles protegem é o <b>texto</b>: ele existe para ser colado numa conversa, e
/// duas coisas o inutilizariam em silêncio — omitir a ausência de placa (que é
/// justamente a informação que explica a lentidão) e vazar algo que impeça
/// alguém de colá-lo.
/// </remarks>
public sealed class DiagnosticoTests
{
    private static Diagnostico Exemplo(string? placa = null, string? motores = null) =>
        new()
        {
            Versao = "0.1.0",
            Windows = "Microsoft Windows 10.0.26100",
            Placa = placa,
            Motores = motores,
            Modelos = ["large-v3", "community-1"],
            Escolhidos = ["large-v3", "community-1", "qwen3-4b-instruct-q4km.gguf"],
            PastaDasGravacoes = @"C:\Users\alguem\Documents\MeetingRecordings",
            DiscoLivreGb = 120.4,
        };

    [Fact]
    public void SemPlacaOTextoDizOQueIssoSignifica()
    {
        // "placa: —" faria a pessoa concluir que o campo não foi lido. O que ela
        // precisa saber é a consequência, e é a consequência que vai no texto.
        string texto = Exemplo().ComoTexto();

        Assert.Contains("nenhuma NVIDIA", texto);
        Assert.Contains("CPU", texto);
    }

    [Fact]
    public void ComPlacaOTextoTrazPlacaEDriver()
    {
        // O driver junto, e não só o modelo da placa: a falha do CUDA 13.3 desta
        // máquina era de driver, não de placa (FASE3-HANDOFF §4), e sem o número
        // do driver o relato não permitiria distinguir os dois casos.
        string texto = Exemplo("NVIDIA GeForce RTX 2060 (driver 595.97)").ComoTexto();

        Assert.Contains("RTX 2060", texto);
        Assert.Contains("595.97", texto);
    }

    [Fact]
    public void MotorFaltandoApareceNoTexto()
    {
        string texto = Exemplo(motores: "o Python dos motores não está em C:\\x\\python.exe")
            .ComoTexto();

        Assert.Contains("python.exe", texto);
        Assert.DoesNotContain("no lugar", texto);
    }

    [Fact]
    public void SemModeloNenhumOTextoNaoFingeQueTem()
    {
        var vazio = new Diagnostico
        {
            Versao = "0.1.0", Windows = "x", Modelos = [], Escolhidos = ["large-v3"],
            PastaDasGravacoes = @"C:\g", DiscoLivreGb = -1,
        };

        Assert.Contains("modelos instalados: nenhum", vazio.ComoTexto());
        // Disco ilegível: melhor não dizer nada do que dizer "-1 GB livres".
        Assert.DoesNotContain("-1", vazio.ComoTexto());
    }

    [Fact]
    public void OTextoCabeNumaMensagemENaoTemLinhaVazia()
    {
        // Ele é colado num chat. Um bloco que rola por vinte linhas não é colado;
        // é resumido pela pessoa, e aí perde justamente o campo que importava.
        var linhas = Exemplo("NVIDIA GeForce RTX 2060 (driver 595.97)")
            .ComoTexto().Split('\n');

        Assert.InRange(linhas.Length, 5, 10);
        Assert.All(linhas, l => Assert.NotEqual("", l.Trim()));
    }

    [Fact]
    public void OTextoSerializadoEOMesmoQueOCalculado()
    {
        // A página desenha do campo `texto`, e não remonta o bloco em
        // JavaScript. Se a propriedade computada deixasse de ser serializada, o
        // botão copiaria vazio e ninguém notaria até alguém precisar dele.
        var d = Exemplo();
        Assert.Equal(d.ComoTexto(), d.Texto);
    }

    [Fact]
    public void AVersaoNaoVemComOSufixoDeBuild()
    {
        // O SDK pendura "+<sha>" no AssemblyInformationalVersion quando o
        // SourceLink está ligado. Quem usa o app diz "estou na 0.1.0", não
        // "estou na 0.1.0+a3f9c1".
        Assert.DoesNotContain("+", Diagnostico.VersaoDoApp());
    }

    [Fact]
    public void ProcurarPlacaNaoLevantaExcecaoOndeNaoHaNvidiaSmi()
    {
        // A suíte roda em Linux, sem nvidia-smi no PATH. Um Win32Exception
        // escapando daqui derrubaria a tela de Ajustes inteira na máquina de
        // quem não tem placa — que é exatamente quem mais precisa do bloco.
        var excecao = Record.Exception(() => Diagnostico.PlacaNvidia());
        Assert.Null(excecao);
    }
}
