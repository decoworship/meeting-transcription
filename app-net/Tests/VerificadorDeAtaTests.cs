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
    /// <summary>O Qwen3-4B como ele é no disco, lido do GGUF em 15/08/2026.</summary>
    private static MetadadosDoGguf Qwen3_4B => new()
    {
        Arquitetura = "qwen3",
        Nome = "Qwen3-4B-Instruct-2507",
        ContextoMaximo = 262144,
        Camadas = 36,
        CabecasDeKv = 8,
        DimensaoDaChave = 128,
        DimensaoDoValor = 128,
        BytesDoArquivo = 2_497_281_120,
    };

    private const long Rtx2060 = 6L * 1024 * 1024 * 1024;

    [Fact]
    public void AReuniaoQueFalhouEmCampoAgoraCabe()
    {
        // 14/08/2026: sessão de trabalho de 39 min, 19.935 tokens de prompt. A
        // escada antiga dava 16k para qualquer coisa até 45 min, e o
        // llama-server recusou com exceed_context_size_error depois de o modelo
        // já ter carregado. Este é o caso, com os números reais.
        int caracteres = 56_000;   // o prompt inteiro daquela reunião

        var (contexto, ctk, ctv) = MotorDeAta.Dimensionar(caracteres, Qwen3_4B, Rtx2060);

        Assert.True(contexto >= 19_935 + 3072,
                    $"contexto {contexto} não cobre os 19.935 tokens medidos mais a saída");
        Assert.Equal("q8_0", ctk);
    }

    [Fact]
    public void UmaHoraDeConversaDensaCabeNaPlacaDe6Gb()
    {
        // 508 tokens por minuto foi a densidade medida naquela reunião. Uma hora
        // nesse ritmo são ~30.500 tokens, e é o caso que o dono do produto pediu
        // para garantir.
        int tokens = 508 * 60;
        int caracteres = (int)(tokens * MotorDeAta.CaracteresPorToken);

        var (contexto, ctk, ctv) = MotorDeAta.Dimensionar(caracteres, Qwen3_4B, Rtx2060);

        Assert.True(contexto >= tokens + 3072, $"contexto {contexto} é pequeno demais");
        // Cabe, mas cedendo no cache: em q8_0 nos dois o KV de 32k já passa dos
        // 2,4 GB que sobram depois do modelo.
        Assert.Contains("q", ctk);
        Assert.Contains("q", ctv);
    }

    [Fact]
    public void OCacheCedePrimeiroNoValorEDepoisNaChave()
    {
        // A chave é a última a ceder porque é ela que decide onde o modelo
        // presta atenção. A ordem importa e é fácil de inverter sem querer.
        //
        // Numa RTX 2060, 32k em q8_0/q8_0 já não cabem: sobram ~2,9 GB depois do
        // modelo e da folga, e 32.768 x 76,5 KiB são 2,5 GB — passa quando se
        // soma o degrau seguinte. Daí os dois pontos escolhidos.
        var curto = MotorDeAta.Dimensionar(20_000, Qwen3_4B, Rtx2060);
        var longo = MotorDeAta.Dimensionar(100_000, Qwen3_4B, Rtx2060);

        Assert.Equal(("q8_0", "q8_0"), (curto.Ctk, curto.Ctv));
        Assert.True(longo.Contexto > curto.Contexto);
        Assert.Equal("q4_0", longo.Ctv);
        // A chave resistiu: só o valor cedeu.
        Assert.Equal("q8_0", longo.Ctk);
    }

    [Fact]
    public void OQueNaoCabeERecusadoAntesDeCarregarOModelo()
    {
        // 200 mil caracteres são ~83 mil tokens: cabem no modelo, não cabem numa
        // placa de 6 GB. O que se protege aqui é o MOMENTO da recusa: agora, com
        // números, e não depois de subir 2,5 GB e receber um JSON do servidor.
        var erro = Assert.Throws<InvalidOperationException>(
            () => MotorDeAta.Dimensionar(200_000, Qwen3_4B, Rtx2060));

        Assert.Contains("não cabe nesta placa", erro.Message);
        Assert.Contains("tokens", erro.Message);
        // Uma saída, e não só um lamento.
        Assert.Contains("Modelos", erro.Message);
    }

    [Fact]
    public void SemSaberAVramNaoSeInventaLimite()
    {
        // Numa máquina onde o nvidia-smi não responde, recusar por uma conta
        // chutada seria pior que deixar o llama.cpp reclamar. O mesmo prompt que
        // uma RTX 2060 recusa passa aqui, porque não há placa conhecida para
        // dizer que não.
        int caracteres = 200_000;

        Assert.Throws<InvalidOperationException>(
            () => MotorDeAta.Dimensionar(caracteres, Qwen3_4B, Rtx2060));

        var (contexto, _, _) = MotorDeAta.Dimensionar(caracteres, Qwen3_4B, vramBytes: 0);
        Assert.True(contexto >= 83_072, $"contexto {contexto} foi limitado sem saber a placa");
    }

    [Fact]
    public void OContextoNuncaPassaDoQueOModeloSuporta()
    {
        // Pedir mais que o treinado não dá erro: dá saída ruim, em silêncio.
        //
        // 16k é o menor teto que ainda deixa o motor funcionar, e a conta diz por
        // quê: a reserva de saída sozinha são 8.192 tokens, então um modelo de
        // contexto 8k não escreveria ata nenhuma nem com transcrição vazia.
        var modelinho = Qwen3_4B with { ContextoMaximo = 16384 };

        var (contexto, _, _) = MotorDeAta.Dimensionar(10_000, modelinho, Rtx2060);
        Assert.True(contexto <= 16384, $"contexto {contexto} passa do máximo do modelo");

        // E quando nem assim cabe, a recusa culpa o modelo — e não a placa, que
        // mandaria a pessoa comprar memória que não resolveria.
        var erro = Assert.Throws<InvalidOperationException>(
            () => MotorDeAta.Dimensionar(40_000, modelinho, Rtx2060));

        Assert.Contains("o modelo", erro.Message);
        Assert.Contains("16.384", erro.Message.Replace(",", "."));
    }

    [Fact]
    public void OCacheDeQ8CustaMaisQueODeQ4()
    {
        // A conta que decide tudo: 36 camadas x 8 cabeças x 128, para K e para V.
        long q8 = Qwen3_4B.BytesDeCachePorToken("q8_0", "q8_0");
        long q4 = Qwen3_4B.BytesDeCachePorToken("q4_0", "q4_0");

        Assert.InRange(q8 / 1024.0, 74, 80);   // ~76,5 KiB por token
        Assert.InRange(q4 / 1024.0, 38, 43);   // ~40,5 KiB por token

        // A escala do bloco quantizado conta: q8_0 são 34 bytes por 32 valores,
        // e ignorá-la subestimaria o cache em 6% — o bastante para a placa
        // encher no fim de uma reunião longa.
        Assert.True(q8 > Qwen3_4B.Camadas * Qwen3_4B.CabecasDeKv * 128L * 2);
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

/// <summary>Os dois ajustes que a segunda ata real mostrou.</summary>
public sealed class AjustesDaSegundaGeracaoTests
{
    [Fact]
    public void PrazoADefinirGanhaOsColchetes()
    {
        // Os colchetes marcam a lacuna como lacuna: sem eles, "prazo a definir"
        // se lê como se fosse um prazo chamado assim.
        var ata = new AtaGerada
        {
            Acoes = [new AcaoDaAta
            {
                Acao = "Validar", Responsavel = "Ana", Prazo = "prazo a definir",
            }],
        };

        VerificadorDeAta.Conferir(ata, [new SegmentoFinal
        {
            Start = 0, End = 1, Text = "a Ana valida isso",
        }], ["Ana"], []);

        Assert.Equal("[prazo a definir]", ata.Acoes[0].Prazo);
    }

    [Fact]
    public void PrazoDeVerdadeNaoGanhaColchete()
    {
        var ata = new AtaGerada
        {
            Acoes = [new AcaoDaAta { Acao = "X", Responsavel = "Ana", Prazo = "amanhã" }],
        };

        VerificadorDeAta.Conferir(ata, [new SegmentoFinal
        {
            Start = 0, End = 1, Text = "a Ana faz amanhã",
        }], ["Ana"], []);

        Assert.Equal("amanhã", ata.Acoes[0].Prazo);
    }

    [Fact]
    public void CompromissoNaoEntraNaContaDeOmissao()
    {
        // A chave de um compromisso é a palavra do prazo. Cobrar que a ata
        // repita a palavra "hoje" é cobrar coisa nenhuma — e foi o que apareceu
        // na lista de "números que não entraram" da segunda ata real.
        var segmentos = new[]
        {
            new SegmentoFinal { Start = 0, End = 1, Text = "eu te mando a base hoje" },
        };
        var roteiro = RoteiroDeFatos.De(segmentos);

        Assert.Single(roteiro);
        Assert.Equal("compromisso", roteiro[0].Tipo);
        Assert.Empty(RoteiroDeFatos.NaoIncorporados(roteiro, "# Ata\n\nnada aqui"));
    }
}
