using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>
/// Requisito 3.1 da FASE1: âncora no relógio do dispositivo, não no de chegada.
/// </summary>
public sealed class DriftAnchorTests
{
    private const int Taxa = CrashSafeWavWriter.TaxaAlvo;

    [Fact]
    public void DentroDaToleranciaNaoCorrige()
    {
        var a = new DriftAnchor();
        // 40 ms de diferença, abaixo dos 50 ms de tolerância. Corrigir aqui
        // significaria mexer na faixa a cada bloco por jitter de agendamento.
        long delta = a.Calcular(posicaoDispositivoAmostras: Taxa + (Taxa * 40 / 1000),
                                amostrasEscritas: Taxa, amostrasNesteBloco: 0);
        Assert.Equal(0, delta);
        Assert.Equal(0, a.Correcoes);
    }

    [Fact]
    public void DispositivoAdiantadoInsereSilencio()
    {
        var a = new DriftAnchor();
        // O hardware digitalizou 1 s a mais do que o arquivo tem: a faixa está
        // curta e precisa ganhar amostras para continuar alinhada no tempo.
        long delta = a.Calcular(2 * Taxa, amostrasEscritas: Taxa, amostrasNesteBloco: 0);
        Assert.Equal(Taxa, delta);
        Assert.Equal(1, a.Correcoes);
        Assert.Equal(Taxa, a.AmostrasLiquidas);
    }

    [Fact]
    public void DispositivoAtrasadoDescarta()
    {
        var a = new DriftAnchor();
        long delta = a.Calcular(Taxa, amostrasEscritas: Taxa, amostrasNesteBloco: Taxa);
        Assert.True(delta < 0);
        Assert.Equal(-Taxa, delta);
    }

    [Fact]
    public void NuncaDescartaMaisDoQueOBlocoTem()
    {
        var a = new DriftAnchor();
        // Dispositivo 10 s atrás, bloco de 0,1 s. Descartar 10 s exigiria
        // desfazer escrita já feita; o excedente se resolve nos próximos blocos.
        int bloco = Taxa / 10;
        long delta = a.Calcular(0, amostrasEscritas: 10 * Taxa, amostrasNesteBloco: bloco);
        Assert.Equal(-bloco, delta);
    }

    /// <summary>
    /// A regressão que motivou o requisito: atraso de processamento não pode
    /// virar correção.
    /// </summary>
    /// <remarks>
    /// O gravador Python compara com <c>time.monotonic()</c> lido na thread de
    /// escrita. Se a fila acumulou 2 s de backlog, o relógio de parede já andou
    /// 2 s além do que foi escrito, e ele "corrige" inserindo 2 s de silêncio —
    /// deslocando a faixa de verdade. Com a posição do dispositivo como
    /// referência, o backlog é invisível: o hardware capturou exatamente o que o
    /// arquivo vai receber.
    /// </remarks>
    [Fact]
    public void BacklogDeProcessamentoNaoGeraCorrecao()
    {
        var a = new DriftAnchor();

        // Cenário: 10 s gravados, 10 s digitalizados pelo hardware, mas o writer
        // só agora processa — e o relógio de parede já marca 12 s.
        long posicaoDispositivo = 10 * Taxa;
        long escritas = 10 * Taxa;

        long delta = a.Calcular(posicaoDispositivo, escritas, amostrasNesteBloco: 0);

        Assert.Equal(0, delta);
        Assert.Equal(0, a.Correcoes);
    }

    [Fact]
    public void InsercaoEntraEmTrechoSilencioso()
    {
        // Bloco com fala no começo e silêncio no fim.
        var bloco = new float[3200];                       // 200 ms
        for (int i = 0; i < 1600; i++) bloco[i] = 0.5f;    // primeira metade audível

        var saida = DriftAnchor.Aplicar(bloco, correcao: 160);

        Assert.Equal(bloco.Length + 160, saida.Length);
        // A fala tem que sair intacta e contígua: se a inserção tivesse caído no
        // meio dela, haveria zeros antes da amostra 1600.
        for (int i = 0; i < 1600; i++)
            Assert.Equal(0.5f, saida[i]);
    }

    [Fact]
    public void SemTrechoSilenciosoInsereNoFim()
    {
        var bloco = new float[3200];
        for (int i = 0; i < bloco.Length; i++) bloco[i] = 0.5f;   // tudo audível

        var saida = DriftAnchor.Aplicar(bloco, correcao: 160);

        Assert.Equal(bloco.Length + 160, saida.Length);
        for (int i = 0; i < bloco.Length; i++) Assert.Equal(0.5f, saida[i]);
        for (int i = bloco.Length; i < saida.Length; i++) Assert.Equal(0f, saida[i]);
    }

    [Fact]
    public void DescarteTiraDoTrechoSilencioso()
    {
        // Fala nos dois extremos, silêncio no meio: o descarte tem que sair do
        // meio. Truncar o fim removeria fala real, tão destrutivo quanto inserir
        // no meio de uma palavra.
        var bloco = new float[3200];
        for (int i = 0; i < 1000; i++) bloco[i] = 0.5f;
        for (int i = 2000; i < 3200; i++) bloco[i] = 0.5f;

        var saida = DriftAnchor.Aplicar(bloco, correcao: -160);

        Assert.Equal(bloco.Length - 160, saida.Length);
        for (int i = 0; i < 1000; i++) Assert.Equal(0.5f, saida[i]);
        // A fala do fim tem que continuar lá — se o corte fosse no rabo, os
        // últimos 160 valores teriam sumido.
        Assert.Equal(0.5f, saida[^1]);
    }

    [Fact]
    public void SemTrechoSilenciosoDescarteCortaOFim()
    {
        var bloco = new float[1000];
        for (int i = 0; i < bloco.Length; i++) bloco[i] = 0.5f;   // tudo audível

        var saida = DriftAnchor.Aplicar(bloco, correcao: -100);

        Assert.Equal(900, saida.Length);
        for (int i = 0; i < 900; i++) Assert.Equal(0.5f, saida[i]);
    }

    [Fact]
    public void CorrecoesSeAcumulamComSinal()
    {
        var a = new DriftAnchor();
        a.Calcular(2 * Taxa, Taxa, 0);                 // +1 s
        a.Calcular(0, 2 * Taxa, Taxa);                 // -1 s (limitado ao bloco)
        Assert.Equal(2, a.Correcoes);
        Assert.Equal(0, a.AmostrasLiquidas);
    }

    /// <summary>
    /// O bug do craquelado no Bluetooth: o preenchimento por relógio corre à
    /// frente, o pacote real chega carimbado atrás, e a âncora joga fora áudio
    /// de verdade para "corrigir" uma deriva que não existe.
    /// </summary>
    /// <remarks>
    /// Medido na gravação `2026-08-10_10-06-09`: 790 correções em 56 s e 14% das
    /// amostras descartadas, contra 2 correções do gravador Python na mesma
    /// reunião. Cada descarte é um corte abrupto no meio da fala — o craquelado
    /// que o ouvido pegou antes de qualquer métrica.
    /// </remarks>
    [Theory]
    [InlineData(100)]    // dispositivo comum: carimbo quase em tempo real
    [InlineData(400)]    // headset Bluetooth: o pacote descreve o passado distante
    public void PreenchimentoOciosoNaoFazAAncoraDescartarAudioReal(int atrasoMs)
    {
        const int taxa = CrashSafeWavWriter.TaxaAlvo;
        const int blocoMs = 10;
        var linha = new PacketTimeline(0);
        var ancora = new DriftAnchor();
        long escritas = 0;

        long qpcDe(double ms) => (long)(ms * PacketTimeline.QpcPorSegundo / 1000);
        int amostras(double ms) => (int)(ms * taxa / 1000);

        // 5 s de captura: a cada 10 ms o laço ou recebe um pacote (carimbado
        // `atrasoMs` no passado) ou preenche o ocioso até agora menos a margem.
        for (int t = 0; t < 5000; t += blocoMs)
        {
            // O preenchimento ocioso do laço de captura: passa o relógio cru, e
            // a linha do tempo desconta a margem que ela mesma mede.
            escritas += linha.SilencioAte(qpcDe(t));

            double carimbo = t - atrasoMs;
            if (carimbo < 0) continue;

            var d = linha.Chegou(qpcDe(carimbo), amostras(blocoMs), AnomaliaPacote.Nenhuma,
                                 qpcAgora: qpcDe(t));
            escritas += d.SilencioAntes;

            long correcao = ancora.Calcular(d.PosicaoAlvo, escritas, amostras(blocoMs));
            escritas += amostras(blocoMs) + correcao;
        }

        // Descarte líquido negativo aqui é áudio real jogado fora: o dispositivo
        // não derivou 14% em 5 s, foi o preenchimento que roubou o lugar dele.
        Assert.True(ancora.AmostrasLiquidas > -taxa / 10,
            $"âncora descartou {-ancora.AmostrasLiquidas} amostras "
            + $"({-ancora.AmostrasLiquidas / (double)taxa:F2} s) com atraso de {atrasoMs} ms; "
            + $"{ancora.Correcoes} correções");
    }
}
