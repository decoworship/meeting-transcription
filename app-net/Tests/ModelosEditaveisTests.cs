using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Os modelos de ata editáveis em Ajustes › Atas (parte 2 da ATA-5).
/// </summary>
/// <remarks>
/// Numa pasta temporária: a pasta de verdade é a do usuário, e um teste que
/// escrevesse lá mudaria a ata da próxima reunião dele.
/// </remarks>
public sealed class ModelosEditaveisTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("modelos").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private static string Valido(string titulo = "Comitê", string secao = "Pauta") => $"""
        # Ata — {titulo}

        ## Estrutura

        ```markdown
        # Ata — [Projeto]

        ## Resumo

        ## {secao}
        [o que foi tratado]

        ## Decisões
        ```

        ## Notas específicas

        Tom neutro.
        """;

    private string Arquivo(string id) => Path.Combine(_pasta, $"{id}.md");

    [Fact]
    public void SalvarUmDoAppCriaACopiaENaoMexeNoOriginal()
    {
        string original = ModelosDeAta.Buscar("daily", _pasta)!.Texto;

        ModelosDeAta.Salvar("daily", Valido(secao: "Rodada nova"), _pasta);

        var salvo = ModelosDeAta.Buscar("daily", _pasta)!;
        Assert.True(salvo.DoUsuario);
        Assert.Contains("Rodada nova", salvo.Secoes);
        Assert.Equal(original, ModelosDeAta.Original("daily"));
        Assert.True(File.Exists(Arquivo("daily")));
    }

    [Theory]
    [InlineData("# Ata — Sem bloco\n\nSó texto.")]
    [InlineData("# Ata\n\n```markdown\n# Ata — X\n\nsem seção nenhuma\n```\n")]
    [InlineData("# Ata\n\n```markdown\n## Pauta\n")]
    public void SalvarRecusaTextoSemEstrutura(string texto)
    {
        var e = Assert.Throws<InvalidOperationException>(
            () => ModelosDeAta.Salvar("daily", texto, _pasta));
        Assert.Contains("```markdown", e.Message);
        Assert.False(File.Exists(Arquivo("daily")));
    }

    [Fact]
    public void OResumoDizQuemEscreveCadaSecao()
    {
        ModelosDeAta.Salvar("daily", Valido(), _pasta);
        var resumo = ModelosDeAta.Resumo(ModelosDeAta.Buscar("daily", _pasta)!);

        Assert.Equal(["Resumo", "Pauta", "Decisões"], resumo.Select(s => s.Titulo));
        Assert.Equal([true, false, true], resumo.Select(s => s.DoApp));
    }

    [Fact]
    public void CriarDuplicaComONomeNoTituloENoArquivo()
    {
        string id = ModelosDeAta.Criar("cliente-update", "Comitê de Dados", _pasta);

        Assert.Equal("comite-de-dados", id);
        var novo = ModelosDeAta.Buscar(id, _pasta)!;
        Assert.Equal("Comitê de Dados", novo.Nome);
        Assert.True(novo.DoUsuario);
        Assert.False(ModelosDeAta.EhDoApp(id));
        Assert.StartsWith("<!-- ata: itens-por-secao -->\n# Ata — Comitê de Dados", novo.Texto.Replace("\r", ""));
        // O resto do modelo veio junto.
        Assert.Equal(ModelosDeAta.Buscar("cliente-update", _pasta)!.Secoes, novo.Secoes);
    }

    [Theory]
    [InlineData("Daily")]
    [InlineData("cliente update")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void CriarRecusaNomeQueJaExisteOuVazio(string nome)
    {
        Assert.Throws<InvalidOperationException>(() => ModelosDeAta.Criar("daily", nome, _pasta));
    }

    [Fact]
    public void CriarRecusaONomeDeUmTipoDoUsuario()
    {
        ModelosDeAta.Criar("daily", "Comitê", _pasta);
        Assert.Throws<InvalidOperationException>(() => ModelosDeAta.Criar("daily", "comitê", _pasta));
    }

    [Fact]
    public void ApagarUmTipoDoUsuarioTiraDaLista()
    {
        string id = ModelosDeAta.Criar("daily", "Comitê", _pasta);
        ModelosDeAta.VoltarAoOriginal(id, _pasta);
        Assert.Null(ModelosDeAta.Buscar(id, _pasta));
    }

    [Fact]
    public void VoltarAoOriginalDevolveODoApp()
    {
        ModelosDeAta.Salvar("daily", Valido(), _pasta);
        ModelosDeAta.VoltarAoOriginal("daily", _pasta);

        var volta = ModelosDeAta.Buscar("daily", _pasta)!;
        Assert.False(volta.DoUsuario);
        Assert.Equal(ModelosDeAta.Original("daily"), volta.Texto);
    }

    [Fact]
    public void AvisaQuandoOOriginalMudouDepoisDePersonalizar()
    {
        ModelosDeAta.Salvar("daily", Valido(), _pasta);
        Assert.False(ModelosDeAta.OriginalMudou("daily", _pasta));

        // Uma versão nova do app trouxe outro daily.md: a marca guardada na
        // hora de personalizar não bate mais.
        File.WriteAllText(Path.Combine(_pasta, ".originais"), "daily\tmarca-de-outra-versao\n");
        Assert.True(ModelosDeAta.OriginalMudou("daily", _pasta));

        // Salvar de novo não apaga o aviso: a cópia continua sendo da versão velha.
        ModelosDeAta.Salvar("daily", Valido(secao: "Outra"), _pasta);
        Assert.True(ModelosDeAta.OriginalMudou("daily", _pasta));

        // Voltar ao original, sim.
        ModelosDeAta.VoltarAoOriginal("daily", _pasta);
        Assert.False(ModelosDeAta.OriginalMudou("daily", _pasta));
    }

    [Fact]
    public void SemMarcaGuardadaNaoAfirmaNada()
    {
        // Personalizado antes desta versão: não há como saber de qual original.
        File.WriteAllText(Arquivo("daily"), Valido());
        Assert.False(ModelosDeAta.OriginalMudou("daily", _pasta));
    }

    [Fact]
    public void TipoDoUsuarioNaoTemOriginalQueMude()
    {
        string id = ModelosDeAta.Criar("daily", "Comitê", _pasta);
        Assert.False(ModelosDeAta.OriginalMudou(id, _pasta));
    }
}
