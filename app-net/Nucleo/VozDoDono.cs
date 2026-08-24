using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>
/// Quando o dono da gravação falou, lido da faixa do microfone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> O desenho de duas faixas compra uma certeza que a
/// diarização não tem: o que entrou pelo microfone é do dono, e ponto. Até
/// 21/08/2026 essa certeza era gasta num teste binário por segmento
/// (<see cref="Montagem.AtribuirDono"/>): o segmento era seu se o microfone
/// superasse o sistema por 6 dB. Medido contra a transcrição do Meet em duas
/// reuniões, <b>todos</b> os erros de atribuição do dono vinham desse teste, e
/// sempre pelo mesmo motivo — <b>alguém falando junto</b>. Em sobreposição não
/// há vencedor, o teste falha, e o segmento inteiro cai para o rótulo que o
/// pyannote deu ao <c>system.wav</c>, que é necessariamente outra pessoa.
/// </para>
/// <para>
/// <b>A troca de unidade é o conserto.</b> Trocar o teste por um limiar
/// absoluto no microfone foi medido e <b>reprovado</b>: recuperava 24 segmentos
/// do dono e roubava 40 de outros, porque um segmento em sobreposição contém
/// fala das duas pessoas e atribuí-lo por inteiro a qualquer uma delas erra.
/// O que resolve é <b>cortar</b>, não escolher — e para isso o dono precisa
/// estar na linha do tempo que o <see cref="Montagem.RepartirPorFalante"/> já
/// usa para cortar segmento com mais de um falante dentro.
/// </para>
/// <para>
/// Então esta classe não decide nada: ela só transforma a faixa do microfone em
/// trechos de falante, iguais aos que o pyannote devolve, para entrarem na
/// mesma lista. O corte por palavra e a atribuição por sobreposição continuam
/// sendo os de sempre.
/// </para>
/// </remarks>
public static class VozDoDono
{
    /// <summary>O rótulo do dono, o mesmo que o app sempre gravou.</summary>
    public const string Rotulo = "You";

    /// <summary>Janela de análise. 20 ms é o passo comum de VAD.</summary>
    public const double JanelaS = 0.020;

    /// <summary>
    /// Acima disto o microfone tem fala, e não ruído de fundo.
    /// </summary>
    /// <remarks>
    /// Medido em duas reuniões com fone: o RMS do microfone é ~0,03–0,10
    /// enquanto o dono fala e ~0,0002 enquanto outra pessoa fala — <b>27 dB de
    /// separação</b>. Qualquer corte entre 0,005 e 0,02 serve; 0,01 fica no meio
    /// da folga em escala logarítmica.
    /// </remarks>
    public const double LimiarDeFala = 1e-2;

    /// <summary>
    /// Buraco menor que isto não fecha um trecho: é pausa entre palavras.
    /// </summary>
    public const double PausaS = 0.30;

    /// <summary>
    /// Trecho menor que isto não entra na linha do tempo.
    /// </summary>
    /// <remarks>
    /// Não é só filtro de estalo e teclado — é o corte entre <b>fala</b> e
    /// <b>"uhum"</b>. Um "uhum" seu no meio da fala de outra pessoa não deve
    /// abrir um trecho: ele não muda de quem é o turno, e abrir um trecho ali
    /// parte a frase do outro em três por nada.
    ///
    /// Varrido nas duas gravações medidas: de 0,25 a 0,8 o acerto da fala do
    /// dono não muda (96,6% e 94,9%), e o número de trechos cai de 182 para 131
    /// e de 27 para 17. Em 1,2 o acerto começa a cair. 0,8 é o maior valor que
    /// não custa nada.
    /// </remarks>
    public const double MinimoS = 0.8;

    /// <summary>
    /// Quanto o microfone capta do que sai pelo alto-falante.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Quem usa caixas em vez de fone tem no microfone uma cópia atenuada da
    /// reunião inteira, e aí <b>nenhum</b> limiar absoluto distingue o dono dos
    /// outros. É o motivo pelo qual a margem de 6 dB existia.
    /// </para>
    /// <para>
    /// Mas o vazamento é medível na própria gravação, e não precisa ser
    /// perguntado a ninguém: olha-se o microfone <b>nas janelas em que o sistema
    /// tem fala</b> e toma-se o percentil baixo. Com fone, o dono está calado na
    /// maior parte dessas janelas e o valor cai no ruído de fundo (0,0002
    /// medido). Com caixas, toda janela dessas carrega o vazamento, e o
    /// percentil baixo sobe junto com o nível do sistema. São ordens de
    /// grandeza de diferença, e é por isso que um corte grosseiro basta.
    /// </para>
    /// </remarks>
    public static double Vazamento(Faixas faixas)
    {
        var comSistema = new List<double>();
        int janela = (int)(JanelaS * Faixas.TaxaDeAmostragem);
        int n = Math.Min(faixas.Mic.Length, faixas.Sistema.Length);

        for (int i = 0; i + janela <= n; i += janela)
        {
            if (RmsDaJanela(faixas.Sistema, i, janela) >= LimiarDeFala)
                comSistema.Add(RmsDaJanela(faixas.Mic, i, janela));
        }

        if (comSistema.Count == 0) return 0;
        comSistema.Sort();
        // Quartil, e não mediana: numa reunião em que o dono fala muito, metade
        // das janelas com fala do sistema pode ter fala dele em cima.
        return comSistema[comSistema.Count / 4];
    }

    /// <summary>
    /// Verdadeiro quando dá para confiar no microfone sozinho.
    /// </summary>
    /// <remarks>
    /// O corte é frouxo de propósito — cinco vezes o ruído de fundo medido, e
    /// ainda meia ordem de grandeza abaixo do limiar de fala. <b>Na dúvida, o
    /// falso é o seguro</b>: sem trilha do dono, o pipeline se comporta como
    /// antes desta mudança, que é o comportamento que já rodou em campo.
    /// </remarks>
    public static bool SemVazamento(Faixas faixas) => Vazamento(faixas) < LimiarDeFala / 5;

    /// <summary>
    /// A faixa do microfone como trechos de falante, prontos para entrar na
    /// lista da diarização.
    /// </summary>
    /// <returns>Vazio quando há vazamento — ver <see cref="SemVazamento"/>.</returns>
    public static IReadOnlyList<SegmentoDeFalante> Trilha(Faixas faixas)
    {
        if (faixas.Mic.Length == 0 || !SemVazamento(faixas)) return [];

        int janela = (int)(JanelaS * Faixas.TaxaDeAmostragem);
        var trechos = new List<SegmentoDeFalante>();
        double inicio = -1, ultimaFala = -1;

        for (int i = 0; i + janela <= faixas.Mic.Length; i += janela)
        {
            double t = (double)i / Faixas.TaxaDeAmostragem;
            bool fala = RmsDaJanela(faixas.Mic, i, janela) >= LimiarDeFala;

            if (fala)
            {
                if (inicio < 0) inicio = t;
                ultimaFala = t + JanelaS;
            }
            else if (inicio >= 0 && t - ultimaFala >= PausaS)
            {
                Fechar(trechos, inicio, ultimaFala);
                inicio = -1;
            }
        }
        if (inicio >= 0) Fechar(trechos, inicio, ultimaFala);

        return trechos;
    }

    private static void Fechar(List<SegmentoDeFalante> trechos, double de, double ate)
    {
        if (ate - de >= MinimoS) trechos.Add(new SegmentoDeFalante(de, ate, Rotulo));
    }

    /// <summary>
    /// A linha do tempo da diarização com o dono dentro.
    /// </summary>
    /// <remarks>
    /// Os trechos do dono entram <b>por cima</b>, sem recortar os do pyannote:
    /// sobreposição é o estado normal de uma conversa, e as duas fontes falam de
    /// canais diferentes. Quem resolve a sobreposição é o
    /// <see cref="Montagem.RepartirPorFalante"/>, cortando o segmento na
    /// fronteira, e depois o <see cref="Montagem.AtribuirFalantes"/>, somando
    /// por falante. Recortar aqui seria decidir cedo demais, e com menos
    /// informação do que quem decide depois.
    /// </remarks>
    public static IReadOnlyList<SegmentoDeFalante> Juntar(
        IReadOnlyList<SegmentoDeFalante> diarizacao,
        IReadOnlyList<SegmentoDeFalante> dono)
    {
        if (dono.Count == 0) return diarizacao;
        return [.. diarizacao, .. dono];
    }

    private static double RmsDaJanela(float[] amostras, int inicio, int quantas)
    {
        double soma = 0;
        int fim = Math.Min(inicio + quantas, amostras.Length);
        for (int i = inicio; i < fim; i++) soma += amostras[i] * (double)amostras[i];
        return fim > inicio ? Math.Sqrt(soma / (fim - inicio)) : 0;
    }
}
