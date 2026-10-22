using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace MeetingApp.Nucleo.Claude;

/// <summary>
/// O que o MCP oferece: ferramentas, recursos e prompts, todos sobre o acervo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Três peças, três usos</b> (docs/INTEGRACAO-CLAUDE.md §2). Os recursos são
/// para anexar uma reunião sem copiar — aparecem no menu "+" do Claude Desktop.
/// As ferramentas são para o Claude buscar sozinho o que uma tarefa de várias
/// reuniões pede. Os prompts são as tarefas que se repetem, com a instrução já
/// escrita.
/// </para>
/// <para>
/// <b>Tudo devolve texto, e não JSON.</b> Quem lê é um modelo, e uma linha
/// <c>[12:03] Carla Nunes: …</c> custa um terço dos tokens do segmento em JSON e
/// se lê sem esforço.
/// </para>
/// </remarks>
public sealed class FerramentasDoMcp(Acervo acervo, string? caminhoDaReuniaoAberta = null)
{
    /// <summary>O que o Claude lê ao conectar, antes de qualquer ferramenta.</summary>
    public const string Instrucoes =
        "O PulseMeet grava e transcreve as reuniões de quem usa este computador. Cada reunião "
        + "tem um id (o nome da pasta, como 2026-10-22_09-01-36), um cliente e um projeto, e pode "
        + "ter ata, transcrição e notas. A ata é o resumo e serve de índice; a transcrição é a "
        + "fonte de verdade, com o minuto e quem falou. Para tarefas sobre várias reuniões, ache "
        + "as reuniões com listar_reunioes e leia as atas com ler_ata; vá à transcrição (buscar, "
        + "ler_transcricao) para conferir um ponto ou citar o que foi dito. Quando a pessoa disser "
        + "\"esta reunião\" ou \"a reunião aberta\", use reuniao_aberta. Cite a data da reunião de "
        + "onde saiu cada coisa. As atas foram escritas por um modelo local e podem errar nomes e "
        + "números; a transcrição também pode errar nomes próprios.";

    private const string Esquema = "pulsemeet://";

    // ───────────────────────────────────────────────────────── ferramentas

    // O construtor, e não [ … ]: a expressão de coleção também cai no Add<T>
    // genérico (ver Recursos).
    public static JsonArray Definicoes() => new(
        Ferramenta("listar_clientes", "Clientes do PulseMeet",
            "Os clientes com reunião gravada, com os projetos de cada um, quantas reuniões e o "
            + "período. Comece por aqui quando não souber como o nome do cliente está escrito.",
            new JsonObject()),

        Ferramenta("listar_reunioes", "Reuniões do PulseMeet",
            "As reuniões gravadas, da mais antiga para a mais nova, com id, data, título, cliente, "
            + "projeto e se têm ata, transcrição e notas. Cliente e projeto casam por pedaço do "
            + "nome, sem acento.",
            new JsonObject
            {
                ["cliente"] = Campo("string", "Pedaço do nome do cliente."),
                ["projeto"] = Campo("string", "Pedaço do nome do projeto."),
                ["desde"] = Campo("string", "Primeiro dia, AAAA-MM-DD."),
                ["ate"] = Campo("string", "Último dia, AAAA-MM-DD."),
                ["limite"] = Campo("integer", "Quantas reuniões no máximo, as mais novas (padrão 200)."),
            }),

        Ferramenta("ler_ata", "Ler a ata",
            "A ata de uma reunião — resumo, decisões, ações com responsável e prazo, pontos em "
            + "aberto —, com as notas de quem gravou.",
            new JsonObject
            {
                ["reuniao"] = Campo("string", "O id da reunião, ou \"aberta\" para a aberta no PulseMeet."),
            }, "reuniao"),

        Ferramenta("ler_transcricao", "Ler a transcrição",
            "A transcrição de uma reunião, uma fala por linha: [minuto] Falante: texto. Use de e "
            + "ate para ler só um trecho.",
            new JsonObject
            {
                ["reuniao"] = Campo("string", "O id da reunião, ou \"aberta\" para a aberta no PulseMeet."),
                ["de"] = Campo("string", "Onde começar, mm:ss ou hh:mm:ss."),
                ["ate"] = Campo("string", "Onde parar, mm:ss ou hh:mm:ss."),
            }, "reuniao"),

        Ferramenta("buscar", "Buscar nas reuniões",
            "Procura palavras nas atas, nas notas e nas transcrições, sem acento e sem diferenciar "
            + "maiúscula. Acha a fala ou a linha que tem todas as palavras. Devolve da reunião "
            + "mais nova para a mais antiga, com o minuto e quem falou.",
            new JsonObject
            {
                ["termo"] = Campo("string", "As palavras a procurar."),
                ["cliente"] = Campo("string", "Só reuniões deste cliente (pedaço do nome)."),
                ["projeto"] = Campo("string", "Só reuniões deste projeto (pedaço do nome)."),
                ["desde"] = Campo("string", "Primeiro dia, AAAA-MM-DD."),
                ["ate"] = Campo("string", "Último dia, AAAA-MM-DD."),
                ["reuniao"] = Campo("string", "Só nesta reunião (id, ou \"aberta\")."),
                ["limite"] = Campo("integer", "Quantos achados no máximo (padrão 30)."),
            }, "termo"),

        Ferramenta("reuniao_aberta", "Reunião aberta no PulseMeet",
            "A última reunião aberta no app do PulseMeet, e quando ela foi aberta. É \"esta "
            + "reunião\" quando a pessoa não diz qual.",
            new JsonObject()));

    /// <summary>Executa uma ferramenta; erro de uso volta como texto, para o modelo corrigir.</summary>
    public JsonObject Chamar(string? nome, JsonObject args)
    {
        Func<JsonObject, string> fazer = nome switch
        {
            "listar_clientes" => _ => ListarClientes(),
            "listar_reunioes" => ListarReunioes,
            "ler_ata" => LerAta,
            "ler_transcricao" => LerTranscricao,
            "buscar" => Buscar,
            "reuniao_aberta" => _ => DescreverAberta(),
            _ => throw new ErroDoMcp(-32602, $"ferramenta desconhecida: {nome}"),
        };

        try
        {
            return Resultado(fazer(args), erro: false);
        }
        catch (ErroDeUso e)
        {
            return Resultado(e.Message, erro: true);
        }
    }

    private string ListarClientes()
    {
        var todas = acervo.Listar();
        var clientes = acervo.Clientes();
        int semCliente = todas.Count(r => r.Cliente is not { Length: > 0 });

        var sb = new StringBuilder();
        sb.AppendLine(clientes.Count == 1 ? "1 cliente com reunião gravada."
                                          : $"{clientes.Count} clientes com reunião gravada.");
        foreach (var c in clientes)
        {
            sb.AppendLine().Append("- ").Append(c.Nome).Append(" — ").Append(Reunioes(c.Reunioes))
              .Append(", de ").Append(c.Primeira.ToString("dd/MM/yyyy"))
              .Append(" a ").AppendLine(c.Ultima.ToString("dd/MM/yyyy"));
            if (c.Projetos.Count > 0)
                sb.Append("  projetos: ")
                  .AppendLine(string.Join(", ", c.Projetos.Select(p => $"{p.Projeto} ({p.Reunioes})")));
        }
        if (semCliente > 0)
            sb.AppendLine().AppendLine($"{Reunioes(semCliente)} sem cliente — aparecem em "
                                       + "listar_reunioes sem filtro.");
        return sb.ToString();
    }

    private string ListarReunioes(JsonObject args)
    {
        var filtro = Filtro(args);
        int limite = Inteiro(args, "limite", 200, 1000);
        var lista = acervo.Listar(filtro);
        string onde = DescreverFiltro(filtro);

        if (lista.Count == 0)
            return $"Nenhuma reunião{onde}. Use listar_clientes para ver os nomes como estão gravados.";

        var sb = new StringBuilder();
        if (lista.Count > limite)
        {
            sb.AppendLine($"{Reunioes(lista.Count)}{onde}; aqui estão as {limite} mais novas. "
                          + "Use desde e ate para ver as outras.");
            lista = lista[^limite..];
        }
        else
        {
            sb.AppendLine($"{Reunioes(lista.Count)}{onde}, da mais antiga para a mais nova.");
        }

        sb.AppendLine();
        foreach (var r in lista) sb.AppendLine(LinhaDaLista(r));
        return sb.ToString();
    }

    /// <summary><c>- id · 22/10/2026 09:00 · Título · 20min 41s · Cliente › Projeto · ata, notas</c></summary>
    private static string LinhaDaLista(ReuniaoNoAcervo r)
    {
        var partes = new List<string>
        {
            r.Id, r.Quando.ToString("dd/MM/yyyy HH:mm"), r.Titulo ?? "(sem título)",
        };
        if (r.DuracaoS > 0) partes.Add(Cabecalho.Duracao(r.DuracaoS));
        if (Acervo.Vinculo(r) is { } v) partes.Add(v);
        partes.Add(OQueTem(r));
        return "- " + string.Join(" · ", partes);
    }

    private static string OQueTem(ReuniaoNoAcervo r)
    {
        var tem = new List<string>();
        if (r.TemAta) tem.Add(r.AtaVelha ? "ata (anterior a uma correção)" : "ata");
        if (r.Transcrita) tem.Add("transcrição");
        if (r.TemNotas) tem.Add("notas");
        return tem.Count > 0 ? string.Join(", ", tem) : "só o áudio, sem transcrição";
    }

    private string LerAta(JsonObject args)
    {
        var r = Reuniao(args);
        if (Acervo.TextoDaAta(r) is { } ata)
            return $"id: {r.Id}" + (Acervo.Vinculo(r) is { } v ? $" · {v}" : "") + "\n\n" + ata;

        var sb = new StringBuilder(Acervo.Cabecalho(r)).AppendLine();
        sb.AppendLine(r.Transcrita
            ? "Esta reunião não tem ata. A transcrição existe: use ler_transcricao."
            : "Esta reunião não tem ata nem transcrição — só o áudio.");
        Acervo.AnexarNotas(sb, r);
        return sb.ToString();
    }

    private string LerTranscricao(JsonObject args)
    {
        var r = Reuniao(args);
        double? de = Instante(args, "de"), ate = Instante(args, "ate");
        if (de is not null && ate is not null && ate <= de)
            throw new ErroDeUso("\"ate\" precisa vir depois de \"de\".");

        return Acervo.TextoDaTranscricao(r, de, ate)
               ?? Acervo.Cabecalho(r) + "\nEsta reunião ainda não foi transcrita — só o áudio existe.";
    }

    private string Buscar(JsonObject args)
    {
        string termo = Arg(args, "termo") ?? throw new ErroDeUso("Diga o que procurar em \"termo\".");
        string? soEm = Arg(args, "reuniao") is not null ? Reuniao(args).Id : null;
        var filtro = Filtro(args);
        int limite = Inteiro(args, "limite", 30, 200);

        var (achados, total) = acervo.Buscar(termo, filtro, soEm, limite);
        string onde = soEm is not null ? $" na reunião {soEm}" : DescreverFiltro(filtro);
        if (total == 0)
            return $"Nada encontrado para \"{termo}\"{onde}. A busca pede todas as palavras na mesma "
                   + "fala; tente menos palavras, ou outra grafia de nome próprio.";

        var sb = new StringBuilder();
        int reunioes = achados.Select(a => a.Reuniao.Id).Distinct().Count();
        sb.Append($"{Ocorrencias(total)} de \"{termo}\"{onde}");
        sb.AppendLine(total > achados.Count
            ? $"; aqui estão as {achados.Count} primeiras, da reunião mais nova para a mais antiga. "
              + "Estreite por cliente, projeto ou data para ver as outras."
            : $", em {Reunioes(reunioes)}.");

        string? atual = null;
        foreach (var a in achados)
        {
            if (a.Reuniao.Id != atual)
            {
                atual = a.Reuniao.Id;
                sb.AppendLine().Append("## ").Append(a.Reuniao.Id).Append(" — ").Append(a.Reuniao.Rotulo);
                if (Acervo.Vinculo(a.Reuniao) is { } v) sb.Append(" (").Append(v).Append(')');
                sb.AppendLine();
            }
            sb.AppendLine(a.Instante is { } t
                ? "- " + Acervo.Linha(new Fala(t, a.Falante, a.Trecho))
                : $"- {a.Fonte}: {a.Trecho}");
        }
        return sb.ToString();
    }

    private string DescreverAberta()
    {
        if (ReuniaoAberta.Ler(caminhoDaReuniaoAberta) is { } marca && acervo.Achar(marca.Id) is { } r)
        {
            var sb = new StringBuilder();
            sb.Append("A última reunião aberta no PulseMeet é a ").Append(r.Id);
            if (marca.AbertaEm > DateTimeOffset.MinValue)
                sb.Append(", aberta em ").Append(marca.AbertaEm.LocalDateTime.ToString("dd/MM/yyyy 'às' HH:mm"));
            sb.AppendLine(".").AppendLine().Append(Acervo.Cabecalho(r));
            sb.Append("Tem: ").AppendLine(OQueTem(r)).AppendLine();
            sb.AppendLine($"Use ler_ata ou ler_transcricao com reuniao = \"{r.Id}\".");
            return sb.ToString();
        }

        var ultima = acervo.Listar().LastOrDefault();
        return "Nenhuma reunião foi aberta no PulseMeet ainda."
               + (ultima is not null
                   ? $" A gravada mais recente é {LinhaDaLista(ultima)[2..]}."
                   : " O acervo está vazio.");
    }

    // ──────────────────────────────────────────────────────────── recursos

    /// <summary>
    /// A ata e a transcrição de cada reunião, da mais nova para a mais antiga,
    /// com a reunião aberta no app em primeiro lugar.
    /// </summary>
    /// <remarks>
    /// O <c>(JsonNode)</c> nos <c>Add</c> não é enfeite: sem ele o compilador
    /// escolhe o <c>Add&lt;T&gt;</c> genérico, que o trimming não garante — o
    /// <c>publicar.sh</c> recusa com IL2026, e os testes passam sem notar.
    /// </remarks>
    public JsonArray Recursos()
    {
        var lista = new JsonArray();
        if (Aberta() is { } aberta)
        {
            if (aberta.TemAta)
                lista.Add((JsonNode)Recurso($"{Esquema}reuniao-aberta/ata", "Reunião aberta no PulseMeet — ata",
                                  aberta.Rotulo, "text/markdown"));
            if (aberta.Transcrita)
                lista.Add((JsonNode)Recurso($"{Esquema}reuniao-aberta/transcricao",
                                  "Reunião aberta no PulseMeet — transcrição", aberta.Rotulo, "text/plain"));
        }

        foreach (var r in Enumerable.Reverse(acervo.Listar()))
        {
            string? sobre = Acervo.Vinculo(r);
            if (r.TemAta)
                lista.Add((JsonNode)Recurso($"{Esquema}reuniao/{r.Id}/ata", $"{r.Rotulo} — ata", sobre, "text/markdown"));
            if (r.Transcrita)
                lista.Add((JsonNode)Recurso($"{Esquema}reuniao/{r.Id}/transcricao", $"{r.Rotulo} — transcrição",
                                  sobre, "text/plain"));
        }
        return lista;
    }

    public static JsonArray ModelosDeRecurso() => new(
        new JsonObject
        {
            ["uriTemplate"] = Esquema + "reuniao/{id}/ata",
            ["name"] = "Ata de uma reunião",
            ["description"] = "A ata, com as notas de quem gravou. O id é o nome da pasta.",
            ["mimeType"] = "text/markdown",
        },
        new JsonObject
        {
            ["uriTemplate"] = Esquema + "reuniao/{id}/transcricao",
            ["name"] = "Transcrição de uma reunião",
            ["description"] = "A transcrição, uma fala por linha, com o minuto e quem falou.",
            ["mimeType"] = "text/plain",
        });

    public JsonObject LerRecurso(string? uri)
    {
        if (uri is null || !uri.StartsWith(Esquema, StringComparison.Ordinal))
            throw new ErroDoMcp(-32602, $"endereço que não é do PulseMeet: {uri}");

        string[] partes = uri[Esquema.Length..].Split('/');
        ReuniaoNoAcervo? r;
        string parte;
        if (partes is ["reuniao-aberta", var p1])
            (r, parte) = (Aberta(), p1);
        else if (partes is ["reuniao", var id, var p2])
            (r, parte) = (acervo.Achar(Uri.UnescapeDataString(id)), p2);
        else
            throw new ErroDoMcp(-32002, $"recurso desconhecido: {uri}");

        if (r is null) throw new ErroDoMcp(-32002, $"reunião não encontrada: {uri}");

        var (texto, tipo) = parte switch
        {
            "ata" => (Acervo.TextoDaAta(r), "text/markdown"),
            "transcricao" => (Acervo.TextoDaTranscricao(r), "text/plain"),
            _ => throw new ErroDoMcp(-32002, $"recurso desconhecido: {uri}"),
        };
        if (texto is null)
            throw new ErroDoMcp(-32002, parte == "ata" ? "esta reunião não tem ata"
                                                       : "esta reunião não foi transcrita");

        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["uri"] = uri,
                ["mimeType"] = tipo,
                ["text"] = texto,
            }),
        };
    }

    private ReuniaoNoAcervo? Aberta() =>
        ReuniaoAberta.Ler(caminhoDaReuniaoAberta) is { } marca ? acervo.Achar(marca.Id) : null;

    private static JsonObject Recurso(string uri, string nome, string? descricao, string tipo)
    {
        var o = new JsonObject { ["uri"] = uri, ["name"] = nome, ["title"] = nome, ["mimeType"] = tipo };
        if (descricao is not null) o["description"] = descricao;
        return o;
    }

    // ───────────────────────────────────────────────────────────── prompts

    public static JsonArray Prompts() => new(
        DefinicaoDePrompt("timeline", "Linha do tempo de um cliente",
            "O que aconteceu com um cliente ao longo das reuniões: decisões, ações e o que ficou pelo caminho.",
            Argumento("cliente", "O cliente, como está no PulseMeet (ou um pedaço do nome).", true),
            Argumento("projeto", "Só este projeto.", false),
            Argumento("desde", "A partir de que dia, AAAA-MM-DD.", false)),

        DefinicaoDePrompt("follow-up", "E-mail de follow-up",
            "O e-mail para os participantes depois de uma reunião.",
            Argumento("reuniao", "O id da reunião. Vazio, a reunião aberta no PulseMeet.", false)),

        DefinicaoDePrompt("preparar-reuniao", "Preparar a próxima reunião",
            "O que está pendente com um cliente, e uma pauta para a próxima reunião.",
            Argumento("cliente", "O cliente, como está no PulseMeet (ou um pedaço do nome).", true),
            Argumento("projeto", "Só este projeto.", false)));

    public static JsonObject Prompt(string? nome, JsonObject args)
    {
        string? Valor(string chave) => Arg(args, chave);
        string Obrigatorio(string chave) =>
            Valor(chave) ?? throw new ErroDoMcp(-32602, $"o prompt {nome} precisa de \"{chave}\"");

        var (descricao, texto) = nome switch
        {
            "timeline" => ("Linha do tempo de um cliente", TextoDaTimeline(
                Obrigatorio("cliente"), Valor("projeto"), Valor("desde"))),
            "follow-up" => ("E-mail de follow-up", TextoDoFollowUp(Valor("reuniao"))),
            "preparar-reuniao" => ("Preparar a próxima reunião", TextoDoPreparo(
                Obrigatorio("cliente"), Valor("projeto"))),
            _ => throw new ErroDoMcp(-32602, $"prompt desconhecido: {nome}"),
        };

        return new JsonObject
        {
            ["description"] = descricao,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonObject { ["type"] = "text", ["text"] = texto },
            }),
        };
    }

    private static string TextoDaTimeline(string cliente, string? projeto, string? desde) =>
        $"Monte a linha do tempo do que aconteceu com o cliente {cliente}"
        + (projeto is not null ? $", no projeto {projeto}" : "")
        + (desde is not null ? $", desde {desde}" : "")
        + ", a partir das reuniões gravadas no PulseMeet.\n\n"
        + "1. Use listar_reunioes com o filtro do cliente para achar as reuniões. Se o nome casar "
        + "com mais de um cliente, diga quais são e pergunte se junto ou separo.\n"
        + "2. Leia a ata de cada uma com ler_ata, em ordem de data. Reunião sem ata: leia a transcrição.\n"
        + "3. De cada reunião, registre a data, o que mudou, as decisões e as ações com responsável e prazo.\n"
        + "4. Acompanhe as ações de uma reunião para a outra: quando uma foi concluída, quando voltou "
        + "a ser cobrada, e quando sumiu sem ninguém dizer que foi feita.\n"
        + "5. Aponte as decisões que foram revistas ou revertidas depois.\n"
        + "6. Quando uma ata parecer errada ou vaga num ponto que importa, confira com buscar ou "
        + "ler_transcricao e cite o minuto.\n\n"
        + "Entregue um resumo de onde o projeto está hoje, a linha do tempo em ordem de data, as "
        + "pendências em aberto e os riscos. Cite a data da reunião em cada item.";

    private static string TextoDoFollowUp(string? reuniao) =>
        "Escreva o e-mail de follow-up da "
        + (reuniao is not null ? $"reunião {reuniao} do PulseMeet"
                               : "reunião aberta no PulseMeet (use reuniao_aberta para saber qual é)")
        + ".\n\nLeia a ata com ler_ata; se ela não bastar para algum ponto, confira na transcrição. "
        + "O e-mail vai para os participantes: agradeça em uma linha, resuma o que foi discutido, "
        + "liste as decisões e as próximas ações com responsável e prazo, e termine com o que ficou "
        + "para a próxima reunião. Escreva direto, e não invente prazo que ninguém disse.";

    private static string TextoDoPreparo(string cliente, string? projeto) =>
        $"Prepare a minha próxima reunião com o cliente {cliente}"
        + (projeto is not null ? $", no projeto {projeto}" : "")
        + ".\n\nUse listar_reunioes para achar as últimas reuniões — as três ou quatro mais recentes "
        + "bastam, a menos que algo mais antigo continue em aberto — e leia as atas. Entregue o que "
        + "ficou pendente do nosso lado e do lado deles, o que foi prometido e ainda não foi cumprido, "
        + "as decisões em suspenso, os riscos que apareceram, e uma pauta com as perguntas que vale "
        + "fazer. Cite a data da reunião de onde saiu cada ponto.";

    // ───────────────────────────────────────────────────────── argumentos

    /// <summary>Um erro de quem chamou: volta como texto com <c>isError</c>, para o modelo corrigir.</summary>
    private sealed class ErroDeUso(string mensagem) : Exception(mensagem);

    private ReuniaoNoAcervo Reuniao(JsonObject args)
    {
        string id = Arg(args, "reuniao")
                    ?? throw new ErroDeUso("Diga qual reunião em \"reuniao\" — o id sai de listar_reunioes.");

        if (id is "aberta" or "atual")
            return Aberta() ?? throw new ErroDeUso(
                "Nenhuma reunião foi aberta no PulseMeet ainda. Use listar_reunioes para escolher uma.");

        return acervo.Achar(id) ?? throw new ErroDeUso(
            $"Não há reunião com id \"{id}\". Os ids saem de listar_reunioes — são o nome da pasta, "
            + "como 2026-10-22_09-01-36.");
    }

    private static FiltroDoAcervo Filtro(JsonObject args) =>
        new(Arg(args, "cliente"), Arg(args, "projeto"), Dia(args, "desde"), Dia(args, "ate"));

    private static string DescreverFiltro(FiltroDoAcervo f)
    {
        var partes = new List<string>();
        if (f.Cliente is not null) partes.Add($"cliente \"{f.Cliente}\"");
        if (f.Projeto is not null) partes.Add($"projeto \"{f.Projeto}\"");
        if (f.Desde is { } d) partes.Add($"desde {d:dd/MM/yyyy}");
        if (f.Ate is { } a) partes.Add($"até {a:dd/MM/yyyy}");
        return partes.Count > 0 ? $" ({string.Join(", ", partes)})" : "";
    }

    private static string? Arg(JsonObject args, string chave) =>
        args[chave] switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s.Trim() is { Length: > 0 } t ? t : null,
            JsonValue v when v.TryGetValue<double>(out var n) => n.ToString(CultureInfo.InvariantCulture),
            _ => null,
        };

    private static DateOnly? Dia(JsonObject args, string chave)
    {
        if (Arg(args, chave) is not { } texto) return null;
        if (DateOnly.TryParseExact(texto, ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var dia))
            return dia;
        if (DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var q))
            return DateOnly.FromDateTime(q.DateTime);
        throw new ErroDeUso($"\"{chave}\" precisa ser uma data AAAA-MM-DD, e veio \"{texto}\".");
    }

    /// <summary><c>12:03</c>, <c>1:02:03</c>, ou um número de minutos.</summary>
    private static double? Instante(JsonObject args, string chave)
    {
        if (Arg(args, chave) is not { } texto) return null;
        if (double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutos))
            return minutos * 60;

        string[] partes = texto.Split(':');
        if (partes.Length is 2 or 3 && partes.All(p => int.TryParse(p, out _)))
        {
            int[] n = [.. partes.Select(int.Parse)];
            return n.Length == 2 ? n[0] * 60 + n[1] : n[0] * 3600 + n[1] * 60 + n[2];
        }
        throw new ErroDeUso($"\"{chave}\" precisa ser mm:ss ou hh:mm:ss, e veio \"{texto}\".");
    }

    private static int Inteiro(JsonObject args, string chave, int padrao, int maximo) =>
        Arg(args, chave) is { } t && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
            ? Math.Clamp((int)n, 1, maximo)
            : padrao;

    // ───────────────────────────────────────────────────────── montagem

    private static JsonObject Ferramenta(string nome, string titulo, string descricao,
                                         JsonObject propriedades, params string[] obrigatorios) => new()
    {
        ["name"] = nome,
        ["title"] = titulo,
        ["description"] = descricao,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = propriedades,
            ["required"] = new JsonArray([.. obrigatorios.Select(o => (JsonNode?)o)]),
        },
        // Só lê, e só o disco desta máquina: o Claude Desktop pode chamar sem
        // pedir confirmação a cada vez, se a pessoa quiser.
        ["annotations"] = new JsonObject
        {
            ["title"] = titulo,
            ["readOnlyHint"] = true,
            ["openWorldHint"] = false,
        },
    };

    private static JsonObject Campo(string tipo, string descricao) =>
        new() { ["type"] = tipo, ["description"] = descricao };

    private static JsonObject DefinicaoDePrompt(string nome, string titulo, string descricao,
                                     params JsonObject[] argumentos) => new()
    {
        ["name"] = nome,
        ["title"] = titulo,
        ["description"] = descricao,
        ["arguments"] = new JsonArray([.. argumentos]),
    };

    private static JsonObject Argumento(string nome, string descricao, bool obrigatorio) =>
        new() { ["name"] = nome, ["description"] = descricao, ["required"] = obrigatorio };

    private static JsonObject Resultado(string texto, bool erro) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = texto }),
        ["isError"] = erro,
    };

    private static string Reunioes(int n) => n == 1 ? "1 reunião" : $"{n} reuniões";

    private static string Ocorrencias(int n) => n == 1 ? "1 ocorrência" : $"{n} ocorrências";
}
