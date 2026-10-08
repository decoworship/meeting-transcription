using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;

namespace MeetingApp.Tests;

/// <summary>
/// Uma reunião inventada com a forma da de 22/09/2026 (ATA-5).
/// </summary>
/// <remarks>
/// Temas, itens de todos os tipos, ações dos dois lados, um dono repetido no
/// texto e prazos vazios — o que o formato por tema mexe. Nenhum nome ou dado
/// de cliente de verdade: o repositório publica releases.
/// </remarks>
internal static class ReuniaoDeExemplo
{
    public static ContextoDaReuniao Contexto() => new()
    {
        Titulo = "Semanal do Portal de Pedidos",
        Cliente = "Acme Bebidas",
        Projeto = "Portal de Pedidos",
        Data = "2026-09-22T10:30:00-03:00",
        DuracaoS = 1910,
        Convidados = ["Bruno Teles", "Diego Prado", "Carla Nunes"],
        Pessoas =
        [
            new Pessoa("Bruno Teles", "casa.com", true),
            new Pessoa("Diego Prado", "casa.com", true),
            new Pessoa("Carla Nunes", "acme.com", false),
        ],
        Falantes = ["Bruno Teles", "Carla Nunes"],
        Notas = "Ranking por canal - Comercial\nRegra de ruptura - Trade",
    };

    public static List<SegmentoFinal> Segmentos() =>
    [
        new SegmentoFinal { Start = 0, End = 6, Speaker = "Bruno Teles",
            Text = " Vamos começar o dashboard antes da integração com a Diana." },
        new SegmentoFinal { Start = 6, End = 12, Speaker = "Carla Nunes",
            Text = " O pedido mínimo agora soma o aplicativo e o SFA, e eu levanto o valor novo por canal." },
        new SegmentoFinal { Start = 12, End = 18, Speaker = "Carla Nunes",
            Text = " As regras de ruptura da Hiper eu busco até sexta." },
        new SegmentoFinal { Start = 18, End = 24, Speaker = "Bruno Teles",
            Text = " Perdemos duas semanas de sugestão em julho, 37% de conversão." },
    ];

    /// <summary>A ata como o modelo devolve hoje: itens sem seção.</summary>
    public static AtaGerada AtaSemSecao() => new()
    {
        Resumo = "Acompanhamento do Portal de Pedidos. O dashboard começa antes da integração.",
        Secoes =
        [
            new SecaoDaAta { Titulo = "Status por frente",
                Texto = "### Dashboard\n**Situação:** Em andamento\nO filtro já foi ajustado." },
            new SecaoDaAta { Titulo = "Próxima reunião", Texto = "A definir." },
        ],
        Decisoes = ["Começar o dashboard antes da integração com a Diana."],
        Acoes =
        [
            new AcaoDaAta { Acao = "Começar o dashboard", Responsavel = "Bruno Teles",
                            Prazo = "[prazo a definir]", Lado = "nosso" },
            new AcaoDaAta { Acao = "Carla Nunes buscar as regras de ruptura da Hiper",
                            Responsavel = "Carla Nunes", Prazo = "sexta", Lado = "cliente" },
        ],
        PontosEmAberto = ["Como implementar a regra que soma o aplicativo e o SFA."],
        Riscos = ["Duas semanas de sugestão perdidas em julho."],
        Observacoes = ["Números citados na reunião que não aparecem nesta ata: 37."],
    };

    /// <summary>A mesma reunião, como o modelo devolve no formato por tema.</summary>
    public static AtaGerada AtaComSecao() => new()
    {
        Resumo = "Acompanhamento do Portal de Pedidos.",
        Secoes =
        [
            new SecaoDaAta { Titulo = "Dashboard", Texto = "O filtro já foi ajustado." },
            new SecaoDaAta { Titulo = "Pedido mínimo", Texto = "A regra nova soma o aplicativo e o SFA.",
                             Grupo = "Portal: próximos passos", Responsavel = "Comercial" },
            new SecaoDaAta { Titulo = "Regra de ruptura", Texto = "",
                             Grupo = "Portal: próximos passos", Responsavel = "Trade" },
            new SecaoDaAta { Titulo = "Sem itens nem texto", Texto = "" },
            new SecaoDaAta { Titulo = "Próxima reunião", Texto = "A definir." },
        ],
        Decisoes =
        [
            new ItemDaAta("Começar antes da integração com a Diana.", "Dashboard"),
            new ItemDaAta("Estudar critérios parecidos com os da Hiper.", "regra de RUPTURA "),
        ],
        Acoes =
        [
            new AcaoDaAta { Acao = "Dashboard e relatório de aderência", Responsavel = "Bruno Teles",
                            Prazo = "[prazo a definir]", Lado = "nosso", Secao = "Dashboard" },
            new AcaoDaAta { Acao = "Novo valor do pedido mínimo por canal", Responsavel = "Carla Nunes",
                            Prazo = "[prazo a definir]", Lado = "cliente", Secao = "Pedido mínimo" },
            new AcaoDaAta { Acao = "Carla Nunes levantar as regras da Hiper", Responsavel = "Carla Nunes",
                            Prazo = "sexta", Lado = "cliente", Secao = "Regra de ruptura" },
            new AcaoDaAta { Acao = "Mandar a pauta", Responsavel = "Diego Prado",
                            Prazo = "[prazo a definir]", Lado = "nosso", Secao = "Tema que não existe" },
        ],
        PontosEmAberto = [new ItemDaAta("Como implementar a soma.", "Pedido mínimo")],
        Riscos =
        [
            new ItemDaAta("O planner atualiza às quartas.", "Pedido mínimo"),
            new ItemDaAta("Duas semanas de sugestão perdidas em julho."),
        ],
        Observacoes = ["Números citados na reunião que não aparecem nesta ata: 37."],
    };
}
