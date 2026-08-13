using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Agenda;

/// <summary>Onde ficam as credenciais do Google. Nunca no repositório.</summary>
public static class Caminhos
{
    public static string Base => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".meeting-recorder");

    public static string SegredoDoCliente => Path.Combine(Base, "google_client_secret.json");
    public static string Token => Path.Combine(Base, "google_token.json");

    /// <summary>
    /// Qual conta o token representa. Guardado à parte porque a bandeja mostra
    /// isso a cada abertura de menu, e perguntar à API seria uma chamada de rede
    /// por clique.
    /// </summary>
    public static string Conta => Path.Combine(Base, "google_account.json");
}

/// <summary>Falha ao renovar o token — distinta de "nunca autorizou".</summary>
public sealed class TokenMortoException(string mensagem) : Exception(mensagem);

/// <summary>
/// O token do Google em disco, no mesmo formato do gravador Python.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mesmo arquivo, mesmo formato.</b> É o que faz o gravador novo herdar a
/// autorização que já existe: quem já usa o Python não reautoriza nada. Isso
/// obriga a escrever de volta num formato que o <c>google-auth</c> ainda leia.
/// </para>
/// <para>
/// Por isso a atualização é feita sobre o JSON existente, mexendo só em
/// <c>token</c> e <c>expiry</c>, em vez de reserializar um objeto tipado: assim
/// nenhuma chave que este código não modela (<c>universe_domain</c>,
/// <c>account</c>, o que o Google acrescentar depois) se perde no caminho.
/// </para>
/// </remarks>
public sealed class Credenciais(HttpClient http)
{
    /// <summary>Renova um pouco antes de expirar, para não correr atrás.</summary>
    private static readonly TimeSpan Folga = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Um access token válido, renovando se preciso.
    /// </summary>
    /// <exception cref="TokenMortoException">
    /// Havia token e a renovação falhou — merece aviso, ao contrário de nunca
    /// ter autorizado.
    /// </exception>
    public async Task<string?> AccessTokenAsync(CancellationToken ct)
    {
        if (!File.Exists(Caminhos.Token)) return null;

        JsonNode? raiz;
        try
        {
            raiz = JsonNode.Parse(await File.ReadAllTextAsync(Caminhos.Token, ct));
        }
        catch (Exception e)
        {
            throw new TokenMortoException($"token ilegível: {e.Message}");
        }
        if (raiz is null) throw new TokenMortoException("token vazio");

        string? atual = (string?)raiz["token"];
        if (atual is { Length: > 0 } && !Expirou(raiz)) return atual;

        string? refresh = (string?)raiz["refresh_token"];
        string? clientId = (string?)raiz["client_id"];
        string? clientSecret = (string?)raiz["client_secret"];
        string tokenUri = (string?)raiz["token_uri"] ?? "https://oauth2.googleapis.com/token";

        if (refresh is null or "" || clientId is null or "" || clientSecret is null or "")
            throw new TokenMortoException("token sem refresh_token utilizável");

        var resp = await http.PostAsync(tokenUri, new FormUrlEncodedContent(
        [
            new("grant_type", "refresh_token"),
            new("refresh_token", refresh),
            new("client_id", clientId),
            new("client_secret", clientSecret),
        ]), ct);

        string corpo = await resp.Content.ReadAsStringAsync(ct);
        RespostaDeToken? novo;
        try { novo = JsonSerializer.Deserialize(corpo, AgendaJson.Default.RespostaDeToken); }
        catch (JsonException) { novo = null; }

        if (novo?.AccessToken is not { Length: > 0 })
        {
            // Caso clássico: app em "Testing" no Google Cloud, onde o Google
            // expira todo refresh token em 7 dias. Publicar em Production
            // resolve. Precisa virar aviso, não degradação silenciosa.
            string motivo = novo?.ErrorDescription ?? novo?.Error ?? $"HTTP {(int)resp.StatusCode}";
            throw new TokenMortoException(motivo);
        }

        raiz["token"] = novo.AccessToken;
        raiz["expiry"] = Expiry(DateTimeOffset.UtcNow.AddSeconds(novo.ExpiresIn));
        // O Google só reemite refresh_token em alguns casos; quando vem, é o que
        // vale dali em diante.
        if (novo.RefreshToken is { Length: > 0 }) raiz["refresh_token"] = novo.RefreshToken;

        await SalvarAsync(raiz, ct);
        return novo.AccessToken;
    }

    private static bool Expirou(JsonNode raiz)
    {
        string? bruto = (string?)raiz["expiry"];
        if (string.IsNullOrWhiteSpace(bruto)) return true;   // sem saber, renova

        // O google-auth grava UTC sem fuso e às vezes sem o "Z"; qualquer uma
        // das formas tem que ser entendida como UTC, e não como hora local.
        string limpo = bruto.TrimEnd('Z');
        if (!DateTime.TryParse(limpo, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var quando))
            return true;

        var expiraEm = new DateTimeOffset(DateTime.SpecifyKind(quando, DateTimeKind.Utc));
        return DateTimeOffset.UtcNow + Folga >= expiraEm;
    }

    /// <summary>
    /// No formato que o <c>google-auth</c> lê: ele corta o "Z" e a fração antes
    /// de fazer <c>strptime</c>, então segundos inteiros com "Z" é o seguro.
    /// </summary>
    internal static string Expiry(DateTimeOffset quando) =>
        quando.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss") + "Z";

    internal static async Task SalvarAsync(JsonNode raiz, CancellationToken ct)
    {
        Directory.CreateDirectory(Caminhos.Base);

        // Escrita atômica: um desligamento no meio não pode deixar o arquivo de
        // credenciais pela metade — seria preciso reautorizar do zero.
        string tmp = Caminhos.Token + ".tmp";
        await File.WriteAllTextAsync(tmp, raiz.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
        }), ct);
        File.Move(tmp, Caminhos.Token, overwrite: true);
    }
}
