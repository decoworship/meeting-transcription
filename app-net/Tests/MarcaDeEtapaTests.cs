using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O marcador que diz em que etapa a transcrição estava quando a máquina caiu.
/// </summary>
/// <remarks>
/// <b>O que estes testes protegem</b> é o único sinal do <c>SUP-2</c> que não
/// depende de pedir informação ao usuário: um marcador que sobra prova que a
/// transcrição anterior não terminou, e diz onde parou. Se ele passar a ser
/// apagado no caminho errado — ou a não ser escrito —, a falha é silenciosa e só
/// aparece na próxima vez que a máquina de alguém desligar.
/// <para>
/// Os testes correm no <c>HOME</c> de verdade, porque é de lá que sai o
/// <see cref="MarcaDeEtapa.Caminho"/>. Cada um limpa antes e depois: o marcador
/// é um arquivo só no app inteiro, então dois testes que o dividissem sem
/// limpar contaminariam um ao outro.
/// </para>
/// </remarks>
/// <summary>
/// As classes que tocam o <c>MarcaDeEtapa</c> — um arquivo só no app inteiro.
/// </summary>
/// <remarks>
/// O <c>Transcritor.ExecutarAsync</c> abre e fecha o marcador, e os testes dele
/// correriam **em paralelo** com os do próprio marcador, no mesmo caminho. A
/// falha era intermitente, que é a pior: passa na hora em que se olha. Uma
/// coleção compartilhada serializa as duas classes, e é mais honesto que dar ao
/// código de produção um caminho configurável que só o teste usaria.
/// </remarks>
[CollectionDefinition("marcador de etapa")]
public sealed class ColecaoDoMarcador { }

[Collection("marcador de etapa")]
public sealed class MarcaDeEtapaTests : IDisposable
{
    public MarcaDeEtapaTests() => MarcaDeEtapa.Terminar();

    public void Dispose() => MarcaDeEtapa.Terminar();

    [Fact]
    public void ComecarEscreveOMarcadorComAEtapaInicial()
    {
        Assert.Null(MarcaDeEtapa.Comecar("2026-09-10_10-00-00", "classico", "large-v3"));

        var m = MarcaDeEtapa.Ler();
        Assert.NotNull(m);
        Assert.Equal("2026-09-10_10-00-00", m!.Gravacao);
        Assert.Equal("mix", m.Etapa);
        Assert.Equal("classico", m.Motor);
        Assert.Equal("large-v3", m.Modelo);
    }

    [Fact]
    public void EtapaAvancaEOMarcadorAcompanha()
    {
        MarcaDeEtapa.Comecar("g", "moss", "moss-transcribe-diarize");
        MarcaDeEtapa.Etapa("asr");
        MarcaDeEtapa.Etapa("diarizacao");

        Assert.Equal("diarizacao", MarcaDeEtapa.Ler()!.Etapa);
    }

    [Fact]
    public void TerminarApagaOMarcador()
    {
        MarcaDeEtapa.Comecar("g", "classico", "large-v3");
        MarcaDeEtapa.Terminar();

        Assert.Null(MarcaDeEtapa.Ler());
    }

    /// <summary>
    /// O caso que dá nome ao instrumento: o marcador sobreviveu, e o começo
    /// seguinte o encontra.
    /// </summary>
    /// <remarks>
    /// Não há como desligar a máquina no meio de um teste, mas o efeito de um
    /// desligamento é exatamente este — um marcador em disco sem ninguém que o
    /// tenha apagado.
    /// </remarks>
    [Fact]
    public void OMarcadorQueSobraEDevolvidoNoComecoSeguinte()
    {
        MarcaDeEtapa.Comecar("2026-09-09_14-00-00", "moss", "moss-transcribe-diarize");
        MarcaDeEtapa.Etapa("diarizacao");

        var orfa = MarcaDeEtapa.Comecar("2026-09-10_09-00-00", "classico", "large-v3");

        Assert.NotNull(orfa);
        Assert.Equal("2026-09-09_14-00-00", orfa!.Gravacao);
        Assert.Equal("diarizacao", orfa.Etapa);
        Assert.Contains("não terminou", MarcaDeEtapa.Descrever(orfa));
        Assert.Contains("diarizacao", MarcaDeEtapa.Descrever(orfa));

        // E o começo novo não herda a etapa do morto.
        Assert.Equal("mix", MarcaDeEtapa.Ler()!.Etapa);
    }

    /// <summary>
    /// Etapa repetida não reescreve o arquivo.
    /// </summary>
    /// <remarks>
    /// O <c>Transcritor</c> envolve o <c>Progresso</c> inteiro, que é chamado
    /// muitas vezes por etapa. Sem esta guarda, o marcador — que escreve com
    /// <c>WriteThrough</c>, ou seja, esperando o disco — entraria no caminho
    /// crítico da transcrição.
    /// </remarks>
    [Fact]
    public void EtapaRepetidaNaoReescreve()
    {
        MarcaDeEtapa.Comecar("g", "classico", "large-v3");
        MarcaDeEtapa.Etapa("asr");
        var antes = File.GetLastWriteTimeUtc(MarcaDeEtapa.Caminho);

        for (int i = 0; i < 50; i++) MarcaDeEtapa.Etapa("asr");

        Assert.Equal(antes, File.GetLastWriteTimeUtc(MarcaDeEtapa.Caminho));
    }

    [Fact]
    public void EtapaSemTranscricaoAbertaNaoCriaArquivo()
    {
        MarcaDeEtapa.Etapa("asr");

        Assert.Null(MarcaDeEtapa.Ler());
    }

    /// <summary>Um arquivo corrompido não pode derrubar o app.</summary>
    /// <remarks>
    /// É o caso literal do desligamento: a máquina pode cair no meio da própria
    /// escrita do marcador, e o que sobra é meio JSON.
    /// </remarks>
    [Fact]
    public void MarcadorCorrompidoLeComoAusente()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MarcaDeEtapa.Caminho)!);
        File.WriteAllText(MarcaDeEtapa.Caminho, "{\"gravacao\": \"g\", \"eta");

        Assert.Null(MarcaDeEtapa.Ler());
    }
}
