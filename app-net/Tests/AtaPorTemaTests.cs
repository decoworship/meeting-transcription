using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>A ata por tema (ATA-5): o esquema, o prompt e o redator.</summary>
public sealed class AtaPorTemaTests
{
    [Fact]
    public void AtaAntigaComListasDeTextoContinuaAbrindo()
    {
        // Toda ata.json gerada antes desta versão tem as três listas como
        // string[]. Ela tem que abrir igual, ou o acervo inteiro quebra.
        var ata = AtaGerada.DeJson("""
            {"resumo":"r","secoes":[],"decisoes":["d1"],"acoes":[],
             "pontos_em_aberto":["p1"],"riscos":["r1"],"observacoes":[]}
            """)!;

        Assert.Equal("d1", Assert.Single(ata.Decisoes).Texto);
        Assert.Null(ata.Decisoes[0].Secao);
        Assert.Equal("p1", Assert.Single(ata.PontosEmAberto).Texto);
        Assert.Equal("r1", Assert.Single(ata.Riscos).Texto);
    }

    [Fact]
    public void ItemComSecaoVaiEVoltaPeloJson()
    {
        var ata = new AtaGerada
        {
            Decisoes = [new ItemDaAta("d1", "Pedido mínimo")],
            Acoes = [new AcaoDaAta { Acao = "a1", Secao = "Pedido mínimo" }],
            Secoes = [new SecaoDaAta { Titulo = "Pedido mínimo", Texto = "t",
                                       Grupo = "Pedido Sugerido", Responsavel = "Comercial" }],
        };

        var volta = AtaGerada.DeJson(ata.ParaJson())!;

        Assert.Equal(new ItemDaAta("d1", "Pedido mínimo"), volta.Decisoes[0]);
        Assert.Equal("Pedido mínimo", volta.Acoes[0].Secao);
        Assert.Equal("Pedido Sugerido", volta.Secoes[0].Grupo);
        Assert.Equal("Comercial", volta.Secoes[0].Responsavel);
    }

    [Fact]
    public void SecaoEmBrancoViraNula()
    {
        // O esquema por tema exige o campo, então o modelo escreve "" quando o
        // item não pertence a seção nenhuma. "" e ausente têm que ser a mesma coisa.
        var ata = AtaGerada.DeJson("""
            {"resumo":"","secoes":[],"decisoes":[{"texto":"d","secao":"  "}],
             "acoes":[{"acao":"a","responsavel":"x","prazo":"","lado":"nosso","secao":""}],
             "pontos_em_aberto":[],"riscos":[],"observacoes":[]}
            """)!;

        Assert.Null(ata.Decisoes[0].Secao);
        Assert.Null(ata.Acoes[0].Secao);
    }

    [Fact]
    public void ItemSemSecaoNaoEscreveOCampo()
    {
        string json = new AtaGerada { Decisoes = ["d1"] }.ParaJson();
        Assert.DoesNotContain("\"secao\"", json);
    }

    [Fact]
    public void DecisaoRebaixadaPeloVerificadorLevaASecao()
    {
        // O verificador move para pontos em aberto a decisão que quase não
        // aparece na transcrição. Ela tem que continuar no tema dela.
        var ata = new AtaGerada
        {
            Decisoes = [new ItemDaAta("Migrar tudo para Kubernetes no trimestre", "Infra")],
        };

        VerificadorDeAta.Conferir(ata, ReuniaoDeExemplo.Segmentos(), ["Bruno Teles"], []);

        Assert.Empty(ata.Decisoes);
        Assert.Equal("Infra", Assert.Single(ata.PontosEmAberto).Secao);
    }

    [Theory]
    [InlineData(null, FormatoDaAta.PorTema)]
    [InlineData("", FormatoDaAta.PorTema)]
    [InlineData("por-tema", FormatoDaAta.PorTema)]
    [InlineData("classico", FormatoDaAta.Classico)]
    [InlineData("outra-coisa", FormatoDaAta.Classico)]
    public void OFormatoLidoDoAppJson(string? escrito, FormatoDaAta esperado) =>
        Assert.Equal(esperado, FormatosDaAta.Ler(escrito));

    [Fact]
    public void AppJsonSemAChaveCaiNoPorTema()
    {
        string caminho = Path.Combine(Path.GetTempPath(), $"app-{Guid.NewGuid():N}.json");
        File.WriteAllText(caminho, """{"tema":"claro"}""");
        try
        {
            // A validação de 08/10/2026 deu 81% no critério 3 (meta 75%), e o
            // dono aprovou assim: o padrão é por tema.
            Assert.Equal(FormatoDaAta.PorTema,
                         MeetingApp.Nucleo.ConfiguracoesDoApp.Carregar(caminho).Formato);
        }
        finally { File.Delete(caminho); }
    }

    [Theory]
    [InlineData("cliente-update", true)]
    [InlineData("trabalho", false)]
    [InlineData("kickoff", false)]
    [InlineData("sprint", false)]
    [InlineData("daily", false)]
    [InlineData("resultados", false)]
    public void SoOsTiposMarcadosAgrupam(string tipo, bool agrupa) =>
        Assert.Equal(agrupa, ModelosDeAta.Buscar(tipo)!.ItensPorSecao);

    [Fact]
    public void AMarcaNaoChegaAoModelo() =>
        Assert.DoesNotContain("itens-por-secao",
                              ModelosDeAta.Buscar("cliente-update")!.TextoParaPrompt());

    private static string Prompt(string tipo, FormatoDaAta formato)
    {
        var s = ReuniaoDeExemplo.Segmentos();
        return PromptDeAta.Montar(ModelosDeAta.Buscar(tipo)!, ReuniaoDeExemplo.Contexto(),
                                  s, RoteiroDeFatos.De(s), formato);
    }

    [Fact]
    public void PorTemaComMarcaLevaOBlocoNovo()
    {
        string p = Prompt("cliente-update", FormatoDaAta.PorTema);
        Assert.Contains(PromptDeAta.BlocoPorTema, p);
        Assert.Contains("no máximo três frases", p);
    }

    [Fact]
    public void PorTemaSemMarcaEOPromptDeHoje() =>
        Assert.Equal(Prompt("daily", FormatoDaAta.Classico), Prompt("daily", FormatoDaAta.PorTema));

    [Fact]
    public void ClassicoComMarcaNaoLevaOBlocoNovo() =>
        Assert.DoesNotContain(PromptDeAta.BlocoPorTema, Prompt("cliente-update", FormatoDaAta.Classico));

    [Fact]
    public void OEsquemaPorTemaEJsonValidoEExigeASecao()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(AtaGerada.EsquemaPorTema);
        var props = doc.RootElement.GetProperty("properties");

        foreach (string lista in new[] { "decisoes", "pontos_em_aberto", "riscos", "acoes" })
        {
            var item = props.GetProperty(lista).GetProperty("items");
            Assert.Equal("object", item.GetProperty("type").GetString());
            Assert.Contains("secao", item.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        }
        var secao = props.GetProperty("secoes").GetProperty("items").GetProperty("properties");
        Assert.True(secao.TryGetProperty("grupo", out _));
        Assert.True(secao.TryGetProperty("responsavel", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OEsquemaCertoParaCadaCaso(bool agrupa) =>
        Assert.Equal(agrupa ? AtaGerada.EsquemaPorTema : AtaGerada.Esquema, AtaGerada.EsquemaPara(agrupa));

    private static string Md(string tipo, FormatoDaAta formato, AtaGerada? ata = null) =>
        RedatorDeAta.Escrever(ata ?? ReuniaoDeExemplo.AtaSemSecao(), ModelosDeAta.Buscar(tipo)!,
                              ReuniaoDeExemplo.Contexto(), formato);

    [Theory]
    [InlineData("daily")]
    [InlineData("cliente-update")]
    public void PorTemaNaoEscrevePrazoVazioNemObservacoes(string tipo)
    {
        string md = Md(tipo, FormatoDaAta.PorTema);
        Assert.DoesNotContain("[prazo a definir]", md);
        Assert.DoesNotContain("Observações sobre a transcrição", md);
        Assert.Contains("— sexta", md);   // prazo de verdade continua
    }

    [Fact]
    public void AsObservacoesVaoParaOArquivoProprio()
    {
        string? obs = RedatorDeAta.Observacoes(ReuniaoDeExemplo.AtaSemSecao());
        Assert.NotNull(obs);
        Assert.StartsWith("## Observações sobre a transcrição", obs);
        Assert.Contains("Números citados", obs);
        Assert.Null(RedatorDeAta.Observacoes(new AtaGerada()));
    }

    [Theory]
    [InlineData("Andre Yuri investigar a influência da Copa", "Andre Yuri", "Investigar a influência da Copa")]
    [InlineData("Carla Nunes buscar as regras", "Carla Nunes", "Buscar as regras")]
    [InlineData("Marina Lopes buscar as regras", "Marina Costa Lopes", "Buscar as regras")]
    [InlineData("Começar o dashboard", "Bruno Teles", "Começar o dashboard")]
    [InlineData("Analisar a base", "Ana Paula", "Analisar a base")]
    [InlineData("Carla Nunes", "Carla Nunes", "Carla Nunes")]
    [InlineData("Agendar alinhamento com o Rodrigo", "[responsável a definir]", "Agendar alinhamento com o Rodrigo")]
    public void ODonoSaiDoComecoDoTexto(string acao, string dono, string esperado) =>
        Assert.Equal(esperado, RedatorDeAta.SemDonoNoComeco(acao, dono));

    [Fact]
    public void PorTemaTiraODonoRepetidoNaAta() =>
        Assert.Contains("- [ ] Buscar as regras de ruptura da Hiper — **Carla Nunes** — sexta",
                        Md("daily", FormatoDaAta.PorTema));

    private static string PorTema(string tipo = "cliente-update", AtaGerada? ata = null) =>
        Md(tipo, FormatoDaAta.PorTema, ata ?? ReuniaoDeExemplo.AtaComSecao());

    private static string Bloco(string md, string titulo)
    {
        int i = md.IndexOf(titulo, StringComparison.Ordinal);
        Assert.True(i >= 0, $"não achei {titulo}");
        int fim = md.IndexOf("\n#", i + titulo.Length, StringComparison.Ordinal);
        return fim < 0 ? md[i..] : md[i..fim];
    }

    [Fact]
    public void OsItensFicamDentroDoTema()
    {
        string pedido = Bloco(PorTema(), "### Pedido mínimo · Comercial");
        Assert.Contains("A regra nova soma o aplicativo e o SFA.", pedido);
        Assert.Contains("**Atenção:** O planner atualiza às quartas.", pedido);
        Assert.Contains("- [ ] Novo valor do pedido mínimo por canal — **Carla Nunes**", pedido);
        Assert.Contains("**Em aberto:** Como implementar a soma.", pedido);
    }

    [Fact]
    public void AOrdemDentroDoTemaEFixa()
    {
        string pedido = Bloco(PorTema(), "### Pedido mínimo");
        int atencao = pedido.IndexOf("**Atenção:**", StringComparison.Ordinal);
        int acao = pedido.IndexOf("- [ ]", StringComparison.Ordinal);
        int aberto = pedido.IndexOf("**Em aberto:**", StringComparison.Ordinal);
        Assert.True(atencao < acao && acao < aberto);

        string dash = Bloco(PorTema(), "## Dashboard");
        Assert.True(dash.IndexOf("**Decidido:**", StringComparison.Ordinal)
                    < dash.IndexOf("- [ ]", StringComparison.Ordinal));
    }

    [Fact]
    public void OSecaoCasaSemLigarParaCaixaNemEspaco()
    {
        // O modelo devolveu "regra de RUPTURA " para a seção "Regra de ruptura".
        Assert.Contains("**Decidido:** Estudar critérios parecidos com os da Hiper.",
                        Bloco(PorTema(), "### Regra de ruptura · Trade"));
    }

    [Fact]
    public void OGrupoViraTituloComOsTemasDentro()
    {
        string md = PorTema();
        int grupo = md.IndexOf("## Portal: próximos passos", StringComparison.Ordinal);
        Assert.True(grupo > md.IndexOf("## Dashboard", StringComparison.Ordinal));
        Assert.True(md.IndexOf("### Pedido mínimo", StringComparison.Ordinal) > grupo);
        Assert.True(md.IndexOf("### Regra de ruptura", StringComparison.Ordinal) > grupo);
    }

    [Fact]
    public void ItemSemTemaVaiParaAListaGeralENaoSome()
    {
        string md = PorTema();
        Assert.Contains("- [ ] Mandar a pauta — **Diego Prado**", Bloco(md, "## Outras pendências"));
        Assert.Contains("- Duas semanas de sugestão perdidas em julho.", Bloco(md, "## Riscos e alertas"));
    }

    [Fact]
    public void NenhumItemSome()
    {
        var ata = ReuniaoDeExemplo.AtaComSecao();
        string md = PorTema();
        foreach (var d in ata.Decisoes) Assert.Contains(d.Texto, md);
        foreach (var p in ata.PontosEmAberto) Assert.Contains(p.Texto, md);
        foreach (var r in ata.Riscos) Assert.Contains(r.Texto, md);
        Assert.Equal(ata.Acoes.Count, md.Split('\n').Count(l => l.StartsWith("- [ ]", StringComparison.Ordinal)));
    }

    [Fact]
    public void QuemFicouComOQueSeparaPorLado()
    {
        string quem = Bloco(PorTema(), "## Quem ficou com o quê");
        Assert.Contains("Do nosso lado: **Bruno Teles**: Dashboard · **Diego Prado**: Outras pendências", quem);
        Assert.Contains("Do lado do cliente: **Carla Nunes**: Pedido mínimo, Regra de ruptura", quem);
        Assert.DoesNotContain("### Do nosso lado", PorTema());
    }

    [Fact]
    public void ORodapeDoEsqueletoFechaAAta()
    {
        string md = PorTema();
        int quem = md.IndexOf("## Quem ficou com o quê", StringComparison.Ordinal);
        Assert.True(quem >= 0);
        Assert.True(md.IndexOf("## Próxima reunião", StringComparison.Ordinal) > quem);
        Assert.DoesNotContain("Sem itens nem texto", md);
    }

    [Fact]
    public void TipoSemMarcaIgnoraASecao()
    {
        string md = Md("daily", FormatoDaAta.PorTema, ReuniaoDeExemplo.AtaComSecao());
        Assert.DoesNotContain("**Decidido:**", md);
        Assert.DoesNotContain("Quem ficou com o quê", md);
        Assert.Contains("## Decisões", md);
    }

    [Fact]
    public void ClassicoIgnoraASecaoMesmoComMarca()
    {
        string md = Md("cliente-update", FormatoDaAta.Classico, ReuniaoDeExemplo.AtaComSecao());
        Assert.DoesNotContain("**Decidido:**", md);
        Assert.Contains("### Do nosso lado", md);
        Assert.Contains("Observações sobre a transcrição", md);
    }

    [Fact]
    public void TipoDoUsuarioComAMarcaSegueORodapeDoEsqueletoDele()
    {
        // Um tipo da pasta do usuário que ligou o agrupamento: o esqueleto é o
        // da sessão de trabalho, onde "Referências citadas" vem depois de
        // "Ações" — então é rodapé, e fecha a ata.
        var trabalho = ModelosDeAta.Buscar("trabalho")!;
        var meu = new ModeloDeAta("meu-trabalho", "Meu trabalho",
                                  ModeloDeAta.Marca + "\n" + trabalho.Texto, DoUsuario: true);
        var ata = ReuniaoDeExemplo.AtaComSecao();
        ata.Secoes.Add(new SecaoDaAta { Titulo = "Referências citadas", Texto = "O chamado 123." });
        string md = RedatorDeAta.Escrever(ata, meu, ReuniaoDeExemplo.Contexto(), FormatoDaAta.PorTema);
        int quem = md.IndexOf("## Quem ficou com o quê", StringComparison.Ordinal);
        Assert.True(quem >= 0);
        Assert.True(md.IndexOf("## Referências citadas", StringComparison.Ordinal) > quem);
    }

    // ── achados da revisão final

    [Fact]
    public void UpdateSemNenhumTemaCasadoAindaSeparaPorLado()
    {
        // O modelo deixou "secao" vazio em tudo: nenhuma ação foi aninhada, e a
        // separação por lado é "a razão de existir" da ata de update.
        string md = Md("cliente-update", FormatoDaAta.PorTema, ReuniaoDeExemplo.AtaSemSecao());
        Assert.Contains("### Do nosso lado", md);
        Assert.Contains("### Do lado do cliente", md);
        Assert.DoesNotContain("Quem ficou com o quê", md);
    }

    private static AtaGerada ComTemasIrmaos(string secaoDaDecisao) => new()
    {
        Secoes =
        [
            new SecaoDaAta { Titulo = "Dashboard", Texto = "O filtro." },
            new SecaoDaAta { Titulo = "Dashboard de aderência", Texto = "O relatório." },
        ],
        Decisoes = [new ItemDaAta("Automatizar o envio.", secaoDaDecisao)],
    };

    [Fact]
    public void TemaIrmaoNaoRoubaOItem()
    {
        string md = Md("cliente-update", FormatoDaAta.PorTema, ComTemasIrmaos("Dashboard de aderência"));
        Assert.Contains("**Decidido:** Automatizar o envio.", Bloco(md, "## Dashboard de aderência"));
        Assert.DoesNotContain("Automatizar", Bloco(md, "## Dashboard\n"));
    }

    [Theory]
    [InlineData("—")]
    [InlineData(" · ")]
    public void SecaoSoDePontuacaoVaiParaAListaGeral(string secao)
    {
        string md = Md("cliente-update", FormatoDaAta.PorTema, ComTemasIrmaos(secao));
        Assert.Contains("- Automatizar o envio.", Bloco(md, "## Decisões"));
    }

    [Fact]
    public void RodapeRepetidoNaoSome()
    {
        var ata = ReuniaoDeExemplo.AtaComSecao();
        ata.Secoes.Add(new SecaoDaAta { Titulo = "Próxima reunião", Texto = "Terça, às 10h." });
        string md = PorTema("cliente-update", ata);
        Assert.Contains("A definir.", md);
        Assert.Contains("Terça, às 10h.", md);
    }

    [Fact]
    public void SecaoSemTituloNaoApagaORodapeNemViraTituloVazio()
    {
        var ata = ReuniaoDeExemplo.AtaComSecao();
        ata.Secoes.Insert(0, new SecaoDaAta { Titulo = "", Texto = "Um parágrafo sem título." });
        string md = PorTema("cliente-update", ata);
        Assert.Contains("Um parágrafo sem título.", md);
        Assert.Contains("A definir.", Bloco(md, "## Próxima reunião"));
        Assert.DoesNotContain(md.Split('\n'), l => l.TrimEnd() is "#" or "##" or "###");
    }

    // ── os menores da revisão, e o que a validação mostrou

    [Theory]
    [InlineData("Ana e Bruno revisarem o contrato", "Ana Lima", "Ana e Bruno revisarem o contrato")]
    [InlineData("Ana com o Bruno revisar o contrato", "Ana Lima", "Ana com o Bruno revisar o contrato")]
    [InlineData("Andre Yuri investigar a queda", "André Yuri", "Investigar a queda")]
    [InlineData("André investigar a queda", "Andre Yuri", "Investigar a queda")]
    [InlineData("Bruno SQL revisar a consulta", "Bruno Teles", "SQL revisar a consulta")]
    public void ODonoSaiSoQuandoEOProprioNome(string acao, string dono, string esperado) =>
        Assert.Equal(esperado, RedatorDeAta.SemDonoNoComeco(acao, dono));

    private static string ComDonoDoTema(string titulo, string? responsavel)
    {
        var ata = new AtaGerada
        {
            Secoes = [new SecaoDaAta { Titulo = titulo, Texto = "Contexto.", Responsavel = responsavel }],
        };
        return Md("cliente-update", FormatoDaAta.PorTema, ata);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("[responsável a definir]")]
    [InlineData("nosso")]
    [InlineData("Cliente")]
    public void DonoDoTemaQueNaoEDonoNaoVaiParaOTitulo(string responsavel) =>
        Assert.Contains("## Pedido mínimo\n", ComDonoDoTema("Pedido mínimo", responsavel).Replace("\r", ""));

    [Fact]
    public void DonoDoTemaQueJaEstaNoTituloNaoRepete() =>
        Assert.Contains("## Pedido mínimo · Comercial\n",
                        ComDonoDoTema("Pedido mínimo · Comercial", "Comercial").Replace("\r", ""));

    [Fact]
    public void GrupoDeUmTemaSoNaoViraTitulo()
    {
        // Medido na validação: "## Carteira da Mariana / ### Carteira da Mariana".
        var ata = new AtaGerada
        {
            Secoes = [new SecaoDaAta { Titulo = "Carteira da Mariana", Texto = "Contexto.",
                                       Grupo = "Carteira da Mariana" }],
        };
        string md = Md("cliente-update", FormatoDaAta.PorTema, ata).Replace("\r", "");
        Assert.Contains("## Carteira da Mariana\n", md);
        Assert.DoesNotContain("### Carteira da Mariana", md);
    }

    [Fact]
    public void QuemFicouComUmLadoSoNaoDizOLado()
    {
        var ata = new AtaGerada
        {
            Secoes = [new SecaoDaAta { Titulo = "Infra", Texto = "Contexto." }],
            Acoes = [new AcaoDaAta { Acao = "Subir o servidor", Responsavel = "Bruno Teles",
                                     Prazo = "sexta", Lado = "nosso", Secao = "Infra" }],
        };
        string quem = Bloco(Md("cliente-update", FormatoDaAta.PorTema, ata), "## Quem ficou com o quê");
        Assert.Contains("**Bruno Teles**: Infra", quem);
        Assert.DoesNotContain("Do nosso lado", quem);
    }

    [Theory]
    [InlineData("classico", FormatoDaAta.Classico)]
    [InlineData("por-tema", FormatoDaAta.PorTema)]
    public void ALinhaDeComandoAceitaOsDoisNomes(string escrito, FormatoDaAta esperado)
    {
        Assert.True(FormatosDaAta.TentarLer(escrito, out var formato));
        Assert.Equal(esperado, formato);
    }

    [Theory]
    [InlineData("portema")]
    [InlineData("Por-Tema")]
    [InlineData("")]
    public void ALinhaDeComandoRecusaONomeErrado(string escrito) =>
        Assert.False(FormatosDaAta.TentarLer(escrito, out _));
}
