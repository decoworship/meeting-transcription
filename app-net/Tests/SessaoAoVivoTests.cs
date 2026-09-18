using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A prévia ao vivo: o que dá para verificar sem placa e sem reunião.
/// </summary>
/// <remarks>
/// <para>
/// O laço em si — esperar o bloco fechar, chamar o motor, entregar — precisa de
/// um MOSS e de uma gravação acontecendo. O que entra aqui é o que decide se ele
/// vai funcionar: <b>ler uma janela de um WAV que ainda está sendo escrito</b>, e
/// <b>recusar-se a existir quando não pode</b>.
/// </para>
/// <para>
/// A leitura durante a gravação é a peça que o <c>T0.5</c> levanta, e ela tem
/// uma armadilha que só o Windows mostra: o <c>CrashSafeWavWriter</c> abre com
/// <c>FileAccess.Write, FileShare.Read</c> e o <c>File.OpenRead</c> pede
/// <c>FileShare.Read</c> — que quer dizer "eu não permito que escrevam". O
/// Windows recusa a abertura. Medido em 09/09/2026 na máquina do dono, e é por
/// isso que o <see cref="Faixas.LerJanela"/> abre com
/// <c>FileShare.ReadWrite</c>.
/// </para>
/// </remarks>
public sealed class SessaoAoVivoTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("aovivo").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    /// <summary>Um WAV com <paramref name="segundos"/> de tom.</summary>
    private string Wav(string nome, double segundos)
    {
        int n = (int)(segundos * Faixas.TaxaDeAmostragem);
        var a = new float[n];
        for (int i = 0; i < n; i++)
            a[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 220 * i / Faixas.TaxaDeAmostragem));

        string caminho = Path.Combine(_pasta, nome);
        Faixas.Escrever(caminho, a);
        return caminho;
    }

    [Fact]
    public void AJanelaTrazSoOPedacoPedido()
    {
        // É o que faz a prévia não reler a reunião inteira a cada três minutos:
        // numa de uma hora seriam 115 MB por faixa, vinte vezes.
        string w = Wav("t.wav", 400);

        var bloco = Faixas.LerJanela(w, 180, 360);

        Assert.Equal(180 * Faixas.TaxaDeAmostragem, bloco.Length);
    }

    [Fact]
    public void AJanelaAlemDoQueExisteDevolveSoOQueExiste()
    {
        // O caso normal de um arquivo crescendo: pede-se o bloco inteiro e ele
        // ainda não terminou de ser gravado. Devolver menos é a resposta certa —
        // quem chama decide se é cedo demais.
        string w = Wav("curto.wav", 200);

        Assert.Equal(20 * Faixas.TaxaDeAmostragem, Faixas.LerJanela(w, 180, 360).Length);
        Assert.Empty(Faixas.LerJanela(w, 300, 480));
    }

    [Fact]
    public void SemJanelaOArquivoInteiroContinuaSendoLido()
    {
        // A mudança não pode alterar o caminho de sempre: o pipeline offline lê
        // a faixa inteira, e é o mesmo código.
        string w = Wav("inteiro.wav", 5);

        Assert.Equal(5 * Faixas.TaxaDeAmostragem, Faixas.LerUma(w).Length);
    }

    [Fact]
    public void LerNaoImpedeQueOutroEscrevaNoMesmoArquivo()
    {
        // **A armadilha, invertida.** No Windows, um leitor que pede
        // FileShare.Read impede a escrita e é recusado quando já há um escritor.
        // Aqui o que se guarda é o outro lado da mesma moeda: enquanto a prévia
        // lê, o gravador precisa continuar escrevendo. Em Linux o sistema não
        // impõe isso, então o teste não prova o comportamento do Windows — ele
        // guarda a INTENÇÃO, e falharia se alguém trocasse o modo de volta para
        // um que o próprio .NET recusa.
        string w = Wav("compartilhado.wav", 3);

        using var escritor = new FileStream(w, FileMode.Open, FileAccess.Write,
                                            FileShare.ReadWrite);
        var lido = Faixas.LerJanela(w, 0, 1);

        Assert.Equal(Faixas.TaxaDeAmostragem, lido.Length);
    }

    /// <summary>
    /// O motor clássico serve à prévia desde 10/09/2026 — e o que impede, quando
    /// impede, é a instalação, não a escolha.
    /// </summary>
    /// <remarks>
    /// Antes disso a prévia exigia o MOSS, porque ela mostrava o falante. Com a
    /// tela em conversa — sua fala de um lado, a dos outros do outro, sem nome —
    /// o que ela precisa é texto, e o dono vem da faixa do microfone.
    /// </remarks>
    [Fact]
    public void ComOMotorClassicoAPreviaExiste()
    {
        var motores = new Motores("python", "asr.py", "diar.py", "modelos.py");
        var cfg = new ConfiguracoesDoApp { MotorDeTranscricao = Vozes.MotorClassico };

        // Estes caminhos não existem em disco, então o que sobra é a queixa da
        // instalação — e ela não fala do MOSS.
        string? impede = SessaoAoVivo.OQueImpede(motores, cfg);

        Assert.DoesNotContain("MOSS", impede ?? "");
    }

    [Fact]
    public void OBlocoDaPreviaEODoPipelineSaoOMesmo()
    {
        // Três minutos, e o número tem medição: o bloco de 3 min tem a
        // diarização do de 5 sem os órfãos do de 1 (FASE7-RESULTADOS §3).
        // A constante morava na MossEmBlocos até 17/09/2026; o teste sobrevive
        // porque o número é do bloco, não do motor que o lia.
        Assert.Equal(180.0, SessaoAoVivo.BlocoS);
    }

    /// <summary>Uma faixa em que só o intervalo pedido tem som.</summary>
    private static float[] FalaEntre(double segundos, double de, double ate, float amplitude)
    {
        var a = new float[(int)(segundos * Faixas.TaxaDeAmostragem)];
        int i0 = (int)(de * Faixas.TaxaDeAmostragem);
        int i1 = Math.Min(a.Length, (int)(ate * Faixas.TaxaDeAmostragem));
        for (int i = i0; i < i1; i++) a[i] = amplitude;
        return a;
    }

    [Fact]
    public void ODonoAparaceNoBlocoQueNaoEOPrimeiro()
    {
        // **O defeito que esta função existe para impedir.** Os carimbos do motor
        // são locais ao bloco e a janela também começa em zero; deslocar antes de
        // medir pede o RMS do minuto 12 a um vetor de três minutos, o Faixas.Rms
        // corta o intervalo e devolve 0, e o dono some. No primeiro bloco `de` é
        // zero e as duas contas coincidem — por isso o teste é do quarto.
        var janela = new Faixas(FalaEntre(180, 5, 10, 0.3f),
                                FalaEntre(180, 5, 10, 0.01f));
        List<SegmentoFinal> locais =
            [new() { Start = 5, End = 10, Text = "eu falei aqui", Speaker = "b3_SPEAKER_00" }];

        var trechos = SessaoAoVivo.DonoEDepoisORelogio(locais, janela, de: 540);

        Assert.Equal("You", trechos[0].Speaker);
        Assert.Equal(545, trechos[0].Start);
        Assert.Equal(550, trechos[0].End);
    }

    [Fact]
    public void QuemNaoEODonoChegaComORotuloDoBlocoEOTempoDaReuniao()
    {
        // O outro lado: som só na faixa do sistema não vira "You", e o rótulo
        // local sobrevive intacto — é dele que a costura da passada final parte.
        var janela = new Faixas(FalaEntre(180, 5, 10, 0.001f),
                                FalaEntre(180, 5, 10, 0.3f));
        List<SegmentoFinal> locais =
            [new() { Start = 5, End = 10, Text = "outra pessoa", Speaker = "b3_SPEAKER_01" }];

        var trechos = SessaoAoVivo.DonoEDepoisORelogio(locais, janela, de: 540);

        Assert.Equal("b3_SPEAKER_01", trechos[0].Speaker);
        Assert.Equal(545, trechos[0].Start);
    }
}
