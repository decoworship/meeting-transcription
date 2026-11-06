using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// A ata de formalização: duas listas — resolvidos e pendentes — no formato
/// do e-mail que vai ao cliente.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que um caminho próprio, e não mais um tipo no caminho comum.</b> No
/// comum o modelo escreve a ata inteira de uma vez, e o verificador confere
/// depois. Medido em 06/11/2026 em três reuniões do RTM Merchan, contra as atas
/// que o dono mandou por e-mail: o Qwen3-4B pelo caminho comum <b>inventou
/// prazo nas três</b> ("28/10/2026" nas quatro pendências de uma delas), e
/// cobriu 38% dos itens. Por aqui, nenhum prazo inventado e ~55% de cobertura.
/// </para>
/// <para>
/// <b>As três etapas</b>, e cada uma existe por um defeito:
/// </para>
/// <list type="number">
/// <item><b>extrair</b>: o modelo lista resolvidos e pendentes, e cada item cita
/// as linhas da transcrição de onde saiu. Pedir só isso, e não a ata, é o que o
/// Re-FRAME/SCOPE (EMNLP 2025) mediu reduzir alucinação;</item>
/// <item><b>conferir</b>, sem modelo: item sem linha que exista cai; prazo que
/// não aparece perto das linhas citadas é apagado; número sem fonte vira
/// observação. É a peça que impede o "28/10/2026";</item>
/// <item><b>escrever</b>: o modelo só reescreve os itens conferidos no estilo
/// do exemplo. <b>Responsável e prazo nunca passam por ele de novo</b> — vêm da
/// extração conferida e são postos aqui, no C#.</item>
/// </list>
/// <para>
/// <b>Votar entre três extrações não entrou</b>: foi medido na mesma rodada e
/// as extrações variam tanto (2, 11 e 12 itens na mesma reunião) que a votação
/// cortava item bom sem tirar erro.
/// </para>
/// </remarks>
public static class AtaDeFormalizacao
{
    /// <summary>O id do tipo que usa este caminho.</summary>
    public const string Id = "formalizacao";

    /// <summary>Uma fala: os trechos seguidos da mesma pessoa, juntos e numerados.</summary>
    public sealed record Turno(int N, string Falante, double Inicio, string Texto);

    public sealed record Resolvido(string Tema, string Decisao, IReadOnlyList<int> Linhas);

    public sealed record Pendente(string OQue, string Responsavel, string Prazo, IReadOnlyList<int> Linhas);

    public sealed record Extracao(IReadOnlyList<Resolvido> Resolvidos, IReadOnlyList<Pendente> Pendentes);

    /// <summary>O que a etapa de escrita devolve, já ligado aos itens conferidos.</summary>
    public sealed record Escrita(
        string Abertura,
        IReadOnlyList<(int N, string Tema, string Texto)> Resolvidos,
        IReadOnlyList<(int N, string Texto)> Pendentes,
        string ProximaReuniao);

    // ------------------------------------------------------------------ turnos

    public static IReadOnlyList<Turno> Turnos(IEnumerable<SegmentoFinal> segmentos)
    {
        var turnos = new List<Turno>();
        foreach (var s in segmentos)
        {
            string texto = s.Text.Trim();
            if (texto.Length == 0) continue;
            string quem = string.IsNullOrWhiteSpace(s.Speaker) ? "?" : s.Speaker.Trim();
            if (turnos.Count > 0 && turnos[^1].Falante == quem)
                turnos[^1] = turnos[^1] with { Texto = turnos[^1].Texto + " " + texto };
            else
                turnos.Add(new Turno(turnos.Count + 1, quem, s.Start, texto));
        }
        return turnos;
    }

    public static string TextoNumerado(IEnumerable<Turno> turnos)
    {
        var sb = new StringBuilder();
        foreach (var t in turnos)
        {
            int min = (int)(t.Inicio / 60), seg = (int)(t.Inicio % 60);
            sb.Append("[L").Append(t.N).Append(' ').Append(min.ToString("00")).Append(':')
              .Append(seg.ToString("00")).Append("] ").Append(t.Falante).Append(": ")
              .AppendLine(t.Texto);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ extrair

    public const string SistemaDaExtracao = """
        Você extrai, de uma transcrição de reunião, só duas coisas:

        1. RESOLVIDOS: o que a reunião fechou — uma regra, um critério, um número, uma forma de fazer que o grupo aceitou. Uma posição provisória aceita ("seguimos com o de 2025 até chegar o novo") conta. Ideia levantada e não fechada NÃO conta.
        2. PENDENTES: o que alguém ficou de fazer, enviar, decidir ou confirmar depois da reunião.

        Regras:
        - Cada item cita em "linhas" os números L das falas onde ele aparece. Cite só linhas que dizem aquilo. Sem linha que sustente, o item não existe.
        - "prazo" só quando o prazo foi DITO nessas linhas ("amanhã", "quinta", "até 09/10"). Se não foi dito, deixe vazio "". Nunca invente data.
        - "responsavel" é quem vai fazer, pelo nome como aparece na transcrição. Se for a empresa e não uma pessoa, o nome da empresa. Se ninguém foi nomeado, "".
        - Mantenha os números exatamente como ditos.
        - Ignore conversa social, problemas de conexão e agenda.
        - Pendente é TRABALHO do projeto. Não são pendentes: confirmar quem vai entrar na reunião, pôr alguém na agenda ou no convite, mandar a ata desta reunião, compartilhar a tela.
        - Mudança de data de reunião não é pendente: escreva como um resolvido de tema "Próxima reunião".
        - Não repita o mesmo item em duas listas.
        """;

    public const string EsquemaDaExtracao = """
        {
          "type": "object",
          "properties": {
            "resolvidos": {"type": "array", "items": {"type": "object", "properties": {
              "tema": {"type": "string"}, "decisao": {"type": "string"},
              "linhas": {"type": "array", "items": {"type": "integer"}, "maxItems": 6}},
              "required": ["tema", "decisao", "linhas"], "additionalProperties": false}},
            "pendentes": {"type": "array", "items": {"type": "object", "properties": {
              "o_que": {"type": "string"}, "responsavel": {"type": "string"},
              "prazo": {"type": "string"},
              "linhas": {"type": "array", "items": {"type": "integer"}, "maxItems": 6}},
              "required": ["o_que", "responsavel", "prazo", "linhas"], "additionalProperties": false}}
          },
          "required": ["resolvidos", "pendentes"],
          "additionalProperties": false
        }
        """;

    public static string PedidoDaExtracao(ContextoDaReuniao ctx, IEnumerable<Turno> turnos)
    {
        var sb = new StringBuilder();
        sb.Append("Reunião: ").Append(ctx.Titulo ?? "").Append(" · ").Append(ctx.Data ?? "");
        if (ctx.Cliente is { Length: > 0 } c) sb.Append(" · Cliente: ").Append(c);
        if (ctx.Projeto is { Length: > 0 } p) sb.Append(" · Projeto: ").Append(p);
        sb.AppendLine();
        if (ctx.Falantes.Count > 0) sb.Append("Falantes: ").AppendLine(string.Join(", ", ctx.Falantes));
        if (ctx.Vocabulario is { Length: > 0 } v) sb.Append("Vocabulário do projeto: ").AppendLine(v);
        sb.AppendLine().AppendLine("TRANSCRIÇÃO").Append(TextoNumerado(turnos));
        return sb.ToString();
    }

    public static Extracao LerExtracao(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        var res = new List<Resolvido>();
        var pen = new List<Pendente>();
        if (raiz.TryGetProperty("resolvidos", out var rs) && rs.ValueKind == JsonValueKind.Array)
            foreach (var r in rs.EnumerateArray())
                res.Add(new Resolvido(Txt(r, "tema"), Txt(r, "decisao"), Linhas(r)));
        if (raiz.TryGetProperty("pendentes", out var ps) && ps.ValueKind == JsonValueKind.Array)
            foreach (var p in ps.EnumerateArray())
                pen.Add(new Pendente(Txt(p, "o_que"), Txt(p, "responsavel"), Txt(p, "prazo"), Linhas(p)));
        return new Extracao(res, pen);
    }

    // ------------------------------------------------------------------ conferir

    private static readonly Regex Prazo = new(
        @"\b(\d{1,2}/\d{1,2}(?:/\d{2,4})?|hoje|amanh[ãa]|segunda|ter[çc]a|quarta|quinta|sexta|s[áa]bado|domingo|"
        + @"semana que vem|pr[óo]xima semana|fim do m[êe]s|\d+ dias?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Numero = new(@"\d+(?:[.,]\d+)?", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> Extenso = new()
    {
        ["um"] = "1", ["uma"] = "1", ["dois"] = "2", ["duas"] = "2", ["tres"] = "3", ["quatro"] = "4",
        ["cinco"] = "5", ["seis"] = "6", ["sete"] = "7", ["oito"] = "8", ["nove"] = "9", ["dez"] = "10",
        ["onze"] = "11", ["doze"] = "12", ["quinze"] = "15", ["vinte"] = "20", ["trinta"] = "30",
        ["quarenta"] = "40", ["cinquenta"] = "50", ["cem"] = "100", ["duzentas"] = "200",
        ["trezentas"] = "300", ["quinhentas"] = "500",
    };

    private static readonly Regex PalavraDeNumero = new(
        @"\b(" + string.Join("|", Extenso.Keys) + @")\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// O que é logística da própria reunião, e não trabalho do projeto.
    /// </summary>
    /// <remarks>
    /// A regra está no prompt e o modelo a ignora às vezes: em 06/11, nas nove
    /// reuniões, saíram "pôr o André na agenda", "confirmar se o William vai
    /// entrar" e "enviar a ata da reunião". Filtrar aqui é o que a torna regra.
    /// </remarks>
    private static readonly Regex Logistica = new(
        @"\b(agenda|convite|calendario)\b|\bata (da|desta) reuniao\b|\b(enviar|mandar|circular|compartilhar) (a )?ata\b"
        + @"|\b(reagendar|remarcar|cancelar|adiar|marcar) a (proxima )?reuniao\b"
        + @"|\bvai entrar\b|\b(entrar|participar) (na|da) reuniao\b|\bcompartilhar a tela\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// O prazo aparece na fala? "23/10" também foi dito quando se disse "dia 23".
    /// </summary>
    private static bool FoiDito(string prazo, string fala)
    {
        if (fala.Contains(prazo)) return true;
        var data = Regex.Match(prazo, @"^(\d{1,2})/\d{1,2}");
        return data.Success && Regex.IsMatch(fala, $@"\bdia {int.Parse(data.Groups[1].Value)}\b");
    }

    /// <summary>
    /// Quantas falas para cada lado das citadas entram na conferência.
    /// </summary>
    /// <remarks>
    /// Três, e não uma: medido em 06/11 que o "hoje" vem na resposta, duas ou
    /// três falas depois do pedido que o modelo cita ("consegue até amanhã?" —
    /// "eu mando isso hoje"). Com uma, a conferência apagava prazo dito.
    /// </remarks>
    public const int Vizinhanca = 3;

    /// <summary>
    /// A etapa sem modelo: o que não tem linha cai, o prazo que não foi dito é
    /// apagado, e o resto vira observação — nada é corrigido em silêncio.
    /// </summary>
    public static (Extracao Conferida, IReadOnlyList<string> Observacoes) Conferir(
        Extracao ext, IReadOnlyList<Turno> turnos, ContextoDaReuniao ctx)
    {
        var porN = turnos.ToDictionary(t => t.N);
        var obs = new List<string>();
        var nomes = ctx.Falantes.Concat(ctx.Convidados)
            .Select(n => SemAcento(n).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "")
            .Where(n => n.Length > 0).ToHashSet();
        string empresas = SemAcento((ctx.Cliente ?? "") + " " + Marca.Nome);

        (List<int> Ids, string Texto) Trecho(IReadOnlyList<int> linhas)
        {
            var ids = linhas.Where(porN.ContainsKey).Distinct().ToList();
            var viz = ids.SelectMany(i => Enumerable.Range(i - Vizinhanca, 2 * Vizinhanca + 1))
                .Where(porN.ContainsKey).Distinct().Order();
            string texto = string.Join(" ", viz.Select(j => SemAcento(porN[j].Falante + " " + porN[j].Texto)));
            return (ids, ComDigitos(texto));
        }

        var res = new List<Resolvido>();
        foreach (var r in ext.Resolvidos)
        {
            var (ids, txt) = Trecho(r.Linhas);
            if (ids.Count == 0 || r.Decisao.Trim().Length == 0)
            {
                obs.Add($"Descartado por não citar a fala de onde saiu: {r.Tema}: {r.Decisao}");
                continue;
            }
            var faltam = Numero.Matches(r.Decisao).Select(m => m.Value)
                .Where(n => !txt.Replace(',', '.').Contains(n.Replace(',', '.'))).ToList();
            if (faltam.Count > 0)
                obs.Add($"Número que não aparece na fala citada ({string.Join(", ", faltam)}): {r.Tema}");
            res.Add(r with { Linhas = ids });
        }

        var pen = new List<Pendente>();
        foreach (var p in ext.Pendentes)
        {
            var (ids, txt) = Trecho(p.Linhas);
            if (ids.Count == 0 || p.OQue.Trim().Length == 0)
            {
                obs.Add($"Descartado por não citar a fala de onde saiu: {p.OQue}");
                continue;
            }
            if (Logistica.IsMatch(SemAcento(p.OQue)))
            {
                obs.Add($"Fora da ata, por ser logística da própria reunião: {p.OQue}");
                continue;
            }
            string prazo = p.Prazo.Trim();
            if (prazo.Length > 0)
            {
                var achados = Prazo.Matches(prazo).Select(m => SemAcento(m.Value)).ToList();
                if (achados.Count == 0 || !achados.All(a => FoiDito(a, txt)))
                {
                    obs.Add($"Prazo apagado, porque não foi dito ({prazo}): {p.OQue}");
                    prazo = "";
                }
            }
            string resp = p.Responsavel.Trim();
            if (resp.Length > 0)
            {
                string primeiro = SemAcento(resp).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                if (!txt.Contains(primeiro) && !nomes.Contains(primeiro) && !empresas.Contains(primeiro))
                    obs.Add($"Responsável que não aparece na fala citada ({resp}): {p.OQue}");
            }
            pen.Add(p with { Prazo = prazo, Responsavel = resp, Linhas = ids });
        }
        return (new Extracao(res, pen), obs);
    }

    // ------------------------------------------------------------------ escrever

    /// <summary>
    /// O exemplo de estilo. <b>É inventado de propósito</b>: o estilo saiu das
    /// atas que o dono manda ao cliente, mas elas carregam nome e número de
    /// cliente, e o exemplo vai dentro do executável que outras pessoas
    /// instalam.
    /// </summary>
    public const string Exemplo = """
        A rodada preliminar ficou com 312 lojas e 88% do volume, ainda sem a parte financeira.

        **Resolvidos em reunião**

        - Corte de volume: 400 caixas por mês, igual para todas as regiões.
        - Ordem da fila: por volume, com as lojas maiores primeiro. Podemos rever quando a margem chegar.
        - Mínimo por visita: 2 h, sem exceção.
        - Rateio dos CDs: seguimos com o de 2025 até chegar o atualizado.

        **Pendentes**

        - Margem por cliente: Rafael, até 09/10
        - Lista das lojas novas, indicando quais serão fixas: Joana
        - Novo cenário com o corte de 400: Acme, na próxima semana

        Próxima reunião: quinta, 23/10, no mesmo horário.
        """;

    public const string SistemaDaEscrita = """
        Você reescreve itens já conferidos de uma ata de formalização, no estilo do exemplo.

        O estilo:
        - Resolvido: "tema" curto (1 a 4 palavras) e "texto" com a decisão em uma ou duas frases, com os números exatos.
        - Pendente: "texto" é só O QUE falta, curto. Quem e quando o sistema acrescenta — não escreva responsável nem prazo no texto.
        - Primeira pessoa do plural para o que é nosso ("usamos", "partimos"). Frases que o cliente diria, não de relatório.
        - Junte itens que dizem a mesma coisa (use o "n" do primeiro). Não acrescente nada que não esteja nos itens.
        - "abertura": 1 frase com o estado do trabalho, só se os itens trouxerem esse número; senão "".
        - "proxima_reuniao": só se algum item disser; senão "".

        EXEMPLO DE ATA NESTE ESTILO (de outra reunião):

        """ + Exemplo;

    public const string EsquemaDaEscrita = """
        {
          "type": "object",
          "properties": {
            "abertura": {"type": "string"},
            "resolvidos": {"type": "array", "items": {"type": "object", "properties": {
              "n": {"type": "integer"}, "tema": {"type": "string"}, "texto": {"type": "string"}},
              "required": ["n", "tema", "texto"], "additionalProperties": false}},
            "pendentes": {"type": "array", "items": {"type": "object", "properties": {
              "n": {"type": "integer"}, "texto": {"type": "string"}},
              "required": ["n", "texto"], "additionalProperties": false}},
            "proxima_reuniao": {"type": "string"}
          },
          "required": ["abertura", "resolvidos", "pendentes", "proxima_reuniao"],
          "additionalProperties": false
        }
        """;

    public static string PedidoDaEscrita(Extracao conferida)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            w.WriteStartObject();
            w.WriteStartArray("resolvidos");
            for (int i = 0; i < conferida.Resolvidos.Count; i++)
            {
                w.WriteStartObject();
                w.WriteNumber("n", i + 1);
                w.WriteString("tema", conferida.Resolvidos[i].Tema);
                w.WriteString("decisao", conferida.Resolvidos[i].Decisao);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("pendentes");
            for (int i = 0; i < conferida.Pendentes.Count; i++)
            {
                w.WriteStartObject();
                w.WriteNumber("n", i + 1);
                w.WriteString("o_que", conferida.Pendentes[i].OQue);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static Escrita LerEscrita(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        var res = new List<(int, string, string)>();
        var pen = new List<(int, string)>();
        if (raiz.TryGetProperty("resolvidos", out var rs) && rs.ValueKind == JsonValueKind.Array)
            foreach (var r in rs.EnumerateArray())
                res.Add((Int(r, "n"), Txt(r, "tema"), Txt(r, "texto")));
        if (raiz.TryGetProperty("pendentes", out var ps) && ps.ValueKind == JsonValueKind.Array)
            foreach (var p in ps.EnumerateArray())
                pen.Add((Int(p, "n"), Txt(p, "texto")));
        return new Escrita(Txt(raiz, "abertura"), res, pen, Txt(raiz, "proxima_reuniao"));
    }

    // ------------------------------------------------------------------ montar

    /// <summary>
    /// A ata que o resto do app entende: o <c>ata.json</c> continua sendo um
    /// <see cref="AtaGerada"/>, porque as pendências entre reuniões e o servidor
    /// MCP leem dele.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A escrita é só estilo, e estilo não decide o que entra: ela não cria
    /// item, e o que ela deixou de fora fica dito nas observações.
    /// </para>
    /// <para>
    /// <b>A próxima reunião sai da extração, nunca da escrita.</b> A escrita não
    /// cita fala, e foi onde nasceram em 06/11 "adiada para confirmar a presença",
    /// "dia 19" e "sexta" — nenhuma dita. Só entra se houver um resolvido
    /// conferido com esse tema.
    /// </para>
    /// </remarks>
    public static AtaGerada Montar(Extracao conferida, Escrita escrita, IReadOnlyList<string> obs,
                                   ContextoDaReuniao ctx)
    {
        var ata = new AtaGerada { Resumo = escrita.Abertura.Trim() };
        string? proxima = null;

        var vistosR = new HashSet<int>();
        foreach (var (n, tema, texto) in escrita.Resolvidos)
        {
            if (n < 1 || n > conferida.Resolvidos.Count || !vistosR.Add(n)) continue;
            var origem = conferida.Resolvidos[n - 1];
            string t = (tema.Trim().Length > 0 ? tema : origem.Tema).Trim().TrimEnd(':');
            string d = (texto.Trim().Length > 0 ? texto : origem.Decisao).Trim();
            if (EProximaReuniao(origem.Tema)) { proxima ??= d; continue; }
            ata.Decisoes.Add($"{t}: {d}");
        }
        if (proxima is null)
            for (int i = 1; i <= conferida.Resolvidos.Count; i++)
                if (!vistosR.Contains(i) && EProximaReuniao(conferida.Resolvidos[i - 1].Tema))
                {
                    proxima = conferida.Resolvidos[i - 1].Decisao.Trim();
                    vistosR.Add(i);
                    break;
                }
        var vistosP = new HashSet<int>();
        foreach (var (n, texto) in escrita.Pendentes)
        {
            if (n < 1 || n > conferida.Pendentes.Count || !vistosP.Add(n)) continue;
            ata.Acoes.Add(Acao(conferida.Pendentes[n - 1], texto, ctx));
        }

        ata.Observacoes.AddRange(obs);
        if (proxima is { Length: > 0 })
            ata.Secoes.Add(new SecaoDaAta { Titulo = "Próxima reunião", Texto = proxima });
        else if (escrita.ProximaReuniao.Trim() is { Length: > 0 } semFonte)
            ata.Observacoes.Add($"Próxima reunião fora da ata, porque nenhuma fala citada a sustenta: {semFonte}");
        // Juntar dois itens é o que a escrita deve fazer, e não dá para
        // distinguir junção de esquecimento. Então o que não voltou não é
        // reposto — duplicaria o juntado — mas fica dito, item por item.
        for (int i = 1; i <= conferida.Resolvidos.Count; i++)
            if (!vistosR.Contains(i))
                ata.Observacoes.Add($"Juntado a outro item ou deixado de fora na escrita: "
                    + $"{conferida.Resolvidos[i - 1].Tema}: {conferida.Resolvidos[i - 1].Decisao}");
        for (int i = 1; i <= conferida.Pendentes.Count; i++)
            if (!vistosP.Contains(i))
                ata.Observacoes.Add($"Juntado a outro item ou deixado de fora na escrita: "
                    + conferida.Pendentes[i - 1].OQue);
        return ata;
    }

    private static bool EProximaReuniao(string tema) =>
        SemAcento(tema).Trim().TrimEnd(':').StartsWith("proxima reuniao", StringComparison.Ordinal);

    private static AcaoDaAta Acao(Pendente p, string texto, ContextoDaReuniao ctx)
    {
        string resp = p.Responsavel.Trim();
        var pessoa = ctx.Pessoas.FirstOrDefault(x =>
            resp.Length > 0 && SemAcento(x.Nome).StartsWith(SemAcento(resp).Split(' ')[0], StringComparison.Ordinal));
        return new AcaoDaAta
        {
            Acao = (texto.Trim().Length > 0 ? texto : p.OQue).Trim().TrimEnd('.'),
            Responsavel = resp,
            Prazo = p.Prazo.Trim(),
            Lado = pessoa?.Lado ?? "nosso",
        };
    }

    /// <summary>O <c>ata.md</c>: as duas listas, no formato do e-mail.</summary>
    public static string Markdown(AtaGerada ata)
    {
        var sb = new StringBuilder();
        if (ata.Resumo.Trim().Length > 0) sb.AppendLine(ata.Resumo.Trim()).AppendLine();
        if (ata.Decisoes.Count > 0)
        {
            sb.AppendLine("**Resolvidos em reunião**").AppendLine();
            foreach (var d in ata.Decisoes) sb.Append("- ").AppendLine(d.Texto);
            sb.AppendLine();
        }
        if (ata.Acoes.Count > 0)
        {
            sb.AppendLine("**Pendentes**").AppendLine();
            foreach (var a in ata.Acoes)
            {
                string quem = string.Join(", ", new[] { a.Responsavel, a.Prazo }.Where(x => x.Length > 0));
                sb.Append("- ").Append(a.Acao).AppendLine(quem.Length > 0 ? ": " + quem : "");
            }
            sb.AppendLine();
        }
        if (ata.Secoes.FirstOrDefault(s => s.Titulo == "Próxima reunião") is { } prox)
            sb.Append("Próxima reunião: ").AppendLine(prox.Texto).AppendLine();
        return sb.ToString().TrimEnd() + "\n";
    }

    /// <summary>O que a conferência mexeu, à parte do <c>ata.md</c> que vai ao cliente.</summary>
    public static string? Observacoes(AtaGerada ata) =>
        ata.Observacoes.Count == 0 ? null
            : "## Conferência da ata\n\n" + string.Join("\n", ata.Observacoes.Select(o => "- " + o)) + "\n";

    // ------------------------------------------------------------------ apoio

    public static string SemAcento(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>"seis dias" também é "6 dias" — senão o 6 da ata parece inventado.</summary>
    public static string ComDigitos(string texto) =>
        PalavraDeNumero.Replace(texto, m => m.Value + " " + Extenso[m.Value]);

    private static string Txt(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.TryGetInt32(out int n) ? n : 0;

    private static IReadOnlyList<int> Linhas(JsonElement e)
    {
        if (!e.TryGetProperty("linhas", out var v) || v.ValueKind != JsonValueKind.Array) return [];
        return [.. v.EnumerateArray().Where(x => x.TryGetInt32(out _)).Select(x => x.GetInt32())];
    }
}
