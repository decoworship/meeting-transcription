using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingApp.Nucleo;

namespace MeetingApp.App;

/// <summary>
/// O que a página pede ao núcleo, e o que o núcleo devolve.
/// </summary>
/// <remarks>
/// Mesma forma do contrato com os motores (docs/SIDECAR.md): JSON com um campo
/// <c>op</c> e um <c>id</c> que volta na resposta. Ter um só formato de
/// mensagem no projeto inteiro significa um só jeito de depurar quando algo não
/// chega do outro lado.
/// </remarks>
internal sealed class Pedido
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("op")] public string? Op { get; init; }
    [JsonPropertyName("gravacao")] public string? Gravacao { get; init; }
    [JsonPropertyName("vocabulario")] public string? Vocabulario { get; init; }
    [JsonPropertyName("idioma")] public string? Idioma { get; init; }
    [JsonPropertyName("modelo")] public string? Modelo { get; init; }
    [JsonPropertyName("cliente")] public string? Cliente { get; init; }
    [JsonPropertyName("projeto")] public string? Projeto { get; init; }
    [JsonPropertyName("prefs")] public PreferenciasDoProjeto? Prefs { get; init; }

    /// <summary>A transcrição inteira, como a página a tem depois de editada.</summary>
    [JsonPropertyName("conteudo")] public string? Conteudo { get; init; }
}

internal sealed class Resposta
{
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>
    /// <c>"progresso"</c> numa mensagem intermediária; ausente na final.
    /// </summary>
    /// <remarks>
    /// É o que permite uma operação longa reportar andamento sem inventar um
    /// segundo canal: a página só resolve a promessa quando o tipo não vem.
    /// </remarks>
    [JsonPropertyName("tipo")] public string? Tipo { get; init; }
    [JsonPropertyName("etapa")] public string? Etapa { get; init; }
    [JsonPropertyName("fracao")] public double? Fracao { get; init; }
    [JsonPropertyName("texto")] public string? Texto { get; init; }

    [JsonPropertyName("erro")] public string? Erro { get; init; }
    [JsonPropertyName("gravacoes")] public List<GravacaoResumo>? Gravacoes { get; init; }
    [JsonPropertyName("transcricao")] public string? Transcricao { get; init; }

    /// <summary>Cliente → seus projetos. A UI precisa dos dois para o cadastro.</summary>
    [JsonPropertyName("clientes")] public Dictionary<string, List<string>>? Clientes { get; init; }
    [JsonPropertyName("prefs")] public PreferenciasDoProjeto? Prefs { get; init; }
}

/// <summary>Uma gravação como a lista precisa mostrá-la.</summary>
/// <remarks>
/// Os avisos vêm prontos do núcleo, e não como campos crus para a página
/// interpretar: decidir que 3% de conteúdo útil é um problema é regra de
/// produto, e regra de produto fica de um lado só.
/// </remarks>
internal sealed class GravacaoResumo
{
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("caminho")] public required string Caminho { get; init; }
    [JsonPropertyName("duracao_s")] public double DuracaoS { get; init; }
    [JsonPropertyName("titulo")] public string? Titulo { get; init; }
    /// <summary>Quantos a agenda listou — convidados, não presentes.</summary>
    [JsonPropertyName("convidados")] public int Convidados { get; init; }
    [JsonPropertyName("transcrita")] public bool Transcrita { get; init; }
    [JsonPropertyName("avisos")] public List<string> Avisos { get; init; } = [];
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Pedido))]
[JsonSerializable(typeof(Resposta))]
[JsonSerializable(typeof(PreferenciasDoProjeto))]
internal sealed partial class PonteJsonBase : JsonSerializerContext;

internal static class PonteJson
{
    // Acento literal, como em todo JSON deste projeto: o nome do dispositivo e o
    // título da reunião passam por aqui.
    public static readonly PonteJsonBase Default = new(new JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>Atende os pedidos da página.</summary>
/// <param name="responder">
/// Envia uma mensagem à página. Chamado mais de uma vez por pedido quando há
/// progresso, e sempre na thread da UI — quem passa o delegate garante isso.
/// </param>
internal sealed class Ponte(string pastaDasGravacoes, Action<string> responder)
{
    private readonly Transcritor _transcritor = new(Motores.AoLadoDoExecutavel());
    private readonly Projetos _projetos = new();

    public async Task AtenderAsync(string mensagem)
    {
        Pedido? p;
        try
        {
            p = JsonSerializer.Deserialize(mensagem, PonteJson.Default.Pedido);
        }
        catch (JsonException e)
        {
            Responder(new Resposta { Id = 0, Erro = $"pedido ilegível: {e.Message}" });
            return;
        }
        if (p is null)
        {
            Responder(new Resposta { Id = 0, Erro = "pedido vazio" });
            return;
        }

        try
        {
            switch (p.Op)
            {
                case "gravacoes":
                    Responder(new Resposta { Id = p.Id, Gravacoes = Listar() });
                    break;

                case "clientes":
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "prefs":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Prefs = _projetos.Preferencias(p.Cliente ?? "", p.Projeto ?? ""),
                    });
                    break;

                case "salvar-projeto":
                    // Cliente e projeto novos nascem aqui: digitar um nome
                    // inédito e transcrever é o fluxo do app Python que o
                    // usuário pediu para manter.
                    _projetos.Salvar(p.Cliente ?? "", p.Projeto ?? "",
                                     p.Prefs ?? new PreferenciasDoProjeto());
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "salvar-transcricao":
                    SalvarTranscricao(p.Gravacao, p.Conteudo);
                    Responder(new Resposta { Id = p.Id });
                    break;

                case "transcricao":
                    Responder(new Resposta { Id = p.Id, Transcricao = LerTranscricao(p.Gravacao) });
                    break;

                case "transcrever":
                    await TranscreverAsync(p);
                    break;

                default:
                    Responder(new Resposta { Id = p.Id, Erro = $"operação desconhecida: {p.Op}" });
                    break;
            }
        }
        catch (Exception e)
        {
            // A página precisa poder mostrar o erro; derrubar a janela por causa
            // de uma pasta ilegível seria pior que a falha original.
            Responder(new Resposta { Id = p.Id, Erro = e.Message });
        }
    }

    /// <summary>Roda o pipeline, reportando andamento pelo mesmo id.</summary>
    private async Task TranscreverAsync(Pedido p)
    {
        if (p.Gravacao is not { Length: > 0 } pasta)
        {
            Responder(new Resposta { Id = p.Id, Erro = "sem gravação" });
            return;
        }

        // O pipeline é pesado e bloquearia a thread da UI, que é a mesma que
        // desenha a janela: sem isto a barra de progresso congelaria justamente
        // enquanto há progresso a mostrar.
        var resultado = await Task.Run(() => _transcritor.ExecutarAsync(
            pasta, p.Vocabulario, p.Idioma,
            modelo: p.Modelo,
            progresso: e => Responder(new Resposta
            {
                Id = p.Id,
                Tipo = "progresso",
                Etapa = e.Etapa,
                Fracao = e.Fracao,
                Texto = e.Texto,
            })));

        Responder(new Resposta { Id = p.Id, Transcricao = resultado.ParaJson() });
    }

    private Dictionary<string, List<string>> MapaDeClientes()
    {
        var mapa = new Dictionary<string, List<string>>();
        foreach (string c in _projetos.ListarClientes()) mapa[c] = _projetos.ListarProjetos(c);
        return mapa;
    }

    /// <summary>
    /// Grava a transcrição editada por cima da que estava lá.
    /// </summary>
    /// <remarks>
    /// Escrita atômica pelo mesmo motivo do resto do projeto: a alternativa é
    /// um desligamento no meio deixar o arquivo pela metade, e aqui isso
    /// custaria a revisão inteira de uma reunião.
    /// </remarks>
    private static void SalvarTranscricao(string? pasta, string? conteudo)
    {
        if (pasta is not { Length: > 0 } || conteudo is not { Length: > 0 })
            throw new InvalidOperationException("nada para salvar");

        // Conferir que é JSON antes de gravar: escrever lixo aqui apagaria a
        // transcrição, e o erro só apareceria na próxima abertura.
        using (JsonDocument.Parse(conteudo)) { }

        string destino = Path.Combine(pasta, "transcricao.json");
        string tmp = destino + ".tmp";
        File.WriteAllText(tmp, conteudo);
        File.Move(tmp, destino, overwrite: true);
    }

    private static string? LerTranscricao(string? pasta)
    {
        if (pasta is not { Length: > 0 }) return null;
        string caminho = Path.Combine(pasta, "transcricao.json");
        return File.Exists(caminho) ? File.ReadAllText(caminho) : null;
    }

    private void Responder(Resposta r) =>
        responder(JsonSerializer.Serialize(r, PonteJson.Default.Resposta));

    /// <summary>As gravações que o gravador deixou, mais recentes primeiro.</summary>
    private List<GravacaoResumo> Listar()
    {
        if (!Directory.Exists(pastaDasGravacoes)) return [];

        var lista = new List<GravacaoResumo>();
        foreach (string pasta in Directory.EnumerateDirectories(pastaDasGravacoes))
        {
            string meta = Path.Combine(pasta, "meta.json");
            if (!File.Exists(meta)) continue;

            try
            {
                lista.Add(LerResumo(pasta, meta));
            }
            catch (Exception)
            {
                // Uma gravação ilegível não pode esconder as outras da lista.
            }
        }
        return [.. lista.OrderByDescending(g => g.Nome)];
    }

    private static GravacaoResumo LerResumo(string pasta, string caminhoMeta)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(caminhoMeta));
        var raiz = doc.RootElement;

        double duracao = raiz.TryGetProperty("duration_s", out var d) ? d.GetDouble() : 0;
        var avisos = new List<string>();

        if (raiz.TryGetProperty("tracks", out var faixas))
        {
            foreach (var faixa in faixas.EnumerateObject())
            {
                string nome = faixa.Name == "mic" ? "microfone" : "áudio do sistema";
                var t = faixa.Value;

                if (t.TryGetProperty("no_audio", out var sem) && sem.GetBoolean())
                    avisos.Add($"O {nome} não teve áudio nenhum.");
                else if (t.TryGetProperty("usable_pct", out var util) && util.GetDouble() < 20)
                    avisos.Add($"O {nome} tem só {util.GetDouble():F0}% de conteúdo útil.");

                // Campo novo do gravador nativo, que até agora ninguém lia.
                if (t.TryGetProperty("disconnected", out var caiu) && caiu.GetBoolean())
                    avisos.Add($"O dispositivo do {nome} caiu durante a gravação.");
            }
        }

        string? titulo = null;
        int convidados = 0;
        if (raiz.TryGetProperty("meeting", out var reuniao))
        {
            if (reuniao.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                titulo = t.GetString();
            if (reuniao.TryGetProperty("attendees", out var a) && a.ValueKind == JsonValueKind.Array)
                convidados = a.GetArrayLength();
        }

        return new GravacaoResumo
        {
            Nome = Path.GetFileName(pasta),
            Caminho = pasta,
            DuracaoS = duracao,
            Titulo = titulo,
            Convidados = convidados,
            Transcrita = File.Exists(Path.Combine(pasta, "transcricao.json")),
            Avisos = avisos,
        };
    }
}
