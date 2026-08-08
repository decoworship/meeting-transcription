using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

public sealed class ConfiguracoesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "cfg-" + Guid.NewGuid().ToString("N")[..8]);

    public ConfiguracoesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);
    private string Caminho => Path.Combine(_dir, "settings.json");

    [Fact]
    public void LeOFormatoDoGravadorPython()
    {
        // Os dois coexistem durante a Fase 1: quem trocar de um para o outro não
        // pode ter que reconfigurar nada.
        File.WriteAllText(Caminho, """
            {
              "mic_index": null,
              "loopback_index": 3,
              "output_dir": "D:\\gravacoes",
              "start_muted": true,
              "use_calendar": false
            }
            """);

        var c = Configuracoes.Carregar(Caminho);
        Assert.Null(c.MicIndex);
        Assert.Equal(3, c.LoopbackIndex);
        Assert.Equal(@"D:\gravacoes", c.OutputDir);
        Assert.True(c.StartMuted);
        Assert.False(c.UseCalendar);
        // Chave que o Python não conhece: assume o padrão em vez de falhar.
        Assert.True(c.Notifications);
    }

    [Fact]
    public void ArquivoIlegivelCaiNosPadroesEmVezDeFalhar()
    {
        // settings.json corrompido não pode impedir uma gravação.
        File.WriteAllText(Caminho, "{ isto não é json");
        var c = Configuracoes.Carregar(Caminho);
        Assert.True(c.UseCalendar);
        Assert.True(c.Notifications);
    }

    [Fact]
    public void ArquivoInexistenteCaiNosPadroes()
    {
        var c = Configuracoes.Carregar(Path.Combine(_dir, "nao-existe.json"));
        Assert.True(c.Notifications);
        Assert.False(c.StartMuted);
    }

    [Fact]
    public void SalvaEReleSemPerder()
    {
        var c = new Configuracoes
        {
            MicIndex = 1, LoopbackIndex = 2, OutputDir = @"C:\x",
            StartMuted = true, UseCalendar = false, Notifications = false,
        };
        c.Salvar(Caminho);

        var lida = Configuracoes.Carregar(Caminho);
        Assert.Equal(1, lida.MicIndex);
        Assert.Equal(2, lida.LoopbackIndex);
        Assert.Equal(@"C:\x", lida.OutputDir);
        Assert.True(lida.StartMuted);
        Assert.False(lida.UseCalendar);
        Assert.False(lida.Notifications);
    }

    [Fact]
    public void NaoDeixaArquivoTemporarioParaTras()
    {
        new Configuracoes().Salvar(Caminho);
        Assert.False(File.Exists(Caminho + ".tmp"));
    }
}
