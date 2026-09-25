using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O estado da ata que a lista de reuniões mostra sem abri-la.
/// </summary>
/// <remarks>
/// O que dói errar aqui não dá erro: um contador de pendências que discorda da
/// ata, uma ata velha que a lista chama de pronta, um resumo inventado. E um
/// <c>ata.md</c> ilegível não pode esconder a gravação da lista.
/// </remarks>
public sealed class EstadoDaAtaTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("estado-ata-testes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    // O formato que o RedatorDeAta escreve, com os dois lados da pendência e um
    // item já fechado no meio.
    private const string Ata = """
        # Ata — Comunicação Beegol + App

        **Data:** 17/09/2026 · **Cliente:** Algar

        ## Resumo

        A reunião definiu o tom da comunicação
        por canal.

        ## Pendências

        ### Do nosso lado

        - [ ] Consolidar as referências — **Carol** — sexta
        - [x] Marcar a validação — **André** — [prazo a definir]

        ### Do lado do cliente

        - [ ] Revisar os textos de cobrança — **Jurídico** — [prazo a definir]
        """;

    private void Escrever(string arquivo, string texto) =>
        File.WriteAllText(Path.Combine(_pasta, arquivo), texto);

    [Fact]
    public void SemAtaNaoHaEstado()
    {
        var e = EstadoDaAta.Ler(_pasta);

        Assert.False(e.Existe);
        Assert.False(e.Velha);
        Assert.Equal(0, e.Pendencias);
        Assert.Empty(e.PrimeirasPendencias);
        Assert.Null(e.Resumo);
    }

    [Fact]
    public void ContaSoAsPendenciasAbertas()
    {
        Escrever("ata.md", Ata);

        Assert.Equal(2, EstadoDaAta.Ler(_pasta).Pendencias);
    }

    [Fact]
    public void AsPrimeirasPendenciasSaemSemAMarcacao()
    {
        Escrever("ata.md", Ata);

        Assert.Equal(
            ["Consolidar as referências — Carol — sexta",
             "Revisar os textos de cobrança — Jurídico — [prazo a definir]"],
            EstadoDaAta.Ler(_pasta).PrimeirasPendencias);
    }

    [Fact]
    public void OResumoEOPrimeiroParagrafoNumaLinhaSo()
    {
        Escrever("ata.md", Ata);

        Assert.Equal("A reunião definiu o tom da comunicação por canal.",
                     EstadoDaAta.Ler(_pasta).Resumo);
    }

    [Fact]
    public void AtaSemResumoNaoInventaUm()
    {
        Escrever("ata.md", "# Ata\n\n## Decisões\n\n- adiar o piloto\n");

        Assert.Null(EstadoDaAta.Ler(_pasta).Resumo);
    }

    [Fact]
    public void ResumoLongoECortadoNumaPalavra()
    {
        string longo = string.Join(" ", Enumerable.Repeat("palavra", 60));
        Escrever("ata.md", $"## Resumo\n\n{longo}\n");

        string resumo = EstadoDaAta.Ler(_pasta).Resumo!;

        Assert.True(resumo.Length <= EstadoDaAta.TamanhoDoResumo + 1);
        Assert.EndsWith("palavra…", resumo);
    }

    [Fact]
    public void AtaMaisVelhaQueATranscricaoEstaVelha()
    {
        Escrever("ata.md", Ata);
        Escrever("transcricao.json", "{}");
        File.SetLastWriteTimeUtc(Path.Combine(_pasta, "ata.md"), DateTime.UtcNow.AddMinutes(-10));

        Assert.True(EstadoDaAta.Ler(_pasta).Velha);
    }

    [Fact]
    public void AtaMaisNovaQueATranscricaoNaoEstaVelha()
    {
        Escrever("transcricao.json", "{}");
        Escrever("ata.md", Ata);
        File.SetLastWriteTimeUtc(Path.Combine(_pasta, "transcricao.json"),
                                 DateTime.UtcNow.AddMinutes(-10));

        Assert.False(EstadoDaAta.Ler(_pasta).Velha);
    }

    [Fact]
    public void AtaSemTranscricaoNaoEstaVelha()
    {
        Escrever("ata.md", Ata);

        Assert.False(EstadoDaAta.Ler(_pasta).Velha);
    }

    [Fact]
    public void AtaTravadaNaoLanca()
    {
        // Um editor com o arquivo aberto no Windows é o caso real. O contrato é
        // não lançar: a lista monta um resumo por gravação dentro de um try, e
        // uma exceção aqui esconderia a reunião inteira. Onde a trava não
        // impedir a leitura, o teste continua valendo — ele confere o contrato,
        // não o caminho.
        Escrever("ata.md", Ata);
        using var trava = new FileStream(Path.Combine(_pasta, "ata.md"),
                                         FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var e = EstadoDaAta.Ler(_pasta);

        Assert.True(e.Existe);
    }
}
