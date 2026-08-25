using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Os quatro defeitos que a comparação com o resumo do Notion expôs
/// (docs/FASE6.md §1.6), medidos sobre a reunião <c>2026-08-13_14-30-15</c>.
/// </summary>
/// <remarks>
/// Os três primeiros são de engenharia determinística e estão aqui. O quarto —
/// a omissão de assunto — só se fecha por inteiro com as duas passadas da §1.1;
/// o que dá para prender agora é o material que faltava chegar ao modelo.
/// </remarks>
public sealed class RoteiroEDonoTests
{
    private static SegmentoFinal Seg(string texto, string? quem = null, double t = 0) =>
        new() { Start = t, End = t + 5, Text = texto, Speaker = quem };

    // ── defeito 2: número certo com unidade errada ──────────────────────────

    [Fact]
    public void ONumeroCarregaOSubstantivoQueOAcompanha()
    {
        // "106 produtos com registros zerados (R$ 129 mil)" — os 129 mil eram
        // contagem de registros, e o modelo reconstruiu a unidade sozinho.
        var fatos = RoteiroDeFatos.De([Seg("são 129 mil registros zerados na base")]);

        var f = Assert.Single(fatos, x => x.Chave.Contains("129"));
        Assert.Equal("registros", f.Unidade);
    }

    [Fact]
    public void ADeQueVemAntesDoSubstantivoNaoAtrapalha()
    {
        var fatos = RoteiroDeFatos.De([Seg("a base tem 27.529 de produtos ativos")]);

        Assert.Equal("produtos", Assert.Single(fatos).Unidade);
    }

    [Fact]
    public void NumeroEmReaisNaoRecebeOutraUnidade()
    {
        // Ele já carrega a unidade no próprio texto; marcar o substantivo
        // seguinte produziria "R$ 180 mil (por)".
        var fatos = RoteiroDeFatos.De([Seg("dá R$ 180 mil por mês de impacto")]);

        Assert.Equal("", Assert.Single(fatos, x => x.Chave.Contains("180")).Unidade);
    }

    [Fact]
    public void SubstantivoLongeDoNumeroNaoViraUnidade()
    {
        // Se a palavra de conteúdo está longe, o número não está sendo medido
        // por ela — e uma unidade errada é pior que nenhuma.
        var fatos = RoteiroDeFatos.De([Seg("são 27.529 , e a gente conversou sobre migração")]);

        Assert.Equal("", Assert.Single(fatos).Unidade);
    }

    [Fact]
    public void AUnidadeChegaAoPrompt()
    {
        string prompt = RoteiroDeFatos.ParaPrompt(
            RoteiroDeFatos.De([Seg("são 129 mil registros zerados")]));

        Assert.Contains("*(registros)*", prompt);
        Assert.Contains("unidade dita na", prompt);
    }

    // ── defeito 3: material que não chegava ao modelo ───────────────────────

    [Fact]
    public void DependenciaExternaViraFatoDeRisco()
    {
        // A seção `riscos` saiu vazia numa reunião que tinha um: o TI do cliente
        // foi orientado a fazer o levantamento, com incidente já aberto.
        var fatos = RoteiroDeFatos.De(
            [Seg("o time de TI deles abriu um incidente com prioridade")]);

        Assert.Single(fatos, f => f.Tipo == "risco");
    }

    [Fact]
    public void OCompromissoDeApresentarEPego()
    {
        // Era o propósito da reunião, e a ata saiu com "a próxima reunião será
        // marcada para amanhã" — sem para quem nem para quê.
        var fatos = RoteiroDeFatos.De(
            [Seg("amanhã a gente apresenta isso para a Carla")]);

        Assert.Single(fatos, f => f.Tipo == "compromisso");
    }

    [Fact]
    public void ORiscoChegaMarcadoNoPrompt()
    {
        string prompt = RoteiroDeFatos.ParaPrompt(
            RoteiroDeFatos.De([Seg("isso depende do time de infra deles")]));

        Assert.Contains("**[risco]**", prompt);
    }

    // ── defeito 4: a seção que se contradizia ───────────────────────────────

    [Fact]
    public void AProsaDoModeloNaoSobreviveAConferencia()
    {
        // A ata afirmava "todos foram registrados com precisão" e listava, na
        // mesma seção, onze números que não apareciam nela. Uma medição e uma
        // opinião sobre a medição, sem como saber qual é a fonte.
        var ata = new AtaGerada
        {
            Resumo = "reunião sobre a base",
            Observacoes =
            [
                "Todos foram registrados com precisão conforme o contexto.",
                "O termo 'SVA' foi corrigido para 'suspensão temporária'.",
            ],
        };

        VerificadorDeAta.Conferir(ata, [Seg("conversa qualquer")], [], []);

        Assert.DoesNotContain(ata.Observacoes, o => o.Contains("precisão"));
        Assert.DoesNotContain(ata.Observacoes, o => o.Contains("SVA"));
    }

    [Fact]
    public void AConferenciaDeCoberturaContinuaSaindo()
    {
        // Tirar a prosa não pode ter levado a medição junto: é ela que declara
        // o que ficou de fora, e é a única coisa da seção que se pode auditar.
        var segmentos = new List<SegmentoFinal> { Seg("são 129 mil registros zerados") };
        var ata = new AtaGerada { Resumo = "não fala de número nenhum" };

        VerificadorDeAta.Conferir(ata, segmentos, [], RoteiroDeFatos.De(segmentos));

        Assert.Contains(ata.Observacoes, o => o.Contains("não aparecem nesta ata"));
    }

    // ── defeito 1: o lado da pendência ──────────────────────────────────────

    private static readonly List<Pessoa> Duas =
    [
        new("Andre Yuri", "beegol.com", true),
        new("Vanessa Levorato", "vivo.com.br", false),
    ];

    private static AtaGerada ComAcao(string acao, string lado) => new()
    {
        Acoes = [new AcaoDaAta
        {
            Acao = acao, Responsavel = "[responsável a definir]", Lado = lado,
        }],
    };

    [Fact]
    public void QuemSeComprometeNaFalaViraODonoEOLadoSegue()
    {
        // O caso medido: a entrega da base saiu com lado "cliente"; na reunião
        // quem se compromete é o André, do nosso lado.
        var ata = ComAcao("enviar o arquivo da base de registros", "cliente");
        var segmentos = new List<SegmentoFinal>
        {
            Seg("eu vou te mandar esse arquivo da base de registros, tá bom?", "Andre Yuri"),
        };

        var notas = DonoPelaFala.Atribuir(ata, segmentos, Duas);

        Assert.Equal("Andre Yuri", ata.Acoes[0].Responsavel);
        Assert.Equal("nosso", ata.Acoes[0].Lado);
        Assert.Single(notas);
    }

    [Fact]
    public void ACitacaoDaFalaVaiNaObservacao()
    {
        // A ata é auditável ou não é nada: quem lê precisa poder conferir de
        // onde saiu a atribuição.
        var ata = ComAcao("enviar o arquivo da base de registros", "cliente");
        var notas = DonoPelaFala.Atribuir(ata,
            [Seg("eu vou te mandar esse arquivo da base de registros", "Andre Yuri")], Duas);

        Assert.Contains("mandar esse arquivo", Assert.Single(notas));
    }

    [Fact]
    public void AcaoQueJaTemDonoNaoEMexida()
    {
        var ata = ComAcao("enviar o arquivo da base de registros", "cliente");
        ata.Acoes[0].Responsavel = "Vanessa Levorato";

        Assert.Empty(DonoPelaFala.Atribuir(ata,
            [Seg("eu vou te mandar esse arquivo da base de registros", "Andre Yuri")], Duas));
        Assert.Equal("Vanessa Levorato", ata.Acoes[0].Responsavel);
    }

    [Fact]
    public void SemEcoForteNinguemEAtribuido()
    {
        // Numa reunião de uma hora há dezenas de "eu te mando". Casar a ação com
        // o trecho errado troca uma pendência sem dono — que alguém lê e resolve
        // — por uma com o dono errado, que ninguém confere.
        var ata = ComAcao("revisar a apresentação do comitê de diretoria", "cliente");
        var notas = DonoPelaFala.Atribuir(ata,
            [Seg("eu vou te mandar o link da planilha", "Andre Yuri")], Duas);

        Assert.Empty(notas);
        Assert.Equal("[responsável a definir]", ata.Acoes[0].Responsavel);
    }

    [Fact]
    public void TerceiraPessoaNaoEhCompromissoDeQuemFalou()
    {
        // "ele vai mandar" dito pelo André não faz o André dono.
        var ata = ComAcao("enviar o arquivo da base de registros", "nosso");
        var notas = DonoPelaFala.Atribuir(ata,
            [Seg("ele disse que o pessoal deles cuida do arquivo da base de registros",
                 "Andre Yuri")], Duas);

        Assert.Empty(notas);
    }

    [Fact]
    public void FalanteNaoNomeadoNaoViraDono()
    {
        // "Speaker 3" não é ninguém, e inventar um nome aqui seria o mesmo erro
        // que o verificador desfaz.
        var ata = ComAcao("enviar o arquivo da base de registros", "cliente");
        var notas = DonoPelaFala.Atribuir(ata,
            [Seg("eu vou te mandar esse arquivo da base de registros", "Speaker 3")], Duas);

        Assert.Empty(notas);
        Assert.Equal("[responsável a definir]", ata.Acoes[0].Responsavel);
    }

    [Fact]
    public void SemPessoasComLadoNadaEAfirmado()
    {
        // Mesma regra do verificador: sem e-mail não se afirma lado.
        var ata = ComAcao("enviar o arquivo da base de registros", "cliente");

        Assert.Empty(DonoPelaFala.Atribuir(ata,
            [Seg("eu vou te mandar esse arquivo da base de registros", "Andre Yuri")], []));
        Assert.Equal("cliente", ata.Acoes[0].Lado);
    }

    [Fact]
    public void ODonoDoMicrofoneEDaCasa()
    {
        // A faixa do microfone é de quem gravou, e quem gravou é da casa.
        var ata = ComAcao("enviar o arquivo da base de registros", "cliente");
        var notas = DonoPelaFala.Atribuir(ata,
            [Seg("eu vou te mandar esse arquivo da base de registros", "You")], Duas);

        Assert.Single(notas);
        Assert.Equal("nosso", ata.Acoes[0].Lado);
    }
}
