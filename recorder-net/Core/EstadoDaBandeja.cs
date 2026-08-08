namespace MeetingRecorder.Core;

/// <summary>Cor do ícone — o único aviso que existe durante a gravação.</summary>
public enum CorDaBandeja
{
    /// <summary>Parado.</summary>
    Cinza,
    /// <summary>Gravando normalmente.</summary>
    Vermelho,
    /// <summary>Gravando com o microfone mudo pela bandeja (decisão sua).</summary>
    Laranja,
    /// <summary>Gravando, mas um canal está sem áudio — algo errado.</summary>
    Amarelo,
}

/// <summary>O que o clique no ícone faz, que depende do estado.</summary>
public enum AcaoDoClique { Iniciar, AlternarMudo }

/// <summary>
/// A lógica da bandeja, separada da UI para poder ser testada.
/// </summary>
/// <remarks>
/// <para>
/// <b>Parar só pelo menu.</b> Um clique acidental que encerra a gravação perde a
/// reunião inteira; um que muta você percebe na hora e desfaz. Por isso o clique
/// no ícone inicia (quando parado) ou alterna o mute (quando gravando), nunca
/// para. Decisão herdada do gravador Python e mantida deliberadamente.
/// </para>
/// <para>
/// <b>Amarelo é diferente de laranja.</b> Laranja é você tendo mutado; amarelo é
/// um canal sem áudio sem ninguém ter pedido — cabo solto, dispositivo errado,
/// mute no hardware. Confundir os dois foi o que deixou a gravação de 06/08 sair
/// 95% muda sem ninguém notar.
/// </para>
/// </remarks>
public sealed class EstadoDaBandeja
{
    /// <summary>Marcos do lembrete de mute esquecido, em minutos.</summary>
    public static readonly int[] LembretesMin = [2, 5, 15, 30];

    public bool Gravando { get; private set; }
    public bool Mudo { get; private set; }
    public bool CanalSemAudio { get; set; }

    /// <summary>
    /// Requisito A14: desligar as notificações. Ligado por padrão, para o
    /// comportamento bater com o gravador Python.
    /// </summary>
    /// <remarks>
    /// Desligar isto **não** desliga a detecção de canal morto: o ícone amarelo
    /// é outro mecanismo, e é ele que existe por causa da gravação de 06/08.
    /// São coisas separadas de propósito.
    /// </remarks>
    public bool NotificacoesLigadas { get; set; } = true;

    private DateTime? _mudoDesde;
    private int _ultimoLembrete;

    public CorDaBandeja Cor =>
        !Gravando ? CorDaBandeja.Cinza
        : Mudo ? CorDaBandeja.Laranja
        : CanalSemAudio ? CorDaBandeja.Amarelo
        : CorDaBandeja.Vermelho;

    public AcaoDoClique AcaoDoCliqueAtual =>
        Gravando ? AcaoDoClique.AlternarMudo : AcaoDoClique.Iniciar;

    public void Iniciou()
    {
        Gravando = true;
        Mudo = false;
        CanalSemAudio = false;
        _mudoDesde = null;
        _ultimoLembrete = 0;
    }

    public void Parou()
    {
        Gravando = false;
        Mudo = false;
        _mudoDesde = null;
    }

    public void DefinirMudo(bool mudo, DateTime agora)
    {
        if (!Gravando) return;
        Mudo = mudo;
        _mudoDesde = mudo ? agora : null;
        if (!mudo) _ultimoLembrete = 0;
    }

    /// <summary>
    /// Texto do lembrete de mute esquecido, ou <c>null</c> se não é hora.
    /// </summary>
    /// <remarks>
    /// Desde que o clique passou a mutar em vez de parar, mute esquecido virou o
    /// modo de falha mais provável — uma gravação de 36 min saiu 95% muda
    /// exatamente assim.
    /// </remarks>
    public string? LembreteDeMute(DateTime agora)
    {
        if (!NotificacoesLigadas || !Gravando || !Mudo || _mudoDesde is null) return null;

        int minutos = (int)(agora - _mudoDesde.Value).TotalMinutes;
        foreach (int marco in LembretesMin)
        {
            if (minutos >= marco && marco > _ultimoLembrete)
            {
                _ultimoLembrete = marco;
                return $"Microfone mudo há {marco} min.\nSua voz não está sendo gravada.";
            }
        }
        return null;
    }

    public string TextoDeStatus(double duracaoS, string? dispositivoMic) =>
        !Gravando
            ? "Parado"
            : $"Gravando {TimeSpan.FromSeconds(duracaoS):hh\\:mm\\:ss}" +
              (Mudo ? " (mudo)" : "") +
              (CanalSemAudio ? " — canal sem áudio" : "") +
              (dispositivoMic is null ? "" : $"\n{dispositivoMic}");
}
