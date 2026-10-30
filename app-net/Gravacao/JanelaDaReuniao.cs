namespace MeetingRecorder.Core;

/// <summary>
/// A aba da reunião como a extensão a vê: o título dela e se está na frente.
/// </summary>
/// <param name="NaFrente">
/// É a aba ativa da janela dela, e a janela não está minimizada. Coberta por
/// outra janela ainda conta como na frente — a captura pede o desenho da
/// própria janela, e não o pedaço de tela.
/// </param>
/// <param name="App">"meet", "teams" ou "zoom": de que reunião é a aba.</param>
public sealed record AbaDaReuniao(string Titulo, bool NaFrente, string? App = null);

/// <summary>O que o quadro do vídeo mostra, com a fonte "janela da reunião".</summary>
public enum SituacaoDaJanela
{
    /// <summary>A reunião está na frente da janela dela: grava a janela.</summary>
    Gravando,

    /// <summary>A extensão não disse de reunião nenhuma.</summary>
    Procurando,

    /// <summary>A extensão diz a reunião, mas nenhuma janela do navegador a mostra.</summary>
    SemJanela,

    /// <summary>A janela da reunião está mostrando outra aba.</summary>
    ForaDaFrente,

    Minimizada,
}

/// <summary>
/// A regra de qual janela do navegador é a da reunião, e de quando gravá-la.
/// </summary>
/// <remarks>
/// <para>
/// <b>O título da janela é o da aba que está na frente.</b> O Chrome e o Edge
/// escrevem "&lt;título da aba&gt; - Google Chrome" (o Edge acrescenta "e mais N
/// guias" e o perfil), e a janela do app instalado (PWA) mostra o título da
/// página. Então a janela cujo título contém o da aba da reunião é a dela —
/// <b>e deixa de conter no instante em que a pessoa troca de aba</b>, antes de a
/// extensão avisar. É essa segunda metade que protege: o vídeo pausa antes de
/// gravar o e-mail que entrou na frente.
/// </para>
/// <para>
/// Sem dependência do Windows, para o teste alcançar: quem lista as janelas e
/// copia os pixels é o <c>App/Nativo</c>.
/// </para>
/// </remarks>
public static class JanelaDaReuniao
{
    /// <summary>
    /// O valor de <c>screen_monitor</c> que pede a janela da reunião, e não um monitor.
    /// </summary>
    public const string Fonte = "janela-da-reuniao";

    /// <summary>O título desta janela mostra a aba da reunião.</summary>
    /// <remarks>
    /// Título curto não casa por "contém": um "Zoom" ou um "Meet" estariam
    /// contidos em janelas que não são a reunião. Ele casa só como começo do
    /// título da janela, seguido de espaço — "Zoom - Google Chrome" —, que é
    /// como o navegador escreve a aba que está na frente. É o caso do Zoom na
    /// web, cuja aba se chama só "Zoom".
    /// </remarks>
    public static bool Mostra(string? tituloDaJanela, string? tituloDaAba)
    {
        if (tituloDaAba?.Trim() is not { Length: >= 3 } aba || tituloDaJanela is not { Length: > 0 } janela)
            return false;
        if (aba.Length >= 6 && janela.Contains(aba, StringComparison.Ordinal)) return true;
        return janela == aba || janela.StartsWith(aba + " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Das janelas do navegador, a que mostra a aba — a de título mais curto
    /// quando mais de uma contém, porque é a que tem menos texto além do da aba.
    /// </summary>
    /// <returns>O índice em <paramref name="titulos"/>, ou -1.</returns>
    public static int Escolher(IReadOnlyList<string> titulos, string? tituloDaAba)
    {
        int melhor = -1;
        for (int i = 0; i < titulos.Count; i++)
            if (Mostra(titulos[i], tituloDaAba) && (melhor < 0 || titulos[i].Length < titulos[melhor].Length))
                melhor = i;
        return melhor;
    }

    /// <summary>
    /// O que fazer neste quadro. Grava só quando as duas fontes concordam: a
    /// extensão diz que a aba está na frente, e o título da janela ainda é o dela.
    /// </summary>
    public static SituacaoDaJanela Decidir(AbaDaReuniao? aba, bool achouJanela, bool minimizada,
                                           string? tituloAtual)
    {
        if (aba is null) return SituacaoDaJanela.Procurando;
        if (!achouJanela) return SituacaoDaJanela.SemJanela;
        if (minimizada) return SituacaoDaJanela.Minimizada;
        if (!aba.NaFrente || !Mostra(tituloAtual, aba.Titulo)) return SituacaoDaJanela.ForaDaFrente;
        return SituacaoDaJanela.Gravando;
    }

    /// <summary>A frase do quadro de pausa, ou nulo quando grava.</summary>
    public static string? Aviso(SituacaoDaJanela s) => s switch
    {
        SituacaoDaJanela.Procurando => "Esperando a reunião abrir no navegador",
        SituacaoDaJanela.SemJanela => "Procurando a janela da reunião",
        SituacaoDaJanela.ForaDaFrente => "A reunião saiu da frente — pausado",
        SituacaoDaJanela.Minimizada => "A janela da reunião está minimizada — pausado",
        _ => null,
    };

    /// <summary>
    /// Onde a janela cabe no quadro sem distorcer: centralizada, com faixa
    /// preta onde sobra. A janela muda de tamanho durante a reunião, e o vídeo não.
    /// </summary>
    public static (int X, int Y, int Largura, int Altura) Encaixar(int largura, int altura,
                                                                  int quadroL, int quadroA)
    {
        if (largura <= 0 || altura <= 0) return (0, 0, 0, 0);
        double escala = Math.Min((double)quadroL / largura, (double)quadroA / altura);
        int l = Math.Max(1, (int)Math.Round(largura * escala));
        int a = Math.Max(1, (int)Math.Round(altura * escala));
        return ((quadroL - l) / 2, (quadroA - a) / 2, l, a);
    }
}
