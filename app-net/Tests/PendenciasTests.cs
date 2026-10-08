using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>A caixinha da pendência, marcada na tela e gravada no ata.md.</summary>
public sealed class PendenciasTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("pendencias").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private const string Ata = """
        # Ata — Semanal

        ## Infra

        - [ ] Subir o servidor — **Bruno Teles** — sexta
        - Um item que não é pendência
        - [x] Mandar a pauta — **Diego Prado**

        ## Outras pendências

        - [ ] Revisar o contrato — **Carla Nunes**
        """;

    [Fact]
    public void MarcaAPendenciaPeloNumeroDela()
    {
        string md = Pendencias.Marcar(Ata, 2, "Revisar o contrato — **Carla Nunes**", feita: true);
        Assert.Contains("- [x] Revisar o contrato — **Carla Nunes**", md);
        Assert.Contains("- [ ] Subir o servidor", md);
        Assert.Contains("- [x] Mandar a pauta", md);
    }

    [Fact]
    public void DesmarcaAPendencia()
    {
        string md = Pendencias.Marcar(Ata, 1, "Mandar a pauta — **Diego Prado**", feita: false);
        Assert.Contains("- [ ] Mandar a pauta — **Diego Prado**", md);
    }

    [Fact]
    public void SoMudaACaixinha()
    {
        string md = Pendencias.Marcar(Ata, 0, "Subir o servidor — **Bruno Teles** — sexta", feita: true);
        Assert.Equal(Ata.Replace("- [ ] Subir", "- [x] Subir"), md);
    }

    [Theory]
    [InlineData(0, "Outro texto")]
    [InlineData(7, "Subir o servidor — **Bruno Teles** — sexta")]
    [InlineData(-1, "Subir o servidor — **Bruno Teles** — sexta")]
    public void RecusaQuandoAAtaMudou(int indice, string texto)
    {
        // A ata foi gerada de novo com a tela aberta: marcar pelo número
        // marcaria a linha errada.
        var e = Assert.Throws<InvalidOperationException>(
            () => Pendencias.Marcar(Ata, indice, texto, feita: true));
        Assert.Contains("a ata mudou", e.Message);
    }

    [Fact]
    public void GuardaOFimDeLinhaDoWindows()
    {
        string crlf = Ata.Replace("\n", "\r\n");
        string md = Pendencias.Marcar(crlf, 2, "Revisar o contrato — **Carla Nunes**", feita: true);
        Assert.Equal(crlf.Replace("- [ ] Revisar", "- [x] Revisar"), md);
    }

    [Fact]
    public void ContaComoATelaConta()
    {
        // A tela conta a caixinha depois de tirar o recuo da linha.
        string md = Pendencias.Marcar("  - [ ] Recuada\n- [ ] Segunda\n", 1, "Segunda", feita: true);
        Assert.Equal("  - [ ] Recuada\n- [x] Segunda\n", md);
    }

    [Fact]
    public void NoArquivoMantemADataDaAta()
    {
        // O aviso de "ata desatualizada" compara a data do ata.md com a da
        // transcrição; marcar uma caixinha não pode escondê-lo.
        string caminho = Path.Combine(_pasta, "ata.md");
        File.WriteAllText(caminho, Ata);
        var antes = new DateTime(2026, 9, 22, 13, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(caminho, antes);

        Pendencias.MarcarNoArquivo(_pasta, 0, "Subir o servidor — **Bruno Teles** — sexta", feita: true);

        Assert.Contains("- [x] Subir o servidor", File.ReadAllText(caminho));
        Assert.Equal(antes, File.GetLastWriteTimeUtc(caminho));
        Assert.Empty(Directory.GetFiles(_pasta, "*.tmp"));
    }
}
