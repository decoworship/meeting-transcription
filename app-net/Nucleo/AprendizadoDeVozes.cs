using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>Um intervalo de fala, na faixa de onde ele deve ser lido.</summary>
public readonly record struct TrechoLimpo(double Inicio, double Fim)
{
    public double Duracao => Fim - Inicio;
}

/// <summary>
/// Liga a biblioteca de vozes ao fluxo: aprende quando alguém é nomeado, e
/// reconhece nas reuniões seguintes.
/// </summary>
/// <remarks>
/// As regras de inscrição da VOZES.md §2 vivem aqui, e não no motor: escolher
/// <b>quais</b> trechos representam alguém é decisão de produto, e o motor só
/// sabe transformar áudio em vetor.
/// </remarks>
public sealed class AprendizadoDeVozes(Motores motores, Vozes vozes)
{
    /// <summary>
    /// Descarta trechos colados a fala de outra pessoa.
    /// </summary>
    /// <remarks>
    /// Cross-talk é a contaminação mais comum: o fim de um turno costuma trazer
    /// a voz de quem entrou por cima. Meio segundo de folga em cada lado é
    /// barato — sobra fala de sobra numa reunião — e corta a classe inteira.
    /// </remarks>
    public const double FolgaEntreTurnos = 0.5;

    /// <summary>
    /// A janela de áudio de um bloco de inscrição.
    /// </summary>
    /// <remarks>
    /// É ao mesmo tempo o que se guarda em <c>trechos/</c> para alguém poder
    /// julgar a amostra e o que o vetor enxerga. Os dois eram números
    /// diferentes até 20/08/2026, e essa diferença era o defeito.
    /// </remarks>
    public const double SegundosDoTrecho = 4.0;

    /// <summary>O rótulo de quem está do lado de cá do microfone.</summary>
    public const string DonoDoMicrofone = "You";

    /// <summary>
    /// Quanto áudio acumular por falante ao reconhecer, em <see cref="ReconhecerAsync"/>.
    /// </summary>
    /// <remarks>
    /// Até 22/09/2026 eram os três trechos mais longos, um número fixo de
    /// blocos e não de segundos. Medido numa reunião real (46 pessoas no
    /// banco, limiar 0,70): três trechos deram 6,1 s e similaridade 0,581 para
    /// uma pessoa que o banco conhecia — abaixo do limiar, apesar de ela ser a
    /// melhor candidata por larga margem (a segunda colocada ficou em 0,492).
    /// Chegar a ~15 s levou a mesma pessoa a 0,706, acima do limiar. O ganho
    /// veio de ter mais áudio, não de um trecho ruim ter saído: o filtro de
    /// contaminação de <see cref="TrechosDe"/> já correu antes, e é o que torna
    /// este orçamento maior tolerável em vez de arriscado.
    /// </remarks>
    public const double SegundosDoOrcamentoDeReconhecimento = 15.0;

    /// <summary>
    /// Quanto áudio exclusivo da diarização acumular para reconhecer, em segundos.
    /// </summary>
    /// <remarks>
    /// O dobro de <see cref="SegundosDoOrcamentoDeReconhecimento"/> porque aqui o
    /// risco de contaminação é outro: cada quadro já é de uma pessoa só pela
    /// diarização, e mais áudio limpo só estabiliza o vetor. É o teto com que o
    /// ganho de 05/11/2026 foi medido.
    /// </remarks>
    public const double SegundosLimposDeReconhecimento = 30.0;

    /// <summary>Um pedaço exclusivo mais curto que isto não entra no vetor.</summary>
    public const double MinimoDoTrechoLimpoS = 0.4;

    /// <summary>
    /// Os pedaços em que só <paramref name="falante"/> fala, pela linha do tempo
    /// da diarização.
    /// </summary>
    /// <param name="linhaDoTempo">
    /// A diarização com os rótulos dos segmentos (<see cref="Montagem.Rotulos"/>)
    /// e a trilha do dono dentro — o dono conta como outra pessoa.
    /// </param>
    /// <param name="mic">Quando há, descarta o pedaço em que o microfone tem fala.</param>
    /// <remarks>
    /// <para>
    /// <b>Por que não os segmentos do ASR.</b> O <see cref="TrechosDe"/> toma os
    /// primeiros 4 s de cada segmento, e o segmento do ASR não sabe onde outra
    /// pessoa entrou por cima. Medido em 05/11/2026 nas sete reuniões com Gemini:
    /// para o mesmo cluster, estes pedaços deram ~0,1 a mais de semelhança
    /// (0,65 → 0,75; 0,66 → 0,76; 0,46 → 0,81), e o acerto de nome por palavra
    /// subiu de 83,6% para 90,1%. Um cluster-fragmento — a mesma pessoa partida
    /// em duas pela diarização — também deixa de ficar sem vetor: os pedaços que
    /// o corte por palavra cria encostam no vizinho e nunca passavam pela guarda
    /// de vizinhos de <see cref="TrechosDe"/>.
    /// </para>
    /// <para>
    /// Do mais longo ao mais curto, até <see cref="SegundosLimposDeReconhecimento"/>.
    /// </para>
    /// </remarks>
    public static List<TrechoLimpo> TrechosDaDiarizacao(
        IReadOnlyList<SegmentoDeFalante> linhaDoTempo, string falante, float[]? mic = null)
    {
        var outros = linhaDoTempo.Where(d => d.Falante != falante).ToList();
        var pedacos = new List<TrechoLimpo>();

        foreach (var d in linhaDoTempo.Where(d => d.Falante == falante))
        {
            var restos = new List<TrechoLimpo> { new(d.Inicio, d.Fim) };
            foreach (var o in outros)
            {
                var novos = new List<TrechoLimpo>();
                foreach (var r in restos)
                {
                    if (o.Fim <= r.Inicio || o.Inicio >= r.Fim) { novos.Add(r); continue; }
                    if (o.Inicio > r.Inicio) novos.Add(new(r.Inicio, o.Inicio));
                    if (o.Fim < r.Fim) novos.Add(new(o.Fim, r.Fim));
                }
                restos = novos;
            }
            pedacos.AddRange(restos.Where(r =>
                r.Duracao >= MinimoDoTrechoLimpoS
                && (mic is null || Faixas.Rms(mic, r.Inicio, r.Fim) < Montagem.RmsMinimoDoDono)));
        }

        var usados = new List<TrechoLimpo>();
        double soma = 0;
        foreach (var p in pedacos.OrderByDescending(p => p.Duracao))
        {
            if (soma >= SegundosLimposDeReconhecimento) break;
            usados.Add(p);
            soma += p.Duracao;
        }
        return usados;
    }

    /// <summary>
    /// Os trechos de um falante que servem para aprender a voz dele.
    /// </summary>
    /// <param name="mic">
    /// A faixa do microfone, quando quem chama a tem. Sem ela a guarda contra
    /// contaminação de dentro do bloco não roda — ver as observações.
    /// </param>
    /// <remarks>
    /// <para>
    /// Ordenados do mais longo para o mais curto: fala contínua é sinal melhor
    /// que retalho, e o piso de duração é atingido com menos emendas.
    /// </para>
    /// <para>
    /// <b>Cada trecho é truncado em <see cref="SegundosDoTrecho"/>.</b> Até
    /// 20/08/2026 o <c>.wav</c> guardado em <c>trechos/</c> era cortado em 4 s e
    /// o <b>vetor</b> usava o intervalo inteiro — de modo que quem auditasse a
    /// amostra ouvindo o arquivo <b>não encontrava o defeito</b>: o áudio estava
    /// limpo e o vetor não. Agora os dois olham para a mesma janela, e
    /// <c>Origem.T0/T1</c> descreve o que o vetor de fato viu.
    /// </para>
    /// <para>
    /// <b>E o bloco é descartado se o microfone estiver ativo nele.</b> A guarda
    /// de cross-talk acima olha os <b>vizinhos</b> do segmento; não havia nada
    /// contra outra pessoa <b>dentro</b> dele — e ordenar por duração é preferir
    /// ativamente os segmentos de maior risco. Medido: o bloco nº 1 do vetor de
    /// uma participante continha 13,2 s de voz do dono do microfone, 30% do
    /// bloco contra 1,5% de taxa base na gravação (docs/FASE6.md §4.2).
    /// </para>
    /// <para>
    /// O critério é o piso do <see cref="Montagem.RmsMinimoDoDono"/>, e não a
    /// margem de dominância: para <b>atribuir</b> um segmento é preciso que o
    /// microfone vença o sistema; para <b>descartar</b> um bloco de inscrição
    /// basta que o dono estivesse falando. Inscrever é a decisão cara — o vetor
    /// persiste entre reuniões e envenena em silêncio —, e sobra fala numa
    /// reunião.
    /// </para>
    /// <para>
    /// <b>Sem <paramref name="mic"/> a guarda não roda</b>, e o resultado é o de
    /// antes: quem chama sem a faixa não fica pior do que estava. O truncamento
    /// roda sempre.
    /// </para>
    /// </remarks>
    public static List<TrechoLimpo> TrechosDe(IReadOnlyList<SegmentoFinal> segmentos,
                                              string falante, float[]? mic = null)
    {
        var limpos = new List<TrechoLimpo>();
        bool ehODono = falante == DonoDoMicrofone;

        for (int i = 0; i < segmentos.Count; i++)
        {
            var s = segmentos[i];
            if (s.Speaker != falante) continue;

            // Vizinho de outra pessoa perto demais: o trecho pode carregar a voz
            // dela, e um vetor contaminado envenena o perfil em silêncio.
            bool antesSujo = i > 0 && segmentos[i - 1].Speaker != falante
                             && s.Start - segmentos[i - 1].End < FolgaEntreTurnos;
            bool depoisSujo = i + 1 < segmentos.Count && segmentos[i + 1].Speaker != falante
                              && segmentos[i + 1].Start - s.End < FolgaEntreTurnos;
            if (antesSujo || depoisSujo) continue;

            // A janela que o vetor vai ver — a mesma que o trecho guardado.
            var janela = new TrechoLimpo(s.Start, Math.Min(s.End, s.Start + SegundosDoTrecho));

            // O microfone é verdade, não estimativa: se o dono falou aqui, este
            // bloco não representa a voz de mais ninguém.
            if (mic is not null && !ehODono
                && Faixas.Rms(mic, janela.Inicio, janela.Fim) >= Montagem.RmsMinimoDoDono)
                continue;

            limpos.Add(janela);
        }

        return [.. limpos.OrderByDescending(t => t.Duracao)];
    }

    /// <summary>
    /// A faixa de onde ler a voz de um falante.
    /// </summary>
    /// <remarks>
    /// A sua voz sai do <c>mic.wav</c>, que é limpo por construção — cross-talk
    /// seu é impossível nessa faixa. A dos outros sai do <c>system.wav</c>, onde
    /// a sua não aparece. Ler tudo do mix seria misturar as duas coisas
    /// justamente na hora de distinguir pessoas.
    /// </remarks>
    public static string FaixaDe(string falante) =>
        falante == DonoDoMicrofone ? "mic" : "system";

    /// <summary>
    /// Aprende a voz de um falante e a guarda com o nome dado.
    /// </summary>
    /// <returns>
    /// A amostra guardada, ou <c>null</c> quando não havia fala limpa suficiente.
    /// </returns>
    /// <param name="mic">
    /// A faixa do microfone, para a guarda de contaminação. Quem não a passa
    /// inscreve sem ela — ver <see cref="TrechosDe"/>.
    /// </param>
    /// <param name="motor">
    /// Qual motor produziu a transcrição de onde este falante saiu. Ausente é o
    /// clássico — ver <see cref="AmostraDeVoz.Motor"/>.
    /// </param>
    public async Task<AmostraDeVoz?> AprenderAsync(
        string pastaDaGravacao, IReadOnlyList<SegmentoFinal> segmentos,
        string falante, string nome, float[]? mic = null,
        CancellationToken ct = default, string? motor = null)
    {
        var trechos = TrechosDe(segmentos, falante, mic);
        double total = trechos.Sum(t => t.Duracao);
        if (total < Vozes.SegundosMinimos) return null;

        // Só o suficiente para passar do piso: quanto mais trechos, maior a
        // chance de um deles estar sujo.
        var usados = new List<TrechoLimpo>();
        double soma = 0;
        foreach (var t in trechos)
        {
            usados.Add(t);
            soma += t.Duracao;
            if (soma >= Vozes.SegundosMinimos * 1.5) break;
        }

        string faixa = FaixaDe(falante);
        string audio = Path.Combine(pastaDaGravacao, $"{faixa}.wav");
        if (!File.Exists(audio)) return null;

        var voz = await ExtrairAsync(audio, usados, ct);

        string gravacao = Path.GetFileName(pastaDaGravacao);
        var amostra = new AmostraDeVoz
        {
            Vetor = voz.Vetor,
            // Carimbado na amostra, e não deduzido na hora de comparar: o modelo
            // pode mudar entre esta inscrição e a próxima leitura, e é
            // exatamente esse caso que o carimbo existe para pegar. Ver
            // AmostraDeVoz.Modelo.
            Modelo = voz.Modelo,
            // A geração de regras sob a qual ela foi colhida. É o que separa
            // esta amostra das que vieram do caminho sem guarda de
            // contaminação. Ver Vozes.RegrasAtuais.
            Regras = Vozes.RegrasAtuais,
            // E de qual motor veio o falante que alguém acabou de nomear. O
            // rótulo do MOSS é uma identidade COSTURADA por vetor de voz, e a
            // costura divide gente demais e pode fundir (§11.4): um vetor
            // inscrito a partir de uma fusão atravessa para todas as reuniões
            // seguintes, e retranscrever não desfaz. Ver AmostraDeVoz.Motor.
            Motor = Vozes.MotorAceitoNaAmostra(motor),
            CriadaEm = DateTimeOffset.UtcNow.ToString("o"),
            DuracaoS = Math.Round(soma, 2),
            Trecho = RecortarTrecho(audio, usados[0], gravacao, nome),
            Origem = new Origem
            {
                Gravacao = gravacao,
                Faixa = faixa,
                T0 = Math.Round(usados[0].Inicio, 2),
                T1 = Math.Round(usados[0].Fim, 2),
                Dispositivo = DispositivoDe(pastaDaGravacao, faixa),
            },
        };

        return vozes.Aprender(nome, amostra);
    }

    /// <summary>
    /// Tenta pôr nome nos falantes de uma transcrição recém-feita.
    /// </summary>
    /// <returns>Rótulo cru → nome reconhecido.</returns>
    public async Task<Dictionary<string, string>> ReconhecerAsync(
        string pastaDaGravacao, IReadOnlyList<SegmentoFinal> segmentos,
        float[]? mic = null, CancellationToken ct = default,
        IReadOnlyList<SegmentoDeFalante>? linhaDoTempo = null)
    {
        var achados = new Dictionary<string, string>();
        if (vozes.Pessoas().Count == 0) return achados;   // biblioteca vazia: nada a fazer

        foreach (string falante in segmentos.Select(s => s.Speaker).Distinct().OfType<string>())
        {
            // "You" já é certeza pela faixa do microfone; adivinhar por voz só
            // poderia piorar o que já se sabe.
            if (falante == DonoDoMicrofone || falante == "Unknown") continue;

            string audio = Path.Combine(pastaDaGravacao, $"{FaixaDe(falante)}.wav");
            if (!File.Exists(audio)) continue;

            try
            {
                // Pela diarização quando ela veio — ver TrechosDaDiarizacao. Sem
                // ela, ou sem fala exclusiva bastante, o caminho de antes: quem
                // chama sem linha do tempo, e o falante que só fala por cima de
                // alguém, não ficam piores do que estavam.
                var usados = linhaDoTempo is null
                    ? []
                    : TrechosDaDiarizacao(linhaDoTempo, falante, mic);
                if (usados.Sum(t => t.Duracao) < Vozes.SegundosMinimos)
                    usados = PeloAsr(segmentos, falante, mic);
                if (usados.Sum(t => t.Duracao) < Vozes.SegundosMinimos) continue;

                var voz = await ExtrairAsync(audio, usados, ct);
                // O modelo vai junto: com um modelo novo ninguém é reconhecido,
                // e todo mundo se reinscreve. É o resultado certo — comparar
                // entre modelos não devolve "não sei", devolve um nome errado.
                if (vozes.Reconhecer(voz.Vetor, voz.Modelo) is { } quem)
                    achados[falante] = quem.Pessoa;
            }
            catch (MotorException)
            {
                // Não reconhecer é o estado normal de quem nunca foi nomeado;
                // falhar aqui não pode custar a transcrição inteira.
            }
        }
        return achados;
    }

    /// <summary>
    /// Os trechos limpos dos segmentos do ASR, até o orçamento em segundos.
    /// </summary>
    /// <remarks>
    /// Acumula até o orçamento, não um número fixo de blocos — ver
    /// <see cref="SegundosDoOrcamentoDeReconhecimento"/>. Se sobrar menos que o
    /// orçamento, usa o que há: o orçamento é teto, não exigência.
    /// </remarks>
    private static List<TrechoLimpo> PeloAsr(IReadOnlyList<SegmentoFinal> segmentos,
                                             string falante, float[]? mic)
    {
        var usados = new List<TrechoLimpo>();
        double soma = 0;
        foreach (var t in TrechosDe(segmentos, falante, mic))
        {
            usados.Add(t);
            soma += t.Duracao;
            if (soma >= SegundosDoOrcamentoDeReconhecimento) break;
        }
        return usados;
    }

    private async Task<VozExtraida> ExtrairAsync(string audio, List<TrechoLimpo> trechos,
                                                CancellationToken ct)
    {
        using var motor = await MotorSidecar.IniciarAsync(
            motores.Python, [motores.ScriptDiarizacao], ct, Motores.Ambiente());

        // **A terceira carga de GPU, e até 04/09/2026 ela era invisível.** O
        // pipeline sobe o pyannote duas vezes — a diarização e este método —, e
        // só a primeira assinava o AoRegistrar. O registro terminava antes do
        // fim do pipeline, que é exatamente onde a máquina do segundo usuário
        // desliga (docs/FASE6.md §3.0): o log dizia que a diarização acabou bem
        // e depois se calava, e ninguém tinha como saber se o corte veio daqui.
        // Uma linha, e é metade do SUP-1 do docs/BACKLOG.md.
        motor.AoRegistrar += l => Registro.Escrever("vozes", l);

        return await motor.VozAsync(audio,
            [.. trechos.Select(t => (t.Inicio, t.Fim))], ct);
    }

    /// <summary>Guarda alguns segundos do áudio que gerou a amostra.</summary>
    private string? RecortarTrecho(string audio, TrechoLimpo trecho,
                                   string gravacao, string nome)
    {
        try
        {
            var faixa = Faixas.Ler(audio, audio).Mic;
            int inicio = (int)(trecho.Inicio * Faixas.TaxaDeAmostragem);
            int fim = Math.Min(faixa.Length,
                inicio + (int)(SegundosDoTrecho * Faixas.TaxaDeAmostragem));
            if (fim <= inicio) return null;

            // Nome previsível e sem colisão: a mesma pessoa pode ter várias
            // amostras da mesma reunião.
            string arquivo = Path.Combine("trechos",
                $"{Sanear(nome)}_{gravacao}_{(int)trecho.Inicio}.wav");
            string destino = vozes.CaminhoDoTrecho(arquivo);
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);

            Faixas.Escrever(destino, faixa[inicio..fim]);
            return arquivo;
        }
        catch (Exception)
        {
            // Sem o trecho a amostra vale menos, mas ainda vale: o vetor é o
            // que reconhece; o áudio é o que permite auditar.
            return null;
        }
    }

    private static string Sanear(string nome) =>
        string.Concat(nome.Select(c => char.IsLetterOrDigit(c) ? c : '-'));

    /// <summary>O dispositivo daquela faixa, para agrupar por condição.</summary>
    private static string? DispositivoDe(string pastaDaGravacao, string faixa)
    {
        try
        {
            string meta = Path.Combine(pastaDaGravacao, "meta.json");
            if (!File.Exists(meta)) return null;

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(meta));
            return doc.RootElement.GetProperty("tracks").GetProperty(faixa)
                .GetProperty("device").GetString();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
