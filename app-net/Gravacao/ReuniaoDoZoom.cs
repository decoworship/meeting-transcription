namespace MeetingRecorder.Core;

/// <summary>Uma janela de topo do <c>Zoom.exe</c>, como o vídeo precisa dela.</summary>
public sealed record JanelaDoZoom(string Classe, bool Visivel, bool Minimizada, int Largura, int Altura);

/// <summary>O que o quadro do vídeo mostra, com a fonte "janela do Zoom".</summary>
public enum SituacaoDoZoom
{
    /// <summary>A janela da reunião está aberta: grava ela.</summary>
    Reuniao,

    /// <summary>Quem grava está compartilhando a tela: grava o que é compartilhado.</summary>
    Compartilhando,

    /// <summary>A reunião existe, mas só como a miniatura flutuante.</summary>
    Minimizada,

    /// <summary>Nenhuma reunião do Zoom aberta.</summary>
    Esperando,
}

/// <summary>
/// A regra de qual janela do Zoom desktop gravar.
/// </summary>
/// <remarks>
/// <para>
/// <b>Medido em 30/10/2026, no Zoom Workplace da máquina do dono</b>, com uma
/// reunião de um só, câmera ligada:
/// </para>
/// <list type="bullet">
/// <item>a reunião é a <c>ConfMultiTabContentWndClass</c> ("Zoom Meeting"), e a
/// cópia pelo <c>PrintWindow</c> traz a câmera inteira, igual à da tela;</item>
/// <item>a janela inicial é a <c>ZPPTMainFrmWndClassEx</c> ("Zoom Workplace"),
/// que nunca é gravada;</item>
/// <item><b>compartilhando a tela, a janela da reunião some</b>, e aparece a
/// <c>ZoomAnnoWindowWndClass</c>, a camada transparente de anotação, do tamanho
/// e na posição exatos do que é compartilhado. É ela que diz o que gravar: o
/// mesmo que os outros estão vendo;</item>
/// <item>minimizada, sobra a miniatura (<c>ZPFloatVideoWndClass</c>), que o
/// <c>PrintWindow</c> devolve preta.</item>
/// </list>
/// <para>
/// <b>Não medido:</b> outra pessoa compartilhando — pelo que se sabe, o conteúdo
/// aparece dentro da janela da reunião, e já entra — e o modo "lado a lado".
/// </para>
/// </remarks>
public static class ReuniaoDoZoom
{
    /// <summary>O valor de <c>screen_monitor</c> que pede a reunião do Zoom.</summary>
    public const string Fonte = "janela-do-zoom";

    public const string Processo = "Zoom";
    public const string ClasseDaReuniao = "ConfMultiTabContentWndClass";
    public const string ClasseDaAnotacao = "ZoomAnnoWindowWndClass";
    public const string ClasseDaMiniatura = "ZPFloatVideoWndClass";

    /// <summary>
    /// O que gravar, e de qual janela: o índice em <paramref name="janelas"/>
    /// da reunião ou da camada de anotação, ou -1 nas pausas.
    /// </summary>
    /// <remarks>
    /// O compartilhamento vence: com a camada de anotação de pé, a janela da
    /// reunião pode ainda constar como visível por um instante, e o que importa
    /// é o que a reunião está vendo.
    /// </remarks>
    public static (SituacaoDoZoom Situacao, int Indice) Decidir(IReadOnlyList<JanelaDoZoom> janelas)
    {
        int anotacao = Achar(janelas, ClasseDaAnotacao, j => j.Visivel && !j.Minimizada && Grande(j));
        if (anotacao >= 0) return (SituacaoDoZoom.Compartilhando, anotacao);

        int reuniao = Achar(janelas, ClasseDaReuniao, j => j.Visivel && !j.Minimizada && Grande(j));
        if (reuniao >= 0) return (SituacaoDoZoom.Reuniao, reuniao);

        bool existe = Achar(janelas, ClasseDaReuniao, j => j.Minimizada) >= 0
                      || Achar(janelas, ClasseDaMiniatura, j => j.Visivel && Grande(j)) >= 0;
        return (existe ? SituacaoDoZoom.Minimizada : SituacaoDoZoom.Esperando, -1);
    }

    /// <summary>A frase do quadro de pausa, ou nulo quando grava.</summary>
    public static string? Aviso(SituacaoDoZoom s) => s switch
    {
        SituacaoDoZoom.Esperando => "Esperando a reunião do Zoom abrir",
        SituacaoDoZoom.Minimizada => "A janela do Zoom está minimizada — pausado",
        _ => null,
    };

    // As janelas escondidas do Zoom existem desde que ele abre, muitas com 0×0:
    // só conta o que tem tamanho de gente.
    private static bool Grande(JanelaDoZoom j) => j.Largura >= 100 && j.Altura >= 60;

    private static int Achar(IReadOnlyList<JanelaDoZoom> janelas, string classe, Func<JanelaDoZoom, bool> condicao)
    {
        for (int i = 0; i < janelas.Count; i++)
            if (janelas[i].Classe == classe && condicao(janelas[i])) return i;
        return -1;
    }
}
