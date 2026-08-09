using MeetingApp.Nucleo;
using MeetingApp.Sidecar;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O núcleo: mix das faixas e atribuição de quem falou.
/// </summary>
/// <remarks>
/// Porte de <c>src/web/recordings.py</c>, e a paridade com ele é o critério A
/// da Fase 2 — por isso os limiares são verificados por valor, não por
/// comportamento aproximado.
/// </remarks>
public sealed class NucleoTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("nucleo-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Wav(string nome, float[] amostras)
    {
        string caminho = Path.Combine(_pasta, nome);
        Faixas.Escrever(caminho, amostras);
        return caminho;
    }

    [Fact]
    public void MixSomaAsFaixasEPreencheAMaisCurta()
    {
        string mic = Wav("mic.wav", [0.25f, 0.25f, 0.25f, 0.25f]);
        string sis = Wav("system.wav", [0.25f, 0.25f]);

        var faixas = Faixas.Ler(mic, sis);
        string destino = Path.Combine(_pasta, "mix.wav");
        faixas.EscreverMix(destino);

        var mixado = Faixas.Ler(destino, destino).Mic;
        Assert.Equal(4, mixado.Length);
        Assert.Equal(0.5f, mixado[0], 3);   // as duas somadas
        Assert.Equal(0.25f, mixado[3], 3);  // só o microfone, o resto é zero
    }

    [Fact]
    public void MixReduzSemNormalizarQuandoEstoura()
    {
        // Reduzir e não normalizar é o que preserva o equilíbrio relativo entre
        // os canais — e é esse equilíbrio que AtribuirDono usa depois. Um mix
        // normalizado por trecho destruiria a informação de quem estava mais
        // alto.
        string mic = Wav("mic.wav", [0.8f, 0.1f]);
        string sis = Wav("system.wav", [0.8f, 0.1f]);

        var faixas = Faixas.Ler(mic, sis);
        string destino = Path.Combine(_pasta, "mix.wav");
        faixas.EscreverMix(destino);

        var mixado = Faixas.Ler(destino, destino).Mic;
        // Pico de 1,6 vira 1,0; a segunda amostra cai pelo mesmo fator, mantendo
        // a razão de 8:1 entre elas.
        Assert.Equal(1.0f, mixado[0], 2);
        Assert.Equal(0.125f, mixado[1], 2);
    }

    [Fact]
    public void FaixaComBlocoDataMaiorQueOArquivoAindaEhLida()
    {
        // É exatamente o que um kill -9 durante a gravação deixa, e o critério B
        // da Fase 1 exige que continue recuperável sem ferramenta externa.
        string caminho = Wav("truncado.wav", [0.1f, 0.2f, 0.3f, 0.4f]);
        var bytes = File.ReadAllBytes(caminho);
        File.WriteAllBytes(caminho, bytes[..^4]);   // some com duas amostras

        var lido = Faixas.Ler(caminho, caminho);
        Assert.Equal(2, lido.Mic.Length);
        Assert.Equal(0.1f, lido.Mic[0], 3);
    }

    [Fact]
    public void FalanteVemDaMaiorSobreposicao()
    {
        var segmentos = new List<SegmentoFinal>
        {
            new() { Start = 0, End = 10, Text = "oi" },
        };
        // O segundo falante cobre 6 s do segmento contra 3 s do primeiro.
        var diarizacao = new List<SegmentoDeFalante>
        {
            new(0, 3, "SPEAKER_00"),
            new(3, 9, "SPEAKER_01"),
        };

        Montagem.AtribuirFalantes(segmentos, diarizacao);
        // Nomeado aqui, não no motor: SPEAKER_01 é o segundo rótulo em ordem.
        Assert.Equal("Speaker 2", segmentos[0].Speaker);
    }

    [Fact]
    public void SegmentoSemDiarizacaoFicaSemFalante()
    {
        var segmentos = new List<SegmentoFinal> { new() { Start = 20, End = 25, Text = "oi" } };
        Montagem.AtribuirFalantes(segmentos, [new SegmentoDeFalante(0, 3, "SPEAKER_00")]);
        Assert.Null(segmentos[0].Speaker);
    }

    [Fact]
    public void MicrofoneDominandoSobrescreveODaDiarizacao()
    {
        // Onde o microfone domina não se está estimando quem falou: se está
        // sabendo. Por isso este passo roda depois e por cima do pyannote.
        var faixas = new Faixas(Tom(0.5f, 16_000), Tom(0.01f, 16_000));
        var segmentos = new List<SegmentoFinal>
        {
            new() { Start = 0, End = 1, Text = "eu falando", Speaker = "SPEAKER_00" },
        };

        int meus = Montagem.AtribuirDono(segmentos, faixas);

        Assert.Equal(1, meus);
        Assert.Equal("You", segmentos[0].Speaker);
    }

    [Fact]
    public void MicrofoneFracoNaoRoubaOSegmento()
    {
        // Vazamento acústico de quem usa caixa de som em vez de fone: o
        // microfone capta, mas não domina. Sem a margem de 2x, toda fala dos
        // outros viraria sua.
        var faixas = new Faixas(Tom(0.05f, 16_000), Tom(0.5f, 16_000));
        var segmentos = new List<SegmentoFinal>
        {
            new() { Start = 0, End = 1, Text = "outra pessoa", Speaker = "SPEAKER_00" },
        };

        Assert.Equal(0, Montagem.AtribuirDono(segmentos, faixas));
        Assert.Equal("SPEAKER_00", segmentos[0].Speaker);
    }

    [Fact]
    public void RuidoDeFundoNoMicrofoneNaoContaComoFala()
    {
        // Silêncio dos dois lados: o microfone "ganha" em razão, mas está abaixo
        // do piso de RMS. Sem esse piso, cada pausa da reunião seria atribuída
        // a você.
        var faixas = new Faixas(Tom(1e-3f, 16_000), Tom(1e-5f, 16_000));
        var segmentos = new List<SegmentoFinal> { new() { Start = 0, End = 1, Text = "..." } };

        Assert.Equal(0, Montagem.AtribuirDono(segmentos, faixas));
    }

    [Fact]
    public void OJsonSaiNoFormatoDoAppAtual()
    {
        // A paridade da Fase 2 se mede comparando este arquivo com o que o app
        // Gradio grava: os nomes são os dele, não os nossos.
        var r = new ResultadoDaTranscricao
        {
            Language = "pt",
            Duration = 12.5,
            Segments = [new SegmentoFinal { Start = 0, End = 1, Text = "olá", Speaker = "You" }],
        };

        string json = r.ParaJson();
        Assert.Contains("\"language\": \"pt\"", json);
        Assert.Contains("\"duration\": 12.5", json);
        Assert.Contains("\"speaker\": \"You\"", json);
        // Acento literal, como o Python com ensure_ascii=False.
        Assert.Contains("olá", json);
    }

    private static float[] Tom(float amplitude, int amostras)
    {
        var a = new float[amostras];
        for (int i = 0; i < amostras; i++)
            a[i] = amplitude * MathF.Sin(2 * MathF.PI * 440 * i / Faixas.TaxaDeAmostragem);
        return a;
    }
}
