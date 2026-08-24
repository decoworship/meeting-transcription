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
    public async Task<AmostraDeVoz?> AprenderAsync(
        string pastaDaGravacao, IReadOnlyList<SegmentoFinal> segmentos,
        string falante, string nome, float[]? mic = null,
        CancellationToken ct = default)
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
        float[]? mic = null, CancellationToken ct = default)
    {
        var achados = new Dictionary<string, string>();
        if (vozes.Pessoas().Count == 0) return achados;   // biblioteca vazia: nada a fazer

        foreach (string falante in segmentos.Select(s => s.Speaker).Distinct().OfType<string>())
        {
            // "You" já é certeza pela faixa do microfone; adivinhar por voz só
            // poderia piorar o que já se sabe.
            if (falante == DonoDoMicrofone || falante == "Unknown") continue;

            var trechos = TrechosDe(segmentos, falante, mic);
            if (trechos.Sum(t => t.Duracao) < Vozes.SegundosMinimos) continue;

            string audio = Path.Combine(pastaDaGravacao, $"{FaixaDe(falante)}.wav");
            if (!File.Exists(audio)) continue;

            try
            {
                var voz = await ExtrairAsync(audio, [.. trechos.Take(3)], ct);
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

    private async Task<VozExtraida> ExtrairAsync(string audio, List<TrechoLimpo> trechos,
                                                CancellationToken ct)
    {
        using var motor = await MotorSidecar.IniciarAsync(
            motores.Python, [motores.ScriptDiarizacao], ct, Motores.Ambiente());

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
