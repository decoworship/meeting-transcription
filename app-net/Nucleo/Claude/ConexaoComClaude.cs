using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingApp.Nucleo.Claude;

/// <summary>O que os Ajustes mostram sobre o Claude Desktop.</summary>
/// <param name="ClaudeEncontrado">Há um Claude Desktop instalado nesta conta do Windows.</param>
/// <param name="Conectado">A configuração dele tem a entrada do PulseMeet.</param>
/// <param name="NesteExecutavel">
/// E a entrada aponta para este executável — e não para um que foi movido ou apagado.
/// </param>
public sealed record EstadoDaConexao(bool ClaudeEncontrado, bool Conectado, bool NesteExecutavel,
                                     string? Arquivo, string? Erro = null);

/// <summary>
/// A entrada do PulseMeet no <c>claude_desktop_config.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>O arquivo não é nosso, e guarda mais do que servidores MCP.</b> No
/// Claude Desktop da Store ele mora fora do pacote e carrega as preferências do
/// app inteiro (medido em 22/10/2026: sessões, pastas confiáveis, extensões).
/// Por isso a regra é mexer numa chave só: lê, troca o
/// <c>mcpServers.pulsemeet</c>, escreve o resto como estava. Um JSON que não se
/// lê <b>não é reescrito</b> — o erro volta para a tela, e o arquivo fica como
/// estava.
/// </para>
/// <para>
/// <b>Três lugares possíveis, na ordem em que o Claude os usa.</b> A versão da
/// Store passou a guardar os dados em <c>%LOCALAPPDATA%\Claude-Data\claude_*</c>
/// e deixa uma marca <c>.user-data-left*</c> na pasta virtualizada antiga; a
/// antiga é <c>Packages\Claude_*\LocalCache\Roaming\Claude</c>; o instalador
/// clássico usa <c>%APPDATA%\Claude</c>. Vale o primeiro arquivo que existe, e
/// sem nenhum, a primeira pasta que existe.
/// </para>
/// </remarks>
public static class ConexaoComClaude
{
    /// <summary>A chave em <c>mcpServers</c>. É o nome que o Claude mostra nas ferramentas.</summary>
    public const string NomeDoServidor = "pulsemeet";

    public const string NomeDoArquivo = "claude_desktop_config.json";

    /// <summary>A cópia de antes da primeira mudança, ao lado do original.</summary>
    public const string SufixoDaCopia = ".antes-do-pulsemeet";

    private static readonly JsonDocumentOptions Leitura = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions Escrita = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Onde o Claude Desktop desta conta lê a configuração, ou nulo se ele não está instalado.</summary>
    public static string? ArquivoDeConfiguracao(string? localAppData = null, string? appData = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        appData ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var candidatos = Candidatos(localAppData, appData).ToList();
        return candidatos.FirstOrDefault(File.Exists)
               ?? candidatos.FirstOrDefault(c => Directory.Exists(Path.GetDirectoryName(c)));
    }

    private static IEnumerable<string> Candidatos(string local, string roaming)
    {
        string dados = Path.Combine(local, "Claude-Data");
        if (Directory.Exists(dados))
            foreach (string d in Directory.EnumerateDirectories(dados, "claude_*").Order(StringComparer.Ordinal))
                yield return Path.Combine(d, NomeDoArquivo);

        string pacotes = Path.Combine(local, "Packages");
        if (Directory.Exists(pacotes))
            foreach (string d in Directory.EnumerateDirectories(pacotes, "Claude_*").Order(StringComparer.Ordinal))
            {
                string pasta = Path.Combine(d, "LocalCache", "Roaming", "Claude");
                if (Directory.Exists(pasta) && Directory.EnumerateFiles(pasta, ".user-data-left*").Any())
                    continue;
                yield return Path.Combine(pasta, NomeDoArquivo);
            }

        yield return Path.Combine(roaming, "Claude", NomeDoArquivo);
    }

    public static EstadoDaConexao Estado(string? arquivo, string executavel)
    {
        if (arquivo is null) return new(false, false, false, null);
        if (!File.Exists(arquivo)) return new(true, false, false, arquivo);

        try
        {
            var entrada = (Ler(arquivo)["mcpServers"] as JsonObject)?[NomeDoServidor] as JsonObject;
            if (entrada is null) return new(true, false, false, arquivo);

            bool aqui = ServidorMcp.Texto(entrada["command"]) is { } comando
                        && MesmoCaminho(comando, executavel);
            return new(true, true, aqui, arquivo);
        }
        catch (Exception e)
        {
            return new(true, false, false, arquivo, e.Message);
        }
    }

    /// <summary>Põe (ou repõe) a entrada apontando para <paramref name="executavel"/>.</summary>
    public static void Conectar(string arquivo, string executavel)
    {
        var raiz = File.Exists(arquivo) ? Ler(arquivo) : new JsonObject();

        if (raiz["mcpServers"] is not JsonObject servidores)
        {
            if (raiz.ContainsKey("mcpServers"))
                throw new InvalidOperationException(
                    "O mcpServers da configuração do Claude não tem o formato esperado; não mexi nela.");
            servidores = new JsonObject();
            raiz["mcpServers"] = servidores;
        }

        servidores[NomeDoServidor] = new JsonObject
        {
            ["command"] = executavel,
            ["args"] = new JsonArray("--mcp"),
        };
        Escrever(arquivo, raiz);
    }

    /// <summary>Tira a entrada. O resto do arquivo fica como estava.</summary>
    public static void Desconectar(string arquivo)
    {
        if (!File.Exists(arquivo)) return;
        var raiz = Ler(arquivo);
        if (raiz["mcpServers"] is JsonObject servidores && servidores.Remove(NomeDoServidor))
            Escrever(arquivo, raiz);
    }

    private static JsonObject Ler(string arquivo)
    {
        string texto = File.ReadAllText(arquivo);
        if (texto.Trim().Length == 0) return new JsonObject();

        try
        {
            return JsonNode.Parse(texto, documentOptions: Leitura) as JsonObject
                   ?? throw new InvalidOperationException(
                       "A configuração do Claude não é um objeto JSON; não mexi nela.");
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException(
                $"A configuração do Claude não é um JSON válido ({e.Message}); não mexi nela.", e);
        }
    }

    /// <summary>
    /// Pelo temporário e com cópia de antes: um arquivo pela metade custaria
    /// as preferências do Claude, e não só a conexão.
    /// </summary>
    private static void Escrever(string arquivo, JsonObject raiz)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        string copia = arquivo + SufixoDaCopia;
        if (File.Exists(arquivo) && !File.Exists(copia)) File.Copy(arquivo, copia);

        string temporario = arquivo + ".pulsemeet.tmp";
        File.WriteAllText(temporario, raiz.ToJsonString(Escrita));
        File.Move(temporario, arquivo, overwrite: true);
    }

    private static bool MesmoCaminho(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
