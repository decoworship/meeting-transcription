namespace MeetingApp.Nucleo;

/// <summary>
/// Uma transcrição enquanto ela acontece: o que a tela precisa para se redesenhar.
/// </summary>
/// <remarks>
/// <para>
/// <b>Existe porque o trabalho não pode morar na tela.</b> Até a Fase 3 o
/// progresso vivia dentro de uma closure da <c>app.js</c>, sobre nós de DOM que
/// o primeiro clique no trilho jogava fora. O pipeline continuava rodando — o
/// <c>Transcritor</c> escreve o <c>transcricao.json</c> no fim de qualquer jeito
/// —, mas quem saía da tela perdia a barra, o texto da etapa e o caminho de
/// volta. O que se perdia era a vista, e é ela que este registro devolve.
/// </para>
/// <para>
/// Mesma escolha do gravador na Fase 2.5: o estado é do núcleo, e a página
/// desenha o que recebe (FASE3.md §2).
/// </para>
/// </remarks>
public sealed class TrabalhoDeTranscricao
{
    /// <summary>A pasta da gravação. É a identidade do trabalho.</summary>
    public required string Gravacao { get; init; }

    /// <summary>Como chamar a reunião numa frase — título da agenda, ou a pasta.</summary>
    public required string Nome { get; init; }

    public string Etapa { get; internal set; } = "mix";

    /// <summary>0 a 1, ou negativo quando a etapa não sabe se medir.</summary>
    public double Fracao { get; internal set; }

    public string Texto { get; internal set; } = "";

    public DateTimeOffset ComecouEm { get; init; } = DateTimeOffset.Now;

    /// <summary>Preenchido só quando terminou mal.</summary>
    public string? Erro { get; internal set; }

    /// <summary>
    /// Foi interrompida a pedido, e não por falha.
    /// </summary>
    /// <remarks>
    /// Separado do erro porque a tela trata os dois de maneiras opostas: falha
    /// pede um alerta vermelho e um "tentar de novo"; parar a pedido é o
    /// comando funcionando, e mostrá-lo como erro faria o app parecer quebrado
    /// justamente quando obedeceu.
    /// </remarks>
    public bool Cancelada { get; internal set; }

    public bool Terminou { get; internal set; }

    /// <summary>
    /// Como se pede ao pipeline que pare.
    /// </summary>
    /// <remarks>
    /// O cancelamento atravessa até os motores: o <c>MotorSidecar</c> registra
    /// <c>ct.Register(() =&gt; Matar(_processo))</c> e mata a árvore de
    /// processos, que é o que **libera a VRAM na hora** — pedir educadamente a
    /// um Python com um modelo carregado devolveria a placa quando ele
    /// resolvesse cooperar.
    /// </remarks>
    internal CancellationTokenSource Fonte { get; } = new();

    public CancellationToken Token => Fonte.Token;
}

/// <summary>
/// O que está sendo transcrito agora, e o que acabou de terminar.
/// </summary>
/// <remarks>
/// <para>
/// <b>Uma de cada vez</b>, e a segunda é recusada nomeando a reunião que está
/// ocupando o motor. Não é limitação de implementação: duas transcrições
/// disputando a mesma GPU não terminam mais rápido, e o modelo grande já aperta
/// os 6 GB da placa sozinho. A mesma trava é o que garante, na Fase 3, que o
/// motor de ata só carregue com a VRAM do ASR já liberada.
/// </para>
/// <para>
/// O último trabalho fica guardado depois de terminar. É o que permite a alguém
/// que saiu da tela no meio descobrir, ao voltar, que a tentativa falhou —
/// senão o erro só existiria enquanto houvesse alguém olhando.
/// </para>
/// </remarks>
public sealed class RegistroDeTranscricoes
{
    private readonly object _trava = new();
    private TrabalhoDeTranscricao? _atual;
    private TrabalhoDeTranscricao? _ultimo;

    /// <remarks>
    /// A trava existe porque o pipeline roda numa thread de trabalho e a página
    /// é atendida na thread da UI: sem ela, "está ocupado?" e "ocupe" seriam
    /// duas decisões separadas, e duas transcrições pedidas junto passariam as
    /// duas.
    /// </remarks>
    public TrabalhoDeTranscricao? Atual { get { lock (_trava) return _atual; } }

    public TrabalhoDeTranscricao? Ultimo { get { lock (_trava) return _ultimo; } }

    public bool Ocupado { get { lock (_trava) return _atual is not null; } }

    /// <summary>Registra o começo, ou explica por que não dá.</summary>
    /// <exception cref="InvalidOperationException">
    /// Já há uma transcrição em curso. A mensagem nomeia qual, porque "ocupado"
    /// sem dizer com o quê manda o usuário procurar sozinho.
    /// </exception>
    public TrabalhoDeTranscricao Comecar(string gravacao, string nome)
    {
        lock (_trava)
        {
            if (_atual is { } emCurso)
                throw new InvalidOperationException(
                    $"já estou transcrevendo \"{emCurso.Nome}\". "
                    + "Uma de cada vez: as duas disputariam a mesma placa de vídeo.");

            // O resultado anterior sai de cena aqui: uma tentativa nova apaga o
            // erro da anterior, senão a tela mostraria os dois ao mesmo tempo.
            _ultimo = null;
            _atual = new TrabalhoDeTranscricao { Gravacao = gravacao, Nome = nome };
            return _atual;
        }
    }

    /// <summary>Andamento vindo do pipeline. Ignorado se não for do trabalho atual.</summary>
    public void Progredir(string gravacao, string etapa, double fracao, string texto)
    {
        lock (_trava)
        {
            if (_atual is not { } t || t.Gravacao != gravacao) return;
            t.Etapa = etapa;
            t.Fracao = fracao;
            t.Texto = texto;
        }
    }

    /// <summary>Fim de linha, bem ou mal. O trabalho sai de "atual" e vira "último".</summary>
    public TrabalhoDeTranscricao? Terminar(string gravacao, string? erro = null,
                                           bool cancelada = false)
    {
        lock (_trava)
        {
            if (_atual is not { } t || t.Gravacao != gravacao) return null;
            t.Terminou = true;
            t.Erro = erro;
            t.Cancelada = cancelada;
            if (erro is null && !cancelada) t.Fracao = 1;
            _ultimo = t;
            _atual = null;
            t.Fonte.Dispose();
            return t;
        }
    }

    /// <summary>
    /// Pede que a transcrição em curso pare, liberando a placa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Existe porque a GPU é uma só e a transcrição não é sempre a coisa mais
    /// importante em execução na máquina — pedido do dono do produto em
    /// 13/08/2026, depois de usar a Fase 3 pela primeira vez.
    /// </para>
    /// <para>
    /// Só <b>pede</b>: quem tira o trabalho do registro é o
    /// <see cref="Terminar"/>, chamado por quem estava rodando o pipeline
    /// quando ele de fato para. Marcar como terminado aqui abriria uma janela em
    /// que o registro diz "livre" com dois modelos ainda na VRAM, e a próxima
    /// transcrição começaria em cima da que está morrendo.
    /// </para>
    /// </remarks>
    /// <returns>Falso quando não havia o que parar.</returns>
    public bool Cancelar(string? gravacao = null)
    {
        lock (_trava)
        {
            if (_atual is not { } t) return false;
            if (gravacao is { Length: > 0 } && t.Gravacao != gravacao) return false;
            t.Texto = "parando…";
            t.Fonte.Cancel();
            return true;
        }
    }

    /// <summary>
    /// Esquece o último resultado, quando a tela já o mostrou.
    /// </summary>
    /// <remarks>
    /// Sem isto, um erro de ontem reapareceria na tela de preparo de hoje como
    /// se fosse novo.
    /// </remarks>
    public void EsquecerUltimo()
    {
        lock (_trava) _ultimo = null;
    }
}
