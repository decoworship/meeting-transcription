using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A rede embaixo da ata: o que o verificador pega antes de virar arquivo.
/// </summary>
/// <remarks>
/// É o que torna um modelo de 4B aceitável. Uma ata que inventa decisão é pior
/// que nenhuma, porque cria memória falsa — e a memória falsa só aparece meses
/// depois, quando alguém cobra o que nunca foi combinado.
/// </remarks>
public sealed class VerificadorDeAtaTests
{
    private static SegmentoFinal Fala(string texto, string? quem = null, double t = 0) =>
        new() { Start = t, End = t + 5, Text = texto, Speaker = quem };

    private static AtaGerada AtaCom(params AcaoDaAta[] acoes) =>
        new() { Resumo = "resumo qualquer", Acoes = [.. acoes] };

    [Fact]
    public void DonoInventadoViraResponsavelADefinir()
    {
        var ata = AtaCom(new AcaoDaAta
        {
            Acao = "Mandar a base", Responsavel = "Fernanda Alves", Prazo = "sexta",
        });

        VerificadorDeAta.Conferir(ata, [Fala("a gente manda a base")],
                                  ["Vanessa Levorato", "Andre Monlevade"], []);

        Assert.Equal("[responsável a definir]", ata.Acoes[0].Responsavel);
        // E não em silêncio: quem lê a ata precisa saber que houve troca.
        Assert.Contains(ata.Observacoes, o => o.Contains("Fernanda Alves"));
    }

    [Fact]
    public void OPrimeiroNomeBastaParaReconhecerODono()
    {
        // A fala usa "Vanessa"; a agenda tem "Vanessa Levorato". Exigir o nome
        // completo transformaria dono legítimo em [responsável a definir].
        var ata = AtaCom(new AcaoDaAta
        {
            Acao = "Analisar os exemplos", Responsavel = "Vanessa", Prazo = "amanhã",
        });

        VerificadorDeAta.Conferir(ata, [Fala("a Vanessa vai analisar")],
                                  ["Vanessa Levorato"], []);

        Assert.Equal("Vanessa", ata.Acoes[0].Responsavel);
        Assert.Empty(ata.Observacoes);
    }

    [Fact]
    public void ResponsavelVazioViraOMarcadorCerto()
    {
        var ata = AtaCom(new AcaoDaAta { Acao = "Investigar", Responsavel = "", Prazo = "" });

        VerificadorDeAta.Conferir(ata, [Fala("alguém investiga isso")], ["Ana"], []);

        Assert.Equal("[responsável a definir]", ata.Acoes[0].Responsavel);
        // Já veio sem dono: não é troca, então não vira observação.
        Assert.Empty(ata.Observacoes);
    }

    [Fact]
    public void DecisaoSemEcoNaTranscricaoDesceParaPontosEmAberto()
    {
        var ata = new AtaGerada
        {
            Decisoes = ["Migrar toda a infraestrutura para Kubernetes no próximo trimestre"],
        };

        VerificadorDeAta.Conferir(
            ata, [Fala("vamos falar do faturamento das contas zeradas")], [], []);

        Assert.Empty(ata.Decisoes);
        Assert.Single(ata.PontosEmAberto);
        Assert.Contains(ata.Observacoes, o => o.Contains("registrada como decisão"));
    }

    [Fact]
    public void DecisaoAncoradaNaFalaFica()
    {
        var ata = new AtaGerada
        {
            Decisoes = ["Manter o filtro de suspensão temporária removido do universo"],
        };

        VerificadorDeAta.Conferir(ata, [
            Fala("aqui a gente aplicou a retirada da suspensão temporária, mantém isso?"),
            Fala("mantém, esse filtro continua removido do universo"),
        ], [], []);

        Assert.Single(ata.Decisoes);
        Assert.Empty(ata.PontosEmAberto);
    }

    [Fact]
    public void NumeroCitadoQueSumiuDaAtaEListado()
    {
        // O modo de falha real do modelo pequeno: ele não inventa, ele esquece.
        var segmentos = new[] { Fala("dá R$ 180 mil por mês"), Fala("são 27.529 contas") };
        var roteiro = RoteiroDeFatos.De(segmentos);

        var ata = new AtaGerada { Resumo = "Foram analisadas 27.529 contas." };
        VerificadorDeAta.Conferir(ata, segmentos, [], roteiro);

        Assert.Contains(ata.Observacoes, o => o.Contains("não aparecem nesta ata"));
        Assert.Contains(ata.Observacoes, o => o.Contains("180"));
    }

    [Fact]
    public void SemOmissaoNaoInventaObservacao()
    {
        var segmentos = new[] { Fala("são 27.529 contas") };
        var roteiro = RoteiroDeFatos.De(segmentos);

        var ata = new AtaGerada { Resumo = "Foram 27.529 contas." };
        VerificadorDeAta.Conferir(ata, segmentos, [], roteiro);

        Assert.Empty(ata.Observacoes);
    }

    [Fact]
    public void RotuloGenericoNaoValidaDono()
    {
        // "Speaker 3" não é gente, e não pode ser dono de nada.
        var ata = AtaCom(new AcaoDaAta
        {
            Acao = "Enviar", Responsavel = "Speaker 3", Prazo = "hoje",
        });

        VerificadorDeAta.Conferir(ata, [Fala("eu envio hoje")], ["Speaker 3", "Speaker 1"], []);

        Assert.Equal("[responsável a definir]", ata.Acoes[0].Responsavel);
    }
}

/// <summary>
/// O redator: o formato que deixa de depender da memória do modelo.
/// </summary>
public sealed class RedatorDeAtaTests
{
    private static ModeloDeAta Tipo() => ModelosDeAta.Buscar("cliente-update")!;

    private static ContextoDaReuniao Contexto() => new()
    {
        Titulo = "Faturamento B2B",
        Cliente = "Vivo",
        Projeto = "Faturamento B2B",
        Data = "2026-08-13T14:30:00-03:00",
        DuracaoS = 1752,
        Falantes = ["Vanessa Levorato", "Speaker 3"],
    };

    [Fact]
    public void OItemDeAcaoSaiNoFormatoDaSkill()
    {
        var ata = new AtaGerada
        {
            Acoes = [new AcaoDaAta
            {
                Acao = "Enviar a base dos 27 mil", Responsavel = "Dimi Randel",
                Prazo = "hoje", Lado = "nosso",
            }],
        };

        string md = RedatorDeAta.Escrever(ata, Tipo(), Contexto());

        Assert.Contains("- [ ] Enviar a base dos 27 mil — **Dimi Randel** — hoje", md);
    }

    [Fact]
    public void SeparaOsLadosSoQuandoHaOsDois()
    {
        var so = new AtaGerada
        {
            Acoes = [new AcaoDaAta { Acao = "A", Responsavel = "X", Prazo = "hoje", Lado = "nosso" }],
        };
        Assert.DoesNotContain("Do nosso lado", RedatorDeAta.Escrever(so, Tipo(), Contexto()));

        var ambos = new AtaGerada
        {
            Acoes = [
                new AcaoDaAta { Acao = "A", Responsavel = "X", Prazo = "hoje", Lado = "nosso" },
                new AcaoDaAta { Acao = "B", Responsavel = "Y", Prazo = "amanhã", Lado = "cliente" },
            ],
        };
        string md = RedatorDeAta.Escrever(ambos, Tipo(), Contexto());
        Assert.Contains("### Do nosso lado", md);
        Assert.Contains("### Do lado do cliente", md);
    }

    [Fact]
    public void SecaoVaziaNaoEEscrita()
    {
        // Regra da skill: "seção vazia treina o leitor a ignorar".
        var ata = new AtaGerada
        {
            Resumo = "houve avanço",
            Secoes = [new SecaoDaAta { Titulo = "Status por frente", Texto = "   " }],
            Riscos = [],
        };

        string md = RedatorDeAta.Escrever(ata, Tipo(), Contexto());

        Assert.DoesNotContain("Status por frente", md);
        Assert.DoesNotContain("Riscos", md);
        Assert.Contains("houve avanço", md);
    }

    [Fact]
    public void FalanteGenericoNaoEntraNosParticipantes()
    {
        string md = RedatorDeAta.Escrever(new AtaGerada { Resumo = "x" }, Tipo(), Contexto());

        Assert.Contains("Vanessa Levorato", md);
        Assert.DoesNotContain("Speaker 3", md);
    }

    [Fact]
    public void OCabecalhoTrazDataDuracaoECliente()
    {
        string md = RedatorDeAta.Escrever(new AtaGerada { Resumo = "x" }, Tipo(), Contexto());

        Assert.Contains("13/08/2026", md);
        Assert.Contains("29min", md);
        Assert.Contains("**Cliente:** Vivo", md);
    }

    [Fact]
    public void AcaoSemDonoSaiComOMarcador()
    {
        var ata = new AtaGerada
        {
            Acoes = [new AcaoDaAta { Acao = "Investigar", Responsavel = "", Prazo = "" }],
        };

        string md = RedatorDeAta.Escrever(ata, Tipo(), Contexto());

        Assert.Contains("- [ ] Investigar — **[responsável a definir]** — [prazo a definir]", md);
    }
}

/// <summary>O dimensionamento de contexto, que é o que faz caber na placa.</summary>
public sealed class MotorDeAtaTests
{
    [Theory]
    [InlineData(20, 16384, "q8_0")]
    [InlineData(45, 16384, "q8_0")]
    [InlineData(60, 24576, "q8_0")]
    [InlineData(90, 32768, "q8_0")]
    [InlineData(122, 49152, "q4_0")]
    public void ADuracaoEscolheOContextoEOKv(double minutos, int contexto, string kv)
    {
        // A escada saiu da medição na RTX 2060: o KV custa ~62 KiB por token em
        // q8_0, e é ele que decide se cabe — não o tamanho do modelo.
        var (c, k) = MotorDeAta.Dimensionar(minutos * 60);

        Assert.Equal(contexto, c);
        Assert.Equal(kv, k);
    }

    [Fact]
    public void OEsquemaEUmJsonSchemaValido()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(AtaGerada.Esquema);
        var raiz = doc.RootElement;

        Assert.Equal("object", raiz.GetProperty("type").GetString());
        var props = raiz.GetProperty("properties");
        foreach (string campo in new[] { "resumo", "secoes", "decisoes", "acoes",
                                         "pontos_em_aberto", "riscos", "observacoes" })
            Assert.True(props.TryGetProperty(campo, out _), $"faltou {campo}");

        // O enum de lado é o que permite separar as pendências no redator.
        var lado = props.GetProperty("acoes").GetProperty("items")
            .GetProperty("properties").GetProperty("lado").GetProperty("enum");
        Assert.Equal(2, lado.GetArrayLength());
    }

    [Fact]
    public void OJsonDoModeloVoltaEmObjeto()
    {
        var ata = AtaGerada.DeJson("""
        {"resumo":"r","secoes":[{"titulo":"Status","texto":"t"}],
         "decisoes":["d"],"acoes":[{"acao":"a","responsavel":"x","prazo":"hoje","lado":"cliente"}],
         "pontos_em_aberto":[],"riscos":[],"observacoes":[]}
        """);

        Assert.NotNull(ata);
        Assert.Equal("r", ata!.Resumo);
        Assert.Equal("cliente", ata.Acoes[0].Lado);
        Assert.Equal("Status", ata.Secoes[0].Titulo);
    }
}

/// <summary>
/// Os três defeitos que só a primeira geração de ponta a ponta mostrou.
/// </summary>
/// <remarks>
/// Nenhum deles aparece em teste de unidade escrito por dedução: são coisas que
/// o modelo faz e que a gente só descobre lendo a ata que saiu.
/// </remarks>
public sealed class AjustesDaPrimeiraGeracaoTests
{
    private static ModeloDeAta Tipo() => ModelosDeAta.Buscar("cliente-update")!;
    private static ContextoDaReuniao Ctx() => new() { Titulo = "Reunião", DuracaoS = 600 };

    [Fact]
    public void SecaoQueDuplicaCampoProprioNaoEEscritaDuasVezes()
    {
        // O modelo preencheu "Decisões" nos dois lugares, e a ata saía com a
        // seção repetida do meio para o fim.
        var ata = new AtaGerada
        {
            Resumo = "r",
            Secoes = [
                new SecaoDaAta { Titulo = "Status por frente", Texto = "andando" },
                new SecaoDaAta { Titulo = "Decisões", Texto = "- decidimos X" },
                new SecaoDaAta { Titulo = "Pontos em aberto", Texto = "- falta Y" },
            ],
            Decisoes = ["decidimos X"],
            PontosEmAberto = ["falta Y"],
        };

        string md = RedatorDeAta.Escrever(ata, Tipo(), Ctx());

        Assert.Equal(1, Contar(md, "## Decisões"));
        Assert.Equal(1, Contar(md, "## Pontos em aberto"));
        Assert.Contains("## Status por frente", md);
    }

    [Fact]
    public void SecaoDeRiscoVaziaNaoVoltaPelaPortaDosFundos()
    {
        // "Nenhum risco relevante foi levantado" escrito como seção: a skill
        // manda omitir a seção, e omitir significa não escrever nada.
        var ata = new AtaGerada
        {
            Resumo = "r",
            Secoes = [new SecaoDaAta
            {
                Titulo = "Riscos e alertas", Texto = "Nenhum risco relevante foi levantado.",
            }],
            Riscos = [],
        };

        Assert.DoesNotContain("Riscos", RedatorDeAta.Escrever(ata, Tipo(), Ctx()));
    }

    [Fact]
    public void ORotuloRepetidoDentroDoValorSai()
    {
        var ata = new AtaGerada
        {
            Acoes = [new AcaoDaAta
            {
                Acao = "Mandar a base", Responsavel = "Responsável: Dimi Randel",
                Prazo = "prazo: amanhã", Lado = "nosso",
            }],
        };

        VerificadorDeAta.Conferir(ata, [new SegmentoFinal
        {
            Start = 0, End = 1, Text = "o Dimi manda a base amanhã",
        }], ["Dimi Randel"], []);

        Assert.Equal("Dimi Randel", ata.Acoes[0].Responsavel);
        Assert.Equal("amanhã", ata.Acoes[0].Prazo);
        Assert.Contains("— **Dimi Randel** — amanhã",
                        RedatorDeAta.Escrever(ata, Tipo(), Ctx()));
    }

    [Fact]
    public void OrganizacaoInventadaNoNomeDeQuemEDaCasaSai()
    {
        // "Andre Monlevade (Vivo)" quando Andre é da equipe: o nome fica, a
        // organização deduzida sai.
        var ata = new AtaGerada
        {
            Acoes = [new AcaoDaAta
            {
                Acao = "Enviar", Responsavel = "Andre Monlevade (Vivo)", Prazo = "hoje",
            }],
        };

        VerificadorDeAta.Conferir(ata, [new SegmentoFinal
        {
            Start = 0, End = 1, Text = "o André envia hoje",
        }], ["Andre Monlevade"], []);

        Assert.Equal("Andre Monlevade", ata.Acoes[0].Responsavel);
    }

    private static int Contar(string texto, string agulha)
    {
        int n = 0, i = 0;
        while ((i = texto.IndexOf(agulha, i, StringComparison.Ordinal)) >= 0) { n++; i += agulha.Length; }
        return n;
    }
}
