using System.Net.WebSockets;
using System.Text;

namespace MeetingRecorder.Core;

/// <summary>
/// O WebSocket com o Teams: conecta, lê, manda comando e volta sozinho.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bônus, nunca dependência.</b> Sem Teams, com a API desligada ou com o
/// pareamento recusado, o gravador funciona como sempre, com o mute à mão.
/// Nada aqui lança para fora: cair é voltar a tentar, com a espera dobrando
/// até <see cref="EsperaMaxima"/> — o Teams fechado é o caso comum, e conferir
/// a porta duas vezes por minuto não custa nada.
/// </para>
/// <para>
/// Os eventos saem da thread de leitura. Quem mexe no gravador com eles tem de
/// pular para a thread da UI antes.
/// </para>
/// </remarks>
public sealed class ConexaoComOTeams(string nome, string versao, Func<string?> token,
                                     int porta = ProtocoloDoTeams.Porta, TimeSpan? esperaMinima = null)
    : IDisposable
{
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _esperaMinima = esperaMinima ?? TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _envio = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _laco;
    private volatile ClientWebSocket? _ws;
    private int _pedido;

    /// <summary>Uma mensagem que a sincronia entende. Sai da thread de leitura.</summary>
    public event Action<MensagemDoTeams>? AoReceber;

    /// <summary>Conectou (verdadeiro) ou caiu (falso). Sai da thread de leitura.</summary>
    public event Action<bool>? AoMudarConexao;

    public bool Conectado => _ws?.State == WebSocketState.Open;

    /// <summary>Por que a última tentativa não conectou, para o registro.</summary>
    public string? UltimaFalha { get; private set; }

    public void Ligar()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _laco = Task.Run(() => LacoAsync(ct));
    }

    public void Desligar()
    {
        if (_cts is not { } cts) return;
        cts.Cancel();
        try { _laco?.Wait(TimeSpan.FromSeconds(2)); } catch { /* o laço não lança; cancelado é o fim */ }
        cts.Dispose();
        _cts = null;
        _laco = null;
    }

    /// <summary>Manda um comando, se houver com quem falar. Sem Teams, não faz nada.</summary>
    public async Task EnviarAsync(string acao)
    {
        if (_ws is not { State: WebSocketState.Open } ws) return;
        string json = ProtocoloDoTeams.Comando(acao, Interlocked.Increment(ref _pedido));
        // Um envio por vez: o ClientWebSocket não aceita dois em voo.
        await _envio.WaitAsync();
        try
        {
            await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true,
                               CancellationToken.None);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            UltimaFalha = e.Message;
        }
        finally { _envio.Release(); }
    }

    private async Task LacoAsync(CancellationToken ct)
    {
        var espera = _esperaMinima;
        while (!ct.IsCancellationRequested)
        {
            using (var ws = new ClientWebSocket())
            {
                try
                {
                    await ws.ConnectAsync(ProtocoloDoTeams.Endereco(token(), nome, versao, porta), ct);
                    _ws = ws;
                    Avisar(() => AoMudarConexao?.Invoke(true));
                    // A espera só volta ao mínimo depois de o Teams dizer alguma
                    // coisa: um token recusado pode ser aceito e fechado na hora,
                    // e voltar ao mínimo aí seria bater na porta sem parar.
                    if (await LerAsync(ws, ct)) espera = _esperaMinima;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                catch (Exception e) { UltimaFalha = e.Message; }
                finally
                {
                    if (_ws == ws)
                    {
                        _ws = null;
                        Avisar(() => AoMudarConexao?.Invoke(false));
                    }
                }
            }

            try { await Task.Delay(espera, ct); }
            catch (OperationCanceledException) { break; }
            espera = TimeSpan.FromTicks(Math.Min(espera.Ticks * 2, EsperaMaxima.Ticks));
        }
    }

    /// <returns>Se chegou ao menos uma mensagem antes de a conexão fechar.</returns>
    private async Task<bool> LerAsync(ClientWebSocket ws, CancellationToken ct)
    {
        bool ouviu = false;
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
                    // Responder o fechamento, e não só largar o socket: o Teams
                    // fechando de propósito espera a resposta.
                    try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                    catch (WebSocketException) { }
                    return ouviu;
                }
                mensagem.Write(buffer, 0, r.Count);
            } while (!r.EndOfMessage);

            ouviu = true;
            if (ProtocoloDoTeams.Ler(Encoding.UTF8.GetString(mensagem.GetBuffer(), 0, (int)mensagem.Length))
                is { } m)
                Avisar(() => AoReceber?.Invoke(m));
        }
        return ouviu;
    }

    /// <summary>Um assinante que falha não derruba a leitura.</summary>
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
