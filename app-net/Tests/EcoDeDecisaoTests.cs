using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A rede que separa decisão dita de decisão inventada.
/// </summary>
/// <remarks>
/// <para>
/// Ela existe porque promover hipótese a decisão é, nas palavras da própria
/// skill, "o erro mais frequente em ata automática". E ela é grosseira de
/// propósito: exige que as palavras de conteúdo da decisão apareçam na conversa.
/// </para>
/// <para>
/// <b>Os três primeiros testes são falsos positivos medidos</b>, em três atas
/// reais, entre 20 e 25/08/2026. Os dois últimos são o controle negativo — sem
/// eles, afrouxar a régua para consertar os três a transformaria em peneira, e
/// uma peneira aqui deixa passar exatamente o que ela existe para pegar.
/// </para>
/// </remarks>
public sealed class EcoDeDecisaoTests
{
    private static AtaGerada Conferir(string decisao, string transcricao)
    {
        var ata = new AtaGerada { Decisoes = [decisao] };
        var segmentos = transcricao
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select((t, i) => new SegmentoFinal
            {
                Start = i * 5, End = i * 5 + 4, Text = t, Speaker = "Speaker 1",
            })
            .ToList();

        return VerificadorDeAta.Conferir(ata, segmentos, ["Speaker 1"], []);
    }

    // ─────────────────────────────────────────────── os falsos positivos

    [Fact]
    public void ProrrataCasada()
    {
        // 2026-08-20_09-59-49. A fala diz "casado" e "prorrata"; a ata escreveu
        // "casada" e "prorratear". Nenhuma palavra casava inteira.
        var ata = Conferir(
            "A prorrata de desconto e a prorrata de mensalidade devem ser tratadas de "
            + "forma consistente (casada) no sistema core; não é possível prorratear "
            + "um e não o outro.",
            "Não existe como fazer o prorrata da mensalidade e não fazer o prorrata "
            + "do desconto. | Tem que ser casado. | Então, ou eu tenho uma regra que "
            + "não prorrateia, ou eu tenho uma regra que prorrateia. | independente "
            + "se é core ou não, se é core, tá errado. | O sistema aplica até zerar, "
            + "né, a faturamento ciclo.");

        Assert.Single(ata.Decisoes);
        Assert.Empty(ata.PontosEmAberto);
    }

    [Fact]
    public void MigrarParaOTeams()
    {
        // 2026-08-21_11-00-33. A migração é o assunto mais falado da reunião.
        var ata = Conferir(
            "Migrar a reunião para o Microsoft Teams, utilizando o convite enviado "
            + "pelo Roger Medeiros.",
            "O pessoal da Algar tem problema para acessar o Google Meet. | eu acho "
            + "que as outras que a gente tem com ele é todo o link que eles mandam "
            + "do Teams, né? | a nossa plataforma aqui bloqueia o Meet | a gente "
            + "depois das próximas reuniões, o Edu combina com o Roger | de repente "
            + "o Roger solta o convite aí pra gente que funciona mais fácil | eu tô "
            + "conversando aqui com o Antônio, ele tá criando um link no Teams pra "
            + "ele poder entrar e a gente, aí todo mundo migra pra lá");

        // Passa raspando, com 0,56 — e contra a transcrição inteira da gravação
        // real dá o mesmo número. Quatro das nove palavras não são ditas mesmo:
        // "Microsoft", "utilizando", "enviado", e o sobrenome, que está no
        // rótulo do falante e não na fala. É o comportamento certo — a decisão é
        // real, e a régua continua exigindo mais da metade.
        Assert.Single(ata.Decisoes);
        Assert.Empty(ata.PontosEmAberto);
    }

    [Fact]
    public void OsQuinzePorCento()
    {
        // 2026-08-20_14-00-06, gerada de ponta a ponta em 25/08. Os 15% são o
        // assunto central; a ata escreveu "diferenças" e "descontos" no plural.
        var ata = Conferir(
            "Considerar diferenças até 15% entre descontos do Siebel e do Kina como "
            + "iguais.",
            "eu considerei aqui uma diferença mais ou menos até uns 15% de diferença "
            + "| então o Quina ainda continua sendo maior do que o Siebel | a gente "
            + "encontrou 86% que o desconto do Siebel está igual ao Kina");

        Assert.Single(ata.Decisoes);
        Assert.Empty(ata.PontosEmAberto);
    }

    // ─────────────────────────────────────────────── o controle negativo

    [Fact]
    public void DecisaoDeOutraReuniaoContinuaSendoRejeitada()
    {
        // A decisão real da reunião de faturamento, contra a transcrição da
        // reunião do agente de atendimento. Se esta passar, a régua virou
        // peneira e não separa mais nada.
        var ata = Conferir(
            "A prorrata de desconto e a prorrata de mensalidade devem ser tratadas de "
            + "forma consistente no sistema core, com impacto no faturamento ciclo.",
            "O pessoal da Algar tem problema para acessar o Google Meet. | de repente "
            + "o Roger solta o convite aí pra gente que funciona mais fácil | nós "
            + "tivemos uma atualização de data da Redir, ela foi pro dia primeiro de "
            + "setembro | vamos simular ponto um, ambiente do cliente");

        Assert.Empty(ata.Decisoes);
        Assert.Single(ata.PontosEmAberto);
    }

    [Fact]
    public void PrefixoCurtoNaoBastaParaEcoar()
    {
        // "conta" e "contrato" compartilham quatro letras, e nada mais. Sem o
        // corte de 60% da palavra mais longa, uma decisão inventada passaria em
        // qualquer transcrição que falasse do mesmo assunto.
        var ata = Conferir(
            "Revisar o contrato comercial antes da renovação anual.",
            "a conta do cliente ficou zerada | o controle disso é manual | "
            + "a gente revisa o processo depois");

        Assert.Empty(ata.Decisoes);
    }

    [Fact]
    public void DecisaoFaladaComAsMesmasPalavrasContinuaPassando()
    {
        // A regressão que importa do outro lado: o caso fácil não pode quebrar.
        var ata = Conferir(
            "Rodar o estudo do mês de julho separadamente para B2B e B2C.",
            "vamos rodar o estudo do mês de julho | separadamente para B2B e para "
            + "B2C, com a classificação da Ellen");

        Assert.Single(ata.Decisoes);
    }
}
