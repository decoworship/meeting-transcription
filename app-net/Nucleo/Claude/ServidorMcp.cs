using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingApp.Nucleo.Claude;

/// <summary>
/// O PulseMeet como servidor MCP: o Claude Desktop abre <c>PulseMeet.exe --mcp</c>
/// e conversa com ele por stdin/stdout.
/// </summary>
/// <remarks>
/// <para>
/// <b>Escrito à mão, e não com o SDK.</b> O protocolo que o app precisa é
/// JSON-RPC 2.0, uma mensagem por linha, e nove métodos. O SDK C# descobre as
/// ferramentas por reflexão, e o binário publicado sai com <c>PublishTrimmed</c>
/// — que desliga a reflexão do <c>System.Text.Json</c>. É a falha que compila,
/// passa nos testes e some no binário entregue, a mesma família do
/// <c>EmbeddedResource</c> com barra invertida. Aqui tudo é <see cref="JsonNode"/>,
/// que não usa reflexão.
/// </para>
/// <para>
/// <b>Nada sai por fora da resposta.</b> O stdout é o protocolo: um
/// <c>Console.WriteLine</c> perdido em qualquer lugar do caminho corrompe a
/// conversa, como o torch e o pyannote fazem com os sidecars (CLAUDE.md,
/// "Armadilhas medidas"). Diagnóstico vai para o stderr, que o Claude Desktop
/// guarda no log dele.
/// </para>
/// <para>
/// O que cada método responde está em docs/INTEGRACAO-CLAUDE.md.
/// </para>
/// </remarks>
public sealed class ServidorMcp
{
    /// <summary>
    /// As versões do protocolo que este servidor fala, da mais nova para a mais velha.
    /// </summary>
    /// <remarks>
    /// Ele usa só o que as quatro têm em comum — ferramentas, recursos e prompts
    /// com texto —, então aceitar a que o cliente pedir é seguro. Uma versão que
    /// não está aqui recebe a mais nova, e o cliente decide se continua.
    /// </remarks>
    public static readonly string[] Versoes = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        // Acento literal, como todo JSON deste projeto.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly FerramentasDoMcp _ferramentas;
    private readonly string _versaoDoApp;

    public ServidorMcp(Acervo acervo, string versaoDoApp, string? caminhoDaReuniaoAberta = null)
    {
        _ferramentas = new FerramentasDoMcp(acervo, caminhoDaReuniaoAberta);
        _versaoDoApp = versaoDoApp;
    }

    /// <summary>Lê pedidos até a entrada fechar — é assim que o Claude Desktop encerra o servidor.</summary>
    public async Task RodarAsync(TextReader entrada, TextWriter saida, CancellationToken ct = default)
    {
        while (await entrada.ReadLineAsync(ct) is { } linha)
        {
            if (linha.Trim().Length == 0) continue;
            if (Responder(linha) is { } resposta)
            {
                await saida.WriteLineAsync(resposta);
                await saida.FlushAsync(ct);
            }
        }
    }

    /// <summary>Uma linha de pedido, uma linha de resposta — ou nada, para notificação.</summary>
    public string? Responder(string linha)
    {
        JsonNode? pedido;
        try
        {
            pedido = JsonNode.Parse(linha);
        }
        catch (JsonException)
        {
            return Erro(null, -32700, "a mensagem não é JSON").ToJsonString(Opcoes);
        }

        // Lote: o 2025-03-26 permitia, o 2025-06-18 tirou. Responder custa pouco.
        if (pedido is JsonArray lote)
        {
            var respostas = new JsonArray();
            foreach (var item in lote)
                if (Tratar(item as JsonObject) is JsonNode r) respostas.Add(r);
            return respostas.Count > 0 ? respostas.ToJsonString(Opcoes) : null;
        }
        return Tratar(pedido as JsonObject)?.ToJsonString(Opcoes);
    }

    private JsonObject? Tratar(JsonObject? pedido)
    {
        if (pedido is null) return Erro(null, -32600, "pedido inválido");

        // Sem método é resposta do cliente a algo que não perguntamos; sem id é
        // notificação (o "initialized", o "cancelled"). Nenhuma das duas se responde.
        if (Texto(pedido["method"]) is not { } metodo || !pedido.ContainsKey("id")) return null;

        var id = pedido["id"]?.DeepClone();
        var p = pedido["params"] as JsonObject ?? new JsonObject();
        try
        {
            JsonNode resultado = metodo switch
            {
                "initialize" => Inicializar(p),
                "ping" => new JsonObject(),
                "tools/list" => new JsonObject { ["tools"] = FerramentasDoMcp.Definicoes() },
                "tools/call" => _ferramentas.Chamar(Texto(p["name"]), p["arguments"] as JsonObject ?? new JsonObject()),
                "resources/list" => new JsonObject { ["resources"] = _ferramentas.Recursos() },
                "resources/templates/list" => new JsonObject
                {
                    ["resourceTemplates"] = FerramentasDoMcp.ModelosDeRecurso(),
                },
                "resources/read" => _ferramentas.LerRecurso(Texto(p["uri"])),
                "prompts/list" => new JsonObject { ["prompts"] = FerramentasDoMcp.Prompts() },
                "prompts/get" => FerramentasDoMcp.Prompt(Texto(p["name"]), p["arguments"] as JsonObject ?? new JsonObject()),
                _ => throw new ErroDoMcp(-32601, $"método desconhecido: {metodo}"),
            };
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = resultado };
        }
        catch (ErroDoMcp e)
        {
            return Erro(id, e.Codigo, e.Message);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[mcp] {metodo}: {e}");
            return Erro(id, -32603, e.Message);
        }
    }

    private JsonObject Inicializar(JsonObject p)
    {
        string pedida = Texto(p["protocolVersion"]) ?? Versoes[0];
        return new JsonObject
        {
            ["protocolVersion"] = Versoes.Contains(pedida) ? pedida : Versoes[0],
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject(),
                ["resources"] = new JsonObject(),
                ["prompts"] = new JsonObject(),
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "pulsemeet",
                ["title"] = Marca.Nome,
                ["version"] = _versaoDoApp,
            },
            ["instructions"] = FerramentasDoMcp.Instrucoes,
        };
    }

    private static JsonObject Erro(JsonNode? id, int codigo, string mensagem) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = codigo, ["message"] = mensagem },
    };

    /// <summary>O texto de um nó, ou nulo se ele não for texto.</summary>
    internal static string? Texto(JsonNode? no) =>
        no is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>Um erro que vira <c>error</c> na resposta, com o código do JSON-RPC.</summary>
public sealed class ErroDoMcp(int codigo, string mensagem) : Exception(mensagem)
{
    public int Codigo { get; } = codigo;
}
