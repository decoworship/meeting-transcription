using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MeetingRecorder.Core;

/// <summary>Uma mensagem da extensão, com só o que o gravador usa.</summary>
/// <param name="App">
/// <c>"meet"</c>, <c>"teams"</c> ou <c>"zoom"</c> — de que página veio o estado. O
/// Zoom na web (extensão 1.3.0) serve só ao vídeo: o mute dele não foi medido, e
/// o gravador não o segue.
/// </param>
/// <param name="Diagnostico">
/// O que a página achou do botão do microfone, para o registro.log: é por ele
/// que se descobre o que mudou quando o Google ou a Microsoft mexem na página.
/// </param>
/// <param name="Aba">A aba da reunião, quando há uma e a extensão é a 1.2.0 ou mais nova.</param>
public sealed record MensagemDaExtensao(EstadoDaChamada? Estado, string? App, string? Diagnostico,
                                        AbaDaReuniao? Aba = null);

/// <summary>
/// O que a extensão de navegador e o PulseMeet dizem um ao outro.
/// </summary>
/// <remarks>
/// <para>
/// A extensão manda <c>{"tipo":"estado","app":…,"em_reuniao":…,"mudo":…}</c> a
/// cada mudança e a cada dez segundos, <c>{"tipo":"diagnostico",…}</c> quando o
/// que ela acha na página muda, e <c>{"tipo":"oi"}</c> para o Chrome não a pôr
/// para dormir. Serve ao Meet e ao Teams no navegador — o Teams desktop é
/// outro caminho, o <see cref="ProtocoloDoTeams"/>. O PulseMeet manda <c>{"tipo":"mutar","mudo":…}</c> — o
/// estado pedido, e não "inverter": a extensão vê o botão e só clica se for
/// diferente.
/// </para>
/// <para>
/// <b>Desde a extensão 1.2.0, o estado leva a aba</b>:
/// <c>"aba":{"titulo":…,"na_frente":…}</c>. É por ela que o vídeo acha a janela
/// da reunião (<see cref="JanelaDaReuniao"/>). Extensão antiga não manda, e o
/// vídeo fica esperando, sem quebrar o mute.
/// </para>
/// <para>
/// O código da extensão está em <c>extensao/meet/</c>, e a porta tem de ser a
/// mesma do <c>fundo.js</c>.
/// </para>
/// </remarks>
public static class ProtocoloDaExtensao
{
    public const int Porta = 47821;

    /// <summary>
    /// O id que o Chrome dá à extensão, pela chave pública do <c>manifest.json</c>.
    /// </summary>
    /// <remarks>
    /// Fixo em qualquer máquina porque a chave está no manifesto. É a origem
    /// que o <see cref="ServidorDaExtensao"/> aceita: um site aberto no
    /// navegador também pode abrir um WebSocket para 127.0.0.1, mas não pode
    /// dizer que é esta extensão. Um teste confere que este id é o da chave.
    /// </remarks>
    public const string Id = "kdfamiddmdojkgiopaneghclddnoeaim";

    public const string Origem = "chrome-extension://" + Id;

    public static MensagemDaExtensao? Ler(string json)
    {
        JsonObject? o;
        try { o = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
        if (o is null) return null;

        string? app = Texto(o, "app") is "meet" or "teams" or "zoom" ? Texto(o, "app") : null;
        switch (Texto(o, "tipo"))
        {
            case "estado":
                bool emReuniao = Bool(o, "em_reuniao");
                // A página não pede permissão para nada: na reunião, dá para mutar.
                AbaDaReuniao? aba = emReuniao && o["aba"] is JsonObject a && Texto(a, "titulo") is { } titulo
                    ? new AbaDaReuniao(titulo.Length > 300 ? titulo[..300] : titulo, Bool(a, "na_frente"), app)
                    : null;
                return new MensagemDaExtensao(
                    new EstadoDaChamada(EmReuniao: emReuniao, Mudo: emReuniao && Bool(o, "mudo"),
                                        PodeMutar: emReuniao, PodeParear: false),
                    app, null, aba);
            case "diagnostico" when Texto(o, "texto") is { } texto:
                return new MensagemDaExtensao(null, app, texto.Length > 1000 ? texto[..1000] : texto);
            default:
                return null;
        }
    }

    private static string? Texto(JsonObject o, string chave) =>
        o[chave] is JsonValue v && v.TryGetValue(out string? s) && s is { Length: > 0 } ? s : null;

    public static string Mutar(bool mudo) =>
        new JsonObject { ["tipo"] = "mutar", ["mudo"] = mudo }.ToJsonString();

    private static bool Bool(JsonObject o, string chave) =>
        o[chave] is JsonValue v && v.TryGetValue(out bool b) && b;
}

/// <summary>
/// A porta local onde a extensão de navegador — Meet e Teams — se conecta.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ao contrário do Teams desktop, aqui o PulseMeet é quem escuta.</b> A
/// reunião é uma página, e quem sai dela para falar com outro programa é a
/// extensão. Só
/// <c>127.0.0.1</c>, e só a origem da extensão (<see cref="ProtocoloDaExtensao.Origem"/>).
/// </para>
/// <para>
/// <b>O aperto de mão é feito à mão</b>, num <see cref="TcpListener"/>, e não
/// pelo <c>HttpListener</c>: no Windows ele passa pelo http.sys, que exige
/// reserva de URL feita como administrador para escutar fora de
/// <c>localhost</c>. São vinte linhas, e assim o teste roda igual no Linux.
/// </para>
/// <para>
/// Uma extensão por vez — a conexão nova substitui a velha, que é o que
/// acontece quando o Chrome reinicia o service worker. Bônus, nunca
/// dependência: porta ocupada ou extensão ausente deixam o mute à mão, como
/// sempre.
/// </para>
/// </remarks>
public sealed class ServidorDaExtensao(int porta = ProtocoloDaExtensao.Porta,
                                       string origem = ProtocoloDaExtensao.Origem) : IDisposable
{
    private static readonly TimeSpan PrazoDoAperto = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EsperaDaPorta = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _envio = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _laco;
    private volatile WebSocket? _ws;

    /// <summary>Um estado ou um diagnóstico da extensão. Sai da thread da conexão.</summary>
    public event Action<MensagemDaExtensao>? AoReceber;

    /// <summary>A extensão conectou (verdadeiro) ou saiu (falso). Sai da thread da conexão.</summary>
    public event Action<bool>? AoMudarConexao;

    public bool Conectado => _ws?.State == WebSocketState.Open;

    /// <summary>O último problema — porta ocupada, origem recusada —, para o registro.</summary>
    public string? UltimaFalha { get; private set; }

    public void Ligar()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _laco = Task.Run(() => EscutarAsync(ct));
    }

    public void Desligar()
    {
        if (_cts is not { } cts) return;
        cts.Cancel();
        _ws?.Abort();
        try { _laco?.Wait(TimeSpan.FromSeconds(2)); } catch { /* cancelado é o fim */ }
        cts.Dispose();
        _cts = null;
        _laco = null;
    }

    /// <summary>Pede à extensão que deixe o microfone da reunião assim. Sem ela, nada.</summary>
    public async Task MutarAsync(bool mudo)
    {
        if (_ws is not { State: WebSocketState.Open } ws) return;
        await _envio.WaitAsync();
        try
        {
            await ws.SendAsync(Encoding.UTF8.GetBytes(ProtocoloDaExtensao.Mutar(mudo)),
                               WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            UltimaFalha = e.Message;
        }
        finally { _envio.Release(); }
    }

    private async Task EscutarAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var ouvinte = new TcpListener(IPAddress.Loopback, porta);
            try
            {
                ouvinte.Start();
                using var parar = ct.Register(ouvinte.Stop);
                while (!ct.IsCancellationRequested)
                {
                    var cliente = await ouvinte.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => AtenderAsync(cliente, ct), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                // Porta ocupada por outro programa: tentar de novo mais tarde,
                // porque ele pode fechar.
                UltimaFalha = e.Message;
            }
            finally { ouvinte.Stop(); }

            try { await Task.Delay(EsperaDaPorta, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task AtenderAsync(TcpClient cliente, CancellationToken ct)
    {
        using var _ = cliente;
        var fluxo = cliente.GetStream();
        WebSocket ws;
        try
        {
            using var prazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
            prazo.CancelAfter(PrazoDoAperto);
            if (await ApertarAsync(fluxo, prazo.Token) is not { } aceito) return;
            ws = aceito;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or SocketException)
        {
            return;
        }

        var antiga = _ws;
        _ws = ws;
        antiga?.Abort();
        Avisar(() => AoMudarConexao?.Invoke(true));
        try
        {
            await LerAsync(ws, ct);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException) { }
        finally
        {
            // Substituída por uma conexão nova, ela sai calada: quem avisa é a nova.
            if (_ws == ws)
            {
                _ws = null;
                Avisar(() => AoMudarConexao?.Invoke(false));
            }
            ws.Dispose();
        }
    }

    /// <summary>O aperto de mão do RFC 6455, ou nulo se o pedido não é da extensão.</summary>
    private async Task<WebSocket?> ApertarAsync(NetworkStream fluxo, CancellationToken ct)
    {
        var cabecalho = new StringBuilder();
        var um = new byte[1];
        // Um byte por vez até a linha em branco: o que vem depois já é
        // WebSocket, e não pode ter sido lido para um buffer daqui.
        while (!cabecalho.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (cabecalho.Length > 8192 || await fluxo.ReadAsync(um, ct) == 0) return null;
            cabecalho.Append((char)um[0]);
        }

        var linhas = cabecalho.ToString().Split("\r\n");
        var campos = linhas.Skip(1)
            .Select(l => l.Split(':', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);

        bool ehWebSocket = linhas[0].StartsWith("GET ", StringComparison.Ordinal)
            && campos.TryGetValue("Upgrade", out var upgrade)
            && upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase)
            && campos.ContainsKey("Sec-WebSocket-Key");
        if (!ehWebSocket)
        {
            await Responder(fluxo, "400 Bad Request", ct);
            return null;
        }
        if (!campos.TryGetValue("Origin", out var de) || de != origem)
        {
            UltimaFalha = $"conexão recusada de {(de is { Length: > 0 } ? de : "origem nenhuma")}";
            await Responder(fluxo, "403 Forbidden", ct);
            return null;
        }

        string aceite = Convert.ToBase64String(SHA1.HashData(
            Encoding.ASCII.GetBytes(campos["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        await fluxo.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
            + $"Sec-WebSocket-Accept: {aceite}\r\n\r\n"), ct);

        return WebSocket.CreateFromStream(fluxo, new WebSocketCreationOptions
        {
            IsServer = true,
            KeepAliveInterval = TimeSpan.FromSeconds(20),
        });
    }

    private static Task Responder(NetworkStream fluxo, string status, CancellationToken ct) =>
        fluxo.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\n\r\n"), ct).AsTask();

    private async Task LerAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];
        using var mensagem = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            mensagem.SetLength(0);
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                    catch (WebSocketException) { }
                    return;
                }
                if (mensagem.Length + r.Count > 65536) return;   // não é a extensão
                mensagem.Write(buffer, 0, r.Count);
            } while (!r.EndOfMessage);

            if (ProtocoloDaExtensao.Ler(Encoding.UTF8.GetString(mensagem.GetBuffer(), 0, (int)mensagem.Length))
                is { } e)
                Avisar(() => AoReceber?.Invoke(e));
        }
    }

    private void Avisar(Action quem)
    {
        try { quem(); }
        catch (Exception e) { UltimaFalha = e.Message; }
    }

    public void Dispose()
    {
        Desligar();
        _envio.Dispose();
    }
}
