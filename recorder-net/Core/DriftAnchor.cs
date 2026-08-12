using System;

namespace MeetingRecorder.Core;

/// <summary>
/// Mantém uma faixa alinhada com o tempo real, corrigindo a deriva do clock do
/// dispositivo.
/// </summary>
/// <remarks>
/// <para>
/// Os dois dispositivos têm clocks de hardware independentes. Medido nesta
/// máquina: <c>system +0,103%</c> e <c>mic +0,145%</c> — o que dá 3,7 s e 5,2 s
/// de desalinhamento em uma hora se não corrigido, e arruína o casamento das
/// faixas com a diarização.
/// </para>
/// <para>
/// <b>A correção sobre o gravador Python (requisito 3.1 da FASE1.md).</b> O
/// <c>_correct_drift</c> atual compara as amostras escritas com
/// <c>time.monotonic()</c> lido <em>na thread de escrita</em>. Isso mede o
/// instante em que o bloco foi processado, não o instante em que o dispositivo o
/// capturou: qualquer atraso na fila — GC, disco lento, a máquina ocupada — vira
/// "faixa atrasada" e o corretor insere silêncio que não deveria existir. A
/// correção espúria é pior que a deriva, porque desloca a faixa de verdade.
/// </para>
/// <para>
/// Aqui a referência é a <b>posição do dispositivo</b>, que o WASAPI entrega
/// junto com cada pacote. Ela conta amostras que o hardware realmente
/// digitalizou, e é imune a atraso de processamento.
/// </para>
/// </remarks>
public sealed class DriftAnchor
{
    /// <summary>
    /// Tolerância antes de corrigir. Abaixo disso não mexe, para não ficar
    /// inserindo e removendo amostra a cada bloco por jitter de agendamento.
    /// </summary>
    public static readonly TimeSpan Tolerancia = TimeSpan.FromMilliseconds(50);

    private readonly int _taxaAlvo;
    private readonly long _toleranciaAmostras;

    /// <summary>Amostras inseridas (positivo) ou descartadas (negativo), acumulado.</summary>
    public long AmostrasLiquidas { get; private set; }
    public int Correcoes { get; private set; }

    public DriftAnchor(int taxaAlvo = CrashSafeWavWriter.TaxaAlvo)
    {
        _taxaAlvo = taxaAlvo;
        _toleranciaAmostras = (long)(Tolerancia.TotalSeconds * taxaAlvo);
    }

    /// <summary>
    /// Quanto corrigir, dadas a posição do dispositivo e o que já foi escrito.
    /// </summary>
    /// <param name="posicaoDispositivoAmostras">
    /// Amostras que o dispositivo capturou desde o início, já convertidas para a
    /// taxa alvo. A fonte é a <b>posição carimbada em cada pacote</b> pelo
    /// <c>IAudioCaptureClient::GetBuffer</c> (<c>u64DevicePosition</c> e
    /// <c>u64QPCPosition</c>) — o carimbo acompanha os dados, e não o instante da
    /// pergunta, que é o que torna a medida imune a backlog de fila, GC e stall
    /// de disco. Consultar o <c>IAudioClock</c> por polling seria plano B, e
    /// estritamente pior pelo mesmo motivo.
    /// </param>
    /// <param name="amostrasEscritas">Amostras já entregues ao arquivo.</param>
    /// <param name="amostrasNesteBloco">Amostras do bloco em processamento.</param>
    /// <returns>
    /// Positivo: inserir N amostras de silêncio (dispositivo à frente do arquivo).
    /// Negativo: descartar N amostras. Zero: dentro da tolerância.
    /// </returns>
    public long Calcular(long posicaoDispositivoAmostras, long amostrasEscritas,
                         int amostrasNesteBloco)
    {
        long depoisDeste = amostrasEscritas + amostrasNesteBloco;
        long delta = posicaoDispositivoAmostras - depoisDeste;

        if (Math.Abs(delta) <= _toleranciaAmostras) return 0;

        // Nunca descartar mais do que o bloco tem: o resto se resolve no próximo,
        // e cortar além do bloco exigiria desfazer escrita já feita.
        if (delta < 0) delta = -Math.Min(-delta, amostrasNesteBloco);

        Correcoes++;
        AmostrasLiquidas += delta;
        return delta;
    }

    /// <summary>
    /// Aplica a correção a um bloco, preferindo mexer em trecho silencioso.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Refinamento previsto no requisito 3.1: inserir ou descartar amostras no
    /// meio de uma palavra produz um clique audível e, pior, altera a fala. Se o
    /// bloco tem um trecho abaixo de <paramref name="limiarSilencio"/> com folga
    /// suficiente, a correção entra ali. Senão, entra no fim — que é o
    /// comportamento do gravador Python, e continua sendo o recurso final.
    /// </para>
    /// <para>
    /// Vale para os <b>dois sentidos</b>. Uma primeira versão só protegia a
    /// inserção e truncava o fim do bloco ao descartar — mas o fim do bloco é
    /// áudio real como qualquer outro, e cortar ali é tão destrutivo quanto
    /// inserir no meio de uma palavra. Descartar de um trecho silencioso remove
    /// silêncio, que é o que se quer.
    /// </para>
    /// </remarks>
    public static float[] Aplicar(ReadOnlySpan<float> bloco, long correcao,
                                  float limiarSilencio = 1e-4f)
    {
        if (correcao == 0) return bloco.ToArray();

        if (correcao < 0)
        {
            int descartar = (int)Math.Min(-correcao, bloco.Length);
            int inicio = AcharTrechoSilencioso(bloco, limiarSilencio);

            // Só remove do trecho silencioso se ele comporta o descarte inteiro;
            // senão corta do fim, que é o recurso final do Python.
            if (inicio + descartar > bloco.Length)
                return bloco[..(bloco.Length - descartar)].ToArray();

            var reduzido = new float[bloco.Length - descartar];
            bloco[..inicio].CopyTo(reduzido);
            bloco[(inicio + descartar)..].CopyTo(reduzido.AsSpan(inicio));
            return reduzido;
        }

        int inserir = (int)correcao;
        int posicao = AcharTrechoSilencioso(bloco, limiarSilencio);

        var saida = new float[bloco.Length + inserir];
        bloco[..posicao].CopyTo(saida);
        // O meio fica em zero por construção do array — é o silêncio inserido.
        bloco[posicao..].CopyTo(saida.AsSpan(posicao + inserir));
        return saida;
    }

    /// <summary>
    /// Índice do começo da janela mais silenciosa, ou o fim do bloco se não
    /// houver nenhuma abaixo do limiar.
    /// </summary>
    private static int AcharTrechoSilencioso(ReadOnlySpan<float> bloco, float limiar)
    {
        const int janela = 160;                       // 10 ms a 16 kHz
        if (bloco.Length < janela * 2) return bloco.Length;

        for (int i = 0; i + janela <= bloco.Length; i += janela)
        {
            float soma = 0f;
            for (int j = i; j < i + janela; j++) soma += bloco[j] * bloco[j];
            if (MathF.Sqrt(soma / janela) < limiar) return i;
        }
        return bloco.Length;
    }
}
