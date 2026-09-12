using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>
/// O que uma passada do MOSS sobre a gravação inteira devolveu.
/// </summary>
/// <param name="Segmentos">
/// O texto cru, com o <b>rótulo local do bloco</b> em
/// <see cref="SegmentoFinal.Speaker"/> — <c>b3_S1</c>, e não uma identidade.
/// </param>
/// <param name="Mudos">Blocos que o portão de RMS não deixou sair daqui.</param>
/// <param name="Falhados">Blocos em que o motor devolveu erro e a reunião seguiu.</param>
public sealed record ResultadoDoMoss(
    List<SegmentoFinal> Segmentos, double Duracao,
    int Blocos, int Mudos, int Falhados);

/// <summary>
/// O MOSS rodando sobre o mix, três minutos por vez.
/// </summary>
/// <remarks>
/// <para>
/// <b>O que ele substitui.</b> No caminho clássico o texto vem do
/// <c>RodarAsrAsync</c> e o falante vem de uma segunda chamada ao pyannote; aqui
/// os dois saem da mesma passada, porque é isso que o
/// <c>MOSS-Transcribe-Diarize</c> faz. Tudo o que vem <b>depois</b> — a voz do
/// dono, o corte por falante, a correção fonética, a revisão de termos, o
/// reconhecimento de vozes — é idêntico ao de hoje, e é essa fronteira que a
/// <c>docs/FASE7-BACKEND.md</c> chama de inegociável.
/// </para>
/// <para>
/// <b>Por que em C# e não dentro do sidecar.</b> Quem já leu as faixas é o
/// núcleo (<see cref="Faixas.Ler"/>), e é o mesmo desenho que a transcrição ao
/// vivo vai precisar, quando o áudio vier da captura em vez do arquivo. O motor
/// recebe uma <b>janela</b> sobre o mix que já existe, e não um WAV por bloco:
/// cortar em arquivos escreveria 20 temporários numa reunião de uma hora.
/// </para>
/// <para>
/// <b>Três minutos não é otimização, é requisito.</b> A passada inteira do MOSS
/// não escala nesta placa — foi interrompida depois de <b>1h36</b> numa gravação
/// de 32 min, a 0,35× o tempo real (<c>docs/FASE7-RESULTADOS.md</c> §7.2). Em
/// bloco de 3 min a velocidade não degrada com a duração (§7.3), e o bloco não
/// custa qualidade de texto (§1.2).
/// </para>
/// </remarks>
public static class MossEmBlocos
{
    /// <summary>O bloco, em segundos. Ver as observações da classe.</summary>
    public const double BlocoS = 180.0;

    /// <summary>
    /// Sobra de menos de um segundo no fim da gravação não vira bloco.
    /// </summary>
    /// <remarks>
    /// A mesma regra do <c>por_blocos</c> do <c>tools/medir_moss.py</c>
    /// (<c>if len(pedaco) &lt; taxa: continue</c>), e é o que faz o resultado
    /// deste arquivo bater com o da medição que aprovou o motor.
    /// </remarks>
    public const double MinimoDoBlocoS = 1.0;

    /// <summary>
    /// Uma gravação inteira, bloco a bloco.
    /// </summary>
    /// <param name="permitirCpu">
    /// A chave "Transcrever sem placa". Sem ela, um MOSS que subiu em CPU para
    /// a transcrição no primeiro bloco em vez de rodar a reunião inteira.
    /// </param>
    public static async Task<ResultadoDoMoss> TranscreverAsync(
        string pastaDaGravacao, Faixas faixas, Motores motores,
        IReadOnlyDictionary<string, string> ambiente, bool permitirCpu,
        Action<Progresso>? progresso = null, CancellationToken ct = default)
    {
        if (motores.OQueFaltaParaMoss() is { } falta) throw new MotorException(falta);

        // O mix é calculado uma vez e serve a dois: o arquivo que o motor abre e
        // o vetor que o portão de RMS mede. Chamar `EscreverMix` e depois `Mix()`
        // alocaria duas vezes o mesmo `float[]` — 230 MB cada, numa reunião de
        // duas horas, e memória em pico é assunto aberto (docs/FASE6.md §3.0).
        string caminhoDoMix = Path.Combine(pastaDaGravacao, "mix.wav");
        var mix = faixas.Mix();
        Faixas.Escrever(caminhoDoMix, mix);

        double duracao = mix.Length / (double)Faixas.TaxaDeAmostragem;
        int blocos = (int)Math.Ceiling(duracao / BlocoS);
        var segmentos = new List<SegmentoFinal>();
        int mudos = 0, falhados = 0;
        bool conferiuAPlaca = false;

        using var motor = await MotorSidecar.IniciarAsync(
            motores.Python, [motores.ScriptMoss], ct, ambiente);
        motor.AoRegistrar += l => Registro.Escrever("moss", l);

        for (int i = 0; i < blocos; i++)
        {
            ct.ThrowIfCancellationRequested();

            double inicio = i * BlocoS;
            double fim = Math.Min(inicio + BlocoS, duracao);
            if (fim - inicio < MinimoDoBlocoS) continue;

            progresso?.Invoke(new Progresso(
                "asr", (double)i / blocos, $"transcrevendo o bloco {i + 1} de {blocos}"));

            // **O portão de RMS, e ele não é economia.** Bloco mudo faz o MOSS
            // alucinar em chinês: sem áudio para transcrever ele completa o
            // prompt padrão embutido no GGUF. No acervo isso apareceu em 1 bloco
            // de 579 — raro, silencioso, e o portão custa uma raiz quadrada.
            //
            // O limiar é o do VozDoDono, que é o mesmo piso de "isto é fala e
            // não ruído de fundo" já medido nesta gravação; ter um segundo
            // número aqui seria ter dois lugares onde a mesma pergunta se
            // responde diferente.
            if (Faixas.Rms(mix, inicio, fim) < VozDoDono.LimiarDeFala)
            {
                mudos++;
                continue;
            }

            TranscricaoComFalantes bloco;
            try
            {
                bloco = await motor.TranscreverESepararAsync(caminhoDoMix, inicio, fim, null, ct);
            }
            catch (MotorException e)
            {
                // Um bloco custa um bloco, não a reunião. O caso conhecido é o
                // teto de geração estourado (`OutputTruncated`), que a
                // biblioteca levanta como exceção — ver motores/moss/motor.py.
                falhados++;
                Registro.Escrever("moss",
                    $"o bloco {i + 1}/{blocos} ({inicio:F0}–{fim:F0} s) falhou e foi pulado: "
                    + e.Message);
                continue;
            }

            if (!conferiuAPlaca)
            {
                conferiuAPlaca = true;
                Registro.Escrever("moss",
                    $"dispositivo: {bloco.Dispositivo ?? "?"}"
                    + (bloco.MotivoDaCpu is { Length: > 0 } m ? $" — {m}" : ""));

                // Mesma regra do ASR clássico, e pelo mesmo motivo: rodar em CPU
                // não é um modo do app, é o que acontece quando o backend nativo
                // não subiu. Descobrir isso no primeiro bloco custa 3 minutos de
                // áudio; descobrir no fim custa a tarde.
                if (bloco.Dispositivo == "cpu" && !permitirCpu)
                    throw new MotorException(Transcritor.SemPlaca(
                        new DispositivoDoMotor(false, null, null, bloco.MotivoDaCpu)));
            }

            foreach (var s in bloco.Segmentos)
                segmentos.Add(new SegmentoFinal
                {
                    // Os carimbos vêm relativos à janela; o deslocamento é o que
                    // os põe na linha do tempo da reunião.
                    Start = s.Inicio + inicio,
                    End = s.Fim + inicio,
                    Text = s.Texto,
                    // **O rótulo é local ao bloco, e a marca de origem diz
                    // isso.** O S1 do bloco 3 não é o S1 do bloco 7, e um rótulo
                    // sem a marca faria qualquer etapa adiante — inclusive a
                    // régua de medição — fingir que são a mesma pessoa. Quem os
                    // transforma em identidade é a CosturaDeFalantes.
                    Speaker = $"b{i}_{s.Falante}",
                    // O MOSS carimba por segmento, não por palavra. Sem palavras
                    // o Montagem.RepartirPorFalante não corta nada — que é o
                    // certo, porque aqui a troca de falante já veio decidida
                    // pelo modelo, e não estimada por sobreposição.
                    Words = null,
                });
        }

        Registro.Escrever("moss",
            $"{blocos} blocos de {BlocoS:F0} s · {segmentos.Count} segmentos · "
            + $"{mudos} mudos (portão de RMS) · {falhados} com erro");

        return new ResultadoDoMoss(segmentos, duracao, blocos, mudos, falhados);
    }
}
