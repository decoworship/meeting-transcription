using MeetingRecorder.Agenda;
using MeetingApp.App.Bandeja;
using MeetingApp.App.Nativo;
using MeetingApp.Nucleo;

namespace MeetingApp.App;

/// <summary>
/// O app inteiro: a bandeja que grava e a janela que transcreve, num processo.
/// </summary>
/// <remarks>
/// <para>
/// <b>O ciclo de vida é o desta classe, não o da janela.</b> Até a Fase 2 o
/// <c>MeetingApp</c> era um app de janela — fechar a janela encerrava o
/// processo. Aqui ele é um app de bandeja que <em>também</em> tem janela:
/// fechar esconde, e sair de verdade só acontece pelo menu da bandeja. Errar
/// isso perde gravação (FASE2.5.md §2).
/// </para>
/// <para>
/// <b>Dois HWND, um laço.</b> A janela invisível da bandeja e a janela do
/// WebView2 são duas janelas Win32 na mesma thread, cada uma com seu
/// <c>WndProc</c>; o <c>GetMessage</c> da primeira despacha as duas. Não há
/// segunda thread de UI, e é por isso que o estado do gravador pode ser lido
/// dos dois lados sem trava.
/// </para>
/// </remarks>
internal sealed class Aplicacao : IDisposable
{
    private readonly JanelaDeMensagens _ancora;
    private readonly Gravador _gravador;
    private readonly Bandeja.Bandeja _bandeja;
    private readonly string _pastaDasGravacoes;
    private readonly string? _telaInicial;

    private JanelaDoApp? _janela;
    private bool _saindo;

    public Aplicacao(string? pastaDoArgumento, string? telaInicial)
    {
        // Antes de qualquer janela: depois da primeira, o Windows ignora.
        Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        _telaInicial = telaInicial;

        _ancora = new JanelaDeMensagens
        {
            AoTick = Tick,
            AoTickRapido = EmpurrarNiveis,
            AoPedirJanela = MostrarJanela,
        };

        _gravador = new Gravador(_ancora.Executar) { PastaForcada = pastaDoArgumento };
        _gravador.Estado.NotificacoesLigadas = _gravador.Cfg.Notifications;
        _gravador.AoMudar += EmpurrarEstado;

        _pastaDasGravacoes = PastaDasGravacoes.Resolver(_gravador.Cfg, pastaDoArgumento);

        _bandeja = new Bandeja.Bandeja(_gravador, _ancora, MostrarJanela, Sair);

        // O print da tela: o atalho global e o botão do Gravador dão no mesmo
        // lugar. O atalho só existe enquanto se grava.
        _ancora.AoAtalhoDeCaptura = () => CapturarTela(peloAtalho: true);
        _gravador.AoPedirCaptura += () => CapturarTela(peloAtalho: false);
        _gravador.AoComecar += _ => LigarAtalhoDeCaptura();
        _gravador.AoTerminar += DesligarAtalhoDeCaptura;

        // Desde a subida, e não só ao gravar: o pareamento acontece dentro de
        // uma reunião, e a gravação pode começar depois dela (GRA-3).
        _gravador.AplicarSincronia(Diagnostico.VersaoDoApp());

        ReleraAgendaEmSegundoPlano();
    }

    /// <summary>
    /// Os e-mails que as gravações antigas não guardaram (GRA-1).
    /// </summary>
    /// <remarks>
    /// Meio minuto depois de abrir, para não disputar a subida com nada, e fora
    /// de qualquer caminho que a pessoa espere: se falhar, fica para a próxima
    /// abertura. Ver <see cref="ReleituraDaAgenda"/>.
    /// </remarks>
    private void ReleraAgendaEmSegundoPlano()
    {
        if (!ClienteDaAgenda.EstaAutorizado()) return;
        string pasta = _pastaDasGravacoes;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30));
                var perguntados = ReleituraDaAgenda.LerPerguntados();
                using var agenda = new ClienteDaAgenda();
                var r = await ReleituraDaAgenda.RelerAsync(pasta, id => agenda.EventoPorIdAsync(id), perguntados);
                ReleituraDaAgenda.GuardarPerguntados(perguntados);
                if (r.Completadas + r.Divergentes + r.Sumidas + r.Adiadas > 0)
                    Nucleo.Registro.Escrever("agenda",
                        $"releitura: {r.Completadas} com e-mails, {r.Divergentes} com convidados "
                        + $"diferentes, {r.Sumidas} apagadas, {r.Adiadas} para depois");
            }
            catch (Exception e)
            {
                Nucleo.Registro.Escrever("agenda", $"releitura falhou: {e.Message}");
            }
        });
    }

    /// <param name="comJanela">
    /// Falso quando o app subiu com <c>--bandeja</c> — o modo de iniciar com o
    /// Windows. Ali abrir uma janela seria abrir uma janela na cara de quem
    /// acabou de ligar o computador.
    /// </param>
    public void Rodar(bool comJanela)
    {
        if (comJanela) MostrarJanela();
        _ancora.Rodar();
    }

    // ───────────────────────────────────────────────────────── janela

    /// <remarks>
    /// A janela é criada na primeira vez que alguém a pede, e daí em diante é
    /// escondida e mostrada. Criar de saída custaria o WebView2 inteiro na
    /// memória de quem só quer gravar; recriar a cada abertura custaria os
    /// segundos de subida do WebView2 toda vez, e perderia a tela onde se estava.
    /// </remarks>
    private void MostrarJanela()
    {
        if (_saindo) return;

        if (_janela is null)
        {
            // O aviso da transcrição pronta atravessa threads: quem o dispara é
            // a tarefa do pipeline, e Shell_NotifyIcon só pode ser chamado da
            // thread que criou o ícone. O Executar da âncora é o mesmo salto que
            // o gravador já usa para tudo que vem de fora.
            _janela = new JanelaDoApp(Nucleo.Marca.Nome, _gravador, _pastaDasGravacoes,
                texto => _ancora.Executar(() => _bandeja.AvisarDoApp(texto)), _telaInicial)
            {
                AoEsconder = () => _ancora.MedidoresLigados(false),
            };
        }
        else
        {
            _janela.Mostrar();
        }

        _ancora.MedidoresLigados(_gravador.Estado.Gravando);
    }

    // ───────────────────────────────────────────────── relógio e eventos

    /// <summary>Uma vez por segundo, esteja a janela aberta ou não.</summary>
    private void Tick()
    {
        _gravador.Atualizar();

        // Os medidores só correm com a janela à vista e algo para medir. Ligar e
        // desligar aqui, e não em cada ação, cobre também o caso de a gravação
        // ter começado pela bandeja com a janela já aberta.
        _ancora.MedidoresLigados(_gravador.Estado.Gravando && _janela is { Visivel: true });
    }

    /// <summary>O estado do gravador indo para a página, sem ela pedir.</summary>
    /// <remarks>
    /// É a única mudança estrutural no contrato da ponte desde que ele existe:
    /// toda resposta era reação a um pedido, e o nível de áudio precisa fluir
    /// sem ninguém perguntar (FASE2.5.md §5).
    /// </remarks>
    private void EmpurrarEstado()
    {
        _bandeja.Redesenhar();
        if (_janela is { Pronta: true, Visivel: true } j)
            j.Enviar(Ponte.EventoDoGravador(_gravador));
    }

    private void EmpurrarNiveis()
    {
        if (_janela is { Pronta: true, Visivel: true } j)
            j.Enviar(Ponte.EventoDoGravador(_gravador));
    }

    // ───────────────────────────────────────────────── print da tela

    private void LigarAtalhoDeCaptura()
    {
        bool ligou = _ancora.LigarAtalhoDeCaptura();
        _gravador.AtalhoDeCaptura = ligou ? JanelaDeMensagens.NomeDoAtalho : null;
        if (!ligou)
            Nucleo.Registro.Escrever("captura",
                $"o atalho {JanelaDeMensagens.NomeDoAtalho} é de outro programa; só o botão captura");
    }

    private void DesligarAtalhoDeCaptura()
    {
        _ancora.DesligarAtalhoDeCaptura();
        _gravador.AtalhoDeCaptura = null;
    }

    /// <summary>
    /// Tira o print, guarda em <c>capturas/</c> e põe a linha nas notas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>O print na thread da UI, o PNG fora dela.</b> O <c>BitBlt</c> leva
    /// milissegundos e tem de ser agora — é o momento que a pessoa quis
    /// guardar —, e esconder a janela do print só vale na thread dela. Já
    /// codificar um 4K leva centenas de milissegundos, e o laço que ficasse
    /// esperando é o mesmo que despacha a bandeja e os medidores.
    /// </para>
    /// <para>
    /// <b>A linha vai para o disco antes de ir para a tela.</b> A janela pode
    /// estar fechada, ou em outra tela; o <c>notas.md</c> é o que sobrevive.
    /// Quem tem o campo aberto recebe o evento e põe a mesma linha nele.
    /// </para>
    /// </remarks>
    private void CapturarTela(bool peloAtalho)
    {
        if (!_gravador.Estado.Gravando || _gravador.PastaAtual is not { Length: > 0 } pasta) return;
        double segundos = _gravador.DuracaoAtual;

        CapturaDeTela.Quadro quadro;
        try
        {
            quadro = CapturaDeTela.DoMonitorEmFoco(_janela?.Hwnd ?? IntPtr.Zero);
        }
        catch (Exception e)
        {
            FalhouACaptura(e.Message, peloAtalho);
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                byte[] png = Png.Codificar(quadro.Largura, quadro.Altura, quadro.Bgra, quadro.Largura * 4);
                string caminho = Capturas.Guardar(pasta, png, Capturas.NomeNoMomento("tela", segundos));
                string linha = Capturas.LinhaDaNota(caminho, segundos);
                Capturas.AnexarANotas(pasta, linha);

                var captura = new CapturaJson { Gravacao = pasta, Caminho = caminho, Linha = linha };
                _ancora.Executar(() =>
                {
                    EnviarAPagina(Ponte.EventoDaCaptura(captura, null));
                    // Só pelo atalho: quem clicou no botão está olhando o
                    // campo das notas, e vê a linha chegar.
                    if (peloAtalho)
                        _bandeja.AvisarDoApp($"Tela capturada em {Capturas.Relogio(segundos)}. Está nas notas.");
                });
            }
            catch (Exception e)
            {
                _ancora.Executar(() => FalhouACaptura(e.Message, peloAtalho));
            }
        });
    }

    private void FalhouACaptura(string motivo, bool peloAtalho)
    {
        Nucleo.Registro.Escrever("captura", $"o print falhou: {motivo}");
        EnviarAPagina(Ponte.EventoDaCaptura(null, $"O print não foi salvo: {motivo}"));
        if (peloAtalho) _bandeja.AvisarDoApp($"O print não foi salvo: {motivo}");
    }

    /// <summary>
    /// Para a página mesmo escondida: o campo das notas continua vivo atrás da
    /// janela fechada, e é ele que precisa da linha.
    /// </summary>
    private void EnviarAPagina(string json)
    {
        if (_janela is { Pronta: true } j) j.Enviar(json);
    }

    // ─────────────────────────────────────────────────────────── saída

    /// <remarks>
    /// Confirma quando há gravação em andamento. É a única confirmação do app
    /// inteiro, e existe porque este é o único clique que perde uma reunião: o
    /// "Sair" fica logo abaixo de "Notificações" no menu, e o áudio não se refaz.
    /// </remarks>
    private void Sair()
    {
        if (_gravador.Estado.Gravando)
        {
            const uint MB_YESNO = 0x00000004;
            const int IDYES = 6;
            int r = Win32.MessageBox(_ancora.Hwnd,
                "Uma gravação está em andamento.\n\n"
                + "Sair agora encerra a gravação e salva o que já foi gravado. Sair?",
                "Reuniões", MB_YESNO | Win32.MB_ICONINFORMATION);
            if (r != IDYES) return;
        }

        _saindo = true;
        // Fecha o laço destruindo a âncora: é o WM_DESTROY dela que posta o
        // WM_QUIT. A limpeza de verdade acontece no Dispose, depois do laço.
        Win32.PostMessageW(_ancora.Hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        // Ordem: a gravação primeiro, porque é ela que tem um meta.json para
        // fechar e um WAV para finalizar. O resto são handles.
        _gravador.AoMudar -= EmpurrarEstado;
        _gravador.Dispose();
        _janela?.Dispose();
        _bandeja.Dispose();
        _ancora.Dispose();
    }
}
