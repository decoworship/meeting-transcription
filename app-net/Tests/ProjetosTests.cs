using System.Text.Json;
using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Clientes, projetos e preferências — o mesmo arquivo do app Python.
/// </summary>
public sealed class ProjetosTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("projetos-testes").FullName;

    private string Caminho => Path.Combine(_pasta, "projects.json");
    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    /// <summary>Um projects.json no formato que o app Python escreve.</summary>
    private void Semear(string conteudo) => File.WriteAllText(Caminho, conteudo);

    [Fact]
    public void LeOFormatoDoAppPython()
    {
        Semear("""
        {
          "clients": {
            "Algar": {
              "projects": {
                "Agentes": {
                  "language": "pt",
                  "model_size": "large-v3",
                  "engine": "faster-whisper",
                  "diarization": true,
                  "condition_on_previous_text": false,
                  "diar_model": "community-1",
                  "initial_prompt": "Beegol, NoBill"
                }
              }
            }
          }
        }
        """);

        var p = new Projetos(Caminho);
        Assert.Equal(["Algar"], p.ListarClientes());
        Assert.Equal(["Agentes"], p.ListarProjetos("Algar"));

        var prefs = p.Preferencias("Algar", "Agentes");
        Assert.NotNull(prefs);
        Assert.Equal("large-v3", prefs.ModelSize);
        Assert.Equal("pt", prefs.Language);
        Assert.True(prefs.Diarization);
        Assert.Equal("Beegol, NoBill", prefs.InitialPrompt);
    }

    [Fact]
    public void ClienteEProjetoNovosNascemAoSalvar()
    {
        // O fluxo que o usuário pediu para manter: digitar um nome que não
        // existe e sair transcrevendo, sem passar por um cadastro à parte.
        var p = new Projetos(Caminho);
        p.Salvar("Cliente Novo", "Projeto Novo",
                 new PreferenciasDoProjeto { ModelSize = "medium", Language = "pt" });

        var relido = new Projetos(Caminho);
        Assert.Contains("Cliente Novo", relido.ListarClientes());
        Assert.Equal(["Projeto Novo"], relido.ListarProjetos("Cliente Novo"));
        Assert.Equal("medium", relido.Preferencias("Cliente Novo", "Projeto Novo")!.ModelSize);
    }

    [Fact]
    public void SalvarNaoApagaChavesQueEsteCodigoNaoConhece()
    {
        // O app Python continua sendo a ferramenta de produção e pode ter
        // escrito campos que só ele entende. Reserializar o objeto inteiro os
        // apagaria — e o usuário só descobriria ao voltar para o outro app.
        Semear("""
        {
          "clients": {
            "Vivo": {
              "projects": {
                "Faturamento B2B": { "model_size": "small", "campo_do_futuro": 42 }
              }
            }
          }
        }
        """);

        var p = new Projetos(Caminho);
        p.Salvar("Vivo", "Faturamento B2B", new PreferenciasDoProjeto { ModelSize = "large-v3" });

        using var doc = JsonDocument.Parse(File.ReadAllText(Caminho));
        var projeto = doc.RootElement.GetProperty("clients").GetProperty("Vivo")
            .GetProperty("projects").GetProperty("Faturamento B2B");

        Assert.Equal("large-v3", projeto.GetProperty("model_size").GetString());
        Assert.Equal(42, projeto.GetProperty("campo_do_futuro").GetInt32());
    }

    [Fact]
    public void ArquivoIlegivelNaoDerrubaOApp()
    {
        // Mesma postura do settings.json do gravador: cair no vazio é melhor
        // que impedir de transcrever por causa de um cadastro corrompido.
        Semear("{ isto não é json");
        var p = new Projetos(Caminho);
        Assert.Empty(p.ListarClientes());
    }

    [Fact]
    public void SemArquivoNenhumComecaVazio()
    {
        var p = new Projetos(Path.Combine(_pasta, "nao-existe.json"));
        Assert.Empty(p.ListarClientes());
    }

    [Fact]
    public void OCadastroRealMigradoEhLegivel()
    {
        // Lê o arquivo que veio do app Python nesta máquina. Se ele não estiver
        // aqui — outra máquina, ou ninguém migrou —, o teste não tem o que
        // provar e sai; o que ele não pode é passar em silêncio quando o
        // arquivo existe e não é entendido.
        string real = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".meeting-transcription", "projects.json");
        if (!File.Exists(real)) return;

        var p = new Projetos(real);
        var clientes = p.ListarClientes();
        Assert.NotEmpty(clientes);
        Assert.All(clientes, c => Assert.NotEmpty(p.ListarProjetos(c)));
    }
}
