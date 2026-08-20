using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A retomada do ASR — o texto que não se perde mais.
/// </summary>
/// <remarks>
/// Nasceu em 19/08/2026, do registro do segundo usuário: o ASR terminava às
/// 09:08:00, a máquina dele desligava durante a diarização, e o texto pronto ia
/// junto porque só era escrito no fim de tudo. Ver <c>docs/FASE6.md</c> §3.0.
/// <para>
/// O que estes testes protegem é a assimetria que torna a retomada segura:
/// <b>recusar um parcial custa minutos de GPU; aceitar o errado devolve o texto
/// de outro modelo sem ninguém perceber.</b> Na dúvida, recusa.
/// </para>
/// </remarks>
public sealed class RetomadaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(
        Path.GetTempPath(), "retomada-" + Guid.NewGuid().ToString("N"));

    public RetomadaTests()
    {
        Directory.CreateDirectory(_pasta);
        foreach (string f in new[] { "mic.wav", "system.wav" })
            File.WriteAllBytes(Path.Combine(_pasta, f), [0x52, 0x49, 0x46, 0x46]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_pasta, recursive: true); } catch (IOException) { }
    }

    private static ResultadoDaTranscricao UmTexto() => new()
    {
        Language = "pt",
        Duration = 1800,
        Segments = [new SegmentoFinal { Start = 0, End = 2, Text = "bom dia" }],
    };

    /// <summary>Escreve o parcial e garante que ele é mais novo que as faixas.</summary>
    private void GravarParcial(string modelo = "medium", string? idioma = null,
                               string? vocabulario = null)
    {
        Retomada.Escrever(_pasta, UmTexto(), modelo, idioma, vocabulario);
        File.SetLastWriteTimeUtc(Path.Combine(_pasta, "transcricao.json"),
                                 DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public void OParcialTemOTextoEDizOQueFalta()
    {
        GravarParcial();

        var lido = ResultadoDaTranscricao.DeJson(
            File.ReadAllText(Path.Combine(_pasta, "transcricao.json")));

        // O ponto inteiro do exercício: o texto existe em disco antes de a
        // diarização começar.
        Assert.Equal("bom dia", Assert.Single(lido!.Segments).Text);
        Assert.Equal([Retomada.Diarizacao], lido.Pending!.Steps);
        Assert.Equal("medium", lido.Pending.Model);
    }

    [Fact]
    public void EscreverNaoMarcaOObjetoQueSegueNoPipeline()
    {
        // A pendência é do arquivo, não do resultado. Se ela ficasse colada no
        // objeto, o pipeline o completaria e o regravaria marcado como parcial —
        // e a gravação ficaria eternamente "por diarizar".
        var resultado = UmTexto();
        Retomada.Escrever(_pasta, resultado, "medium", null, null);

        Assert.Null(resultado.Pending);
    }

    [Fact]
    public void EscreverNuncaLevantaExcecao()
    {
        // Mesma regra do Registro: uma rede que derruba a transcrição
        // bem-sucedida que ela protegeria é pior que rede nenhuma.
        Assert.Null(Record.Exception(
            () => Retomada.Escrever(Path.Combine(_pasta, "nao-existe"), UmTexto(),
                                    "medium", null, null)));
    }

    [Fact]
    public void RetomaOQueFoiGravadoComOsMesmosParametros()
    {
        GravarParcial();

        var r = Retomada.Ler(_pasta, "medium", null, null);

        Assert.NotNull(r);
        Assert.Equal("bom dia", Assert.Single(r.Segmentos).Text);
        Assert.Equal("pt", r.Idioma);
        Assert.Equal(1800, r.Duracao);
    }

    [Theory]
    [InlineData("large-v3", null, null)]      // outro modelo
    [InlineData("medium", "en", null)]        // outro idioma
    [InlineData("medium", null, "Vivo, AA")]  // outro vocabulário
    public void RecusaQuandoOAsrTeriaSaidaDiferente(string modelo, string? idioma,
                                                    string? vocabulario)
    {
        // Os três parâmetros que decidem a saída do ASR. Aceitar aqui devolveria
        // o texto do modelo errado sem avisar — a falha que o usuário não tem
        // como perceber.
        GravarParcial();

        Assert.Null(Retomada.Ler(_pasta, modelo, idioma, vocabulario));
    }

    [Fact]
    public void NuloEVazioSaoAMesmaCoisa()
    {
        // A tela manda "" onde o núcleo entende null. Tratá-los como diferentes
        // faria a retomada nunca acontecer, em silêncio.
        GravarParcial(idioma: "", vocabulario: "   ");

        Assert.NotNull(Retomada.Ler(_pasta, "medium", null, null));
    }

    [Fact]
    public void NaoRetomaUmaTranscricaoJaPronta()
    {
        // Sem "pending" o arquivo está completo, e retomá-lo faria a diarização
        // rodar de novo por nada.
        File.WriteAllText(Path.Combine(_pasta, "transcricao.json"), UmTexto().ParaJson());
        File.SetLastWriteTimeUtc(Path.Combine(_pasta, "transcricao.json"),
                                 DateTime.UtcNow.AddSeconds(5));

        Assert.Null(Retomada.Ler(_pasta, "medium", null, null));
    }

    [Fact]
    public void NaoRetomaQuandoAGravacaoMudouDepois()
    {
        // Regravar por cima da mesma pasta é o que torna o parcial mentira: o
        // texto seria de um áudio que não está mais lá.
        GravarParcial();
        File.SetLastWriteTimeUtc(Path.Combine(_pasta, "system.wav"),
                                 DateTime.UtcNow.AddMinutes(5));

        Assert.Null(Retomada.Ler(_pasta, "medium", null, null));
    }

    [Fact]
    public void SemParcialOuComArquivoIlegivelTranscreveDoComeco()
    {
        Assert.Null(Retomada.Ler(_pasta, "medium", null, null));

        File.WriteAllText(Path.Combine(_pasta, "transcricao.json"), "{ isto não é json");
        Assert.Null(Retomada.Ler(_pasta, "medium", null, null));
    }

    [Fact]
    public void ALitaDeGravacoesEnxergaOParcial()
    {
        // A gravação conta como transcrita — o texto está lá —, então o único
        // jeito de a pessoa saber por que ninguém tem nome é o aviso.
        Assert.False(Retomada.EstaPendente(_pasta));

        GravarParcial();
        Assert.True(Retomada.EstaPendente(_pasta));

        File.WriteAllText(Path.Combine(_pasta, "transcricao.json"), UmTexto().ParaJson());
        Assert.False(Retomada.EstaPendente(_pasta));
    }

    [Fact]
    public void OArquivoCompletoNaoGanhouCampoNovo()
    {
        // "pending" é o único campo que se omite quando nulo. Um arquivo
        // completo precisa sair como saía antes da retomada existir — é assim
        // que a paridade com o history/ do Python se mede.
        Assert.DoesNotContain("pending", UmTexto().ParaJson());
    }
}
