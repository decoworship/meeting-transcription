using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Perguntar ao modelo o que já aconteceu, durante a própria reunião.
/// </summary>
/// <remarks>
/// O que se testa aqui é tudo o que não precisa de placa: o texto que o modelo
/// vai ler, o corte quando a reunião não cabe no contexto, e a guarda que
/// impede duas subidas do motor ao mesmo tempo. A qualidade da resposta é
/// assunto do prompt, e se ajusta contra reunião de verdade.
/// </remarks>
public sealed class PerguntaDaReuniaoTests
{
    private static TurnoDaLegenda Turno(bool dono, string texto) =>
        new() { Dono = dono, Texto = texto };

    private static BlocoAoVivo Bloco(int numero, params SegmentoFinal[] trechos) =>
        new(numero, 0, 180, "provisorio", trechos);

    [Fact]
    public void OTextoUsaOsDoisRotulosQueALegendaConhece()
    {
        // A legenda separa você dos outros, e não sabe mais que isso. Pôr nome
        // de pessoa aqui seria inventar — quem é cada um vem na passada final.
        var texto = PerguntaDaReuniao.DaLegenda(
            [Turno(true, "bom dia"), Turno(false, "bom dia, tudo bem?")], 10_000);

        Assert.Equal("Você: bom dia\nOutra pessoa: bom dia, tudo bem?", texto.Texto);
        Assert.False(texto.Cortado);
    }

    [Fact]
    public void QuandoAReuniaoNaoCabeOComecoEQueCai()
    {
        // O fim é o que a pergunta "o que aconteceu até agora" quer de verdade,
        // e é o que ainda está na cabeça de quem pergunta.
        var texto = PerguntaDaReuniao.DaLegenda(
            [Turno(true, "o primeiro assunto"),
             Turno(false, "o segundo assunto"),
             Turno(true, "o terceiro assunto")],
            limiteDeCaracteres: 40);

        Assert.True(texto.Cortado);
        Assert.DoesNotContain("o primeiro assunto", texto.Texto);
        Assert.Contains("o terceiro assunto", texto.Texto);
        Assert.True(texto.Texto.Length <= 40, $"sobrou {texto.Texto.Length} caracteres");
    }

    [Fact]
    public void UmTurnoSozinhoMaiorQueOLimiteEntraPeloFim()
    {
        // Uma pessoa falando sem parar por meia hora vira um turno só. Cortar
        // turno inteiro deixaria o texto vazio, que é o pior desfecho.
        var texto = PerguntaDaReuniao.DaLegenda(
            [Turno(false, new string('a', 100) + "o final da fala")], limiteDeCaracteres: 30);

        Assert.True(texto.Cortado);
        Assert.Contains("o final da fala", texto.Texto);
    }

    [Fact]
    public void SemNadaTranscritoOTextoSaiVazio()
    {
        // É o que deixa a Ponte recusar a pergunta antes de carregar 2,5 GB de
        // modelo para responder sobre o silêncio.
        Assert.Equal("", PerguntaDaReuniao.DaLegenda([], 10_000).Texto);
    }

    [Fact]
    public void OsBlocosViramTextoComORotuloDoMoss()
    {
        // A prévia em blocos entrega falante local ("Speaker 1"), e o dono vem
        // da faixa do microfone como "You". Os dois valem mais que "outra
        // pessoa", então são preservados como estão.
        var texto = PerguntaDaReuniao.DosBlocos(
        [
            Bloco(1,
                new SegmentoFinal { Start = 0, End = 2, Text = " bom dia", Speaker = "You" },
                new SegmentoFinal { Start = 2, End = 4, Text = " oi", Speaker = "Speaker 1" }),
        ], 10_000);

        Assert.Equal("Você: bom dia\nSpeaker 1: oi", texto.Texto);
    }

    [Fact]
    public void OPromptTrazATranscricaoEAPergunta()
    {
        string prompt = PerguntaDaReuniao.Montar(
            new TextoDaReuniao("Você: bom dia", Cortado: false), "o que ficou decidido?");

        Assert.Contains("Você: bom dia", prompt);
        Assert.Contains("o que ficou decidido?", prompt);
    }

    [Fact]
    public void OPromptCortadoAvisaOModeloDeQueEleNaoViuOComeco()
    {
        // Sem isto o modelo responde "a reunião começou com X" sobre o que na
        // verdade é o meio dela.
        string prompt = PerguntaDaReuniao.Montar(
            new TextoDaReuniao("Você: bom dia", Cortado: true), "sobre o que falamos?");

        Assert.Contains("parte final", prompt);
    }

    [Fact]
    public async Task ASegundaPerguntaEnquantoAPrimeiraRodaERecusada()
    {
        // Duas subidas do llama-server ao mesmo tempo são dois modelos na placa
        // durante uma reunião que está sendo gravada.
        var segura = new TaskCompletionSource<string>();
        var perguntador = new PerguntaDaReuniao(( _, _) => segura.Task);
        var texto = new TextoDaReuniao("Você: bom dia", Cortado: false);

        var primeira = perguntador.ResponderAsync("e aí?", texto, default);

        var erro = await Assert.ThrowsAsync<InvalidOperationException>(
            () => perguntador.ResponderAsync("e aí de novo?", texto, default));
        Assert.Contains("uma pergunta por vez", erro.Message);

        segura.SetResult("respondido");
        Assert.Equal("respondido", await primeira);
    }

    [Fact]
    public async Task DepoisDeResponderOMotorFicaLivreParaAProximaPergunta()
    {
        var perguntador = new PerguntaDaReuniao((_, _) => Task.FromResult("primeira"));
        var texto = new TextoDaReuniao("Você: bom dia", Cortado: false);

        await perguntador.ResponderAsync("e aí?", texto, default);

        Assert.Equal("primeira", await perguntador.ResponderAsync("de novo?", texto, default));
    }

    [Fact]
    public async Task UmaPerguntaQueFalhaNaoTrancaOMotorParaSempre()
    {
        // O motor morre por falta de VRAM mais do que gostaríamos, e a segunda
        // tentativa é o que a pessoa faz na hora.
        var perguntador = new PerguntaDaReuniao(
            (_, _) => Task.FromException<string>(new InvalidOperationException("sem placa")));
        var texto = new TextoDaReuniao("Você: bom dia", Cortado: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => perguntador.ResponderAsync("e aí?", texto, default));

        Assert.False(perguntador.Ocupado);
    }

    private static MetadadosDoGguf Modelo(int contextoMaximo) => new()
    {
        Arquitetura = "qwen3", Nome = "teste", ContextoMaximo = contextoMaximo,
        Camadas = 36, CabecasDeKv = 8, DimensaoDaChave = 128, DimensaoDoValor = 128,
        BytesDoArquivo = 2_500_000_000,
    };

    [Fact]
    public void OLimiteCabeNoContextoDoModeloComOQueOMotorReserva()
    {
        // O MotorDeAta.Dimensionar reserva os TokensDeSaida dele (8.192) para a
        // escrita, e recusa o pedido **depois** de o modelo ter carregado. Um
        // limite que ignore essa reserva transforma uma reunião longa numa
        // espera de nove segundos terminada em erro.
        int limite = PerguntaDaReuniao.LimiteDeCaracteres(Modelo(32_768));

        int tokensDoPrompt = (int)(limite / MotorDeAta.CaracteresPorToken);
        Assert.True(tokensDoPrompt + MotorDeAta.TokensDeSaida <= 32_768,
            $"{tokensDoPrompt} + {MotorDeAta.TokensDeSaida} passa de 32.768");
        Assert.True(limite > 40_000, $"limite apertado demais: {limite}");
    }

    [Fact]
    public void ModeloQueNaoDeclaraContextoGanhaUmTetoConservador()
    {
        // Zero é o "não sei" do MetadadosDoGguf. Pedir contexto grande a um
        // modelo pequeno não dá erro — dá resposta ruim, em silêncio.
        int limite = PerguntaDaReuniao.LimiteDeCaracteres(Modelo(0));

        Assert.True(limite > 0);
        Assert.True(limite < PerguntaDaReuniao.LimiteDeCaracteres(Modelo(32_768)));
    }

    // ─────────────────────────────── a janela de uma hora, e a chave

    [Fact]
    public void AJanelaGuardaUmaHoraDeFala()
    {
        // Decidido pelo dono do produto em 16/09/2026: acima de uma hora, o
        // começo da reunião já não é o que alguém precisa para se situar. E ela
        // é o teto de VRAM disfarçado de decisão de produto — sem ela, uma
        // reunião de duas horas pede 32k de contexto, que não cabe ao lado da
        // legenda (docs/ESTUDO-RESUMO-AO-VIVO.md §10).
        Assert.Equal(60, PerguntaDaReuniao.JanelaMaximaMinutos);
        Assert.Equal(60 * PerguntaDaReuniao.CaracteresPorMinuto,
                     PerguntaDaReuniao.JanelaMaximaCaracteres);
    }

    [Fact]
    public void OLimiteNuncaPassaDaJanela_MesmoNumModeloDeContextoEnorme()
    {
        // O qwen3.5-4b tem 262.144 nativos. Sem a janela, o limite sairia em
        // ~600 mil caracteres, e a placa não tem como carregar isso durante uma
        // reunião que está sendo gravada.
        int limite = PerguntaDaReuniao.LimiteDeCaracteres(Modelo(262_144));

        Assert.Equal(PerguntaDaReuniao.JanelaMaximaCaracteres, limite);
    }

    [Fact]
    public void NumModeloPequenoQuemManda_EOContextoENaoAJanela()
    {
        // A janela é um teto, não um piso: um modelo que não comporta uma hora
        // de fala continua sendo cortado pelo que ele aguenta.
        int limite = PerguntaDaReuniao.LimiteDeCaracteres(Modelo(8_192));

        Assert.True(limite < PerguntaDaReuniao.JanelaMaximaCaracteres,
                    $"a janela venceu o contexto: {limite}");
        Assert.True(limite > 0);
    }

    [Fact]
    public void AChaveNasceDesligada()
    {
        // Tudo o que sobe modelo durante a gravação nasce desligado enquanto o
        // SUP-2 estiver aberto. Um padrão que mude aqui é regressão de política.
        Assert.False(new ConfiguracoesDoApp().PerguntarAoVivo);
    }

    [Fact]
    public void DesligadaDizPorQueNaoExiste()
    {
        var cfg = new ConfiguracoesDoApp { PerguntarAoVivo = false };

        string? porque = PerguntaDaReuniao.OQueImpede(
            cfg, new CaminhosDoMotorDeAta("servidor.exe", "modelo.gguf"));

        Assert.NotNull(porque);
        Assert.Contains("Ajustes", porque);
    }

    [Fact]
    public void LigadaSemOMotorEmDiscoDizOndeBaixar()
    {
        // "Não está lá" é o estado normal de quem acabou de instalar: o motor de
        // ata não vem no instalador.
        var cfg = new ConfiguracoesDoApp { PerguntarAoVivo = true };

        string? porque = PerguntaDaReuniao.OQueImpede(
            cfg, new CaminhosDoMotorDeAta("/nao/existe.exe", "/nao/existe.gguf"));

        Assert.NotNull(porque);
        Assert.Contains("Modelos", porque);
    }
}
