using System;
using System.IO;

namespace MeetingRecorder.Core;

/// <summary>
/// Escreve WAV PCM 16-bit mono cujo header é válido a qualquer instante.
/// </summary>
/// <remarks>
/// <para>
/// Requisito 3.2 da <c>FASE1.md</c>. O <c>wave</c> do Python — e o
/// <c>WaveFileWriter</c> do NAudio — só gravam os tamanhos no header quando o
/// arquivo é fechado. Um <c>kill -9</c>, um travamento do driver ou uma queda de
/// energia no meio de uma reunião de 40 minutos deixam um arquivo que nenhum
/// player abre, com o áudio inteiro presente e inacessível.
/// </para>
/// <para>
/// A correção é escrever o header a cada <see cref="IntervaloFlushPadrao"/>
/// com os tamanhos <em>até aquele momento</em>. O custo é dois seeks e 8 bytes
/// por flush; o ganho é que o pior caso deixa de ser "perdeu a reunião" e passa a
/// ser "perdeu os últimos segundos".
/// </para>
/// <para>
/// Critério de aceite B: matar o processo no meio de uma gravação de 10 min deixa
/// arquivos recuperáveis sem ferramenta externa.
/// </para>
/// </remarks>
public sealed class CrashSafeWavWriter : IDisposable
{
    public const int TaxaAlvo = 16_000;
    private const int BitsPorAmostra = 16;
    private const int Canais = 1;

    /// <summary>
    /// Com 10 s, o pior caso perde ~10 s de áudio e o custo de I/O é irrelevante
    /// numa gravação de dezenas de minutos.
    /// </summary>
    public static readonly TimeSpan IntervaloFlushPadrao = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _intervaloFlush;
    private readonly FileStream _fs;
    private readonly BinaryWriter _bw;
    private readonly object _trava = new();
    private long _bytesDados;
    private DateTime _ultimoFlush = DateTime.UtcNow;
    private bool _fechado;

    /// <summary>Amostras já gravadas em disco (não conta o que está em buffer do SO).</summary>
    public long AmostrasEscritas => _bytesDados / (BitsPorAmostra / 8);

    /// <param name="intervaloFlush">
    /// Injetável para os testes: com o padrão de 10 s, verificar o
    /// comportamento de flush exigiria dormir 10 s por caso.
    /// </param>
    public CrashSafeWavWriter(string caminho, TimeSpan? intervaloFlush = null)
    {
        _intervaloFlush = intervaloFlush ?? IntervaloFlushPadrao;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(caminho))!);
        _fs = new FileStream(caminho, FileMode.Create, FileAccess.Write, FileShare.Read);
        _bw = new BinaryWriter(_fs);
        EscreverHeader();
    }

    /// <summary>Grava amostras float normalizadas (-1..1), com clipping.</summary>
    public void Escrever(ReadOnlySpan<float> amostras)
    {
        if (amostras.IsEmpty) return;

        // Buffer local em vez de writer.Write por amostra: uma chamada de I/O por
        // bloco em vez de milhares.
        Span<byte> destino = amostras.Length <= 4096
            ? stackalloc byte[amostras.Length * 2]
            : new byte[amostras.Length * 2];

        for (int i = 0; i < amostras.Length; i++)
        {
            float v = amostras[i];
            // Clamp antes de converter: float fora de faixa faz wrap no cast e
            // um estouro leve viraria estalo de amplitude máxima.
            short s = v >= 1f ? short.MaxValue
                    : v <= -1f ? short.MinValue
                    : (short)(v * 32767f);
            destino[i * 2] = (byte)(s & 0xFF);
            destino[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }

        lock (_trava)
        {
            if (_fechado) throw new ObjectDisposedException(nameof(CrashSafeWavWriter));
            _fs.Write(destino);
            _bytesDados += destino.Length;

            if (DateTime.UtcNow - _ultimoFlush >= _intervaloFlush)
            {
                AtualizarTamanhos();
                _ultimoFlush = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Reescreve os dois campos de tamanho e força o buffer para o disco.
    /// </summary>
    /// <remarks>
    /// A ordem importa: gravar os tamanhos <em>antes</em> do
    /// <see cref="FileStream.Flush(bool)"/> garante que, se a máquina cair entre
    /// as duas operações, o header no disco descreve menos áudio do que existe —
    /// o arquivo abre e a cauda se perde. A ordem inversa poderia deixar um
    /// header prometendo áudio que não chegou ao disco, e aí o player lê lixo.
    /// </remarks>
    private void AtualizarTamanhos()
    {
        long posicao = _fs.Position;

        _fs.Seek(4, SeekOrigin.Begin);                 // RIFF chunk size
        _bw.Write((uint)(36 + _bytesDados));
        _fs.Seek(40, SeekOrigin.Begin);                // data chunk size
        _bw.Write((uint)_bytesDados);

        _fs.Seek(posicao, SeekOrigin.Begin);
        _bw.Flush();
        _fs.Flush(flushToDisk: true);
    }

    private void EscreverHeader()
    {
        int taxaBytes = TaxaAlvo * Canais * (BitsPorAmostra / 8);

        _bw.Write(new[] { 'R', 'I', 'F', 'F' });
        _bw.Write((uint)36);                           // ainda sem dados
        _bw.Write(new[] { 'W', 'A', 'V', 'E' });
        _bw.Write(new[] { 'f', 'm', 't', ' ' });
        _bw.Write((uint)16);                           // tamanho do bloco fmt
        _bw.Write((ushort)1);                          // PCM
        _bw.Write((ushort)Canais);
        _bw.Write((uint)TaxaAlvo);
        _bw.Write((uint)taxaBytes);
        _bw.Write((ushort)(Canais * (BitsPorAmostra / 8)));
        _bw.Write((ushort)BitsPorAmostra);
        _bw.Write(new[] { 'd', 'a', 't', 'a' });
        _bw.Write((uint)0);
        _bw.Flush();
    }

    public void Dispose()
    {
        lock (_trava)
        {
            if (_fechado) return;
            _fechado = true;
            AtualizarTamanhos();
            _bw.Dispose();
            _fs.Dispose();
        }
    }
}
