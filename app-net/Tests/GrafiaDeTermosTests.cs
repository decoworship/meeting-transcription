using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// As variantes de espaço e hífen — o termo ouvido certo e escrito separado.
/// </summary>
/// <remarks>
/// <para>
/// <b>O que estes testes guardam.</b> A Fase 7 mediu que parte do vocabulário
/// que se dava por perdido não estava perdido: o MOSS escreve <c>next best</c>
/// onde o projeto escreve <c>NextBest</c>, e <c>lifecycle</c> onde o projeto
/// escreve <c>life cycle</c>. Contando a forma canônica, a distância entre os
/// dois motores era de 26,4 pontos; contando o termo como dito, é de 15,8. A
/// diferença são estas variantes (<c>docs/FASE7-RESULTADOS.md</c> §12.2).
/// </para>
/// <para>
/// O que eles guardam de verdade é o <b>corte</b>: a regra é segura porque exige
/// as mesmas letras na mesma ordem e um separador de um dos lados. Frouxá-la é
/// fácil e o estrago é o de sempre — reescrever português correto.
/// </para>
/// </remarks>
public sealed class GrafiaDeTermosTests
{
    private static IReadOnlyList<Proposta> Propor(string texto, string vocabulario) =>
        RevisaoDeTermos.Validar(
            RevisaoDeTermos.ProporGrafia([texto], [vocabulario]), [vocabulario]);

    [Fact]
    public void ColaOQueOModeloSeparou()
    {
        var p = Propor("a gente usa next best pra priorizar", "NextBest, Algar");

        var troca = Assert.Single(p);
        Assert.Equal("next best", troca.De);
        Assert.Equal("NextBest", troca.Para);
        Assert.Equal("grafia", troca.Fonte);
    }

    [Fact]
    public void SeparaOQueOModeloColou()
    {
        // O outro sentido do mesmo caso, e quem manda é sempre o vocabulário: a
        // forma canônica é a que a pessoa digitou.
        var p = Propor("o lifecycle do produto", "life cycle");

        var troca = Assert.Single(p);
        Assert.Equal("lifecycle", troca.De);
        Assert.Equal("life cycle", troca.Para);
    }

    [Fact]
    public void OHifenEUmSeparadorComoOEspaco()
    {
        var p = Propor("isso é must have para o cliente", "must-have");
        Assert.Equal("must-have", Assert.Single(p).Para);
    }

    [Fact]
    public void ATrocaChegaAoTexto()
    {
        const string texto = "a gente usa next best pra priorizar";
        var (saida, trocas) = RevisaoDeTermos.Aplicar(texto, Propor(texto, "NextBest"));

        Assert.Equal("a gente usa NextBest pra priorizar", saida);
        // Toda troca fica registrada: é o que a tela mostra e o que a pessoa
        // desfaz. Ver SegmentoFinal.Swaps.
        Assert.Single(trocas);
    }

    [Fact]
    public void SoMaiusculaNaoEMotivoParaReescrever()
    {
        // **O corte que mantém a regra estreita.** Sem ele, um projeto com
        // "Cloud" no vocabulário teria toda ocorrência de "cloud" reescrita — e
        // trocar maiúscula não é o defeito que foi medido. É a mesma lição que
        // custou "Falar→Algar" na primeira versão da classe irmã.
        Assert.Empty(Propor("a gente subiu pra cloud ontem", "Cloud"));
    }

    [Fact]
    public void NaoAtravessaPontuacao()
    {
        // "next" e "best" aqui são de orações diferentes. Colar através da
        // vírgula inventaria um termo de dois pedaços que não se tocam.
        Assert.Empty(Propor("veio o next, best que pudemos fazer", "NextBest"));
    }

    [Fact]
    public void NaoInventaTermoComArtigo()
    {
        // "a PI" é português correto, e num projeto com "API" no vocabulário
        // seria a única forma de esta regra inventar um termo. Palavra de uma
        // letra não entra numa colagem.
        Assert.Empty(Propor("isso vai para a PI do cliente", "API"));
    }

    [Fact]
    public void AsLetrasTemQueSerAsMesmas()
    {
        // Ela não é uma segunda regra de distância: "nest best" tem outra letra
        // e não é alcançável por aqui. Quem pega erro de escuta é o Propor.
        Assert.Empty(Propor("a gente usa nest best pra priorizar", "NextBest"));
    }

    [Fact]
    public void OAlvoTemQueSerEntidadeConhecida()
    {
        // A mesma exigência que toda proposta atravessa: a regra não inventa
        // alvo, e o Validar é a porta única.
        Assert.Empty(RevisaoDeTermos.Validar(
            [new Proposta("next best", "NextBest", "grafia")], ["Algar"]));
    }

    [Fact]
    public void OTermoJaCanonicoNaoViraProposta()
    {
        Assert.Empty(Propor("a gente usa NextBest pra priorizar", "NextBest"));
    }
}
