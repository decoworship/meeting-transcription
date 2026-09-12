using MeetingApp.Nucleo;
using Xunit;

namespace MeetingApp.Tests;

/// <summary>
/// A legenda ao vivo — a camada 1, texto sub-segundo durante a reunião.
/// </summary>
/// <remarks>
/// <b>O que estes testes protegem</b> é o que a medição custou a descobrir. O
/// caso que mais dói não é a legenda falhar: é ela conviver com a prévia por
/// blocos e as duas ficarem lentas demais para servir — 2,46× viram 0,45× com as
/// duas ligadas, e abaixo de 1× a fila cresce sem parar
/// (docs/FASE7-ROTA.md §4). Isso não dá erro em lugar nenhum; só fica ruim.
/// </remarks>
public sealed class LegendaAoVivoTests
{
    private static Motores SemMotores() =>
        new("python", "asr.py", "diar.py", "modelos.py");

    [Fact]
    public void DesligadaPorPadrao()
    {
        // Tudo o que roda durante a gravação nasce desligado enquanto o SUP-2
        // estiver aberto. Um padrão que mude aqui é uma regressão de política,
        // não de código.
        Assert.False(new ConfiguracoesDoApp().LegendaAoVivo);
    }

    [Fact]
    public void DesligadaDizPorQueNaoExiste()
    {
        var cfg = new ConfiguracoesDoApp { LegendaAoVivo = false };

        string? impede = LegendaAoVivo.OQueImpede(SemMotores(), cfg);

        Assert.NotNull(impede);
        Assert.Contains("desligada", impede);
    }

    /// <summary>
    /// As duas ao vivo não convivem, e o app recusa antes de a pessoa descobrir.
    /// </summary>
    /// <remarks>
    /// Medido em 11/09/2026 numa reunião real: a legenda sozinha com o Meet
    /// aberto roda a 2,46× o tempo real; somando o bloco do MOSS, despenca para
    /// 0,45×. O sintoma seria a legenda atrasando sem parar — e ninguém ligaria
    /// uma coisa à outra.
    /// </remarks>
    [Fact]
    public void ComAPreviaEmBlocosLigadaARecusaExplicaAPlaca()
    {
        var cfg = new ConfiguracoesDoApp { LegendaAoVivo = true, TranscricaoAoVivo = true };

        string? impede = LegendaAoVivo.OQueImpede(SemMotores(), cfg);

        Assert.NotNull(impede);
        Assert.Contains("não cabem juntas", impede);
    }

    [Fact]
    public void LigadaSozinhaSoFaltaOMotorEmDisco()
    {
        var cfg = new ConfiguracoesDoApp { LegendaAoVivo = true, TranscricaoAoVivo = false };

        string? impede = LegendaAoVivo.OQueImpede(SemMotores(), cfg);

        // Estes caminhos não existem, então o que sobra é a queixa da
        // instalação — e ela não fala de placa nem de chave.
        Assert.NotNull(impede);
        Assert.Contains("motor de legenda", impede);
    }

    [Fact]
    public void OMotorDaLegendaFicaAoLadoDosOutros()
    {
        var m = Motores.AoLadoDoExecutavel();

        Assert.EndsWith(Path.Combine("legenda", "motor.py"), m.ScriptLegenda);
        Assert.NotEqual(m.ScriptMoss, m.ScriptLegenda);
    }

    /// <summary>
    /// O quadro é de 200 ms, e o número tem origem.
    /// </summary>
    /// <remarks>
    /// Medido no <c>R1</c>: menor faz o custo por chamada dominar, maior põe um
    /// piso artificial no atraso — que é o que esta camada existe para manter
    /// baixo. Mudá-lo sem remedir invalida os 0,11 s.
    /// </remarks>
    [Fact]
    public void OQuadroEDeDuzentosMilissegundos()
    {
        Assert.Equal(0.2, LegendaAoVivo.QuadroS);

        // E é muito menor que a folga do bloco: lá ler cedo custa um bloco
        // curto, aqui custa um quadro a repetir.
        Assert.True(LegendaAoVivo.FolgaS < SessaoAoVivo.FolgaDoFlushS);
    }

    /// <summary>
    /// O arquivo que a legenda deixa **não** é o da transcrição.
    /// </summary>
    /// <remarks>
    /// A regra do §7 da docs/FASE7.md, e aqui ela vale com força total: o texto
    /// da legenda vem de <b>outro modelo</b>. Se chegasse ao parcial, a
    /// <c>Retomada</c> o leria como ASR feito e devolveria o texto de um motor
    /// rotulado como de outro, em silêncio — o defeito da 0.4.0 de volta.
    /// </remarks>
    [Fact]
    public void OArquivoDaLegendaNaoEODaTranscricao()
    {
        Assert.Equal("legenda.json", LegendaAoVivo.Arquivo);
        Assert.NotEqual("transcricao.json", LegendaAoVivo.Arquivo);
    }

    [Fact]
    public void SemArquivoALeituraDevolveNulo()
    {
        string pasta = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(pasta);
        try
        {
            Assert.Null(LegendaAoVivo.Ler(pasta));
        }
        finally { Directory.Delete(pasta, true); }
    }

    /// <summary>Um arquivo pela metade não pode derrubar a tela.</summary>
    /// <remarks>
    /// É o caso literal do <c>SUP-2</c>: a máquina pode cair no meio da escrita,
    /// e o que sobra é meio JSON. A tela de preparo abre esse arquivo toda vez
    /// que alguém vai transcrever.
    /// </remarks>
    [Fact]
    public void ArquivoCorrompidoLeComoAusente()
    {
        string pasta = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(pasta);
        try
        {
            File.WriteAllText(Path.Combine(pasta, LegendaAoVivo.Arquivo),
                              "{\"turnos\": [{\"dono\": true, \"tex");
            Assert.Null(LegendaAoVivo.Ler(pasta));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void OQueFoiGravadoVoltaComOsDonosNaOrdem()
    {
        string pasta = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(pasta);
        try
        {
            File.WriteAllText(Path.Combine(pasta, LegendaAoVivo.Arquivo),
                "{\"turnos\":[{\"dono\":true,\"texto\":\"bom dia\"},"
                + "{\"dono\":false,\"texto\":\"bom dia, tudo bem?\"}]}");

            var lida = LegendaAoVivo.Ler(pasta);

            Assert.NotNull(lida);
            Assert.Equal(2, lida!.Turnos.Count);
            Assert.True(lida.Turnos[0].Dono);
            Assert.False(lida.Turnos[1].Dono);
            Assert.Equal("bom dia", lida.Turnos[0].Texto);
        }
        finally { Directory.Delete(pasta, true); }
    }
}
