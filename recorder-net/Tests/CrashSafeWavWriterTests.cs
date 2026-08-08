using System.Text;
using MeetingRecorder.Core;
using Xunit;

namespace MeetingRecorder.Tests;

/// <summary>
/// Critério de aceite B da FASE1: matar o processo no meio de uma gravação
/// deixa arquivos recuperáveis sem ferramenta externa.
/// </summary>
/// <remarks>
/// Estes testes não matam processo de verdade — leem o arquivo <b>enquanto ele
/// está aberto</b>, que é a mesma situação em que o disco fica após um kill: os
/// dados escritos até o último flush, e o header como estava naquele momento.
/// </remarks>
public sealed class CrashSafeWavWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "mrtest-" + Guid.NewGuid().ToString("N")[..8]);

    public CrashSafeWavWriterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Caminho(string nome) => Path.Combine(_dir, nome);

    /// <summary>Lê os campos de tamanho do header direto dos bytes.</summary>
    private static (uint riff, uint dados, long bytesReais) LerHeader(string caminho)
    {
        // FileShare.ReadWrite: o writer ainda está com o arquivo aberto, que é
        // exatamente o cenário sob teste.
        using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read,
                                      FileShare.ReadWrite);
        using var br = new BinaryReader(fs);
        var riffTag = Encoding.ASCII.GetString(br.ReadBytes(4));
        Assert.Equal("RIFF", riffTag);
        uint riff = br.ReadUInt32();
        fs.Seek(40, SeekOrigin.Begin);
        uint dados = br.ReadUInt32();
        return (riff, dados, fs.Length);
    }

    [Fact]
    public void HeaderValidoAntesDeQualquerAmostra()
    {
        string p = Caminho("vazio.wav");
        using (var _ = new CrashSafeWavWriter(p)) { }

        var (riff, dados, tamanho) = LerHeader(p);
        Assert.Equal(44, tamanho);
        Assert.Equal(36u, riff);
        Assert.Equal(0u, dados);
    }

    [Fact]
    public void HeaderDescreveOsDadosDepoisDoFlushPeriodico()
    {
        string p = Caminho("flush.wav");
        var amostras = new float[CrashSafeWavWriter.TaxaAlvo];   // 1 s

        var rapido = TimeSpan.FromMilliseconds(50);
        using var w = new CrashSafeWavWriter(p, rapido);
        w.Escrever(amostras);

        // Antes do intervalo de flush o header ainda diz zero — e é justamente
        // por isso que ele precisa ser periódico e não só no close.
        var antes = LerHeader(p);
        Assert.Equal(0u, antes.dados);

        // Força o flush avançando além do intervalo.
        Thread.Sleep(rapido + TimeSpan.FromMilliseconds(50));
        w.Escrever(amostras);

        var (riff, dados, _) = LerHeader(p);
        Assert.True(dados > 0, "o header deveria descrever os dados após o flush");
        Assert.Equal(dados + 36, riff);
    }

    [Fact]
    public void ArquivoAbandonadoSemCloseContinuaLegivel()
    {
        string p = Caminho("abandonado.wav");
        var amostras = new float[CrashSafeWavWriter.TaxaAlvo];

        // Escopo sem `using`: simula o processo morrendo sem chamar Dispose.
        var rapido = TimeSpan.FromMilliseconds(50);
        var w = new CrashSafeWavWriter(p, rapido);
        w.Escrever(amostras);
        Thread.Sleep(rapido + TimeSpan.FromMilliseconds(50));
        w.Escrever(amostras);

        var (riff, dados, tamanho) = LerHeader(p);
        Assert.True(dados >= CrashSafeWavWriter.TaxaAlvo * 2,
            $"esperado ao menos 1 s de áudio no header, veio {dados} bytes");
        Assert.Equal(dados + 36, riff);
        Assert.True(tamanho >= dados + 44);

        w.Dispose();
    }

    [Theory]
    [InlineData(2f, short.MaxValue)]
    [InlineData(-2f, short.MinValue)]
    [InlineData(1f, short.MaxValue)]
    [InlineData(-1f, short.MinValue)]
    public void ClipaEmVezDeEstourar(float entrada, short esperado)
    {
        // Sem o clamp, o cast de float fora de faixa faz wrap e um estouro leve
        // vira estalo de amplitude máxima com sinal invertido.
        string p = Caminho($"clip{entrada}.wav");
        using (var w = new CrashSafeWavWriter(p)) w.Escrever(new[] { entrada });

        using var fs = new FileStream(p, FileMode.Open, FileAccess.Read);
        fs.Seek(44, SeekOrigin.Begin);
        using var br = new BinaryReader(fs);
        Assert.Equal(esperado, br.ReadInt16());
    }

    [Fact]
    public void ContaAmostrasEscritas()
    {
        string p = Caminho("contagem.wav");
        using var w = new CrashSafeWavWriter(p);
        w.Escrever(new float[1000]);
        w.Escrever(new float[500]);
        Assert.Equal(1500, w.AmostrasEscritas);
    }
}
