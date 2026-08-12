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
            DiarizacaoPadrao = "3.1",
        };

        var lista = Catalogo.Listar(config);

        Assert.True(lista.Single(i => i.Pacote.Id == "medium").EmUso);
        Assert.True(lista.Single(i => i.Pacote.Id == "3.1").EmUso);
        Assert.False(lista.Single(i => i.Pacote.Id == "large-v3").EmUso);

        // Um por família, nunca dois: a tela usa isto para acender um cartão só.
        Assert.Single(lista, i => i.Pacote.Familia == "asr" && i.EmUso);
        Assert.Single(lista, i => i.Pacote.Familia == "diarizacao" && i.EmUso);
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

        Assert.All(Catalogo.Pacotes, p =>
            Assert.Contains(p.Familia, new[] { "asr", "diarizacao" }));
    }
}
