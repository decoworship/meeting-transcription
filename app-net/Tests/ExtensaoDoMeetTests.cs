using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>O mute do Meet pela extensão de navegador (GRA-3, a segunda chamada).</summary>
public sealed class ExtensaoDoMeetTests
{
    [Fact]
    public void LeOEstadoDaExtensao()
    {
        Assert.Equal(new MensagemDaExtensao(new EstadoDaChamada(true, true, true, false), "meet", null),
            ProtocoloDaExtensao.Ler("""{"tipo":"estado","app":"meet","em_reuniao":true,"mudo":true}"""));
        Assert.Equal(new MensagemDaExtensao(new EstadoDaChamada(false, false, false, false), "teams", null),
            ProtocoloDaExtensao.Ler("""{"tipo":"estado","app":"teams","em_reuniao":false,"mudo":true}"""));
    }

    [Fact]
    public void UmAppDesconhecidoNaoEntra()
    {
        // O app vai para o registro e para a tela; não é lugar de texto livre.
        Assert.Null(ProtocoloDaExtensao.Ler("""{"tipo":"estado","app":"zoom","em_reuniao":true}""")!.App);
        Assert.NotNull(ProtocoloDaExtensao.Ler("""{"tipo":"estado","em_reuniao":true}""")!.Estado);
    }

    [Fact]
    public void LeODiagnostico()
    {
        var m = ProtocoloDaExtensao.Ler("""{"tipo":"diagnostico","app":"teams","texto":"teams: botão button id=microphone-button"}""");
        Assert.Equal("teams: botão button id=microphone-button", m?.Diagnostico);
        Assert.Null(m?.Estado);

        var longo = ProtocoloDaExtensao.Ler($$"""{"tipo":"diagnostico","texto":"{{new string('x', 5000)}}"}""");
        Assert.Equal(1000, longo?.Diagnostico?.Length);
        Assert.Null(ProtocoloDaExtensao.Ler("""{"tipo":"diagnostico"}"""));
    }

    [Theory]
    [InlineData("""{"tipo":"oi"}""")]
    [InlineData("""{"em_reuniao":true,"mudo":true}""")]
    [InlineData("[]")]
    [InlineData("nada")]
    public void OQueNaoEEstadoVoltaNulo(string json) => Assert.Null(ProtocoloDaExtensao.Ler(json));

    [Fact]
    public void OPedidoDizOEstadoENaoInverter()
    {
        var n = JsonNode.Parse(ProtocoloDaExtensao.Mutar(true))!;
        Assert.Equal("mutar", (string?)n["tipo"]);
        Assert.True((bool?)n["mudo"]);
    }

    [Fact]
    public void OIdEODaChaveDoManifesto()
    {
        // O Chrome tira o id dos 128 primeiros bits do SHA-256 da chave
        // pública, com os dígitos hexadecimais trocados por a–p. Trocar a chave
        // sem trocar o id faria o PulseMeet recusar a própria extensão.
        string? manifesto = Achar(Path.Combine("extensao", "meet", "manifest.json"));
        Assert.NotNull(manifesto);
        string chave = (string)JsonNode.Parse(File.ReadAllText(manifesto))!["key"]!;
        string hex = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(chave)))[..32];
        string id = new(hex.ToLowerInvariant().Select(c => (char)('a' + Convert.ToInt32(c.ToString(), 16))).ToArray());
        Assert.Equal(ProtocoloDaExtensao.Id, id);
    }

    [Fact]
    public void AExtensaoRodaNoMeetENoTeams()
    {
        string? manifesto = Achar(Path.Combine("extensao", "meet", "manifest.json"));
        var sites = JsonNode.Parse(File.ReadAllText(manifesto!))!["content_scripts"]![0]!["matches"]!
            .AsArray().Select(n => (string)n!).ToList();
        Assert.Contains("https://meet.google.com/*", sites);
        Assert.Contains("https://teams.microsoft.com/*", sites);
        Assert.Contains("https://teams.cloud.microsoft/*", sites);
    }

    [Fact]
    public void AExtensaoUsaAMesmaPorta()
    {
        string? fundo = Achar(Path.Combine("extensao", "meet", "fundo.js"));
        Assert.NotNull(fundo);
        Assert.Contains($"const PORTA = {ProtocoloDaExtensao.Porta};", File.ReadAllText(fundo));
    }

    [Fact]
    public async Task OServidorConversaComAExtensao()
    {
        int porta = PortaLivre();
        using var servidor = new ServidorDaExtensao(porta);
        var recebidos = new System.Collections.Concurrent.BlockingCollection<MensagemDaExtensao>();
        var conexoes = new System.Collections.Concurrent.BlockingCollection<bool>();
        servidor.AoReceber += recebidos.Add;
        servidor.AoMudarConexao += conexoes.Add;
        servidor.Ligar();

        using var ws = await Conectar(porta, ProtocoloDaExtensao.Origem);
        Assert.True(conexoes.TryTake(out bool conectou, TimeSpan.FromSeconds(10)) && conectou);

        await Enviar(ws, """{"tipo":"oi"}""");
        await Enviar(ws, """{"tipo":"estado","em_reuniao":true,"mudo":false}""");
        Assert.True(recebidos.TryTake(out var e, TimeSpan.FromSeconds(10)));
        Assert.Equal(false, e!.Estado!.Mudo);

        await servidor.MutarAsync(true);
        var pedido = JsonNode.Parse(await Receber(ws))!;
        Assert.True((bool?)pedido["mudo"]);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "fim", CancellationToken.None);
        Assert.True(conexoes.TryTake(out bool saiu, TimeSpan.FromSeconds(10)) && !saiu);
        Assert.False(servidor.Conectado);
    }

    [Fact]
    public async Task UmaConexaoNovaSubstituiAVelha()
    {
        // É o Chrome reiniciando o service worker: a conexão antiga pode nem
        // ter fechado direito, e quem manda agora é a nova.
        int porta = PortaLivre();
        using var servidor = new ServidorDaExtensao(porta);
        servidor.Ligar();

        using var velha = await Conectar(porta, ProtocoloDaExtensao.Origem);
        using var nova = await Conectar(porta, ProtocoloDaExtensao.Origem);
        await Task.Delay(200);
        await servidor.MutarAsync(false);
        Assert.False((bool?)JsonNode.Parse(await Receber(nova))!["mudo"]);
        Assert.True(servidor.Conectado);
    }

    [Theory]
    [InlineData("https://meet.google.com")]
    [InlineData("chrome-extension://outraextensaoqualquerdeoutrapessoa")]
    [InlineData(null)]
    public async Task OutraOrigemERecusada(string? origem)
    {
        // Um site aberto no navegador também alcança 127.0.0.1; só não pode
        // dizer que é a extensão.
        int porta = PortaLivre();
        using var servidor = new ServidorDaExtensao(porta);
        servidor.Ligar();

        await Assert.ThrowsAnyAsync<WebSocketException>(() => Conectar(porta, origem));
        Assert.False(servidor.Conectado);
        Assert.Contains("recusada", servidor.UltimaFalha);
    }

    [Fact]
    public async Task SemExtensaoMutarNaoFalha()
    {
        using var servidor = new ServidorDaExtensao(PortaLivre());
        servidor.Ligar();
        await servidor.MutarAsync(true);
        Assert.False(servidor.Conectado);
    }

    private static async Task<ClientWebSocket> Conectar(int porta, string? origem)
    {
        var ws = new ClientWebSocket();
        if (origem is not null) ws.Options.SetRequestHeader("Origin", origem);
        // O servidor sobe numa tarefa: a primeira tentativa pode chegar antes.
        for (int i = 0; ; i++)
        {
            try
            {
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{porta}/"), CancellationToken.None);
                return ws;
            }
            catch (WebSocketException e) when (i < 20 && e.InnerException is System.Net.Http.HttpRequestException
                                               { InnerException: System.Net.Sockets.SocketException })
            {
                ws.Dispose();
                ws = new ClientWebSocket();
                if (origem is not null) ws.Options.SetRequestHeader("Origin", origem);
                await Task.Delay(100);
            }
        }
    }

    private static int PortaLivre()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int porta = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return porta;
    }

    private static Task Enviar(WebSocket ws, string texto) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(texto), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<string> Receber(WebSocket ws)
    {
        var buffer = new byte[4096];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var r = await ws.ReceiveAsync(buffer, cts.Token);
        return Encoding.UTF8.GetString(buffer, 0, r.Count);
    }

    private static string? Achar(string relativo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string tentativa = Path.Combine(dir.FullName, relativo);
            if (File.Exists(tentativa)) return tentativa;
            dir = dir.Parent;
        }
        return null;
    }
}
