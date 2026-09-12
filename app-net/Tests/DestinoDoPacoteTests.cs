using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Onde cada pacote cai no disco — a pasta de cache ou um arquivo avulso.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que este arquivo existe.</b> Em 09/09/2026, o primeiro download do
/// GGUF do MOSS na máquina do dono do produto **funcionou e falhou ao mesmo
/// tempo**: baixou os 700.313.760 bytes certos, com a assinatura <c>GGUF</c>
/// intacta, e os gravou **no lugar da pasta** — <c>motores/moss/modelos</c>
/// virou um arquivo de 700 MB em vez de uma pasta com o modelo dentro. O motor
/// procurou <c>modelos/MOSS-….gguf</c>, não achou, e a tela seguiu dizendo
/// "ausente" sobre um download que tinha dado certo.
/// </para>
/// <para>
/// <b>A causa foi uma pergunta respondida em dois lugares.</b> O
/// <c>Catalogo</c> sabia, pelo <see cref="Catalogo.EhArquivoAvulso"/>, que
/// "ata" e "moss" são famílias de arquivo único; a <c>App/Ponte.cs</c>
/// perguntava a mesma coisa com um <c>Familia == "ata"</c> escrito à mão, e
/// ficou para trás quando a família nova chegou. É a mesma forma do defeito que
/// o <see cref="MarcaTests"/> guarda.
/// </para>
/// <para>
/// <b>O que dá para testar daqui.</b> A escolha do destino mora na
/// <c>Ponte</c>, que é <c>net8.0-windows</c> e não entra nesta suíte. O que
/// entra é a invariante que a Ponte tem de respeitar — e é ela que, violada,
/// produz exatamente o arquivo de 700 MB no lugar da pasta.
/// </para>
/// </remarks>
public sealed class DestinoDoPacoteTests
{
    [Fact]
    public void NoArquivoAvulsoOModeloFicaDENTRODaPastaDoPacote()
    {
        // Se o arquivo e a pasta fossem o mesmo caminho, baixar "para a pasta"
        // e baixar "para o arquivo" dariam no mesmo, e o erro seria impossível.
        // Eles são diferentes, e é por isso que quem baixa precisa escolher — e
        // por isso a escolha não pode estar escrita em dois lugares.
        foreach (var p in Catalogo.Pacotes.Where(Catalogo.EhArquivoAvulso))
        {
            string pasta = Catalogo.PastaDoPacote(p);
            string arquivo = Catalogo.ArquivoDoPacote(p);

            Assert.NotEqual(pasta, arquivo);
            Assert.Equal(pasta, Path.GetDirectoryName(arquivo));
            Assert.False(string.IsNullOrEmpty(Path.GetFileName(arquivo)),
                $"{p.Id} não tem nome de arquivo — baixar escreveria por cima da pasta.");
        }
    }

    [Fact]
    public void TodaFamiliaDeArquivoAvulsoDizQualArquivoBaixar()
    {
        // O `arquivo` vai ao sidecar `modelos` e é o que faz o download trazer
        // UM .gguf em vez do repositório inteiro — que nos de ata seriam 20 GB
        // para usar 2,5.
        foreach (var p in Catalogo.Pacotes.Where(Catalogo.EhArquivoAvulso))
            Assert.False(string.IsNullOrWhiteSpace(p.Arquivo),
                $"{p.Id} é de arquivo avulso e não diz qual arquivo baixar.");
    }

    [Fact]
    public void OMossEArquivoAvulsoEOAsrNao()
    {
        // Os dois lados da distinção, cravados: o ASR é repositório inteiro no
        // cache do HuggingFace; o MOSS é um GGUF que o transcribe.cpp abre por
        // caminho, como o llama.cpp faz com os de ata.
        var moss = Catalogo.Pacotes.Single(p => p.Familia == "moss");
        var asr = Catalogo.Pacotes.First(p => p.Familia == "asr");

        Assert.True(Catalogo.EhArquivoAvulso(moss));
        Assert.False(Catalogo.EhArquivoAvulso(asr));

        // E o nome do arquivo é o que o motores/moss/motor.py procura. Os dois
        // estão escritos nos dois lugares de propósito — o motor precisa dele
        // para carregar e o catálogo para baixar —, então um teste os amarra.
        Assert.Equal("MOSS-Transcribe-Diarize-Q5_K_M.gguf",
                     Path.GetFileName(Catalogo.ArquivoDoPacote(moss)));
    }
}
