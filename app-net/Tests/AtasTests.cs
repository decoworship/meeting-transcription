using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Os tipos de ata: os embutidos, os do usuário, e o recorte da skill.
/// </summary>
/// <remarks>
/// O teste do recorte é o mais importante do arquivo: se alguém reescrever o
/// SKILL.md e o título que o recorte procura sumir, o app mandaria ao modelo um
/// prompt <b>sem as regras que impedem ata errada</b> — e nada quebraria. Aqui
/// quebra.
/// </remarks>
public sealed class ModelosDeAtaTests
{
    [Fact]
    public void OsSeisTiposDaSkillEstaoEmbutidos()
    {
        var todos = ModelosDeAta.Todos();
        var ids = todos.Select(m => m.Id).ToArray();

        Assert.Contains("cliente-update", ids);
        Assert.Contains("sprint", ids);
        Assert.Contains("trabalho", ids);
        Assert.Contains("kickoff", ids);
        Assert.Contains("resultados", ids);
        Assert.Contains("daily", ids);
        Assert.All(todos, m => Assert.False(string.IsNullOrWhiteSpace(m.Texto)));
    }

    [Fact]
    public void ORecorteDoSkillPegaAsRegrasEDeixaOPasso1DeFora()
    {
        string regras = ModelosDeAta.RegrasComuns();

        // O que precisa estar: as regras que impedem ata errada.
        Assert.Contains("Separar decidido de discutido", regras);
        Assert.Contains("Action items exigem dono", regras);
        Assert.Contains("Fidelidade", regras);

        // O que precisa sumir: o Passo 1 manda classificar e perguntar ao
        // usuário, e quem classifica é a tela (ATA.md §1).
        Assert.DoesNotContain("Passo 1", regras);
        Assert.DoesNotContain("pergunte ao usuário antes de escrever", regras);

        // E o Passo 3 e o Passo 4, que são de chat e não deste motor: o 3 manda
        // escrever o cabeçalho da ata, o 4 manda entregar Markdown na conversa e
        // oferecer .docx. Mandá-los fazia o modelo escrever uma ata inteira em
        // Markdown DENTRO de uma seção do JSON — medido nos dois modelos em
        // 17/08/2026, e é o defeito que produzia a seção de 2.500 caracteres.
        Assert.DoesNotContain("Passo 3", regras);
        Assert.DoesNotContain("Passo 4", regras);
        Assert.DoesNotContain("Toda ata começa assim", regras);
        Assert.DoesNotContain("direto na conversa", regras);
    }

    [Fact]
    public void OContratoDoJsonDizOQueOAppJaEscreve()
    {
        // Estas regras não moram no SKILL.md de propósito: ele é fonte única e a
        // mesma cópia serve à skill de chat, onde nada disto faz sentido.
        string contrato = PromptDeAta.ContratoDoJson;

        // O defeito da ata duplicada.
        Assert.Contains("nunca repita", contrato, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Resumo", contrato);

        // O viés de dar todas as ações a quem mais falou.
        Assert.Contains("Quem fala mais não é dono de tudo", contrato);

        // A decisão que mais custa quando some: a restrição.
        Assert.Contains("Restrição também é decisão", contrato);
    }

    [Fact]
    public void OPromptMontadoNaoMandaEscreverCabecalho()
    {
        // A régua de ponta a ponta: o que de fato chega ao modelo. As duas
        // instruções abaixo estavam no prompt e disputavam com o esquema JSON.
        var tipo = ModelosDeAta.Buscar("trabalho")!;
        string prompt = PromptDeAta.Montar(tipo, new ContextoDaReuniao(), [], []);

        Assert.DoesNotContain("Toda ata começa assim", prompt);
        Assert.DoesNotContain("Saída em Markdown, direto na conversa", prompt);

        // E o modelo precisa saber que o esqueleto em Markdown é catálogo de
        // seções, não formato de resposta.
        Assert.Contains("Ele não é o formato da sua resposta", prompt);
    }

    [Fact]
    public void OEsqueletoDaReferenciaVirouListaDeSecoes()
    {
        var modelo = ModelosDeAta.Buscar("cliente-update");

        Assert.NotNull(modelo);
        var secoes = modelo!.Secoes;
        Assert.Contains("Resumo", secoes);
        Assert.Contains("Decisões", secoes);
        Assert.Contains("Pendências", secoes);
        // A ordem é a do esqueleto, e é ela que a ata vai seguir.
        Assert.True(secoes.ToList().IndexOf("Resumo") < secoes.ToList().IndexOf("Decisões"));
    }

    [Fact]
    public void ODailyTemMenosSecoesQueOUpdateComCliente()
    {
        // Não é curiosidade: é a régua de que cada tipo tem estrutura própria, e
        // de que o esqueleto está mesmo sendo lido de cada arquivo.
        var daily = ModelosDeAta.Buscar("daily")!;
        var cliente = ModelosDeAta.Buscar("cliente-update")!;

        Assert.True(daily.Secoes.Count < cliente.Secoes.Count,
            $"daily tem {daily.Secoes.Count}, cliente-update tem {cliente.Secoes.Count}");
    }

    [Fact]
    public void TipoDesconhecidoNaoExiste()
    {
        Assert.Null(ModelosDeAta.Buscar("reuniao-inventada"));
        Assert.Null(ModelosDeAta.Buscar(""));
        Assert.Null(ModelosDeAta.Buscar(null));
    }
}

/// <summary>
/// O roteiro de fatos, que é a rede contra omissão.
/// </summary>
/// <remarks>
/// Existe por medição: o modelo local não inventou nada e deixou de fora metade
/// dos números da reunião, incluindo o impacto financeiro (ATA.md §8).
/// </remarks>
public sealed class RoteiroDeFatosTests
{
    private static SegmentoFinal Fala(string texto, double t = 0) =>
        new() { Start = t, End = t + 5, Text = texto };

    [Fact]
    public void PegaOsNumerosQueImportamEIgnoraOsPequenos()
    {
        var fatos = RoteiroDeFatos.De([
            Fala("a gente chegou em 27.529 contas"),
            Fala("são 2 casos aqui"),
            Fala("dá R$ 180 mil por mês"),
            Fala("precisa faturar 95% dos casos"),
        ]);

        var chaves = fatos.Select(f => f.Chave).ToArray();
        Assert.Contains(chaves, c => c.Contains("27.529"));
        Assert.Contains(chaves, c => c.Contains("180"));
        Assert.Contains(chaves, c => c.Contains("95"));
        // "2 casos" não entra: número de um dígito enche a lista sem informar.
        Assert.DoesNotContain(chaves, c => c.Trim() == "2");
    }

    [Fact]
    public void OTrechoEmVoltaVemJunto()
    {
        // Sem contexto o número é inútil: "129 mil" o quê?
        var fatos = RoteiroDeFatos.De([Fala("são 129 mil registros zerados na base")]);

        Assert.Single(fatos);
        Assert.Contains("registros zerados", fatos[0].Trecho);
    }

    [Fact]
    public void NaoRepeteOMesmoNumero()
    {
        var fatos = RoteiroDeFatos.De([
            Fala("os 27.529 casos"), Fala("aqueles 27.529 de novo"),
        ]);

        Assert.Single(fatos);
    }

    [Fact]
    public void CompromissoComPrazoViraFato()
    {
        var fatos = RoteiroDeFatos.De([
            Fala("eu te mando a base amanhã"),
            Fala("eu te mando a base"),          // sem prazo: é conversa
        ]);

        Assert.Single(fatos);
        Assert.Contains("amanhã", fatos[0].Chave, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OPromptSaiVazioQuandoNaoHaFato()
    {
        Assert.Equal("", RoteiroDeFatos.ParaPrompt([]));
    }

    [Fact]
    public void OPromptDizParaIgnorarOQueNaoServe()
    {
        // O roteiro é índice, não pauta: sem esta instrução o modelo tenta
        // encaixar todo número numa seção.
        string p = RoteiroDeFatos.ParaPrompt(RoteiroDeFatos.De([Fala("são 27.529 contas")]));

        Assert.Contains("ignore o resto", p);
        Assert.Contains("27.529", p);
    }

    [Fact]
    public void OQueNaoEntrouNaAtaEListado()
    {
        var roteiro = RoteiroDeFatos.De([
            Fala("são 27.529 contas"), Fala("e dá R$ 180 mil por mês"),
        ]);

        var faltando = RoteiroDeFatos.NaoIncorporados(
            roteiro, "# Ata\n\nForam 27.529 contas no universo.");

        Assert.Contains(faltando, f => f.Contains("180"));
        Assert.DoesNotContain(faltando, f => f.Contains("27.529"));
    }

    [Fact]
    public void APontuacaoNaoFazONumeroParecerAusente()
    {
        // A transcrição diz "27.529" e a ata pode dizer "27529": não é omissão.
        var roteiro = RoteiroDeFatos.De([Fala("são 27.529 contas")]);

        Assert.Empty(RoteiroDeFatos.NaoIncorporados(roteiro, "foram 27529 contas"));
    }
}
