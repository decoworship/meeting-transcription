namespace MeetingApp.Nucleo;

/// <summary>
/// As notas que quem estava na reunião escreveu, em <c>notas.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Markdown puro ao lado de <c>mic.wav</c> e <c>meta.json</c>, pela mesma razão
/// dos outros arquivos da gravação: legível e editável fora do app, sem
/// precisar dele para recuperar o que se escreveu. Nada de campo novo no
/// <c>meta.json</c>, que é do gravador e tem schema congelado — ver
/// <see cref="DadosDaReuniao"/>, que fez a mesma escolha pelo mesmo motivo.
/// </para>
/// <para>
/// <b>Elas valem mais que a transcrição naquilo que dizem.</b> O que uma pessoa
/// escreveu durante a reunião é decisão registrada por quem estava lá; o que o
/// modelo ouviu é a melhor tentativa de uma máquina. Quando a ata por LLM
/// chegar (FASE3.md §4), as notas entram no prompt marcadas como tal.
/// </para>
/// </remarks>
public static class Notas
{
    public const string NomeDoArquivo = "notas.md";

    public static string Caminho(string pastaDaGravacao) =>
        Path.Combine(pastaDaGravacao, NomeDoArquivo);

    /// <summary>O que está escrito, ou vazio. Nunca lança.</summary>
    /// <remarks>
    /// Nunca lança porque a nota é acessório da gravação: um arquivo ilegível
    /// não pode impedir de abrir a reunião, ouvir o áudio ou transcrever.
    /// </remarks>
    public static string Ler(string pastaDaGravacao)
    {
        try
        {
            string caminho = Caminho(pastaDaGravacao);
            return File.Exists(caminho) ? File.ReadAllText(caminho) : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static bool Existem(string pastaDaGravacao)
    {
        try
        {
            string caminho = Caminho(pastaDaGravacao);
            return File.Exists(caminho) && new FileInfo(caminho).Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Grava o que foi escrito, ou apaga o arquivo quando não sobrou nada.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Escreve num arquivo temporário e move por cima. O salvamento é
    /// automático e acontece <b>enquanto a reunião é gravada</b>: uma queda de
    /// energia no meio de um <c>File.WriteAllText</c> deixaria um notas.md
    /// truncado no lugar do que já estava escrito, e nota de reunião não se
    /// refaz de memória meia hora depois.
    /// </para>
    /// <para>
    /// <c>Directory.CreateDirectory</c> antes: durante a gravação a pasta
    /// existe, mas quem escreve nota sobre uma gravação que o usuário acabou de
    /// apagar não deve derrubar a tela por causa disso.
    /// </para>
    /// </remarks>
    public static void Salvar(string pastaDaGravacao, string? texto)
    {
        string caminho = Caminho(pastaDaGravacao);

        if (texto is not { Length: > 0 } || texto.Trim().Length == 0)
        {
            if (File.Exists(caminho)) File.Delete(caminho);
            return;
        }

        Directory.CreateDirectory(pastaDaGravacao);
        string temporario = caminho + ".tmp";
        File.WriteAllText(temporario, texto);
        File.Move(temporario, caminho, overwrite: true);
    }

    /// <summary>
    /// Os termos que valem como vocabulário: o que a pessoa escreveu à mão.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nome próprio, sigla e nome de sistema escritos por quem estava na reunião
    /// são exatamente o que o ASR erra e o que a correção fonética conserta —
    /// e chegam aqui já com a grafia certa, que é o que a lista de vocabulário
    /// precisa ter.
    /// </para>
    /// <para>
    /// O critério é grosseiro de propósito: palavra com maiúscula no meio da
    /// frase, ou toda em maiúsculas. Errar para mais custa uma sugestão boba
    /// numa lista que o usuário revisa antes de usar; errar para menos custa o
    /// nome que ele queria. <b>Sugestão, nunca injeção automática</b> — quem
    /// confirma é a tela de preparo.
    /// </para>
    /// </remarks>
    public static List<string> TermosSugeridos(string texto)
    {
        var achados = new List<string>();
        if (texto is not { Length: > 0 }) return achados;

        foreach (var linha in texto.Split('\n'))
        {
            // O tempo marcado ([00:12:34]) e a marcação de lista não são termos.
            string limpa = linha.TrimStart('#', '-', '*', ' ', '\t');
            var palavras = limpa.Split([' ', '\t', ',', ';', '(', ')', '[', ']', '"'],
                                       StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < palavras.Length; i++)
            {
                string p = palavras[i].Trim('.', ':', '!', '?', '…');
                if (p.Length < 3 || p.Length > 40) continue;
                if (!char.IsLetter(p[0])) continue;

                // Sigla inteira em maiúsculas, ou palavra capitalizada que não
                // está abrindo a frase — abrir frase com maiúscula é gramática,
                // não nome próprio.
                bool sigla = p.All(c => !char.IsLetter(c) || char.IsUpper(c));
                bool nomeNoMeio = i > 0 && char.IsUpper(p[0]);

                if ((sigla || nomeNoMeio) && !achados.Contains(p)) achados.Add(p);
            }
        }
        return achados;
    }
}
