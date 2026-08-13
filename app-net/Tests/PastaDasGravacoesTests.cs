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
}
