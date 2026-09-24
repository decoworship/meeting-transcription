using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A cadeia inteira da correção da legenda: pasta → vínculo → vocabulário do
/// projeto → correção.
/// </summary>
/// <remarks>
/// <b>O vínculo chega, muitas vezes, depois da separação.</b> Em 5 das 9
/// reuniões recentes o <c>reuniao.json</c> foi escrito depois de a separação
/// terminar — e a correção rodava sem vocabulário, com 0 trocas, em silêncio. O
/// que se prova aqui é que o vocabulário vem do vínculo, que a falta dele é
/// dita, e que rodar de novo quando o vínculo chega é seguro.
/// </remarks>
public sealed class CorrecaoDaLegendaDaReuniaoTests : IDisposable
{
    private readonly string _raiz =
        Directory.CreateTempSubdirectory("correcao-da-reuniao").FullName;

    private string Reuniao => Directory.CreateDirectory(Path.Combine(_raiz, "reuniao")).FullName;

    public void Dispose() => Directory.Delete(_raiz, recursive: true);

    private Projetos ProjetosCom(string? vocabulario)
    {
        string caminho = Path.Combine(_raiz, "projects.json");
        var projetos = new Projetos(caminho);
        projetos.Salvar("Beegol", "Suporte", new PreferenciasDoProjeto { InitialPrompt = vocabulario });
        return new Projetos(caminho);
    }

    private static List<TrechoDaLegenda> Trechos() =>
    [
        new() { InicioMs = 0, FimMs = 1120, Dono = false, Texto = "problema de Wi", Falante = "Daniel Prada" },
        new() { InicioMs = 1120, FimMs = 2240, Dono = false, Texto = "Fi e canal", Falante = "Daniel Prada" },
    ];

    [Fact]
    public void OVocabularioVemDoProjetoDaReuniao()
    {
        string pasta = Reuniao;
        new DadosDaReuniao { Cliente = "Beegol", Projeto = "Suporte" }.Salvar(pasta);

        var r = CorrecaoDaLegenda.CorrigirDaReuniao(pasta, Trechos(), ProjetosCom("Wifi"));

        Assert.False(r.SemVocabulario);
        var t = Assert.Single(r.Correcao.Trechos);
        Assert.Equal("problema de Wifi e canal", t.Texto);
        Assert.Contains(t.Swaps!, s => s is { De: "Wi Fi", Para: "Wifi" });
        Assert.StartsWith("termos corrigidos:", r.Resumo());
    }

    [Fact]
    public void ReuniaoSemProjetoDevolveOsTrechosEDizQueFaltouVocabulario()
    {
        string pasta = Reuniao;
        var entrada = Trechos();

        var r = CorrecaoDaLegenda.CorrigirDaReuniao(pasta, entrada, ProjetosCom("Wifi"));

        Assert.True(r.SemVocabulario);
        Assert.Equal(0, r.Correcao.Trocas);
        Assert.Equal(entrada.Select(t => t.Texto), r.Correcao.Trechos.Select(t => t.Texto));
        Assert.All(r.Correcao.Trechos, t => Assert.Null(t.Swaps));
        Assert.Contains("sem vocabulário (reunião sem projeto)", r.Resumo());
    }

    [Fact]
    public void ProjetoSemVocabularioTambemDiz()
    {
        string pasta = Reuniao;
        new DadosDaReuniao { Cliente = "Beegol", Projeto = "Suporte" }.Salvar(pasta);

        var r = CorrecaoDaLegenda.CorrigirDaReuniao(pasta, Trechos(), ProjetosCom(null));

        Assert.True(r.SemVocabulario);
        Assert.Equal(0, r.Correcao.Trocas);
        Assert.Equal(2, r.Correcao.Trechos.Count);
        Assert.Contains("sem vocabulário (projeto sem vocabulário)", r.Resumo());
    }

    [Fact]
    public void RodarDeNovoNaoTrocaNada()
    {
        // É o que torna seguro corrigir outra vez quando o vínculo é salvo
        // depois da separação.
        string pasta = Reuniao;
        new DadosDaReuniao { Cliente = "Beegol", Projeto = "Suporte" }.Salvar(pasta);
        var projetos = ProjetosCom("Wifi");

        var primeira = CorrecaoDaLegenda.CorrigirDaReuniao(pasta, Trechos(), projetos);
        var segunda = CorrecaoDaLegenda.CorrigirDaReuniao(pasta, primeira.Correcao.Trechos, projetos);

        Assert.True(primeira.Correcao.Trocas > 0);
        Assert.Equal(0, segunda.Correcao.Trocas);
        Assert.Equal(0, segunda.Correcao.Fundidos);
        Assert.Equal(primeira.Correcao.Trechos.Select(t => t.Texto),
                     segunda.Correcao.Trechos.Select(t => t.Texto));
    }
}
