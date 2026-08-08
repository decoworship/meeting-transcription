using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Agenda;

/// <summary>
/// O fluxo OAuth interativo: abre o navegador uma vez e guarda o token.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que TcpListener e não HttpListener.</b> O <c>HttpListener</c> exige
/// reserva de URL no Windows (<c>netsh http add urlacl</c>) e lança "acesso
/// negado" para processo sem elevação — e a bandeja roda como usuário comum.
/// Aqui só é preciso ler uma linha de requisição e devolver uma página; falar
/// esse HTTP à mão custa poucas linhas e nenhuma permissão.
/// </para>
/// <para>
/// <b>PKCE mesmo com client_secret.</b> Um app de desktop não tem como esconder
/// o segredo, então ele não é segredo de verdade; o <c>code_verifier</c> é o que
/// impede que um código interceptado no loopback seja trocado por token.
/// </para>
/// </remarks>
public static class Autorizacao
{
    /// <summary>
    /// Autoriza, salva o token e devolve o access token — ou <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Apaga o token antes de começar: com um válido em disco, trocar de conta
    /// seria impossível.
    /// </remarks>
    public static async Task<string?> AutorizarAsync(CancellationToken ct = default)
    {
        var cliente = LerSegredo();
        if (cliente?.ClientId is not { Length: > 0 }) return null;

        ClienteDaAgenda.Desconectar();

        var (verificador, desafio) = ParPkce();
        string estado = Aleatorio(24);

        // Porta 0: o sistema escolhe uma livre. O Google aceita qualquer porta
        // em redirect de loopback, o que evita depender de uma fixa que outro
        // programa pode estar ocupando.
        var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        int porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
        string redirect = $"http://127.0.0.1:{porta}/";

        try
        {
            string url = (cliente.AuthUri ?? "https://accounts.google.com/o/oauth2/auth")
                + "?response_type=code"
                + $"&client_id={Uri.EscapeDataString(cliente.ClientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(redirect)}"
                + $"&scope={Uri.EscapeDataString(ClienteDaAgenda.Escopo)}"
                + $"&state={estado}"
                + $"&code_challenge={desafio}&code_challenge_method=S256"
                // access_type=offline é o que faz vir refresh_token; sem ele a
                // autorização morre em uma hora e o usuário reautoriza sempre.
                + "&access_type=offline"
                // "select_account" força o seletor. Só "consent" reaproveita a
                // sessão do navegador e reconecta a mesma conta — inútil para
                // quem quer trocar da conta pessoal para a da empresa.
                + "&prompt=" + Uri.EscapeDataString("select_account consent");

            AbrirNavegador(url);

            string? codigo = await EsperarCodigoAsync(ouvinte, estado, ct);
            if (codigo is null) return null;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var resp = await http.PostAsync(
                cliente.TokenUri ?? "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(
                [
                    new("grant_type", "authorization_code"),
                    new("code", codigo),
                    new("code_verifier", verificador),
                    new("client_id", cliente.ClientId),
                    new("client_secret", cliente.ClientSecret ?? ""),
                    new("redirect_uri", redirect),
                ]), ct);

            var token = JsonSerializer.Deserialize(
                await resp.Content.ReadAsStringAsync(ct), AgendaJson.Default.RespostaDeToken);
            if (token?.AccessToken is not { Length: > 0 }) return null;

            await SalvarTokenAsync(token, cliente, ct);
            return token.AccessToken;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            ouvinte.Stop();
        }
    }

    /// <summary>
    /// No formato do <c>google-auth</c>, para o gravador Python continuar lendo
    /// o mesmo arquivo enquanto os dois coexistem.
    /// </summary>
    private static async Task SalvarTokenAsync(RespostaDeToken token, DadosDoCliente cliente,
                                               CancellationToken ct)
    {
        var raiz = new JsonObject
        {
            ["token"] = token.AccessToken,
            ["refresh_token"] = token.RefreshToken,
            ["token_uri"] = cliente.TokenUri ?? "https://oauth2.googleapis.com/token",
            ["client_id"] = cliente.ClientId,
            ["client_secret"] = cliente.ClientSecret,
            ["scopes"] = new JsonArray(ClienteDaAgenda.Escopo),
            ["universe_domain"] = "googleapis.com",
            ["account"] = "",
            ["expiry"] = Credenciais.Expiry(DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn)),
        };
        await Credenciais.SalvarAsync(raiz, ct);
    }

    /// <summary>Lê a primeira requisição, extrai o código e responde ao navegador.</summary>
    private static async Task<string?> EsperarCodigoAsync(TcpListener ouvinte, string estado,
                                                          CancellationToken ct)
    {
        // Cinco minutos: tempo de escolher a conta e ler a tela de consentimento
        // sem deixar o ouvinte aberto para sempre se a pessoa desistir.
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromMinutes(5));

        using var conexao = await ouvinte.AcceptTcpClientAsync(limite.Token);
        using var fluxo = conexao.GetStream();

        var buffer = new byte[4096];
        int lidos = await fluxo.ReadAsync(buffer, limite.Token);
        string pedido = Encoding.UTF8.GetString(buffer, 0, lidos);

        // "GET /?code=...&state=... HTTP/1.1"
        string alvo = pedido.Split('\n')[0].Split(' ').ElementAtOrDefault(1) ?? "";
        var query = Query(alvo);

        bool ok = query.TryGetValue("code", out string? codigo) && codigo.Length > 0
                  && query.GetValueOrDefault("state") == estado;

        await ResponderAsync(fluxo, ok
            ? "Conta conectada. Pode fechar esta aba e voltar ao gravador."
            : "Autorizacao nao concluida. Volte ao gravador e tente de novo.", limite.Token);

        // O state protege contra um código injetado por outra página aberta no
        // mesmo navegador; sem conferir, qualquer aba poderia plantar um.
        return ok ? codigo : null;
    }

    /// <summary>
    /// Query string em dicionário. Escrito à mão para não arrastar o
    /// <c>System.Web</c> por causa de duas chaves.
    /// </summary>
    internal static Dictionary<string, string> Query(string alvo)
    {
        var mapa = new Dictionary<string, string>();
        int i = alvo.IndexOf('?');
        if (i < 0) return mapa;

        foreach (string par in alvo[(i + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int igual = par.IndexOf('=');
            if (igual <= 0) continue;
            mapa[Uri.UnescapeDataString(par[..igual])] =
                Uri.UnescapeDataString(par[(igual + 1)..]);
        }
        return mapa;
    }

    private static async Task ResponderAsync(NetworkStream fluxo, string mensagem,
                                             CancellationToken ct)
    {
        string corpo = $"<!doctype html><meta charset=utf-8>"
            + "<body style=\"font-family:system-ui;padding:3rem;text-align:center\">"
            + $"<p>{mensagem}</p></body>";
        byte[] bytes = Encoding.UTF8.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n"
            + $"Content-Length: {Encoding.UTF8.GetByteCount(corpo)}\r\nConnection: close\r\n\r\n"
            + corpo);
        await fluxo.WriteAsync(bytes, ct);
        await fluxo.FlushAsync(ct);
    }

    private static DadosDoCliente? LerSegredo()
    {
        try
        {
            var s = JsonSerializer.Deserialize(File.ReadAllText(Caminhos.SegredoDoCliente),
                                               AgendaJson.Default.SegredoDoCliente);
            return s?.Installed ?? s?.Web;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void AbrirNavegador(string url)
    {
        try
        {
            // UseShellExecute é o que faz o Windows abrir no navegador padrão;
            // sem isso o .NET tentaria executar a URL como se fosse um programa.
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Sem navegador o fluxo expira sozinho no timeout do ouvinte.
        }
    }

    private static (string verificador, string desafio) ParPkce()
    {
        string verificador = Aleatorio(64);
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(verificador));
        return (verificador, Base64Url(hash));
    }

    private static string Aleatorio(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] dados) =>
        Convert.ToBase64String(dados).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
