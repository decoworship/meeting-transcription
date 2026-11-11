using MeetingApp.Nucleo;
using MeetingRecorder.Core;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// Uma pasta só para gravar e para ler (Fase 2.5, critério F).
/// </summary>
/// <remarks>
/// Eram dois programas com duas chaves, e uma delas — o
/// <c>pasta_das_gravacoes</c> do <c>app.json</c> — era escrita pela tela de
/// ajustes e <b>lida por ninguém</b>. Fundidos, a autoridade é o
/// <c>output_dir</c> do gravador, porque é onde o áudio de fato cai; o que
/// alguém tiver escolhido na chave morta precisa sobreviver à fusão sem
/// reconfigurar nada.
/// </remarks>
public sealed class PastaDasGravacoesTests : IDisposable
{
    private readonly string _pasta =
        Directory.CreateTempSubdirectory("pasta-gravacoes").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Settings => Path.Combine(_pasta, "settings.json");
    private string AppJson => Path.Combine(_pasta, "app.json");

    [Fact]
    public void OArgumentoVenceTudoENaoGravaNada()
    {
        var cfg = new Configuracoes { OutputDir = @"C:\configurada" };

        string r = PastaDasGravacoes.Resolver(cfg, @"C:\de-suporte", Settings, AppJson);

        Assert.Equal(@"C:\de-suporte", r);
        // Abrir o acervo de outra máquina não pode mudar onde esta grava.
        Assert.Equal(@"C:\configurada", cfg.OutputDir);
        Assert.False(File.Exists(Settings));
    }

    [Fact]
    public void SemNadaConfiguradoCaiNoPadrao()
    {
        string r = PastaDasGravacoes.Resolver(new Configuracoes(), null, Settings, AppJson);

        Assert.Equal(PastaDasGravacoes.Padrao, r);
        Assert.EndsWith("MeetingRecordings", r);
    }

    [Fact]
    public void APastaDoGravadorVenceADoApp()
    {
        new ConfiguracoesDoApp { PastaDasGravacoes = @"C:\do-app" }.Salvar(AppJson);
        var cfg = new Configuracoes { OutputDir = @"C:\do-gravador" };

        Assert.Equal(@"C:\do-gravador",
            PastaDasGravacoes.Resolver(cfg, null, Settings, AppJson));
    }

    [Fact]
    public void AEscolhaFeitaNoAppMigraParaOGravador()
    {
        new ConfiguracoesDoApp { PastaDasGravacoes = @"D:\Reunioes" }.Salvar(AppJson);
        var cfg = new Configuracoes();

        string r = PastaDasGravacoes.Resolver(cfg, null, Settings, AppJson);

        Assert.Equal(@"D:\Reunioes", r);
        // Gravada dos dois lados: no do gravador porque passou a valer, e
        // apagada no do app para a migração não acontecer de novo depois de
        // alguém trocar a pasta pelo menu da bandeja.
        Assert.Equal(@"D:\Reunioes", Configuracoes.Carregar(Settings).OutputDir);
        Assert.Null(ConfiguracoesDoApp.Carregar(AppJson).PastaDasGravacoes);
    }

    [Fact]
    public void MigrarDuasVezesNaoDesfazUmaTrocaPosterior()
    {
        new ConfiguracoesDoApp { PastaDasGravacoes = @"D:\Reunioes" }.Salvar(AppJson);
        PastaDasGravacoes.Resolver(new Configuracoes(), null, Settings, AppJson);

        // O usuário troca a pasta pelo menu da bandeja, e o app reabre.
        var depois = new Configuracoes { OutputDir = @"E:\Outra" };

        Assert.Equal(@"E:\Outra",
            PastaDasGravacoes.Resolver(depois, null, Settings, AppJson));
    }

    // A janela mapeia esta pasta no WebView2 antes de carregar a página, e o
    // mapeamento falha se ela não existe. Numa máquina nova ela só nascia na
    // primeira gravação — e até lá o app abria com a janela vazia (PR 444966
    // do winget-pkgs, 10/11/2026).
    [Fact]
    public void GarantirCriaAPastaQueAindaNaoExiste()
    {
        string nova = Path.Combine(_pasta, "Documentos", "MeetingRecordings");

        Assert.True(PastaDasGravacoes.Garantir(nova));
        Assert.True(Directory.Exists(nova));
    }

    [Fact]
    public void GarantirAceitaAPastaQueJaExiste()
    {
        Assert.True(PastaDasGravacoes.Garantir(_pasta));
    }

    [Fact]
    public void GarantirNaoLancaQuandoAPastaNaoPodeSerCriada()
    {
        // Um arquivo no meio do caminho: o mesmo efeito de um compartilhamento
        // de rede fora do ar. A janela tem de abrir mesmo assim.
        string arquivo = Path.Combine(_pasta, "um-arquivo");
        File.WriteAllText(arquivo, "");

        Assert.False(PastaDasGravacoes.Garantir(Path.Combine(arquivo, "sub")));
    }
}
