using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O índice do vídeo da tela: onde ele cai no áudio, e quando não há vídeo.
/// </summary>
/// <remarks>
/// O que dói errar é mostrar a aba Vídeo para um MP4 que não abre — o de um
/// processo que morreu antes do índice — ou deslocar a legenda pelo início.
/// </remarks>
public sealed class VideoDaTelaTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("video-da-tela").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private void ComMp4() => File.WriteAllBytes(Path.Combine(_pasta, VideoDaTela.Video), [0, 0, 0, 1]);

    [Fact]
    public void OIndiceEscritoVoltaIgual()
    {
        ComMp4();
        new VideoDaTela(2.2416, 1920, 1080, 10, 36000, @"\\.\DISPLAY1", null).Escrever(_pasta);

        var v = VideoDaTela.Ler(_pasta);
        Assert.NotNull(v);
        // Milissegundo basta: é um décimo de quadro a 10 qps.
        Assert.Equal(2.242, v.InicioS);
        Assert.Equal((1920, 1080, 10, 36000), (v.Largura, v.Altura, v.QuadrosPorSegundo, v.Quadros));
        Assert.Equal(@"\\.\DISPLAY1", v.Monitor);
        Assert.Null(v.Falha);
    }

    [Fact]
    public void AFalhaDoVideoFicaRegistradaSemEsconderOQueFoiGravado()
    {
        ComMp4();
        new VideoDaTela(1, 1280, 720, 10, 50, null, "IMFSinkWriter::WriteSample falhou").Escrever(_pasta);

        var v = VideoDaTela.Ler(_pasta);
        Assert.NotNull(v);
        Assert.Equal("IMFSinkWriter::WriteSample falhou", v.Falha);
    }

    [Fact]
    public void GravacaoSemVideoNaoEUmErro() => Assert.Null(VideoDaTela.Ler(_pasta));

    [Fact]
    public void Mp4SemIndiceNaoConta()
    {
        // O índice só é escrito depois de o MP4 fechar: sem ele, o processo
        // morreu no meio, e o arquivo não abre em tocador nenhum.
        ComMp4();
        Assert.Null(VideoDaTela.Ler(_pasta));
    }

    [Fact]
    public void IndiceSemMp4NaoConta()
    {
        new VideoDaTela(1, 1280, 720, 10, 50, null, null).Escrever(_pasta);
        Assert.Null(VideoDaTela.Ler(_pasta));
    }

    [Fact]
    public void IndiceSemQuadrosOuIlegivelNaoConta()
    {
        ComMp4();
        new VideoDaTela(1, 1280, 720, 10, 0, null, null).Escrever(_pasta);
        Assert.Null(VideoDaTela.Ler(_pasta));

        File.WriteAllText(Path.Combine(_pasta, VideoDaTela.Indice), "{ não é json");
        Assert.Null(VideoDaTela.Ler(_pasta));
    }
}
