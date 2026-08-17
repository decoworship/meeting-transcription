using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// O catálogo de pacotes de modelo.
/// </summary>
/// <remarks>
/// O que estes testes protegem é a única parte que não é declaração: traduzir
/// o nome do repositório para a pasta do cache. Errar essa tradução faz a tela
/// dizer "ainda não baixado" sobre 3 GB que estão no disco — um erro silencioso,
/// que só apareceria como um download repetido.
/// </remarks>
public sealed class CatalogoTests
{
    [Theory]
    [InlineData("Systran/faster-whisper-large-v3", "models--Systran--faster-whisper-large-v3")]
    [InlineData("pyannote/speaker-diarization-community-1",
                "models--pyannote--speaker-diarization-community-1")]
    public void ATraducaoDoRepositorioPreservaOsHifensDoNome(string repo, string pasta)
    {
        var pacote = new PacoteDeModelo
        {
            Id = "x", Nome = "X", Familia = "asr", Descricao = "",
            Repositorio = repo, TamanhoEsperadoBytes = 1,
        };

        // Só a barra vira "--". Se os hifens do nome dobrassem, o caminho não
        // existiria e o pacote apareceria como ausente para sempre.
        Assert.Equal(pasta, Path.GetFileName(Catalogo.PastaDoPacote(pacote)));
    }

    [Fact]
    public void OCacheSegueAVariavelDeAmbienteDoHuggingFace()
    {
        string antes = Environment.GetEnvironmentVariable("HF_HUB_CACHE") ?? "";
        try
        {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", "/outro/disco/hub");
            Assert.Equal("/outro/disco/hub", Catalogo.PastaDoCache());
        }
        finally
        {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE",
                antes.Length == 0 ? null : antes);
        }
    }

    [Fact]
    public void MarcaEmUsoOQueAConfiguracaoEscolheu()
    {
        var config = new ConfiguracoesDoApp
        {
            ModeloPadrao = "medium",
            ModeloDeAta = "qwen3-4b-instruct-q4km.gguf",
        };

        var lista = Catalogo.Listar(config);

        Assert.True(lista.Single(i => i.Pacote.Id == "medium").EmUso);
        Assert.False(lista.Single(i => i.Pacote.Id == "large-v3").EmUso);

        // Um por família, nunca dois: a tela usa isto para acender um cartão só.
        Assert.Single(lista, i => i.Pacote.Familia == "asr" && i.EmUso);
        Assert.Single(lista, i => i.Pacote.Familia == "ata" && i.EmUso);
    }

    [Fact]
    public void PacoteAusenteNaoQuebraNemInventaTamanho()
    {
        string vazio = Directory.CreateTempSubdirectory("catalogo-vazio").FullName;
        string antes = Environment.GetEnvironmentVariable("HF_HUB_CACHE") ?? "";
        try
        {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", vazio);
            var lista = Catalogo.Listar(new ConfiguracoesDoApp());

            Assert.All(lista, i =>
            {
                Assert.Equal("ausente", i.Estado);
                Assert.Equal(0, i.BytesEmDisco);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE",
                antes.Length == 0 ? null : antes);
            Directory.Delete(vazio, recursive: true);
        }
    }

    [Fact]
    public void PacoteBaixadoPelaMetadeApareceComoParcial()
    {
        // É o caso que o usuário mais provavelmente vê: fechar o app no meio de
        // um download de 3 GB. Se isto aparecesse como "instalado", a próxima
        // transcrição falharia com um erro do faster-whisper, longe da causa.
        string cache = Directory.CreateTempSubdirectory("catalogo-parcial").FullName;
        string antes = Environment.GetEnvironmentVariable("HF_HUB_CACHE") ?? "";
        try
        {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", cache);

            var pacote = Catalogo.Pacotes.Single(p => p.Id == "large-v3");
            string pasta = Catalogo.PastaDoPacote(pacote);
            Directory.CreateDirectory(pasta);
            File.WriteAllBytes(Path.Combine(pasta, "model.bin.incomplete"), new byte[1024]);

            var item = Catalogo.Listar(new ConfiguracoesDoApp())
                               .Single(i => i.Pacote.Id == "large-v3");

            Assert.Equal("parcial", item.Estado);
            Assert.Equal(1024, item.BytesEmDisco);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE",
                antes.Length == 0 ? null : antes);
            Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void TodoPacoteTemIdUnicoEFamiliaConhecida()
    {
        // O id vai cru para o `--modelo` do motor e para o app.json. Dois iguais
        // fariam a tela escolher um e o motor carregar outro.
        Assert.Equal(Catalogo.Pacotes.Count,
                     Catalogo.Pacotes.Select(p => p.Id).Distinct().Count());

        // "ata" entrou na Fase 3, e "diarizacao" saiu na Fase 4 — os pesos
        // passaram a viajar dentro do instalador, e o catálogo é sobre o que se
        // baixa. A lista é fechada de propósito: a tela agrupa por família, e
        // uma família nova sem bloco na tela some sem avisar.
        Assert.All(Catalogo.Pacotes, p =>
            Assert.Contains(p.Familia, new[] { "asr", "ata" }));
    }

    [Fact]
    public void ADiarizacaoNaoEstaMaisNoCatalogo()
    {
        // Ela viaja dentro do instalador desde a Fase 4. Um pacote de
        // diarização aqui voltaria a medir o cache do HuggingFace, que o motor
        // não lê mais: diria "ausente" sobre uma diarização que funciona, e
        // ofereceria "Remover" sobre arquivos do instalador.
        Assert.DoesNotContain(Catalogo.Pacotes, p => p.Familia == "diarizacao");
    }

    [Fact]
    public void ModeloAusenteImpedeATranscricaoComUmaFraseQueDizOQueFazer()
    {
        string vazio = Directory.CreateTempSubdirectory("catalogo-impede").FullName;
        string antes = Environment.GetEnvironmentVariable("HF_HUB_CACHE") ?? "";
        try
        {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", vazio);

            string? impede = Catalogo.OQueImpede("large-v3");

            // A frase importa mais que o booleano: ela é o que a pessoa lê no
            // lugar de "o motor morreu". Tem que dizer onde ir.
            Assert.NotNull(impede);
            Assert.Contains("Modelos", impede);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HF_HUB_CACHE",
                antes.Length == 0 ? null : antes);
            Directory.Delete(vazio, recursive: true);
        }
    }

    [Fact]
    public void ModeloDesconhecidoNaoEBarrado()
    {
        // Ignorância não é veto: quem aponta para um modelo que não está no
        // catálogo pode ter montado o cache à mão, e barrar por não conhecer
        // quebraria um arranjo que funciona.
        Assert.Null(Catalogo.OQueImpede("um-modelo-que-nao-conhecemos"));
        Assert.Null(Catalogo.OQueImpede(null));
        Assert.Null(Catalogo.OQueImpede(""));
    }

    [Fact]
    public void ODiscoDeDestinoERespondidoAntesDeBaixar()
    {
        // Não se afirma quanto é livre — isso é da máquina. O que se protege é
        // que a pergunta tenha resposta: -1 em toda máquina faria a checagem de
        // espaço nunca reprovar nada, silenciosamente.
        var pacote = Catalogo.Pacotes.First(p => p.Familia == "asr");
        Assert.True(Catalogo.LivreNoDestino(pacote) > 0);
    }

    [Fact]
    public void PacoteDeAtaSabeQualArquivoBaixarEComoChamarEmDisco()
    {
        // Sem `arquivo`, o download traria o repositório inteiro — uma dezena de
        // quantizações, 20 GB para usar 2,5. Sem `nome_local`, a configuração
        // carregaria a quantização no nome e mudar de quantização quebraria o
        // que já estava escolhido.
        var atas = Catalogo.Pacotes.Where(p => p.Familia == "ata").ToList();

        Assert.NotEmpty(atas);
        Assert.All(atas, p =>
        {
            Assert.EndsWith(".gguf", p.Arquivo);
            Assert.EndsWith(".gguf", p.NomeLocal);
        });
    }

    [Fact]
    public void OModeloDeAtaNaoMoraNoCacheDoHuggingFace()
    {
        // Quem abre o .gguf é o llama.cpp, por caminho, e não a biblioteca do
        // HF: ele fica ao lado do llama-server.
        var ata = Catalogo.Pacotes.First(p => p.Familia == "ata");

        Assert.Contains("ata", Catalogo.ArquivoDoPacote(ata));
        Assert.EndsWith(ata.NomeLocal!, Catalogo.ArquivoDoPacote(ata));
    }
}
