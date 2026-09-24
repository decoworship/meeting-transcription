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
        Assert.NotEqual(m.ScriptAsr, m.ScriptLegenda);
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

    /// <summary>
    /// Com a legenda ligada e o bloco desligado, **nada impede a legenda**.
    /// </summary>
    /// <remarks>
    /// <b>Este é o caso que ficou uma semana quebrado, e a suíte não pegava.</b>
    /// Os testes cobriam as peças — a chave, o impedimento, o caminho do motor —
    /// e nenhum descrevia a combinação que o usuário de fato usa: legenda ligada,
    /// blocos desligados.
    /// <para>
    /// O defeito não estava aqui, estava na ordem das guardas do
    /// <c>Ponte.ComecarAPrevia</c>: o <c>if (!cfg.TranscricaoAoVivo) return;</c>,
    /// que é do caminho dos blocos, vinha antes da legenda e fazia o método sair
    /// na segunda linha. Este teste crava a **configuração** que tem de
    /// funcionar; a ordem das guardas é o que a faz valer.
    /// </para>
    /// </remarks>
    [Fact]
    public void LegendaLigadaEBlocoDesligadoNaoTemImpedimentoDeConfiguracao()
    {
        var cfg = new ConfiguracoesDoApp { LegendaAoVivo = true, TranscricaoAoVivo = false };

        string? impede = LegendaAoVivo.OQueImpede(SemMotores(), cfg);

        // O que sobra é a queixa da instalação — estes caminhos não existem.
        // O que NÃO pode aparecer é queixa de chave ou de placa: nesta
        // combinação, a configuração está certa.
        Assert.DoesNotContain("desligada", impede ?? "");
        Assert.DoesNotContain("não cabem juntas", impede ?? "");
    }

    /// <summary>
    /// A prévia em blocos desligada **não** é motivo para a legenda não rodar.
    /// </summary>
    [Fact]
    public void OImpedimentoDaLegendaNaoFalaDaPreviaEmBlocos()
    {
        var ligada = new ConfiguracoesDoApp { LegendaAoVivo = true, TranscricaoAoVivo = false };
        var comAsDuas = new ConfiguracoesDoApp { LegendaAoVivo = true, TranscricaoAoVivo = true };

        // Só a combinação impossível reclama da placa.
        Assert.Contains("não cabem juntas",
                        LegendaAoVivo.OQueImpede(SemMotores(), comAsDuas)!);
        Assert.DoesNotContain("não cabem juntas",
                              LegendaAoVivo.OQueImpede(SemMotores(), ligada) ?? "");
    }

    /// <summary>
    /// Arquivo que ainda não existe é "espere", nunca "acabou".
    /// </summary>
    /// <remarks>
    /// <b>O defeito que este teste crava custou quatro RCs.</b> A legenda começa
    /// no mesmo instante que a gravação, e o <c>CrashSafeWavWriter</c> ainda não
    /// criou o WAV: o laço lia a ausência como fim e encerrava com <b>0 quadros
    /// no mesmo segundo</b> em que o modelo terminava de carregar na GPU. A
    /// prévia em blocos nunca sofreu disso porque espera três minutos antes de
    /// ler pela primeira vez.
    /// </remarks>
    [Fact]
    public void SemArquivoAindaSeEspera()
    {
        // O caso do defeito: gravação recém-começada, WAV ainda não criado.
        Assert.True(LegendaAoVivo.PrecisaEsperar(existe: false, segundosEmDisco: 0, ate: 0.2));

        // Existe, mas ainda não tem áudio suficiente para o quadro pedido.
        Assert.True(LegendaAoVivo.PrecisaEsperar(true, segundosEmDisco: 0.3, ate: 0.2));

        // Tem o quadro e a folga: pode ler.
        Assert.False(LegendaAoVivo.PrecisaEsperar(
            true, segundosEmDisco: 0.2 + LegendaAoVivo.FolgaS + 0.01, ate: 0.2));
    }

    /// <summary>A folga entra na conta, e não é enfeite.</summary>
    /// <remarks>
    /// Ler antes de o gravador ter escrito o quadro inteiro devolveria áudio
    /// curto, e o motor transcreveria menos do que foi dito.
    /// </remarks>
    [Fact]
    public void AFolgaEExigidaAlemDoQuadro()
    {
        // Exatamente o quadro, sem a folga: ainda espera.
        Assert.True(LegendaAoVivo.PrecisaEsperar(true, segundosEmDisco: 0.2, ate: 0.2));
    }

    /// <summary>
    /// O WAV cujo header ainda diz zero é lido pelos bytes que existem.
    /// </summary>
    /// <remarks>
    /// <b>Este é o defeito que deixou a legenda cinco RCs sem funcionar.</b> O
    /// <c>CrashSafeWavWriter</c> escreve as amostras continuamente e só reescreve
    /// o header a cada 10 s. O <c>LerJanela</c> respeita o tamanho declarado — o
    /// que é correto para recuperar gravação interrompida — e devolvia
    /// <b>vazio nos primeiros 10 segundos</b>; o laço lia isso como fim e
    /// encerrava com 0 quadros.
    /// <para>
    /// Mesmo depois dos 10 s o problema continuaria: a leitura viria em degraus
    /// de 10 s, e uma legenda de 0,11 s de atraso viraria uma de 10 s.
    /// </para>
    /// <para>
    /// O teste monta exatamente esse arquivo: header declarando <b>zero</b>
    /// amostras, com dois segundos de áudio depois dele.
    /// </para>
    /// </remarks>
    [Fact]
    public void WavComHeaderAtrasadoELidoPelosBytesQueExistem()
    {
        string caminho = Path.Combine(Path.GetTempPath(),
                                      Path.GetRandomFileName() + ".wav");
        try
        {
            EscreverWavComHeaderMentindo(caminho, segundos: 2);

            // A leitura de sempre respeita o header: não vê nada.
            Assert.Empty(Faixas.LerJanela(caminho, 0, 0.2));

            // A leitura viva vê os bytes — e é o que a legenda usa.
            var vivo = Faixas.LerJanelaViva(caminho, 0, 0.2);
            Assert.Equal((int)(0.2 * Faixas.TaxaDeAmostragem), vivo.Length);
        }
        finally { File.Delete(caminho); }
    }

    /// <summary>Um WAV com header declarando 0 e amostras de verdade depois.</summary>
    private static void EscreverWavComHeaderMentindo(string caminho, int segundos)
    {
        int amostras = segundos * Faixas.TaxaDeAmostragem;
        using var f = new FileStream(caminho, FileMode.Create);
        using var w = new BinaryWriter(f);
        w.Write("RIFF".ToCharArray());
        w.Write(36);                       // tamanho mentindo, como na gravação
        w.Write("WAVE".ToCharArray());
        w.Write("fmt ".ToCharArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(Faixas.TaxaDeAmostragem);
        w.Write(Faixas.TaxaDeAmostragem * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data".ToCharArray());
        w.Write(0);                        // **zero**: o header ainda não sabe
        for (int i = 0; i < amostras; i++) w.Write((short)(i % 1000));
    }

    // ─────────────────────────────── o carimbo de tempo (VIVO-1)

    [Fact]
    public void OPedacoFirmeGuardaOIntervaloQueEleCobre()
    {
        // **É o que destrava a diarização sobre a legenda.** A atribuição de
        // falante é por sobreposição temporal, e até 17/09/2026 o legenda.json
        // não tinha tempo nenhum.
        var trechos = new List<TrechoDaLegenda>();
        var turnos = new List<TurnoDaLegenda>();

        LegendaAoVivo.Registrar(trechos, turnos, anteriorMs: 0, ateMs: 2170, dono: false,
                                novo: " Roupa curta aí");
        LegendaAoVivo.Registrar(trechos, turnos, anteriorMs: 2170, ateMs: 4410, dono: false,
                                novo: " tá frio");

        Assert.Equal(2, trechos.Count);
        Assert.Equal(0, trechos[0].InicioMs);
        Assert.Equal(2170, trechos[0].FimMs);
        Assert.Equal(2170, trechos[1].InicioMs);
        Assert.Equal(4410, trechos[1].FimMs);
        Assert.Equal("Roupa curta aí", trechos[0].Texto);
    }

    [Fact]
    public void OTrechoGuardaODonoDeleMesmoQuandoOTurnoNaoQuebra()
    {
        // **O turno não serve de carimbo, e o acervo mostrou por quê:** em três
        // das nove gravações a reunião inteira é um turno só, porque o dono
        // nunca mudou (microfone mudo). O trecho é a granularidade que sobrevive
        // a isso — ~1,1 s de mediana, medido em 17/09.
        var trechos = new List<TrechoDaLegenda>();
        var turnos = new List<TurnoDaLegenda>();

        for (int i = 0; i < 4; i++)
            LegendaAoVivo.Registrar(trechos, turnos, i * 1000, (i + 1) * 1000, false, $" f{i}");

        Assert.Single(turnos);              // um turno só, como no acervo
        Assert.Equal(4, trechos.Count);     // e quatro pontos no tempo
        Assert.All(trechos, t => Assert.False(t.Dono));
    }

    [Fact]
    public void UmRelogioQueAndaParaTrasNaoViraIntervaloNegativo()
    {
        // O motor recomeça o fluxo quando ele trava, e o acumulado atravessa os
        // recomeços — mas confiar nisso sem guarda deixaria um intervalo
        // invertido chegar à diarização, que o leria como sobreposição zero.
        var trechos = new List<TrechoDaLegenda>();
        var turnos = new List<TurnoDaLegenda>();

        LegendaAoVivo.Registrar(trechos, turnos, anteriorMs: 5000, ateMs: 3000, false, " oi");

        Assert.Single(trechos);
        Assert.True(trechos[0].FimMs >= trechos[0].InicioMs,
                    $"intervalo invertido: {trechos[0].InicioMs}–{trechos[0].FimMs}");
    }

    [Fact]
    public void SemTextoNovoNaoNasceTrecho()
    {
        var trechos = new List<TrechoDaLegenda>();
        var turnos = new List<TurnoDaLegenda>();

        LegendaAoVivo.Registrar(trechos, turnos, 0, 1000, false, "");
        LegendaAoVivo.Registrar(trechos, turnos, 0, 1000, false, "   ");

        Assert.Empty(trechos);
        Assert.Empty(turnos);
    }

    [Fact]
    public void OArquivoAntigoSemTrechosContinuaLegivel()
    {
        // Sete legenda.json já existem no acervo sem o campo. Quebrá-los para
        // ganhar o carimbo seria trocar dado por dado.
        string pasta = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(pasta);
        try
        {
            File.WriteAllText(Path.Combine(pasta, LegendaAoVivo.Arquivo),
                """{"turnos":[{"dono":true,"texto":"bom dia"}]}""");

            var lida = LegendaAoVivo.Ler(pasta);

            Assert.NotNull(lida);
            Assert.Single(lida!.Turnos);
            Assert.Empty(lida.Trechos);
        }
        finally { Directory.Delete(pasta, recursive: true); }
    }

    // ──────────────────── a palavra partida entre dois commits

    [Fact]
    public void OTrechoQueContinuaAPalavraAnteriorEMarcado()
    {
        // **Visto em uso em 18/09/2026:** o motor firmou 'a Palo' e depois
        // 'ma tem Uberlândia', e a diarização deu falantes DIFERENTES às duas
        // metades de "Paloma". A fronteira do commit não é fronteira de palavra.
        var trechos = new List<TrechoDaLegenda>();
        var turnos = new List<TurnoDaLegenda>();

        LegendaAoVivo.Registrar(trechos, turnos, 0, 1000, false, " a Palo");
        LegendaAoVivo.Registrar(trechos, turnos, 1000, 2000, false, "ma tem Uberlândia",
                                anteriorBruto: " a Palo");

        Assert.False(trechos[0].Colado);
        Assert.True(trechos[1].Colado);
    }

    [Fact]
    public void EspacoNaFronteiraSignificaPalavraNova()
    {
        // O espaço vive no COMEÇO do pedaço novo, e é ele que diz que a palavra
        // anterior acabou. Sem esta guarda, 'vou' + ' acho' seria fundido.
        var trechos = new List<TrechoDaLegenda>();
        var turnos = new List<TurnoDaLegenda>();

        LegendaAoVivo.Registrar(trechos, turnos, 0, 1000, false, "Eu não vou");
        LegendaAoVivo.Registrar(trechos, turnos, 1000, 2000, false, " acho que vai",
                                anteriorBruto: "Eu não vou");

        Assert.False(trechos[1].Colado);
    }

    [Fact]
    public void PontuacaoNaFronteiraNaoEPalavraPartida()
    {
        // 'Eu não vou' + ', acho que vai' não tem espaço, mas também não parte
        // palavra nenhuma. Fundir por isso agruparia meia reunião.
        var trechos = new List<TrechoDaLegenda>();
        var turnos = new List<TurnoDaLegenda>();

        LegendaAoVivo.Registrar(trechos, turnos, 0, 1000, false, "Eu não vou");
        LegendaAoVivo.Registrar(trechos, turnos, 1000, 2000, false, ", acho que vai",
                                anteriorBruto: "Eu não vou");

        Assert.False(trechos[1].Colado);
    }

    [Fact]
    public void UmEspacoNOFIMDoAnteriorTambemFechaAPalavra()
    {
        // O commit pode terminar com espaço, e aí o Trim o apagaria do texto
        // guardado — é por isso que a regra olha o BRUTO, e não o que ficou.
        var trechos = new List<TrechoDaLegenda>();
        var turnos = new List<TurnoDaLegenda>();

        LegendaAoVivo.Registrar(trechos, turnos, 0, 1000, false, "a Palo ");
        LegendaAoVivo.Registrar(trechos, turnos, 1000, 2000, false, "ma tem",
                                anteriorBruto: "a Palo ");

        Assert.False(trechos[1].Colado);
    }

    // ──────────────────── as trocas da correção de termos

    [Fact]
    public void AsTrocasDoTrechoSobrevivemAoArquivo()
    {
        string pasta = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var trecho = new TrechoDaLegenda
            {
                InicioMs = 0, FimMs = 1120, Dono = false, Texto = "Wifi",
                Swaps = [new TrocaFeita { De = "Wi Fi", Para = "Wifi" }],
            };

            LegendaAoVivo.Gravar(pasta, [], [trecho], prontos: true);
            var lida = LegendaAoVivo.Ler(pasta)!;

            var troca = Assert.Single(lida.Trechos[0].Swaps!);
            Assert.Equal(("Wi Fi", "Wifi"), (troca.De, troca.Para));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void TrechoSemTrocaNaoEscreveOCampo()
    {
        // Os sete legenda.json antigos do acervo não têm o campo, e o arquivo
        // novo sem troca tem de continuar igual a eles.
        string pasta = Directory.CreateTempSubdirectory().FullName;
        try
        {
            LegendaAoVivo.Gravar(pasta, [],
                [new TrechoDaLegenda { InicioMs = 0, FimMs = 1, Dono = false, Texto = "oi" }],
                prontos: false);

            Assert.DoesNotContain("swaps", File.ReadAllText(Path.Combine(pasta, "legenda.json")));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void TrocarOFalantePreservaAColagemEAsTrocas()
    {
        // Até 23/09/2026 o reconhecimento de vozes recriava o trecho campo a
        // campo e esquecia o Colado: "Palo" + "ma" voltava a sair "Palo ma".
        var t = new TrechoDaLegenda
        {
            InicioMs = 10, FimMs = 20, Dono = false, Texto = "ma", Colado = true,
            Falante = "SPEAKER_00", Swaps = [new TrocaFeita { De = "a", Para = "b" }],
        };

        var n = t.ComFalante("Paloma Santos");

        Assert.Equal("Paloma Santos", n.Falante);
        Assert.True(n.Colado);
        Assert.Same(t.Swaps, n.Swaps);
        Assert.Equal((10L, 20L, "ma"), (n.InicioMs, n.FimMs, n.Texto));
    }
}
