using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace MeetingApp.Sidecar;

/// <summary>Erro do lado do motor, ou do canal com ele.</summary>
public sealed class MotorException(string mensagem) : Exception(mensagem);

/// <summary>
/// Um motor rodando como processo separado, falando o protocolo por linha do
/// <c>docs/SIDECAR.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// O processo fica <b>quente entre requisições</b>: carregar o pyannote a cada
/// gravação custaria mais que diarizar. Descartar é decisão do cliente — o motor
/// não tem timeout próprio, porque não sabe se o usuário foi almoçar.
/// </para>
/// <para>
/// <b>Cancelar é matar.</b> Não há operação de cancelamento no protocolo e não
/// deve haver: dentro de uma inferência o motor não tem ponto em que possa
/// cooperar. Matar libera a VRAM na hora, que é o critério B da Fase 2.
/// </para>
/// </remarks>
public sealed class MotorSidecar : IDisposable
{
    private readonly Process _processo;
    private int _proximoId = 1;

    /// <summary>Nome e versão que o motor declarou no handshake.</summary>
    public string Nome { get; }
    public string Versao { get; }

    /// <summary>Linhas do <c>stderr</c> do motor: log livre, nunca protocolo.</summary>
    public event Action<string>? AoRegistrar;

    private MotorSidecar(Process processo, string nome, string versao)
    {
        _processo = processo;
        Nome = nome;
        Versao = versao;
    }

    /// <summary>Sobe o motor e espera o handshake.</summary>
    /// <param name="comando">Executável do motor (o Python embutido, na v1).</param>
    /// <param name="argumentos">Argumentos, tipicamente o script do motor.</param>
    /// <param name="ambiente">
    /// Variáveis a acrescentar ao processo do motor. É por aqui que o token do
    /// HuggingFace chega ao pyannote sem ninguém configurar variável de sistema.
    /// </param>
    public static async Task<MotorSidecar> IniciarAsync(
        string comando, IEnumerable<string> argumentos, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? ambiente = null)
    {
        var info = new ProcessStartInfo(comando)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Sem isto cada spawn pisca um console preto no Windows.
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (string a in argumentos) info.ArgumentList.Add(a);
        if (ambiente is not null)
            foreach (var (chave, valor) in ambiente) info.Environment[chave] = valor;

        var processo = Process.Start(info)
            ?? throw new MotorException($"não foi possível iniciar o motor: {comando}");

        // Imediatamente depois de subir, e antes de qualquer await: se o app for
        // morto entre o Start e a adoção, o motor fica órfão — que é justamente
        // o buraco que o job fecha. Ver JobDosMotores.
        JobDosMotores.Adotar(processo);

        var registro = new List<string>();

        // O motor ainda não existe quando a drenagem começa — ele nasce depois
        // do handshake. O holder é o que permite a mesma tarefa servir os dois
        // momentos: antes, só acumula; depois, também repassa.
        MotorSidecar? criado = null;

        _ = Task.Run(async () =>
        {
            // Drenado sempre: um stderr cheio bloqueia o processo do outro lado.
            while (await processo.StandardError.ReadLineAsync() is { } linha)
            {
                lock (registro)
                {
                    registro.Add(linha);
                    // Uma transcrição de duas horas rende centenas de linhas de
                    // aviso, e guardá-las todas é vazamento lento. O que a
                    // mensagem de morte usa é a cauda.
                    if (registro.Count > 500) registro.RemoveRange(0, 200);
                }

                // **Repassar também depois do handshake**, que é onde estava o
                // buraco: o interessante — carga do modelo, dispositivo
                // escolhido, avisos de CUDA — acontece DEPOIS de o motor dizer
                // que está pronto, e antes disto essas linhas morriam em
                // memória. Foi o que faltou para diagnosticar a máquina de um
                // usuário em 18/08/2026.
                criado?.AoRegistrar?.Invoke(linha);
            }
        }, CancellationToken.None);

        try
        {
            var pronto = await LerMensagemAsync(processo, ct)
                ?? throw new MotorException(
                    "o motor morreu antes de dizer que estava pronto." + Cauda(registro));

            if (pronto.Tipo != "pronto")
                throw new MotorException(
                    $"o motor falou '{pronto.Tipo}' onde o handshake era esperado.");

            var motor = new MotorSidecar(processo, pronto.Motor ?? "?", pronto.Versao ?? "?");
            criado = motor;
            lock (registro)
            {
                foreach (string l in registro) motor.AoRegistrar?.Invoke(l);
            }
            return motor;
        }
        catch
        {
            Matar(processo);
            throw;
        }
    }

    /// <param name="progresso">Chamado a cada aviso de progresso: fração e texto.</param>
    /// <exception cref="MotorException">
    /// O motor recusou a requisição, ou morreu no meio dela. As duas coisas
    /// precisam chegar legíveis à UI sem derrubar o app (critério C da Fase 2).
    /// </exception>
    /// <param name="modelo">
    /// O pipeline a usar, pelo nome da pasta em <c>modelos/</c>. Nulo usa o
    /// padrão do motor.
    /// </param>
    /// <param name="motorDeDiarizacao">
    /// <c>"onnx"</c>, o único motor desde 22/09/2026. Nulo usa o padrão do
    /// motor, <c>"onnx"</c>. Ver docs/DIARIZACAO-ONNX.md.
    /// </param>
    public async Task<IReadOnlyList<SegmentoDeFalante>> DiarizarAsync(
        string caminhoDoAudio, Action<double, string>? progresso = null,
        string? modelo = null, CancellationToken ct = default,
        string? motorDeDiarizacao = null)
    {
        var m = await ExecutarAsync(
            new Requisicao
            {
                Id = _proximoId++, Op = "diarizar", Audio = caminhoDoAudio, Modelo = modelo,
                MotorDeDiarizacao = motorDeDiarizacao,
            },
            progresso, ct);

        return (m.Segmentos ?? [])
            .Select(s => new SegmentoDeFalante(s.Inicio, s.Fim, s.Falante ?? ""))
            .ToList();
    }

    /// <param name="vocabulario">Termos do projeto, como <c>hotwords</c> do ASR.</param>
    /// <inheritdoc cref="DiarizarAsync"/>
    /// <summary>
    /// Qual placa o motor usaria, perguntado <b>antes</b> de carregar o modelo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Custa o import do torch (uns segundos) e não carrega os 3 GB do modelo.
    /// É barato o bastante para perguntar sempre, e é o que evita descobrir
    /// tarde demais que a transcrição foi para a CPU.
    /// </para>
    /// <para>
    /// <b>Por que não basta o nvidia-smi.</b> O bloco de diagnóstico da tela
    /// pergunta ao driver; quem decide o dispositivo é o torch, que precisa
    /// também das DLLs de CUDA alcançáveis. Os dois discordaram na máquina de um
    /// usuário em 18/08/2026 — a tela dizia "RTX 4050" e o modelo rodava na CPU,
    /// até o Windows cair por falta de RAM.
    /// </para>
    /// </remarks>
    public async Task<DispositivoDoMotor> DispositivoAsync(CancellationToken ct = default)
    {
        var m = await ExecutarAsync(
            new Requisicao { Id = _proximoId++, Op = "dispositivo" }, null, ct);

        return new DispositivoDoMotor(m.Cuda ?? false, m.Nome, m.CudaDoTorch, m.Motivo);
    }

    public async Task<Transcricao> TranscreverAsync(
        string caminhoDoAudio, string? vocabulario = null, string? idioma = null,
        Action<double, string>? progresso = null, CancellationToken ct = default)
    {
        var m = await ExecutarAsync(
            new Requisicao
            {
                Id = _proximoId++,
                Op = "transcrever",
                Audio = caminhoDoAudio,
                Vocabulario = vocabulario,
                Idioma = idioma,
            },
            progresso, ct);

        return new Transcricao(
            (m.Segmentos ?? []).Select(s => new SegmentoDeTexto(
                s.Inicio, s.Fim, s.Texto ?? "",
                s.Palavras?.Select(p => new Palavra(p.Inicio, p.Fim, p.Texto ?? "")).ToList()))
                .ToList(),
            m.Idioma, m.Duracao ?? 0, m.Dispositivo, m.Motivo);
    }

    /// <summary>
    /// Texto e falante numa passada só — a operação do motor MOSS.
    /// </summary>
    /// <param name="inicio">
    /// Começo e fim da janela dentro do arquivo, em segundos. Nulos processam o
    /// arquivo inteiro, que é a forma documentada no <c>docs/SIDECAR.md</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Uma janela, e não um arquivo por bloco.</b> O MOSS roda em blocos de 3
    /// minutos porque a passada inteira não escala na placa desta máquina — foi
    /// interrompida depois de 1h36 numa gravação de 32 min
    /// (<c>docs/FASE7-RESULTADOS.md</c> §7.2). Quem corta é o núcleo, que já tem
    /// as faixas em memória; cortar aqui escreveria 20 WAVs temporários numa
    /// reunião de uma hora.
    /// </para>
    /// <para>
    /// <b>Os rótulos saem crus e locais ao bloco.</b> O <c>S1</c> de um bloco
    /// não é o <c>S1</c> do vizinho, e este método não tem como saber — quem
    /// costura é <c>Nucleo/CosturaDeFalantes.cs</c>, por vetor de voz.
    /// </para>
    /// </remarks>
    public async Task<TranscricaoComFalantes> TranscreverESepararAsync(
        string caminhoDoAudio, double? inicio = null, double? fim = null,
        Action<double, string>? progresso = null, CancellationToken ct = default)
    {
        var m = await ExecutarAsync(
            new Requisicao
            {
                Id = _proximoId++,
                Op = "transcrever_e_separar",
                Audio = caminhoDoAudio,
                Inicio = inicio,
                Fim = fim,
            },
            progresso, ct);

        return new TranscricaoComFalantes(
            (m.Segmentos ?? []).Select(s => new SegmentoDitoPorAlguem(
                s.Inicio, s.Fim, s.Texto ?? "", s.Falante ?? "")).ToList(),
            m.Duracao ?? 0, m.Dispositivo, m.Motivo);
    }

    /// <summary>
    /// O vetor que identifica a voz de quem fala nos trechos indicados.
    /// </summary>
    /// <remarks>
    /// Quem escolhe os trechos é o núcleo: quais representam a pessoa é decisão
    /// de produto, e o motor só sabe transformar áudio em vetor.
    /// </remarks>
    public async Task<VozExtraida> VozAsync(
        string caminhoDoAudio, IReadOnlyList<(double Inicio, double Fim)> trechos,
        CancellationToken ct = default)
    {
        var m = await ExecutarAsync(
            new Requisicao
            {
                Id = _proximoId++,
                Op = "voz",
                Audio = caminhoDoAudio,
                Trechos = [.. trechos.Select(t => new TrechoJson { Inicio = t.Inicio, Fim = t.Fim })],
            },
            null, ct);

        return new VozExtraida(
            m.Vetor ?? throw new MotorException("o motor não devolveu vetor de voz."),
            m.ModeloDaVoz);
    }

    /// <summary>
    /// Baixa um repositório de modelo para o cache local.
    /// </summary>
    /// <remarks>
    /// Não devolve nada: o que interessa é o efeito no disco, e quem responde
    /// "está aí?" é o <c>Catalogo</c>, lendo o cache. Ter o motor devolver o
    /// caminho criaria uma segunda fonte da mesma verdade.
    /// </remarks>
    /// <param name="arquivo">
    /// Um arquivo só do repositório, em vez do repositório inteiro. Os GGUF de
    /// ata moram em repositórios com uma dezena de quantizações, e baixar tudo
    /// traria 20 GB para usar 2,5.
    /// </param>
    public async Task BaixarAsync(
        string repositorio, string pasta, long tamanhoEsperado,
        Action<double, string>? progresso, CancellationToken ct = default,
        string? arquivo = null)
    {
        await ExecutarAsync(
            new Requisicao
            {
                Id = _proximoId++,
                Op = "baixar",
                Repositorio = repositorio,
                Pasta = pasta,
                TamanhoEsperado = tamanhoEsperado,
                Arquivo = arquivo,
            },
            progresso, ct);
    }

    /// <summary>Envia uma requisição e devolve a mensagem de resultado dela.</summary>
    /// <summary>Um pedaço de legenda, como o motor o emite.</summary>
    /// <param name="Firme">
    /// O que o LocalAgreement já fechou e não se reescreve mais.
    /// </param>
    /// <param name="Tentativo">A hipótese corrente, volátil.</param>
    /// <param name="AteMs">Até onde do áudio o texto firme chega.</param>
    public sealed record ParcialDaLegenda(string Firme, string Tentativo, long AteMs);

    /// <summary>
    /// Abre uma sessão de legenda e a alimenta até o canal fechar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>É a única operação deste motor que escreve enquanto lê.</b> O resto do
    /// protocolo é pedido e resposta; aqui a requisição fica aberta, os quadros
    /// de áudio sobem em linhas soltas e os parciais descem como
    /// <c>progresso</c> — que, pela regra de sempre, não encerra o pedido.
    /// </para>
    /// <para>
    /// <b>As escritas são serializadas por um semáforo</b>, e não por acaso: a
    /// tarefa que envia áudio e a que responde ao cancelamento usariam o mesmo
    /// <c>StandardInput</c>, e duas linhas entrelaçadas viram JSON inválido —
    /// o mesmo sintoma que a regra do descritor duplicado existe para evitar,
    /// só que na outra direção.
    /// </para>
    /// <para>
    /// <b>Fechar o canal encerra com graça</b>, e o motor devolve o texto firme
    /// inteiro. Cancelar mata o processo, como em toda operação deste tipo.
    /// </para>
    /// </remarks>
    public async Task<string> LegendarAsync(
        ChannelReader<float[]> audio, Action<ParcialDaLegenda> aoParcial,
        string? idioma, CancellationToken ct)
    {
        int id = _proximoId++;
        var escrita = new SemaphoreSlim(1, 1);

        async Task MandarAsync(Requisicao r)
        {
            await escrita.WaitAsync(ct);
            try { await EnviarAsync(r, ct); }
            finally { escrita.Release(); }
        }

        await MandarAsync(new Requisicao { Id = id, Op = "legendar", Idioma = idioma });

        var alimentar = Task.Run(async () =>
        {
            await foreach (var quadro in audio.ReadAllAsync(ct))
                await MandarAsync(new Requisicao
                {
                    Id = id, Op = "audio", Pcm = ParaBase64(quadro),
                });
            await MandarAsync(new Requisicao { Id = id, Op = "encerrar" });
        }, ct);

        try
        {
            while (true)
            {
                using var registroDeMorte = ct.Register(() => Matar(_processo));

                var m = await LerMensagemAsync(_processo, ct);
                if (m is null)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new MotorException($"o motor '{Nome}' morreu durante a legenda.");
                }
                if (m.Id is not null && m.Id != id) continue;

                switch (m.Tipo)
                {
                    case "progresso":
                        if (m.Firme is not null || m.Tentativo is not null)
                            aoParcial(new ParcialDaLegenda(
                                m.Firme ?? "", m.Tentativo ?? "", m.AteMs ?? 0));
                        break;
                    case "resultado":
                        return m.Texto ?? "";
                    case "erro":
                        throw new MotorException(m.MensagemDeErro ?? "erro sem mensagem.");
                }
            }
        }
        finally
        {
            // A tarefa de alimentar pode estar presa no canal; deixá-la morrer
            // sozinha vazaria uma escrita num pipe que já fechou.
            try { await alimentar; } catch (Exception) { }
            escrita.Dispose();
        }
    }

    /// <summary>float32 em [-1,1] → int16 little-endian em base64.</summary>
    /// <remarks>
    /// A conversão mora aqui e não no motor porque é aqui que se sabe de onde o
    /// áudio veio: o <c>Faixas.Mix</c> já normalizou o pico, então recortar em
    /// ±1 é rede de segurança e não política.
    /// </remarks>
    private static string ParaBase64(float[] quadro)
    {
        var bytes = new byte[quadro.Length * 2];
        for (int i = 0; i < quadro.Length; i++)
        {
            short v = (short)Math.Clamp(quadro[i] * 32767f, short.MinValue, short.MaxValue);
            bytes[i * 2] = (byte)(v & 0xFF);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return Convert.ToBase64String(bytes);
    }

    private async Task<Mensagem> ExecutarAsync(
        Requisicao requisicao, Action<double, string>? progresso, CancellationToken ct)
    {
        int id = requisicao.Id;
        await EnviarAsync(requisicao, ct);

        while (true)
        {
            // O cancelamento chega aqui como morte do processo, e a leitura
            // devolve null — é o mesmo caminho de "o motor morreu".
            using var registroDeMorte = ct.Register(() => Matar(_processo));

            var m = await LerMensagemAsync(_processo, ct);
            if (m is null)
            {
                ct.ThrowIfCancellationRequested();
                throw new MotorException(
                    $"o motor '{Nome}' morreu durante a operação '{requisicao.Op}'.");
            }

            // Resposta de uma requisição anterior já abandonada: ignorar em vez
            // de tratar como erro de protocolo.
            if (m.Id is not null && m.Id != id) continue;

            switch (m.Tipo)
            {
                case "progresso":
                    progresso?.Invoke(m.Pct ?? 0, m.Texto ?? "");
                    break;

                case "resultado":
                    return m;

                case "erro":
                    // Erro encerra a requisição, não o motor: ele continua vivo
                    // e pronto para a próxima.
                    throw new MotorException(m.MensagemDeErro ?? "erro sem mensagem.");

                default:
                    throw new MotorException($"o motor falou um tipo desconhecido: '{m.Tipo}'.");
            }
        }
    }

    private async Task EnviarAsync(Requisicao r, CancellationToken ct)
    {
        string linha = JsonSerializer.Serialize(r, ProtocoloJson.Default.Requisicao);
        try
        {
            await _processo.StandardInput.WriteLineAsync(linha.AsMemory(), ct);
            await _processo.StandardInput.FlushAsync(ct);
        }
        catch (IOException e)
        {
            // Pipe fechado: o motor morreu entre a última resposta e esta
            // pergunta.
            throw new MotorException($"o motor '{Nome}' não aceitou a requisição: {e.Message}");
        }
    }

    /// <returns><c>null</c> quando o pipe fecha, isto é, quando o motor morreu.</returns>
    private static async Task<Mensagem?> LerMensagemAsync(Process p, CancellationToken ct)
    {
        string? linha = await p.StandardOutput.ReadLineAsync(ct);
        if (linha is null) return null;

        try
        {
            return JsonSerializer.Deserialize(linha, ProtocoloJson.Default.Mensagem)
                ?? throw new MotorException("o motor mandou uma linha JSON vazia.");
        }
        catch (JsonException e)
        {
            // Quase sempre é uma biblioteca escrevendo no stdout do motor. Ver a
            // regra do descritor duplicado em docs/SIDECAR.md.
            throw new MotorException(
                $"lixo no canal do protocolo: {e.Message}\nlinha: {Cortar(linha)}");
        }
    }

    private static string Cortar(string s) => s.Length <= 200 ? s : s[..200] + "...";

    private static string Cauda(List<string> registro)
    {
        lock (registro)
        {
            // As últimas linhas do stderr são o que explica a morte; sem elas a
            // mensagem seria "morreu" e nada mais.
            return registro.Count == 0 ? ""
                : "\n" + string.Join("\n", registro.TakeLast(10));
        }
    }

    private static void Matar(Process p)
    {
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { /* já saiu */ }
        p.Dispose();
    }

    public void Dispose() => Matar(_processo);
}
