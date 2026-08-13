using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MeetingRecorder.Capture;

/// <summary>Um endpoint de áudio, já com o nome lido.</summary>
public sealed record Dispositivo(string Id, string Nome, bool EhPadrao);

/// <summary>Fotografia dos endpoints ativos num instante.</summary>
public sealed record CatalogoInstantaneo(
    IReadOnlyList<Dispositivo> Entradas,
    IReadOnlyList<Dispositivo> Saidas)
{
    public static readonly CatalogoInstantaneo Vazio = new([], []);
}

/// <summary>
/// Cache dos dispositivos de áudio, para o menu não pagar o preço de lê-los.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> Ler <c>MMDevice.FriendlyName</c> custa ~170 ms por
/// dispositivo — medido nesta máquina: 925 ms para 5 saídas, 248 ms para 2
/// entradas, e não fica mais barato ao repetir, então o Windows não guarda isso
/// em cache. Montar os dois submenus a cada abertura custava mais de um segundo
/// na thread da UI, que é exatamente o travamento reclamado. Enumerar os
/// endpoints e ler os <c>ID</c> são operações de milissegundos; o custo está
/// todo em abrir o property store de cada dispositivo para pegar o nome.
/// </para>
/// <para>
/// <b>Como se mantém fresco.</b> Em vez de reler por tempo, o catálogo escuta o
/// <see cref="IMMNotificationClient"/> e só relê quando o Windows avisa que algo
/// mudou — headset conectado, dispositivo desabilitado, padrão trocado. É a
/// diferença entre a lista estar sempre correta e estar correta na média.
/// </para>
/// <para>
/// A releitura acontece fora da thread da UI. Enquanto a primeira não termina,
/// <see cref="Atual"/> devolve <see cref="CatalogoInstantaneo.Vazio"/> e o menu
/// mostra "(carregando...)" — preferível a segurar a abertura do menu.
/// </para>
/// </remarks>
public sealed class CatalogoDeDispositivos : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerador = new();
    private readonly object _trava = new();
    private volatile CatalogoInstantaneo _atual = CatalogoInstantaneo.Vazio;
    private bool _lendo;
    private bool _pendente;

    public CatalogoDeDispositivos()
    {
        _enumerador.RegisterEndpointNotificationCallback(this);
        Reler();
    }

    /// <summary>A última fotografia. Nunca bloqueia.</summary>
    public CatalogoInstantaneo Atual => _atual;

    /// <summary>Relê em segundo plano, no máximo uma releitura por vez.</summary>
    /// <remarks>
    /// <para>
    /// Conectar um headset rende vários eventos seguidos (adicionado, estado
    /// mudou, padrão mudou); sem coalescer, cada um dispararia uma releitura de
    /// mais de um segundo em paralelo com as outras.
    /// </para>
    /// <para>
    /// Coalescer não é o mesmo que descartar: um evento que chega no meio de uma
    /// releitura marca <c>_pendente</c> e provoca outra volta ao terminar. Se
    /// fosse simplesmente ignorado, a mudança que ele anunciava ficaria de fora
    /// da fotografia — e o menu ficaria errado até a próxima mudança acontecer.
    /// </para>
    /// </remarks>
    public void Reler()
    {
        lock (_trava)
        {
            if (_lendo) { _pendente = true; return; }
            _lendo = true;
        }
        Task.Run(Laco);
    }

    private void Laco()
    {
        while (true)
        {
            try
            {
                _atual = Ler();
            }
            catch (Exception)
            {
                // Falhar em listar não pode derrubar a bandeja: fica com a
                // fotografia anterior, e o padrão do Windows continua gravável.
            }

            lock (_trava)
            {
                if (!_pendente) { _lendo = false; return; }
                _pendente = false;
            }
        }
    }

    private CatalogoInstantaneo Ler() => new(
        Lado(DataFlow.Capture, Role.Communications),
        Lado(DataFlow.Render, Role.Multimedia));

    private List<Dispositivo> Lado(DataFlow fluxo, Role papel)
    {
        string? padrao = null;
        try
        {
            if (_enumerador.HasDefaultAudioEndpoint(fluxo, papel))
                padrao = _enumerador.GetDefaultAudioEndpoint(fluxo, papel).ID;
        }
        catch (Exception) { /* sem padrão definido; a lista ainda serve */ }

        var lista = new List<Dispositivo>();
        foreach (var d in _enumerador.EnumerateAudioEndPoints(fluxo, DeviceState.Active))
        {
            string nome;
            try { nome = d.FriendlyName; }
            catch (Exception) { nome = "(nome indisponível)"; }
            lista.Add(new Dispositivo(d.ID, nome, d.ID == padrao));
        }
        return lista;
    }

    // ───────────────────────────────────── IMMNotificationClient
    // Chamados por uma thread do Windows, não pela da UI.

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Reler();
    public void OnDeviceAdded(string pwstrDeviceId) => Reler();
    public void OnDeviceRemoved(string deviceId) => Reler();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Reler();

    /// <remarks>
    /// Ignorado de propósito: dispara com frequência alta (volume, formato) e
    /// nada disso muda a lista. Relê só quando o nome muda, que é raro.
    /// </remarks>
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        if (key.formatId == PropertyKeys.PKEY_Device_FriendlyName.formatId &&
            key.propertyId == PropertyKeys.PKEY_Device_FriendlyName.propertyId)
            Reler();
    }

    public void Dispose()
    {
        try { _enumerador.UnregisterEndpointNotificationCallback(this); }
        catch (Exception) { /* saindo mesmo */ }
        _enumerador.Dispose();
    }
}
