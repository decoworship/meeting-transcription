using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingApp.Nucleo.Claude;

/// <summary>
/// A última reunião aberta no app — o "esta reunião" de quem pede algo ao Claude.
/// </summary>
/// <remarks>
/// <para>
/// <b>Um arquivo, e não uma conversa com o app aberto.</b> O MCP é outro
/// processo, aberto pelo Claude Desktop, e o app pode nem estar de pé. O app
/// escreve aqui toda vez que abre uma reunião (a ponte, no pedido
/// <c>transcricao</c>), e o MCP só lê.
/// </para>
/// <para>
/// <b>A última aberta, e não a que está na tela agora.</b> Voltar para a lista
/// não apaga: "a reunião que eu estava vendo" continua sendo aquela, e saber a
/// hora em que ela foi aberta basta para o Claude não confundir uma de ontem
/// com a de agora.
/// </para>
/// </remarks>
public static class ReuniaoAberta
{
    public static string CaminhoPadrao => Path.Combine(
        Path.GetDirectoryName(ConfiguracoesDoApp.CaminhoPadrao)!, "reuniao-aberta.json");

    /// <summary>Anota a reunião. Nunca lança: é conveniência, e abrir a reunião vem antes.</summary>
    public static void Marcar(string pastaDaGravacao, string? caminho = null,
                              DateTimeOffset? agora = null)
    {
        caminho ??= CaminhoPadrao;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            var json = new JsonObject
            {
                ["id"] = Path.GetFileName(pastaDaGravacao.TrimEnd('/', '\\')),
                ["pasta"] = pastaDaGravacao,
                ["aberta_em"] = (agora ?? DateTimeOffset.Now).ToString("o", CultureInfo.InvariantCulture),
            };
            string temporario = caminho + ".tmp";
            File.WriteAllText(temporario, json.ToJsonString());
            File.Move(temporario, caminho, overwrite: true);
        }
        catch (Exception)
        {
            // Sem a marca, o Claude só não sabe qual é "esta reunião" — e pergunta.
        }
    }

    /// <summary>O id da reunião e quando ela foi aberta, ou nulo.</summary>
    public static (string Id, DateTimeOffset AbertaEm)? Ler(string? caminho = null)
    {
        caminho ??= CaminhoPadrao;
        try
        {
            if (!File.Exists(caminho)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(caminho));
            var raiz = doc.RootElement;
            if (!raiz.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } texto)
                return null;
            var quando = raiz.TryGetProperty("aberta_em", out var a)
                         && DateTimeOffset.TryParse(a.GetString(), CultureInfo.InvariantCulture,
                                                    DateTimeStyles.None, out var q)
                ? q : DateTimeOffset.MinValue;
            return (texto, quando);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
