using System.IO.Compression;
using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// As imagens da reunião: o print que o app tira e o que a pessoa cola nas notas.
/// </summary>
/// <remarks>
/// O que dói errar aqui é perder a imagem, guardar um arquivo que não é imagem
/// na pasta que o usuário abre, e deixar a marcação da imagem vazar para onde
/// só cabe texto — o vocabulário e o prompt da ata.
/// </remarks>
public sealed class CapturasTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("capturas-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    // ─────────────────────────────────────────────────────────── PNG

    /// <summary>2×2 em BGRA: vermelho, verde / azul, branco.</summary>
    private static readonly byte[] Bgra2x2 =
    [
        0, 0, 255, 0,    0, 255, 0, 0,
        255, 0, 0, 0,    255, 255, 255, 0,
    ];

    [Fact]
    public void OPngTemAssinaturaCabecalhoEFim()
    {
        byte[] png = Png.Codificar(2, 2, Bgra2x2, 8);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(2, LerInt(png, 16));
        Assert.Equal(2, LerInt(png, 20));
        Assert.Equal(8, png[24]);   // bits por canal
        Assert.Equal(2, png[25]);   // RGB, sem alfa
        Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }

    [Fact]
    public void OPngGuardaOsPixelsEmRgbSemAlfa()
    {
        // O alfa do BitBlt vem zerado: copiá-lo faria um print transparente.
        byte[] png = Png.Codificar(2, 2, Bgra2x2, 8);

        Assert.Equal(new byte[]
        {
            0, 255, 0, 0,   0, 255, 0,
            0, 0, 0, 255,   255, 255, 255,
        }, Pixels(png));
    }

    [Fact]
    public void OPngRespeitaOStrideMaiorQueALinha()
    {
        // A linha de um DIB pode ter enchimento; ele não pode virar pixel.
        byte[] comEnchimento =
        [
            0, 0, 255, 0,  9, 9, 9, 9,
            255, 0, 0, 0,  9, 9, 9, 9,
        ];
        byte[] png = Png.Codificar(1, 2, comEnchimento, 8);

        Assert.Equal(new byte[] { 0, 255, 0, 0, 0, 0, 0, 255 }, Pixels(png));
    }

    [Fact]
    public void OCrcDeCadaPedacoConfere()
    {
        byte[] png = Png.Codificar(2, 2, Bgra2x2, 8);

        int i = 8;
        while (i < png.Length)
        {
            int tamanho = LerInt(png, i);
            uint esperado = (uint)LerInt(png, i + 8 + tamanho);
            Assert.Equal(esperado, Png.Crc32(png.AsSpan(i + 4, 4 + tamanho)));
            i += 12 + tamanho;
        }
        Assert.Equal(png.Length, i);
    }

    // ───────────────────────────────────────────────────── guardar

    private static readonly byte[] UmPng = Png.Codificar(2, 2, Bgra2x2, 8);

    [Fact]
    public void GuardaNaPastaDeCapturasEDevolveOCaminhoRelativo()
    {
        string relativo = Capturas.Guardar(_pasta, UmPng, "tela-00-12-34");

        Assert.Equal("capturas/tela-00-12-34.png", relativo);
        Assert.Equal(UmPng, File.ReadAllBytes(Path.Combine(_pasta, "capturas", "tela-00-12-34.png")));
    }

    [Fact]
    public void DoisPrintsNoMesmoSegundoNaoSeSobrescrevem()
    {
        string a = Capturas.Guardar(_pasta, UmPng, "tela-00-12-34");
        string b = Capturas.Guardar(_pasta, UmPng, "tela-00-12-34");
        string c = Capturas.Guardar(_pasta, UmPng, "tela-00-12-34");

        Assert.Equal("capturas/tela-00-12-34.png", a);
        Assert.Equal("capturas/tela-00-12-34-2.png", b);
        Assert.Equal("capturas/tela-00-12-34-3.png", c);
    }

    [Fact]
    public void AExtensaoVemDoConteudoENaoDoNome()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 0];

        Assert.Equal("capturas/imagem.jpg", Capturas.Guardar(_pasta, jpeg, "imagem"));
    }

    [Fact]
    public void RecusaOQueNaoEImagem()
    {
        // Colar nas notas é a porta: um .exe renomeado não entra na pasta que
        // o usuário abre achando que é um print.
        Assert.Throws<InvalidDataException>(
            () => Capturas.Guardar(_pasta, "MZ isto não é imagem"u8.ToArray(), "imagem"));
        Assert.False(Directory.Exists(Path.Combine(_pasta, "capturas")));
    }

    [Fact]
    public void RecusaImagemGrandeDemais()
    {
        byte[] enorme = new byte[Capturas.TamanhoMaximo + 1];
        UmPng.CopyTo(enorme, 0);

        Assert.Throws<InvalidDataException>(() => Capturas.Guardar(_pasta, enorme, "imagem"));
    }

    [Fact]
    public void ONomeBaseNaoEscapaDaPasta()
    {
        string relativo = Capturas.Guardar(_pasta, UmPng, @"..\..\fora/de/lugar");

        Assert.StartsWith("capturas/", relativo);
        Assert.DoesNotContain("..", relativo);
        Assert.True(File.Exists(Path.Combine(_pasta, relativo)));
    }

    [Fact]
    public void NaoDeixaTemporarioParaTras()
    {
        Capturas.Guardar(_pasta, UmPng, "tela");

        Assert.Equal(["tela.png"],
            Directory.GetFiles(Path.Combine(_pasta, "capturas")).Select(f => Path.GetFileName(f)!));
    }

    // ───────────────────────────────────────────────────── a nota

    [Fact]
    public void ONomeDoPrintEOTempoDaReuniao()
    {
        Assert.Equal("tela-00-12-34", Capturas.NomeNoMomento("tela", 754.4));
        Assert.Equal("tela-01-02-03", Capturas.NomeNoMomento("tela", 3723));
        Assert.Equal("tela-00-00-00", Capturas.NomeNoMomento("tela", -2));
    }

    [Fact]
    public void ALinhaDaNotaTemOMomentoEAImagem()
    {
        Assert.Equal("[00:12:34] ![captura de tela](capturas/tela-00-12-34.png)",
                     Capturas.LinhaDaNota("capturas/tela-00-12-34.png", 754));
        Assert.Equal("![imagem](capturas/imagem.png)",
                     Capturas.LinhaDaNota("capturas/imagem.png", null, "imagem"));
    }

    [Fact]
    public void AnexarPoeALinhaNoFimDasNotasNumaLinhaPropria()
    {
        Notas.Salvar(_pasta, "- adiar o piloto");
        Capturas.AnexarANotas(_pasta, "[00:12:34] ![captura de tela](capturas/a.png)");

        Assert.Equal("- adiar o piloto\n[00:12:34] ![captura de tela](capturas/a.png)",
                     Notas.Ler(_pasta));
    }

    [Fact]
    public void AnexarCriaAsNotasQuandoNaoHaNenhuma()
    {
        Capturas.AnexarANotas(_pasta, "[00:00:05] ![captura de tela](capturas/a.png)");

        Assert.Equal("[00:00:05] ![captura de tela](capturas/a.png)", Notas.Ler(_pasta));
    }

    [Fact]
    public void AnexarNaoDuplicaAQuebraDeLinhaQueJaExiste()
    {
        Notas.Salvar(_pasta, "abertura\n");
        Capturas.AnexarANotas(_pasta, "![x](capturas/a.png)");

        Assert.Equal("abertura\n![x](capturas/a.png)", Notas.Ler(_pasta));
    }

    // ─────────────────────────────────────── onde a imagem não cabe

    [Fact]
    public void SemImagensTiraAMarcacaoEALinhaQueSoTinhaElas()
    {
        string notas = "abertura\n"
                       + "[00:12:34] ![captura de tela](capturas/tela-00-12-34.png)\n"
                       + "[00:13:00] slide do orçamento ![imagem](capturas/imagem.png)\n"
                       + "fechamento";

        Assert.Equal("abertura\n[00:13:00] slide do orçamento\nfechamento",
                     Notas.SemImagens(notas));
    }

    [Fact]
    public void ImagemNaoViraTermoDoVocabulario()
    {
        string notas = "[00:12:34] ![Captura de Tela](capturas/Tela-00-12-34.png)\nfalar com a Vivo";

        Assert.Equal(["Vivo"], Notas.TermosSugeridos(notas));
    }

    [Fact]
    public void OInicioDasNotasNaoMostraAMarcacao()
    {
        Notas.Salvar(_pasta, "![captura de tela](capturas/a.png)\ncobrar a Vivo");

        Assert.Equal("cobrar a Vivo", Notas.Inicio(_pasta));
    }

    [Fact]
    public void ImagemQueNaoEDaReuniaoContinuaComoTexto()
    {
        // Só a pasta de capturas é do app; um link qualquer que alguém colou é
        // texto dele, e some-lo do prompt seria apagar o que a pessoa escreveu.
        Assert.Equal("ver ![diagrama](https://exemplo.com/d.png)",
                     Notas.SemImagens("ver ![diagrama](https://exemplo.com/d.png)"));
    }

    // ─────────────────────────────────────────────────────── apoio

    private static int LerInt(byte[] b, int i) =>
        (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

    /// <summary>Os bytes descomprimidos do IDAT: filtro + RGB por linha.</summary>
    private static byte[] Pixels(byte[] png)
    {
        int i = 8;
        using var idat = new MemoryStream();
        while (i < png.Length)
        {
            int tamanho = LerInt(png, i);
            string tipo = System.Text.Encoding.ASCII.GetString(png, i + 4, 4);
            if (tipo == "IDAT") idat.Write(png, i + 8, tamanho);
            i += 12 + tamanho;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var saida = new MemoryStream();
        z.CopyTo(saida);
        return saida.ToArray();
    }
}
