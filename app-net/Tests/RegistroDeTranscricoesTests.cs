using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O registro do que está transcrevendo.
/// </summary>
/// <remarks>
/// O que se testa aqui é o que a tela não consegue provar sozinha: que a trava
/// de uma por vez segura, que o erro sobrevive a ninguém estar olhando, e que
/// progresso de um trabalho velho não escreve por cima do novo.
/// </remarks>
public sealed class RegistroDeTranscricoesTests
{
    [Fact]
    public void ComecaVazio()
    {
        var r = new RegistroDeTranscricoes();

        Assert.Null(r.Atual);
        Assert.Null(r.Ultimo);
        Assert.False(r.Ocupado);
    }

    [Fact]
    public void ASegundaTranscricaoERecusadaNomeandoAPrimeira()
    {
        // A régua do critério C: recusar sem dizer o quê manda o usuário
        // procurar sozinho qual reunião está ocupando a placa.
        var r = new RegistroDeTranscricoes();
        r.Comecar("C:/gravacoes/manha", "Comitê de dados");

        var erro = Assert.Throws<InvalidOperationException>(
            () => r.Comecar("C:/gravacoes/tarde", "Sprint"));

        Assert.Contains("Comitê de dados", erro.Message);
        Assert.Equal("C:/gravacoes/manha", r.Atual?.Gravacao);
    }

    [Fact]
    public void DepoisDeTerminarDaParaComecarOutra()
    {
        var r = new RegistroDeTranscricoes();
        r.Comecar("a", "A");
        r.Terminar("a");

        Assert.False(r.Ocupado);
        r.Comecar("b", "B");
        Assert.Equal("b", r.Atual?.Gravacao);
    }

    [Fact]
    public void OProgressoSoAtingeOTrabalhoAtual()
    {
        // Um aviso atrasado do pipeline anterior não pode reescrever a etapa do
        // trabalho de agora: as duas mensagens chegam pela mesma thread de fora.
        var r = new RegistroDeTranscricoes();
        r.Comecar("a", "A");
        r.Progredir("a", "asr", 0.5, "metade");
        r.Terminar("a");
        r.Comecar("b", "B");

        r.Progredir("a", "diarizacao", 0.9, "quase");

        Assert.Equal("mix", r.Atual?.Etapa);
        Assert.Equal(0, r.Atual?.Fracao);
    }

    [Fact]
    public void OErroFicaGuardadoDepoisDeTerminar()
    {
        // É o que permite descobrir a falha ao voltar para a tela: sem isto o
        // erro só existiria enquanto houvesse alguém olhando.
        var r = new RegistroDeTranscricoes();
        r.Comecar("a", "A");
        r.Terminar("a", "o motor de ASR não respondeu");

        Assert.Null(r.Atual);
        Assert.Equal("o motor de ASR não respondeu", r.Ultimo?.Erro);
        Assert.True(r.Ultimo?.Terminou);
    }

    [Fact]
    public void TerminarBemFechaABarraEm100()
    {
        // A última fração reportada pelo pipeline não é necessariamente 1, e uma
        // barra parada em 97% num trabalho concluído parece travada.
        var r = new RegistroDeTranscricoes();
        r.Comecar("a", "A");
        r.Progredir("a", "montagem", 0.97, "montando");
        r.Terminar("a");

        Assert.Equal(1, r.Ultimo?.Fracao);
        Assert.Null(r.Ultimo?.Erro);
    }

    [Fact]
    public void UmaTentativaNovaApagaOResultadoDaAnterior()
    {
        var r = new RegistroDeTranscricoes();
        r.Comecar("a", "A");
        r.Terminar("a", "falhou");

        r.Comecar("a", "A");

        Assert.Null(r.Ultimo);
    }

    [Fact]
    public void TerminarOQueNaoEstaRodandoNaoFazNada()
    {
        var r = new RegistroDeTranscricoes();
        r.Comecar("a", "A");

        Assert.Null(r.Terminar("b"));
        Assert.Equal("a", r.Atual?.Gravacao);
    }

    [Fact]
    public void CancelarSinalizaOTokenQueChegaAosMotores()
    {
        // O token é o que o MotorSidecar registra para matar o processo — e
        // matar é o que devolve a VRAM na hora. Sem isto o "parar" seria um
        // botão que só muda a tela.
        var r = new RegistroDeTranscricoes();
        var t = r.Comecar("a", "A");

        Assert.False(t.Token.IsCancellationRequested);
        Assert.True(r.Cancelar("a"));
        Assert.True(t.Token.IsCancellationRequested);
    }

    [Fact]
    public void CancelarNaoLiberaORegistroSozinho()
    {
        // Quem tira do registro é o Terminar, quando os motores de fato morrem.
        // Liberar aqui deixaria a próxima transcrição começar com dois modelos
        // ainda na placa — exatamente o que a trava existe para impedir.
        var r = new RegistroDeTranscricoes();
        r.Comecar("a", "A");
        r.Cancelar("a");

        Assert.True(r.Ocupado);
        Assert.Throws<InvalidOperationException>(() => r.Comecar("b", "B"));

        r.Terminar("a", cancelada: true);
        Assert.False(r.Ocupado);
    }

    [Fact]
    public void TerminarCanceladaNaoEUmErro()
    {
        // A tela trata os dois de maneiras opostas: falha pede alerta vermelho,
        // parar a pedido é o app obedecendo.
        var r = new RegistroDeTranscricoes();
        r.Comecar("a", "A");
        r.Progredir("a", "asr", 0.4, "metade");
        r.Terminar("a", cancelada: true);

        Assert.True(r.Ultimo?.Cancelada);
        Assert.Null(r.Ultimo?.Erro);
        // A fração não salta para 1: a transcrição não chegou ao fim.
        Assert.Equal(0.4, r.Ultimo?.Fracao);
    }

    [Fact]
    public void CancelarSemNadaRodandoNaoQuebra()
    {
        var r = new RegistroDeTranscricoes();
        Assert.False(r.Cancelar());
        Assert.False(r.Cancelar("a"));
    }

    [Fact]
    public void CancelarOutraGravacaoNaoDerrubaAQueRoda()
    {
        // A tela manda o caminho de quem ela está mostrando. Se for outra, o
        // pedido não pode atingir a transcrição em curso.
        var r = new RegistroDeTranscricoes();
        var t = r.Comecar("a", "A");

        Assert.False(r.Cancelar("b"));
        Assert.False(t.Token.IsCancellationRequested);
    }

    [Fact]
    public void DuasChamadasConcorrentesSoDeixamUmaPassar()
    {
        // A trava não é decoração: o pedido chega pela thread da UI e o fim do
        // pipeline vem de uma thread de trabalho.
        var r = new RegistroDeTranscricoes();
        int passaram = 0;

        Parallel.For(0, 32, i =>
        {
            try
            {
                r.Comecar($"gravacao-{i}", $"G{i}");
                Interlocked.Increment(ref passaram);
            }
            catch (InvalidOperationException) { /* recusada, que é o esperado */ }
        });

        Assert.Equal(1, passaram);
    }
}
