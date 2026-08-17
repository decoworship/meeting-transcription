using System.Buffers.Binary;
using System.Text;

namespace MeetingApp.Nucleo.Atas;

/// <summary>
/// O que o arquivo <c>.gguf</c> diz sobre si mesmo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que ler o modelo em vez de tabelar.</b> O tamanho do cache KV — que é
/// o que decide se uma reunião cabe na placa — sai da geometria do modelo:
/// camadas × cabeças de KV × dimensão. Escrever esses números numa tabela ao
/// lado de cada modelo funciona até alguém acrescentar um modelo e errar um
/// número; aí o app dimensiona errado e a falha aparece como um 400 do
/// <c>llama-server</c> no meio de uma ata.
/// </para>
/// <para>
/// Ler do arquivo não tem como ficar desatualizado, e é o que faz o
/// dimensionamento valer para um modelo que ainda não existe aqui — que é
/// exatamente o caso quando se está avaliando trocar de modelo.
/// </para>
/// <para>
/// Só o cabeçalho é lido: as chaves vêm antes dos tensores, então nada de
/// gigabyte é tocado.
/// </para>
/// </remarks>
public sealed record MetadadosDoGguf
{
    public required string Arquitetura { get; init; }
    public required string Nome { get; init; }

    /// <summary>O contexto máximo com que o modelo foi treinado.</summary>
    public required int ContextoMaximo { get; init; }

    public required int Camadas { get; init; }

    /// <summary>Cabeças de KV — em GQA são menos que as de atenção.</summary>
    public required int CabecasDeKv { get; init; }

    public required int DimensaoDaChave { get; init; }
    public required int DimensaoDoValor { get; init; }

    /// <summary>O tamanho do arquivo, que é quanto o modelo ocupa na placa.</summary>
    public required long BytesDoArquivo { get; init; }

    /// <summary>
    /// Bytes de cache por token, para uma quantização de K e outra de V.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>camadas × cabeças_kv × dimensão</c> valores para K e outro tanto para
    /// V, cada um no seu tipo. É a conta que decide tudo: no Qwen3-4B dá
    /// <b>76,5 KiB por token em q8_0</b> e <b>40,5 KiB em q4_0</b> — ou seja, o
    /// cache de uma reunião de uma hora pesa mais que o próprio modelo.
    /// </para>
    /// </remarks>
    public long BytesDeCachePorToken(string tipoK, string tipoV)
    {
        long k = (long)Camadas * CabecasDeKv * DimensaoDaChave;
        long v = (long)Camadas * CabecasDeKv * DimensaoDoValor;
        return (long)(k * BytesPorValor(tipoK)) + (long)(v * BytesPorValor(tipoV));
    }

    /// <summary>Quanto ocupa um valor do cache, por tipo do llama.cpp.</summary>
    /// <remarks>
    /// Os blocos quantizados carregam a escala junto: q8_0 são 34 bytes por 32
    /// valores, e não 32. Ignorar isso subestima o cache em 6%, que é
    /// exatamente o tipo de erro que só aparece quando a placa enche.
    /// </remarks>
    public static double BytesPorValor(string tipo) => tipo switch
    {
        "f32" => 4.0,
        "f16" or "bf16" => 2.0,
        "q8_0" => 34.0 / 32.0,
        "q5_1" => 24.0 / 32.0,
        "q5_0" => 22.0 / 32.0,
        "q4_1" => 20.0 / 32.0,
        "q4_0" => 18.0 / 32.0,
        _ => 2.0,
    };

    public static MetadadosDoGguf Ler(string caminho)
    {
        using var fluxo = File.OpenRead(caminho);
        using var leitor = new BinaryReader(fluxo, Encoding.UTF8, leaveOpen: true);

        if (leitor.ReadUInt32() != 0x46554747u)  // "GGUF" em little-endian
            throw new InvalidDataException($"{caminho} não é um arquivo GGUF");

        leitor.ReadUInt32();                      // versão
        leitor.ReadUInt64();                      // número de tensores
        ulong chaves = leitor.ReadUInt64();

        var lidas = new Dictionary<string, object>();
        for (ulong i = 0; i < chaves; i++)
        {
            string nome = LerTexto(leitor);
            uint tipo = leitor.ReadUInt32();
            object? valor = LerValor(leitor, tipo);
            if (valor is not null) lidas[nome] = valor;
        }

        string arq = lidas.TryGetValue("general.architecture", out var a) ? (string)a : "?";

        int Inteiro(string sufixo, int padrao = 0) =>
            lidas.TryGetValue($"{arq}.{sufixo}", out var v) ? Convert.ToInt32(v) : padrao;

        int camadas = Inteiro("block_count");
        int cabecasKv = Inteiro("attention.head_count_kv");
        int embedding = Inteiro("embedding_length");
        int cabecas = Inteiro("attention.head_count");

        // key_length e value_length são opcionais: quando faltam, a dimensão por
        // cabeça é embedding ÷ cabeças de atenção, que é o caso comum.
        int dimK = Inteiro("attention.key_length",
                           cabecas > 0 ? embedding / cabecas : 0);
        int dimV = Inteiro("attention.value_length",
                           cabecas > 0 ? embedding / cabecas : 0);

        return new MetadadosDoGguf
        {
            Arquitetura = arq,
            Nome = lidas.TryGetValue("general.name", out var n) ? (string)n : arq,
            ContextoMaximo = Inteiro("context_length"),
            Camadas = camadas,
            CabecasDeKv = cabecasKv > 0 ? cabecasKv : cabecas,
            DimensaoDaChave = dimK,
            DimensaoDoValor = dimV,
            BytesDoArquivo = new FileInfo(caminho).Length,
        };
    }

    private static string LerTexto(BinaryReader leitor)
    {
        ulong n = leitor.ReadUInt64();
        return Encoding.UTF8.GetString(leitor.ReadBytes((int)n));
    }

    /// <summary>
    /// Lê um valor, ou pula o que não interessa.
    /// </summary>
    /// <remarks>
    /// Os arrays são pulados e não lidos: o vocabulário do tokenizador é um
    /// array de centenas de milhares de strings, e materializá-lo para ler seis
    /// inteiros seria trocar um cabeçalho por 300 MB de memória.
    /// </remarks>
    private static object? LerValor(BinaryReader leitor, uint tipo)
    {
        switch (tipo)
        {
            case 0: return (int)leitor.ReadByte();
            case 1: return (int)leitor.ReadSByte();
            case 2: return (int)leitor.ReadUInt16();
            case 3: return (int)leitor.ReadInt16();
            case 4: return (long)leitor.ReadUInt32();
            case 5: return (long)leitor.ReadInt32();
            case 6: return leitor.ReadSingle();
            case 7: return leitor.ReadBoolean();
            case 8: return LerTexto(leitor);
            case 10: return (long)leitor.ReadUInt64();
            case 11: return leitor.ReadInt64();
            case 12: return leitor.ReadDouble();
            case 9:
                uint interno = leitor.ReadUInt32();
                ulong quantos = leitor.ReadUInt64();
                for (ulong i = 0; i < quantos; i++) LerValor(leitor, interno);
                return null;
            default:
                throw new InvalidDataException($"tipo de metadado desconhecido no GGUF: {tipo}");
        }
    }
}
