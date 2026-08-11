using System.IO.Compression;
using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>Os quatro formatos de saída.</summary>
public sealed class ExportacaoTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("exportacao-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static ResultadoDaTranscricao Exemplo() => new()
    {
        Language = "pt",
        Duration = 3725,
        Segments =
        [
            new SegmentoFinal { Start = 8.2, End = 9.1, Text = " Bom dia, Júri.", Speaker = "Vanessa" },
            new SegmentoFinal { Start = 10.7, End = 12.0, Text = " E aí?", Speaker = "You" },
            // Passa de uma hora: é onde o formato de tempo muda.
            new SegmentoFinal { Start = 3661.5, End = 3663.0, Text = " fechado", Speaker = "Vanessa" },
        ],
    };

    [Fact]
    public void OTxtTemTempoNomeETexto()
    {
        string txt = Exportacao.Txt(Exemplo());

        Assert.Contains("[00:08] Vanessa: Bom dia, Júri.", txt);
        Assert.Contains("[00:10] You: E aí?", txt);
        // Acima de uma hora a marca ganha a hora; abaixo, não — "00:07:12" numa
        // reunião de vinte minutos é ruído de leitura.
        Assert.Contains("[01:01:01] Vanessa: fechado", txt);
    }

    [Fact]
    public void OTxtPodeSairSemOsNomes()
    {
        string txt = Exportacao.Txt(Exemplo(), comFalantes: false);
        Assert.DoesNotContain("Vanessa", txt);
        Assert.Contains("Bom dia, Júri.", txt);
    }

    [Fact]
    public void OSrtEhNumeradoEUsaVirgulaNosMilissegundos()
    {
        string srt = Exportacao.Srt(Exemplo());

        Assert.StartsWith("1\n", srt);
        Assert.Contains("00:00:08,200 --> 00:00:09,100", srt);
        // Trocar a vírgula por ponto faz players recusarem o arquivo inteiro.
        Assert.DoesNotContain("00:00:08.200", srt);
    }

    [Fact]
    public void OVttTemOCabecalhoEUsaPontoNosMilissegundos()
    {
        string vtt = Exportacao.Vtt(Exemplo());

        // Sem "WEBVTT" na primeira linha o navegador ignora a legenda.
        Assert.StartsWith("WEBVTT", vtt);
        Assert.Contains("00:00:08.200 --> 00:00:09.100", vtt);
    }

    [Fact]
    public void ODocxEhUmZipComAsTresPartesObrigatorias()
    {
        string destino = Path.Combine(_pasta, "saida.docx");
        Exportacao.Docx(Exemplo(), destino, "Algar - Impedimentos");

        using var zip = ZipFile.OpenRead(destino);
        var nomes = zip.Entries.Select(e => e.FullName).ToList();

        // Faltando qualquer uma, o Word recusa abrir o arquivo.
        Assert.Contains("[Content_Types].xml", nomes);
        Assert.Contains("_rels/.rels", nomes);
        Assert.Contains("word/document.xml", nomes);

        using var doc = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        string xml = doc.ReadToEnd();
        Assert.Contains("Algar - Impedimentos", xml);
        Assert.Contains("Bom dia, Júri.", xml);
    }

    [Fact]
    public void OXmlDoDocxEscapaOQueQuebrariaODocumento()
    {
        // Um "&" ou "<" cru no texto torna o XML inválido, e o Word diz apenas
        // que o arquivo está corrompido — sem dizer por quê.
        var r = new ResultadoDaTranscricao
        {
            Segments = [new SegmentoFinal { Start = 0, End = 1, Text = " A & B < C", Speaker = "X" }],
        };

        string destino = Path.Combine(_pasta, "escapado.docx");
        Exportacao.Docx(r, destino, "T & T");

        using var zip = ZipFile.OpenRead(destino);
        using var doc = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        string xml = doc.ReadToEnd();

        Assert.Contains("A &amp; B &lt; C", xml);
        Assert.DoesNotContain("A & B", xml);
        // E o documento continua sendo XML válido de verdade.
        System.Xml.Linq.XDocument.Parse(xml);
    }

    [Fact]
    public void UmaTranscricaoRealSaiNosQuatroFormatos()
    {
        // Contra o acervo desta máquina, quando existe: 387 trechos com acento,
        // três falantes e falas de tamanhos irregulares dizem mais sobre os
        // formatos que três segmentos inventados.
        string real = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents", "MeetingRecordings", "2026-08-10_11-50-26", "transcricao.json");
        if (!File.Exists(real)) return;

        var r = ResultadoDaTranscricao.DeJson(File.ReadAllText(real));
        Assert.NotNull(r);
        Assert.True(r.Segments.Count > 100);

        foreach (string formato in new[] { "txt", "srt", "vtt" })
        {
            string saida = Path.Combine(_pasta, $"real.{formato}");
            string conteudo = formato switch
            {
                "txt" => Exportacao.Txt(r),
                "srt" => Exportacao.Srt(r),
                _ => Exportacao.Vtt(r),
            };
            File.WriteAllText(saida, conteudo);
            Assert.True(new FileInfo(saida).Length > 1000, $"{formato} saiu vazio demais");
        }

        string docx = Path.Combine(_pasta, "real.docx");
        Exportacao.Docx(r, docx, "Algar - Impedimentos");

        using var zip = ZipFile.OpenRead(docx);
        using var doc = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        // Válido de verdade, com o conteúdo real dentro — é o que o Word exige.
        var xml = System.Xml.Linq.XDocument.Parse(doc.ReadToEnd());
        Assert.Contains("Júri", xml.ToString());
    }

    [Fact]
    public void ONomeDeArquivoNaoCarregaCaractereProibido()
    {
        string nome = Exportacao.NomeDeArquivo("SALA - liberação de API/s", "txt");

        Assert.DoesNotContain("/", nome);
        Assert.EndsWith(".txt", nome);
        // O acento fica: ele é válido em nome de arquivo e é o que o usuário lê.
        Assert.Contains("liberação", nome);
    }
}
