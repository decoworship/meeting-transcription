using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>O mute do gravador acompanhando o do Teams, e o contrário (GRA-3).</summary>
public sealed class SincroniaDoMuteTests
{
    private static readonly DateTime T0 = new(2026, 10, 22, 15, 0, 0, DateTimeKind.Utc);

    private static EstadoDaChamada NaReuniao(bool mudo) =>
        new(EmReuniao: true, Mudo: mudo, PodeMutar: true, PodeParear: false);

    private static readonly EstadoDaChamada ForaDaReuniao =
        new(EmReuniao: false, Mudo: false, PodeMutar: false, PodeParear: false);

    // ─────────────────────────────────────────── Teams → gravador

    [Fact]
    public void MutarNoTeamsMutaAGravacao()
    {
        var s = new SincroniaDoMute();
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoEstadoDaChamada(NaReuniao(false), true, false, T0));
        Assert.Equal(AcaoDaSincronia.MutarNoApp, s.AoEstadoDaChamada(NaReuniao(true), true, false, T0));
        Assert.Equal(AcaoDaSincronia.DesmutarNoApp, s.AoEstadoDaChamada(NaReuniao(false), true, true, T0));
    }

    [Fact]
    public void ParadoOTeamsNaoMexeEmNada()
    {
        // O mute do gravador só existe enquanto ele grava: começar a gravar é
        // que alinha os dois (AoComecarAGravar).
        var s = new SincroniaDoMute();
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoEstadoDaChamada(NaReuniao(true), false, false, T0));
    }

    [Fact]
    public void SoAMudancaDoMuteDoTeamsConta()
    {
        // O Teams manda o estado inteiro a cada mudança — câmera, mão
        // levantada. Se cada uma realinhasse o gravador, um mute escolhido só
        // aqui seria desfeito pela próxima mão levantada.
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(NaReuniao(false), true, false, T0);
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoEstadoDaChamada(NaReuniao(false), true, true, T0));
    }

    [Fact]
    public void EntrarNaReuniaoMutadoMutaAGravacao()
    {
        // A primeira leitura dentro da reunião vale como mudança: é a hora em
        // que os dois passam a significar a mesma coisa.
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(ForaDaReuniao, true, false, T0);
        Assert.Equal(AcaoDaSincronia.MutarNoApp, s.AoEstadoDaChamada(NaReuniao(true), true, false, T0));
    }

    [Fact]
    public void SairDaReuniaoNaoDesmutaAGravacao()
    {
        // Fora da reunião o Teams diz "não mudo" porque não há o que mutar.
        // Seguir isso desmutaria uma gravação que alguém mutou de propósito.
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(NaReuniao(true), true, true, T0);
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoEstadoDaChamada(ForaDaReuniao, true, true, T0));
    }

    [Fact]
    public void SemParearOTeamsNaoDizOMute()
    {
        var s = new SincroniaDoMute();
        var semEstado = new EstadoDaChamada(EmReuniao: true, Mudo: null, PodeMutar: true, PodeParear: true);
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoEstadoDaChamada(semEstado, true, false, T0));
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoMutarNoApp(true, T0));
    }

    // ─────────────────────────────────────────── gravador → Teams

    [Fact]
    public void MutarNoGravadorMutaNoTeams()
    {
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(NaReuniao(false), true, false, T0);
        Assert.Equal(AcaoDaSincronia.AlternarNaChamada, s.AoMutarNoApp(true, T0));
    }

    [Fact]
    public void JaIguaisNadaVaiAoTeams()
    {
        // O Teams só sabe alternar: mandar o comando com os dois já iguais
        // seria desfazer o que se pediu.
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(NaReuniao(true), true, true, T0);
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoMutarNoApp(true, T0));
    }

    [Fact]
    public void ForaDaReuniaoOuSemPermissaoNadaVaiAoTeams()
    {
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(ForaDaReuniao, true, false, T0);
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoMutarNoApp(true, T0));

        s.AoEstadoDaChamada(NaReuniao(false) with { PodeMutar = false }, true, false, T0);
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoMutarNoApp(true, T0));
    }

    [Fact]
    public void OEcoDoProprioComandoNaoVolta()
    {
        // O gravador mutou e mandou alternar; a resposta do Teams confirma o
        // que o gravador já é, e não pode ser lida como um pedido novo.
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(NaReuniao(false), true, false, T0);
        s.AoMutarNoApp(true, T0);
        Assert.Equal(AcaoDaSincronia.Nenhuma,
            s.AoEstadoDaChamada(NaReuniao(true), true, true, T0.AddMilliseconds(300)));
    }

    [Fact]
    public void UmEstadoAtrasadoNaoDesfazOClique()
    {
        // Uma atualização que saiu do Teams antes de o comando chegar lá diz o
        // mute antigo. Obedecê-la desmutaria o gravador que acabou de mutar.
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(NaReuniao(false), true, false, T0);
        s.AoMutarNoApp(true, T0);
        var atrasado = NaReuniao(false) with { PodeParear = true };   // mudou outra coisa
        Assert.Equal(AcaoDaSincronia.Nenhuma,
            s.AoEstadoDaChamada(atrasado, true, true, T0.AddMilliseconds(200)));
        Assert.Equal(AcaoDaSincronia.Nenhuma,
            s.AoEstadoDaChamada(NaReuniao(true), true, true, T0.AddMilliseconds(400)));
    }

    [Fact]
    public void DoisCliquesRapidosTerminamNoSegundo()
    {
        // Mutar e desmutar antes de o Teams responder ao primeiro: o segundo
        // comando parte do que o Teams vai ser, não do que ele ainda diz.
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(NaReuniao(false), true, false, T0);
        Assert.Equal(AcaoDaSincronia.AlternarNaChamada, s.AoMutarNoApp(true, T0));
        Assert.Equal(AcaoDaSincronia.AlternarNaChamada, s.AoMutarNoApp(false, T0.AddMilliseconds(100)));

        Assert.Equal(AcaoDaSincronia.Nenhuma,
            s.AoEstadoDaChamada(NaReuniao(true), true, false, T0.AddMilliseconds(300)));
        Assert.Equal(AcaoDaSincronia.Nenhuma,
            s.AoEstadoDaChamada(NaReuniao(false), true, false, T0.AddMilliseconds(500)));
    }

    [Fact]
    public void PassadaAEsperaOTeamsVoltaASerOuvido()
    {
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(NaReuniao(false), true, false, T0);
        s.AoMutarNoApp(true, T0);
        s.AoEstadoDaChamada(NaReuniao(true), true, true, T0.AddMilliseconds(300));

        var depois = T0 + SincroniaDoMute.Espera + TimeSpan.FromSeconds(1);
        Assert.Equal(AcaoDaSincronia.DesmutarNoApp, s.AoEstadoDaChamada(NaReuniao(false), true, true, depois));
    }

    // ─────────────────────────────────────────── começo, pareamento, queda

    [Fact]
    public void ComecarAGravarNaReuniaoSegueOTeams()
    {
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(NaReuniao(true), false, false, T0);
        Assert.Equal(AcaoDaSincronia.MutarNoApp, s.AoComecarAGravar(false));

        s.AoEstadoDaChamada(NaReuniao(false), false, false, T0);
        Assert.Equal(AcaoDaSincronia.DesmutarNoApp, s.AoComecarAGravar(true));
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoComecarAGravar(false));
    }

    [Fact]
    public void ComecarAGravarForaDaReuniaoNaoMexe()
    {
        var s = new SincroniaDoMute();
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoComecarAGravar(true));
        s.AoEstadoDaChamada(ForaDaReuniao, false, false, T0);
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoComecarAGravar(true));
    }

    [Fact]
    public void PedeParearUmaVezPorReuniao()
    {
        var s = new SincroniaDoMute();
        var podePedir = new EstadoDaChamada(EmReuniao: true, Mudo: null, PodeMutar: true, PodeParear: true);
        s.AoEstadoDaChamada(podePedir, false, false, T0);
        Assert.True(s.QuerParear());
        Assert.False(s.QuerParear());

        s.AoEstadoDaChamada(ForaDaReuniao, false, false, T0);
        s.AoEstadoDaChamada(podePedir, false, false, T0);
        Assert.True(s.QuerParear());
    }

    [Fact]
    public void PareadoNaoPedeDeNovo()
    {
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(NaReuniao(false) with { PodeParear = true }, false, false, T0);
        Assert.False(s.QuerParear());
    }

    [Fact]
    public void PerderOTeamsEsqueceOQueEleDisse()
    {
        // Um Teams que reiniciou pode voltar noutra reunião, ou em nenhuma.
        var s = new SincroniaDoMute();
        s.AoEstadoDaChamada(NaReuniao(false), true, false, T0);
        s.AoPerderAChamada();
        Assert.Null(s.Chamada);
        Assert.Equal(AcaoDaSincronia.Nenhuma, s.AoMutarNoApp(true, T0));
    }

    // ─────────────────────────────────────────── o protocolo

    [Fact]
    public void LeOEstadoDepoisDePareado()
    {
        const string json = """
            {"meetingUpdate":{"meetingState":{"isMuted":true,"isVideoOn":false,"isHandRaised":false,
             "isInMeeting":true,"isRecordingOn":false,"isBackgroundBlurred":false,"isSharing":false,
             "hasUnreadMessages":false},"meetingPermissions":{"canToggleMute":true,"canToggleVideo":true,
             "canToggleHand":true,"canToggleBlur":false,"canLeave":true,"canReact":true,
             "canToggleShareTray":true,"canToggleChat":true,"canStopSharing":false,"canPair":false}}}
            """;
        var m = ProtocoloDoTeams.Ler(json);
        Assert.Equal(new EstadoDaChamada(EmReuniao: true, Mudo: true, PodeMutar: true, PodeParear: false), m?.Estado);
    }

    [Fact]
    public void AntesDeParearSoVemAsPermissoes()
    {
        // Sem o meetingState, o Teams não disse o mute — e "não disse" não
        // pode virar "não está mudo".
        const string json = """
            {"meetingUpdate":{"meetingPermissions":{"canToggleMute":true,"canPair":true}}}
            """;
        var e = ProtocoloDoTeams.Ler(json)?.Estado;
        Assert.NotNull(e);
        Assert.Null(e.Mudo);
        Assert.True(e.PodeParear);
        // Poder parear só acontece dentro de uma reunião.
        Assert.True(e.EmReuniao);
    }

    [Fact]
    public void LeOTokenNovoEOErro()
    {
        Assert.Equal("abc-123", ProtocoloDoTeams.Ler("""{"tokenRefresh":"abc-123"}""")?.TokenNovo);
        Assert.Equal("no active call",
            ProtocoloDoTeams.Ler("""{"requestId":3,"errorMsg":"no active call"}""")?.Erro);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("[1,2]")]
    [InlineData("""{"requestId":2,"response":"Success"}""")]
    public void OQueNaoEntendeVoltaVazio(string json)
    {
        var m = ProtocoloDoTeams.Ler(json);
        Assert.True(m is null || (m.Estado is null && m.TokenNovo is null && m.Erro is null));
    }

    [Fact]
    public void OEnderecoLevaOTokenSoQuandoHa()
    {
        var sem = ProtocoloDoTeams.Endereco(null, "PulseMeet", "0.11.1");
        Assert.DoesNotContain("token=", sem.Query);
        Assert.Contains("protocol-version=2.0.0", sem.Query);
        Assert.Contains("app=PulseMeet", sem.Query);
        Assert.Equal(ProtocoloDoTeams.Porta, sem.Port);

        var com = ProtocoloDoTeams.Endereco("a b&c", "PulseMeet", "0.11.1");
        Assert.Contains("token=a%20b%26c", com.Query);
    }

    [Fact]
    public void OComandoTemAcaoParametrosEPedido()
    {
        var n = JsonNode.Parse(ProtocoloDoTeams.Comando(ProtocoloDoTeams.AlternarMudo, 7))!;
        Assert.Equal("toggle-mute", (string?)n["action"]);
        Assert.Equal(7, (int?)n["requestId"]);
        Assert.NotNull(n["parameters"]);
    }

    // ─────────────────────────────────────────── a conexão

    [Fact]
    public async Task AConexaoRecebeEEnviaEVoltaQuandoOTeamsVolta()
    {
        int porta = PortaLivre();
        using var servidor = new HttpListener();
        servidor.Prefixes.Add($"http://localhost:{porta}/");
        servidor.Start();

        var recebidos = new System.Collections.Concurrent.BlockingCollection<MensagemDoTeams>();
        using var conexao = new ConexaoComOTeams("PulseMeet", "0.0.0", () => null, porta,
                                                 TimeSpan.FromMilliseconds(100));
        conexao.AoReceber += recebidos.Add;
        conexao.Ligar();

        for (int vez = 0; vez < 2; vez++)
        {
            // Duas vezes: a segunda é o Teams que fechou e abriu de novo.
            var ctx = await servidor.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("protocol-version=2.0.0", ctx.Request.Url!.Query);
            var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;

            await Enviar(ws, """{"meetingUpdate":{"meetingState":{"isMuted":true,"isInMeeting":true},"meetingPermissions":{"canToggleMute":true}}}""");
            Assert.True(recebidos.TryTake(out var m, TimeSpan.FromSeconds(10)));
            Assert.True(m!.Estado!.Mudo);
            Assert.True(conexao.Conectado);

            await conexao.EnviarAsync(ProtocoloDoTeams.AlternarMudo);
            var pedido = JsonNode.Parse(await Receber(ws))!;
            Assert.Equal("toggle-mute", (string?)pedido["action"]);

            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "fim", CancellationToken.None);
        }
        conexao.Desligar();
        Assert.False(conexao.Conectado);
    }

    [Fact]
    public async Task SemOTeamsEnviarNaoFalha()
    {
        using var conexao = new ConexaoComOTeams("PulseMeet", "0.0.0", () => null, PortaLivre(),
                                                 TimeSpan.FromMilliseconds(100));
        conexao.Ligar();
        await conexao.EnviarAsync(ProtocoloDoTeams.AlternarMudo);
        Assert.False(conexao.Conectado);
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
}
