using MeetingApp.Sidecar;

namespace MeetingApp.Nucleo;

/// <summary>
/// Lê os nomes de um print: sobe o motor de OCR, pede as linhas, derruba o
/// motor e entrega as linhas ao <see cref="NomesDoPrint"/>.
/// </summary>
/// <remarks>
/// O motor não fica quente. Ler nomes acontece uma vez por reunião, e um
/// Python com o OpenCV e dois modelos carregados, parado na memória o dia
/// inteiro por causa disso, é o custo errado — os ~3 s de subida são
/// pagos por quem clicou, e só por ele.
/// </remarks>
public static class LeitorDeNomes
{
    /// <summary>Por que não dá para ler nesta instalação, ou nulo se dá.</summary>
    public static string? OQueImpede(Motores motores)
    {
        if (motores.ScriptOcr is not { Length: > 0 } script || !File.Exists(script))
            return "o leitor de nomes não veio nesta versão dos motores";
        if (!File.Exists(motores.Python))
            return "o Python dos motores não está instalado";
        return null;
    }

    public static async Task<NomesDoPrint.Leitura> LerAsync(
        Motores motores, string imagem, IReadOnlyList<string> convidados, CancellationToken ct = default)
    {
        if (OQueImpede(motores) is { } porque) throw new MotorException(porque);

        using var motor = await MotorSidecar.IniciarAsync(
            motores.Python, [motores.ScriptOcr], ct, Motores.Ambiente());
        motor.AoRegistrar += linha => Registro.Escrever("ocr", linha);

        IReadOnlyList<LinhaLida> linhas;
        try
        {
            linhas = await motor.LerTextoAsync(imagem, ct);
        }
        catch (MotorException e) when (e.Message.Contains("ModuleNotFoundError"))
        {
            // O motor.py chegou pelo publicar.sh, mas o Python embarcado é de
            // antes do RapidOCR: o empacotar_motores.sh ainda não passou.
            throw new MotorException(
                "o leitor de nomes precisa dos motores atualizados (falta o RapidOCR no Python embarcado)");
        }
        Registro.Escrever("ocr", $"{linhas.Count} linhas lidas de {Path.GetFileName(imagem)}");
        return NomesDoPrint.Ler(linhas, convidados);
    }
}
