using MeetingApp.Nucleo;
using MeetingApp.Sidecar;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O corte do segmento na troca de falante (docs/FASE6.md §4.1 e §4.5).
/// </summary>
/// <remarks>
/// O defeito que isto conserta some quando se olha só a contagem de segmentos:
/// um trecho de 40 s com três pessoas dentro recebe um rótulo e parece certo.
/// Por isso os testes conferem <b>o texto de cada pedaço</b>, e não só quantos
/// pedaços saíram — é o texto que a ata vai ler.
/// </remarks>
public sealed class RepartirPorFalanteTests
{
    /// <summary>Palavras de 1 s cada, começando em <paramref name="de"/>.</summary>
    private static SegmentoFinal Segmento(double de, params string[] palavras)
    {
        var ditas = palavras
            .Select((t, i) => new PalavraDita { Start = de + i, End = de + i + 1, Text = " " + t })
            .ToList();
        return new SegmentoFinal
        {
            Start = de,
            End = de + palavras.Length,
            Text = string.Concat(ditas.Select(p => p.Text)),
            Words = ditas,
        };
    }

    [Fact]
    public void CortaOndeODiarizadorDizQueOFalanteMudou()
    {
        // O caso medido: um segmento longo com duas pessoas dentro. Antes disto
        // ele saía inteiro, com o rótulo de quem falou mais — e a fala do outro
        // ficava atribuída a ele.
        var segmentos = new List<SegmentoFinal>
        {
            Segmento(0, "bom", "dia", "pessoal", "oi", "tudo", "bem"),
        };
        var diarizacao = new List<SegmentoDeFalante>
        {
            new(0, 3, "SPEAKER_00"),
            new(3, 6, "SPEAKER_01"),
        };

        int cortados = Montagem.RepartirPorFalante(segmentos, diarizacao);

        Assert.Equal(1, cortados);
        Assert.Equal(2, segmentos.Count);
        Assert.Equal(" bom dia pessoal", segmentos[0].Text);
        Assert.Equal(" oi tudo bem", segmentos[1].Text);

        Montagem.AtribuirFalantes(segmentos, diarizacao);
        Assert.Equal("Speaker 1", segmentos[0].Speaker);
        Assert.Equal("Speaker 2", segmentos[1].Speaker);
    }

    [Fact]
    public void OsPedacosSomamOTextoQueOSegmentoTinha()
    {
        // A régua que impede o corte de comer palavra: concatenar os pedaços
        // tem que devolver exatamente o texto de antes.
        var original = Segmento(0, "um", "dois", "três", "quatro");
        string antes = original.Text;
        var segmentos = new List<SegmentoFinal> { original };

        Montagem.RepartirPorFalante(segmentos,
            [new SegmentoDeFalante(0, 2, "A"), new SegmentoDeFalante(2, 4, "B")]);

        Assert.Equal(antes, string.Concat(segmentos.Select(s => s.Text)));
    }

    [Fact]
    public void AsPontasDoSegmentoNaoEncolhem()
    {
        // Os tempos das palavras não cobrem o silêncio da borda. Encolher para
        // dentro deles perderia o áudio que a revisão toca ao clicar no trecho.
        var seg = Segmento(10, "olá", "sumido");
        seg = new SegmentoFinal
        {
            Start = 9.5, End = 12.7, Text = seg.Text, Words = seg.Words,
        };
        var segmentos = new List<SegmentoFinal> { seg };

        Montagem.RepartirPorFalante(segmentos,
            [new SegmentoDeFalante(0, 11, "A"), new SegmentoDeFalante(11, 20, "B")]);

        Assert.Equal(2, segmentos.Count);
        Assert.Equal(9.5, segmentos[0].Start);
        Assert.Equal(12.7, segmentos[^1].End);
    }

    [Fact]
    public void SegmentoDeUmFalanteSoSaiIntacto()
    {
        // Mesmo objeto, e não uma cópia equivalente: o caso comum não pode
        // pagar nada, e um objeto novo perderia o que outra etapa já pôs nele.
        var seg = Segmento(0, "tudo", "meu");
        var segmentos = new List<SegmentoFinal> { seg };

        Assert.Equal(0, Montagem.RepartirPorFalante(segmentos,
            [new SegmentoDeFalante(0, 10, "SPEAKER_00")]));
        Assert.Same(seg, Assert.Single(segmentos));
    }

    [Fact]
    public void SemPalavrasNaoCorta()
    {
        // Parcial gravado antes de 19/08/2026, ou motor antigo: o resultado é o
        // de antes do corte existir, nunca um erro.
        var seg = new SegmentoFinal { Start = 0, End = 40, Text = "sem alinhamento" };
        var segmentos = new List<SegmentoFinal> { seg };

        Assert.Equal(0, Montagem.RepartirPorFalante(segmentos,
            [new SegmentoDeFalante(0, 20, "A"), new SegmentoDeFalante(20, 40, "B")]));
        Assert.Same(seg, Assert.Single(segmentos));
    }

    [Fact]
    public void SemDiarizacaoNaoCorta()
    {
        var segmentos = new List<SegmentoFinal> { Segmento(0, "a", "b", "c") };
        Assert.Equal(0, Montagem.RepartirPorFalante(segmentos, []));
        Assert.Single(segmentos);
    }

    [Fact]
    public void InterjeicaoCurtaNaoViraTrechoProprio()
    {
        // "uhum" de 0,3 s no meio da fala de outra pessoa. Sem piso, a revisão
        // — que é uma linha por trecho — encheria de confete; e os dois pedaços
        // em volta são da mesma pessoa, então voltam a ser um só.
        var palavras = new List<PalavraDita>
        {
            new() { Start = 0, End = 1, Text = " então" },
            new() { Start = 1, End = 2, Text = " eu" },
            new() { Start = 2.0, End = 2.3, Text = " uhum" },
            new() { Start = 2.3, End = 3.3, Text = " acho" },
            new() { Start = 3.3, End = 4.3, Text = " que" },
        };
        var segmentos = new List<SegmentoFinal>
        {
            new() { Start = 0, End = 4.3, Text = string.Concat(palavras.Select(p => p.Text)),
                    Words = palavras },
        };

        int cortados = Montagem.RepartirPorFalante(segmentos,
        [
            new SegmentoDeFalante(0, 2.0, "A"),
            new SegmentoDeFalante(2.0, 2.3, "B"),
            new SegmentoDeFalante(2.3, 5, "A"),
        ]);

        Assert.Equal(0, cortados);
        Assert.Single(segmentos);
    }

    [Fact]
    public void TresPessoasDentroDeUmSegmentoViramTresTrechos()
    {
        // O caso da reunião medida: 12% dos segmentos carregavam 46% das
        // palavras, e cada um deles tinha mais de uma pessoa dentro.
        var segmentos = new List<SegmentoFinal>
        {
            Segmento(0, "um", "um", "dois", "dois", "três", "três"),
        };

        Assert.Equal(1, Montagem.RepartirPorFalante(segmentos,
        [
            new SegmentoDeFalante(0, 2, "SPEAKER_00"),
            new SegmentoDeFalante(2, 4, "SPEAKER_01"),
            new SegmentoDeFalante(4, 6, "SPEAKER_02"),
        ]));
        Assert.Equal(3, segmentos.Count);
        Assert.Equal([" um um", " dois dois", " três três"],
                     segmentos.Select(s => s.Text));
    }

    [Fact]
    public void PalavraSemDiarizacaoFicaComAAnterior()
    {
        // Silêncio entre turnos que o pyannote não cobriu. Abrir corte ali seria
        // cortar onde não há informação de troca nenhuma.
        var segmentos = new List<SegmentoFinal> { Segmento(0, "a", "b", "c", "d") };

        Assert.Equal(0, Montagem.RepartirPorFalante(segmentos,
            [new SegmentoDeFalante(0, 2, "SPEAKER_00")]));
        Assert.Single(segmentos);
    }

    [Fact]
    public void OArquivoProntoNaoLevaAsPalavras()
    {
        // A paridade com o history/ do Python se mede byte a byte, e `words` é
        // insumo de uma etapa — não conteúdo da transcrição. Ver
        // SegmentoFinal.Words.
        var pronto = new ResultadoDaTranscricao
        {
            Segments = [new SegmentoFinal { Start = 0, End = 1, Text = "oi", Speaker = "You" }],
        };
        Assert.DoesNotContain("\"words\"", pronto.ParaJson());
    }

    [Fact]
    public void OParcialLevaAsPalavras()
    {
        // E este é o motivo de elas serem escritas: a retomada chega à
        // diarização com o texto do disco, e sem as palavras não teria como
        // cortar — o defeito voltaria justamente nas gravações que já custaram
        // uma queda de máquina.
        var parcial = new ResultadoDaTranscricao { Segments = [Segmento(0, "oi", "sumido")] };
        string json = parcial.ParaJson();

        Assert.Contains("\"words\"", json);
        var lido = ResultadoDaTranscricao.DeJson(json);
        Assert.Equal(2, lido!.Segments[0].Words!.Count);
        Assert.Equal(" sumido", lido.Segments[0].Words![1].Text);
    }
}
