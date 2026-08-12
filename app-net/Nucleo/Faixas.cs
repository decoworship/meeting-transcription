namespace MeetingApp.Nucleo;

/// <summary>
/// As duas faixas de uma gravação, lidas em memória.
/// </summary>
/// <remarks>
/// Porte de <c>src/web/recordings.py</c>. Ter as faixas separadas resolve metade
/// do problema de diarização de graça: qualquer trecho com energia no microfone
/// é você, com certeza — e o pyannote só precisa separar as pessoas do outro
/// lado, no <c>system.wav</c>.
/// </remarks>
public sealed class Faixas
{
    public const int TaxaDeAmostragem = 16_000;

    public float[] Mic { get; }
    public float[] Sistema { get; }

    public Faixas(float[] mic, float[] sistema)
    {
        Mic = mic;
        Sistema = sistema;
    }

    public static Faixas Ler(string caminhoMic, string caminhoSistema) =>
        new(LerWav(caminhoMic), LerWav(caminhoSistema));

    /// <summary>Escreve um WAV 16 kHz mono — o formato das duas faixas.</summary>
    public static void Escrever(string caminho, float[] amostras) =>
        EscreverWav(caminho, amostras);

    /// <summary>
    /// Soma as duas faixas num WAV só, para a transcrição enxergar a conversa
    /// inteira — sobreposições inclusive.
    /// </summary>
    /// <remarks>
    /// As faixas já saem alinhadas do gravador (ancoradas no relógio do
    /// dispositivo), então basta somar. Se a soma estoura, ela é <b>reduzida</b>
    /// e não normalizada: normalizar alteraria o equilíbrio relativo entre os
    /// canais, e é justamente esse equilíbrio que o <see cref="AtribuirDono"/>
    /// usa depois para saber quem falou.
    /// </remarks>
    public void EscreverMix(string destino) => EscreverWav(destino, Mix());

    /// <summary>A soma das duas faixas, como o ASR a recebe.</summary>
    public float[] Mix()
    {
        int n = Math.Max(Mic.Length, Sistema.Length);
        var mix = new float[n];
        for (int i = 0; i < n; i++)
            mix[i] = (i < Mic.Length ? Mic[i] : 0) + (i < Sistema.Length ? Sistema[i] : 0);

        float pico = 0;
        foreach (float v in mix) pico = Math.Max(pico, Math.Abs(v));
        if (pico > 1f)
            for (int i = 0; i < n; i++) mix[i] /= pico;

        return mix;
    }

    /// <summary>Energia média (RMS) de um trecho, em segundos.</summary>
    public static double Rms(float[] audio, double inicio, double fim)
    {
        int a = (int)(Math.Max(0, inicio) * TaxaDeAmostragem);
        int b = (int)(Math.Min(fim, audio.Length / (double)TaxaDeAmostragem) * TaxaDeAmostragem);
        if (b <= a) return 0;

        double soma = 0;
        for (int i = a; i < b; i++) soma += audio[i] * (double)audio[i];
        return Math.Sqrt(soma / (b - a));
    }

    private static float[] LerWav(string caminho)
    {
        using var fluxo = File.OpenRead(caminho);
        using var leitor = new BinaryReader(fluxo);

        if (new string(leitor.ReadChars(4)) != "RIFF")
            throw new InvalidDataException($"{Path.GetFileName(caminho)}: não é um WAV.");
        leitor.ReadInt32();
        if (new string(leitor.ReadChars(4)) != "WAVE")
            throw new InvalidDataException($"{Path.GetFileName(caminho)}: não é um WAV.");

        short canais = 0, bits = 0;
        int taxa = 0;

        // Percorrer os blocos em vez de assumir o cabeçalho canônico de 44
        // bytes: o gravador reescreve o header a cada 10 s para sobreviver a
        // crash, e um WAV com bloco extra continua sendo um WAV válido.
        while (fluxo.Position < fluxo.Length)
        {
            string id = new(leitor.ReadChars(4));
            int tamanho = leitor.ReadInt32();

            if (id == "fmt ")
            {
                leitor.ReadInt16();                 // formato
                canais = leitor.ReadInt16();
                taxa = leitor.ReadInt32();
                leitor.ReadInt32();                 // bytes por segundo
                leitor.ReadInt16();                 // alinhamento
                bits = leitor.ReadInt16();
                fluxo.Position += tamanho - 16;
            }
            else if (id == "data")
            {
                if (taxa != TaxaDeAmostragem || canais != 1 || bits != 16)
                    throw new InvalidDataException(
                        $"{Path.GetFileName(caminho)}: esperado 16 kHz mono 16 bits, "
                        + $"veio {taxa} Hz {canais}ch {bits} bits.");

                // O bloco pode declarar mais do que existe: um kill -9 durante a
                // gravação deixa exatamente isso, e é para ser recuperável.
                int disponivel = (int)Math.Min(tamanho, fluxo.Length - fluxo.Position);
                var amostras = new float[disponivel / 2];
                for (int i = 0; i < amostras.Length; i++)
                    amostras[i] = leitor.ReadInt16() / 32768f;
                return amostras;
            }
            else
            {
                fluxo.Position += tamanho + (tamanho % 2);   // blocos têm tamanho par
            }
        }
        throw new InvalidDataException($"{Path.GetFileName(caminho)}: sem bloco 'data'.");
    }

    private static void EscreverWav(string caminho, float[] amostras)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminho))!);
        using var fluxo = File.Create(caminho);
        using var escritor = new BinaryWriter(fluxo);

        int bytes = amostras.Length * 2;
        escritor.Write("RIFF"u8);
        escritor.Write(36 + bytes);
        escritor.Write("WAVEfmt "u8);
        escritor.Write(16);
        escritor.Write((short)1);                    // PCM
        escritor.Write((short)1);                    // mono
        escritor.Write(TaxaDeAmostragem);
        escritor.Write(TaxaDeAmostragem * 2);
        escritor.Write((short)2);
        escritor.Write((short)16);
        escritor.Write("data"u8);
        escritor.Write(bytes);

        // 32767 e não 32768: é o que o mix_tracks do Python usa, e a diferença
        // de um bit no fundo de escala mudaria a comparação byte a byte.
        foreach (float v in amostras)
            escritor.Write((short)(Math.Clamp(v, -1f, 1f) * 32767));
    }
}
