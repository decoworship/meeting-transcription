namespace MeetingRecorder.Core;

/// <summary>O que a chamada — o Teams ou o Meet — disse sobre a reunião.</summary>
/// <param name="Mudo">
/// Nulo quando a chamada não disse: o Teams, antes de a pessoa permitir o
/// PulseMeet, só manda as permissões — e "não disse" não pode virar "não está
/// mudo".
/// </param>
public sealed record EstadoDaChamada(bool EmReuniao, bool? Mudo, bool PodeMutar, bool PodeParear);

/// <summary>O que o gravador tem de fazer depois de um evento.</summary>
public enum AcaoDaSincronia { Nenhuma, MutarNoApp, DesmutarNoApp, AlternarNaChamada }

/// <summary>
/// Um mute só, em dois lugares: o do gravador e o da chamada (GRA-3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Existe para não se mutar duas vezes.</b> Quem não vai falar muta na
/// reunião e no gravador — e esquecer o segundo grava o que se disse fora da
/// chamada, ou esquecer de desmutá-lo perde a própria fala.
/// </para>
/// <para>
/// <b>O comando só sai quando ele muda alguma coisa.</b> O Teams só sabe
/// alternar — não há "mutar", só "inverter" —, e por isso só se manda quando se
/// sabe o estado da chamada e ele é diferente do pedido.
/// </para>
/// <para>
/// <b>Só a mudança do mute da chamada conta.</b> O Teams manda o estado inteiro a
/// cada câmera ligada ou mão levantada; seguir cada um desfaria um mute que a
/// pessoa escolheu só no gravador. E sair da reunião não desmuta: fora dela a
/// chamada diz "não mudo" porque não há o que mutar.
/// </para>
/// <para>
/// Sem relógio e sem rede, para ser testável, e uma por chamada. Quem fala com
/// elas é a <see cref="ConexaoComOTeams"/> e o <see cref="ServidorDaExtensao"/>;
/// quem aplica é o <c>Gravador</c>.
/// </para>
/// </remarks>
public sealed class SincroniaDoMute
{
    /// <summary>
    /// Quanto tempo depois de mandar alternar um mute contrário é eco atrasado.
    /// </summary>
    /// <remarks>
    /// Uma atualização que saiu da chamada antes de o comando chegar lá diz o mute
    /// antigo; obedecê-la desfaria o clique que acabou de acontecer.
    /// </remarks>
    public static readonly TimeSpan Espera = TimeSpan.FromSeconds(2);

    /// <summary>O último estado da chamada, ou nulo se ela não está conectada.</summary>
    public EstadoDaChamada? Chamada { get; private set; }

    private (bool Desejado, DateTime Ate)? _esperando;
    private bool _pediuPareamento;

    public AcaoDaSincronia AoEstadoDaChamada(EstadoDaChamada e, bool gravando, bool mudoNoApp, DateTime agora)
    {
        var antes = Chamada;
        Chamada = e;

        if (!e.EmReuniao)
        {
            _esperando = null;
            _pediuPareamento = false;
            return AcaoDaSincronia.Nenhuma;
        }
        if (e.Mudo is not bool mudo) return AcaoDaSincronia.Nenhuma;

        if (_esperando is { } w)
        {
            if (agora < w.Ate)
            {
                if (mudo == w.Desejado) _esperando = null;
                return AcaoDaSincronia.Nenhuma;
            }
            _esperando = null;
        }

        // A primeira leitura dentro da reunião vale como mudança: é a hora em
        // que os dois passam a significar a mesma coisa.
        bool mudou = antes is not { EmReuniao: true, Mudo: bool } || antes.Mudo != mudo;
        if (!mudou || !gravando || mudo == mudoNoApp) return AcaoDaSincronia.Nenhuma;
        return mudo ? AcaoDaSincronia.MutarNoApp : AcaoDaSincronia.DesmutarNoApp;
    }

    /// <summary>A pessoa mutou ou desmutou no gravador — a bandeja ou a tela.</summary>
    public AcaoDaSincronia AoMutarNoApp(bool mudo, DateTime agora)
    {
        if (Chamada is not { EmReuniao: true, Mudo: bool daChamada, PodeMutar: true })
            return AcaoDaSincronia.Nenhuma;

        // Com um comando em voo, a chamada vai ser o que se pediu a ela, e não o
        // que ainda diz: dois cliques rápidos são dois comandos.
        bool vaiSer = _esperando?.Desejado ?? daChamada;
        if (vaiSer == mudo) return AcaoDaSincronia.Nenhuma;

        _esperando = (mudo, agora + Espera);
        return AcaoDaSincronia.AlternarNaChamada;
    }

    /// <summary>Começar a gravar no meio da reunião segue o mute que ela já tem.</summary>
    public AcaoDaSincronia AoComecarAGravar(bool mudoNoApp) =>
        Chamada is { EmReuniao: true, Mudo: bool daChamada } && daChamada != mudoNoApp
            ? (daChamada ? AcaoDaSincronia.MutarNoApp : AcaoDaSincronia.DesmutarNoApp)
            : AcaoDaSincronia.Nenhuma;

    /// <summary>Verdadeiro uma vez por reunião, quando dá para pedir o pareamento (Teams).</summary>
    public bool QuerParear()
    {
        if (Chamada is not { EmReuniao: true, PodeParear: true, Mudo: null } || _pediuPareamento)
            return false;
        _pediuPareamento = true;
        return true;
    }

    public void AoPerderAChamada()
    {
        Chamada = null;
        _esperando = null;
        _pediuPareamento = false;
    }
}
