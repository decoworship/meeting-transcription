using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>
/// Qual janela do Zoom desktop o vídeo grava. As janelas são as medidas no
/// Zoom Workplace em 30/10/2026 (ver <see cref="ReuniaoDoZoom"/>).
/// </summary>
public sealed class ReuniaoDoZoomTests
{
    private static readonly JanelaDoZoom Inicial = new("ZPPTMainFrmWndClassEx", true, false, 1296, 999);
    private static readonly JanelaDoZoom Escondida = new("ZPFloatVideoWndClass", false, false, 0, 0);
    private static readonly JanelaDoZoom Reuniao = new(ReuniaoDoZoom.ClasseDaReuniao, true, false, 1216, 737);
    private static readonly JanelaDoZoom Anotacao = new(ReuniaoDoZoom.ClasseDaAnotacao, true, false, 1920, 1080);
    private static readonly JanelaDoZoom Miniatura = new(ReuniaoDoZoom.ClasseDaMiniatura, true, false, 224, 126);
    private static readonly JanelaDoZoom Barra = new("ZPFloatToolbarClass", true, false, 891, 87);

    [Fact]
    public void ComOZoomSoAbertoEspera() =>
        Assert.Equal((SituacaoDoZoom.Esperando, -1), ReuniaoDoZoom.Decidir([Escondida, Inicial]));

    [Fact]
    public void NaReuniaoGravaAJanelaDelaENuncaAInicial() =>
        Assert.Equal((SituacaoDoZoom.Reuniao, 2), ReuniaoDoZoom.Decidir([Escondida, Inicial, Reuniao]));

    [Fact]
    public void CompartilhandoGravaOQueSeCompartilha()
    {
        // Como medido: a reunião some, e aparecem a barra, a miniatura e a anotação.
        Assert.Equal((SituacaoDoZoom.Compartilhando, 0),
            ReuniaoDoZoom.Decidir([Anotacao, Barra, Miniatura, Inicial]));
        // No instante da troca a reunião ainda pode constar visível: o
        // compartilhamento vence, porque é o que a reunião está vendo.
        Assert.Equal((SituacaoDoZoom.Compartilhando, 1), ReuniaoDoZoom.Decidir([Reuniao, Anotacao]));
    }

    [Fact]
    public void MinimizadaPausa()
    {
        Assert.Equal((SituacaoDoZoom.Minimizada, -1),
            ReuniaoDoZoom.Decidir([Reuniao with { Minimizada = true }, Inicial]));
        Assert.Equal((SituacaoDoZoom.Minimizada, -1), ReuniaoDoZoom.Decidir([Miniatura, Inicial]));
    }

    [Fact]
    public void JanelaSemTamanhoNaoConta() =>
        Assert.Equal((SituacaoDoZoom.Esperando, -1),
            ReuniaoDoZoom.Decidir([Reuniao with { Largura = 0, Altura = 0 }, Anotacao with { Visivel = false }]));

    [Fact]
    public void SoAsPausasTemAviso()
    {
        foreach (var s in Enum.GetValues<SituacaoDoZoom>())
            Assert.Equal(s is SituacaoDoZoom.Esperando or SituacaoDoZoom.Minimizada,
                         ReuniaoDoZoom.Aviso(s) is { Length: > 0 });
    }
}
