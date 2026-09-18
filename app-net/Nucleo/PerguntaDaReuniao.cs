using System.Text;
using MeetingApp.Nucleo.Atas;

namespace MeetingApp.Nucleo;

/// <summary>O que o modelo vai ler, e se ele está vendo a reunião inteira.</summary>
/// <param name="Cortado">
/// <c>true</c> quando o começo não coube no contexto. A tela diz isso a quem
/// perguntou, e o prompt diz ao modelo.
/// </param>
public sealed record TextoDaReuniao(string Texto, bool Cortado);

/// <summary>
/// Perguntar ao modelo o que já aconteceu, durante a própria reunião.
/// </summary>
/// <remarks>
/// <para>
/// <b>É a camada de cima das três</b> (docs/FASE7-ROTA.md §3): ela não transcreve
/// nada — lê o que a <see cref="LegendaAoVivo"/> ou a <see cref="SessaoAoVivo"/>
/// já produziram e pergunta ao <see cref="MotorDeAta"/>.
/// </para>
/// <para>
/// <b>O motor sobe por pergunta e morre depois dela.</b> Numa placa de 6 GB, um
/// 4B quente a reunião inteira é o terceiro contexto CUDA que derrubou a legenda
/// de 2,46× para 0,45× em 11/09/2026 — e legenda abaixo de 1× atrasa sem parar.
/// Custa 6 a 9 segundos por pergunta, e é o preço de a reunião continuar sendo
/// transcrita enquanto se pergunta sobre ela.
/// </para>
/// <para>
/// <b>Uma pergunta por vez</b>, e é por isso que esta é uma instância com estado
/// em vez de um punhado de funções: duas subidas simultâneas são dois modelos na
/// placa durante uma reunião que está sendo gravada.
/// </para>
/// </remarks>
/// <param name="aoPerguntar">
/// Quem leva o prompt ao modelo. É injetado para esta classe poder ser testada
/// sem placa — o <c>MotorDeAta</c> é processo, HTTP e 2,5 GB de GGUF.
/// </param>
public sealed class PerguntaDaReuniao(Func<string, CancellationToken, Task<string>> aoPerguntar)
{
    /// <summary>O rótulo do dono, que vem da faixa do microfone.</summary>
    /// <remarks>
    /// O mesmo <c>"You"</c> que a <c>Montagem.AtribuirDono</c> escreve e que o
    /// <c>aovivo.js</c> conhece. Aqui ele vira "Você", que é como o modelo lê.
    /// </remarks>
    public const string RotuloDoDono = "You";

    /// <summary>Quanto o modelo pode escrever de resposta.</summary>
    /// <remarks>
    /// Bem menos que os 8.192 da ata, e de propósito: ata é documento, isto é
    /// resposta para quem está no meio de uma reunião e vai ler de relance.
    /// </remarks>
    public const int TokensDeSaida = 1024;

    /// <summary>Contexto a supor quando o GGUF não declara o dele.</summary>
    private const int ContextoQuandoNaoSeSabe = 16_384;

    /// <summary>
    /// Quanta reunião o modelo lê, no máximo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Decisão do dono do produto em 16/09/2026</b>, e ela tem dois lados que
    /// caem no mesmo lugar. O de produto: <i>"se passar disso, o início da
    /// reunião pode ser que não seja tão relevante para ser resumido"</i>. O de
    /// máquina: sem esta janela, uma reunião de duas horas pede ~32k de contexto,
    /// e aí <b>não cabe na placa ao lado da legenda</b> — medido em 16/09, o
    /// <c>ministral-3-3b</c> sobe para 4.183 MiB e o <c>qwen3.5-4b</c> para
    /// 3.436, contra ~3.553 MiB livres numa reunião de verdade
    /// (docs/ESTUDO-RESUMO-AO-VIVO.md §10).
    /// </para>
    /// <para>
    /// <b>É teto, e não piso:</b> num modelo que não comporta uma hora de fala,
    /// quem corta continua sendo o contexto dele.
    /// </para>
    /// </remarks>
    public const int JanelaMaximaMinutos = 60;

    /// <summary>
    /// Caracteres de transcrição por minuto de reunião.
    /// </summary>
    /// <remarks>
    /// <b>Medido em três reuniões do acervo</b>, no formato em turnos: 820, 617
    /// e 619 caracteres por minuto. O valor usado é o <b>maior</b>, e a escolha
    /// é deliberada — superestimar encurta a janela, que é o lado seguro;
    /// subestimar a alonga, e o custo disso é a placa estourar durante uma
    /// reunião que está sendo gravada.
    /// <para>
    /// <b>Por que caracteres e não relógio.</b> A legenda ao vivo não carimba
    /// turno, então "a última hora" não existe como corte de tempo — só como
    /// tamanho de texto. Quando o turno ganhar carimbo, isto vira minuto de
    /// verdade e esta constante some.
    /// </para>
    /// </remarks>
    public const int CaracteresPorMinuto = 800;

    /// <summary>A janela em caracteres, que é como ela é aplicada.</summary>
    public const int JanelaMaximaCaracteres = JanelaMaximaMinutos * CaracteresPorMinuto;

    /// <summary>O que impede a pergunta de acontecer, ou <c>null</c>.</summary>
    /// <remarks>
    /// Perguntado <b>antes</b> de montar o prompt, para o motivo aparecer na
    /// tela em vez de a caixa simplesmente não responder.
    /// </remarks>
    public static string? OQueImpede(ConfiguracoesDoApp config, CaminhosDoMotorDeAta caminhos)
    {
        if (!config.PerguntarAoVivo)
            return "perguntar durante a reunião está desligado em Ajustes › Transcrição.";

        return caminhos.OQueFalta();
    }

    /// <summary>Folga para a pergunta e a moldura, em tokens.</summary>
    private const int FolgaDaInstrucao = 512;

    /// <summary>
    /// Quanta transcrição cabe no prompt deste modelo.
    /// </summary>
    /// <remarks>
    /// <b>Dois tetos, e vence o menor.</b> Um é o contexto do modelo menos o que
    /// vamos escrever; o outro é a <see cref="JanelaMaximaCaracteres"/>, que na
    /// prática é quem manda em todo modelo de contexto grande. O
    /// <see cref="MotorDeAta.Dimensionar"/> recusa **depois** de o modelo ter
    /// carregado, e uma pergunta que falhasse assim custaria nove segundos de
    /// espera para terminar em erro, numa reunião acontecendo.
    /// <para>
    /// A constante de 2,5 caracteres por token erra por ~1,5× para o lado
    /// seguro: medido em 16/09/2026, o formato em turnos dá 3,3 a 3,8.
    /// </para>
    /// </remarks>
    public static int LimiteDeCaracteres(MetadadosDoGguf modelo)
    {
        int contexto = modelo.ContextoMaximo > 0 ? modelo.ContextoMaximo : ContextoQuandoNaoSeSabe;
        // A reserva é a NOSSA saída, e não os 8.192 da ata: o Dimensionar passou
        // a aceitar o orçamento de quem chama, e eram sete mil tokens de
        // contexto pedidos à toa.
        int paraOTexto = contexto - TokensDeSaida - FolgaDaInstrucao;
        int cabeNoModelo = Math.Max(0, (int)(paraOTexto * MotorDeAta.CaracteresPorToken));

        return Math.Min(JanelaMaximaCaracteres, cabeNoModelo);
    }

    private int _ocupado;

    /// <summary>Há uma pergunta em voo agora.</summary>
    public bool Ocupado => Volatile.Read(ref _ocupado) == 1;

    /// <summary>A transcrição vista pela legenda ao vivo.</summary>
    public static TextoDaReuniao DaLegenda(IReadOnlyList<TurnoDaLegenda> turnos, int limiteDeCaracteres) =>
        Montar(
            turnos.Select(t => Linha(t.Dono ? "Você" : "Outra pessoa", t.Texto)),
            limiteDeCaracteres);

    /// <summary>A transcrição vista pela prévia em blocos.</summary>
    /// <remarks>
    /// O rótulo do bloco vale mais que "outra pessoa" — é falante local, mas
    /// distingue duas pessoas na mesma resposta. Ele é local ao bloco, e quem o
    /// transforma em pessoa é a <c>CosturaDeFalantes</c>, depois.
    /// </remarks>
    public static TextoDaReuniao DosBlocos(IReadOnlyList<BlocoAoVivo> blocos, int limiteDeCaracteres) =>
        Montar(
            blocos.SelectMany(b => b.Trechos).Select(t => Linha(
                t.Speaker switch
                {
                    RotuloDoDono => "Você",
                    null or "" => "Outra pessoa",
                    var outro => outro,
                },
                t.Text)),
            limiteDeCaracteres);

    /// <summary>
    /// Qual instrução vale: a do botão, ou a da caixa livre.
    /// </summary>
    /// <remarks>
    /// <b>Prender toda pergunta ao formato de lista faria "quem ficou de mandar
    /// o material?" devolver uma lista de assuntos.</b> O botão tem forma; a
    /// caixa livre tem só as regras. Ver <see cref="PromptDeReuniao"/>.
    /// </remarks>
    public static string Instrucao(bool resumo) =>
        resumo ? PromptDeReuniao.Resumo : PromptDeReuniao.Livre;

    /// <summary>
    /// O prompt inteiro: a transcrição, e a pergunta no fim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Não há instrução aqui, e é decisão do dono do produto (16/09/2026):</b>
    /// <i>"para esse ponto não vamos ter prompt nem esquema, por enquanto é livre
    /// para perguntar sobre a reunião"</i>. Que instrução dar é o que a
    /// <c>tools/comparar_modelos_de_pergunta.py</c> existe para descobrir.
    /// </para>
    /// <para>
    /// <b>O que sobra não é instrução, é entrega:</b> a moldura que separa a
    /// transcrição da pergunta, e o aviso de corte — que é fato sobre o que o
    /// modelo está vendo, e sem ele ele afirma como a reunião começou olhando
    /// para o meio dela.
    /// </para>
    /// <para>
    /// <b>A pergunta vai por último</b>, depois da transcrição, porque o que fica
    /// perto do fim do prompt é o que mais pesa na geração. É a mesma ordem do
    /// <see cref="PromptDeAta"/>, e pela mesma razão.
    /// </para>
    /// </remarks>
    public static string Montar(TextoDaReuniao texto, string pergunta)
    {
        var sb = new StringBuilder();
        if (texto.Cortado)
            sb.AppendLine(
                "(Atenção: o que segue é apenas a parte final desta reunião — "
                + "o começo não coube.)").AppendLine();
        sb.AppendLine("=== TRANSCRIÇÃO DA REUNIÃO ATÉ AGORA ===");
        sb.AppendLine(texto.Texto);
        sb.AppendLine("=== FIM DA TRANSCRIÇÃO ===");
        sb.AppendLine();
        sb.Append(pergunta);
        return sb.ToString();
    }

    /// <summary>Pergunta, e devolve o que o modelo respondeu.</summary>
    /// <exception cref="InvalidOperationException">Já há uma pergunta em voo.</exception>
    public async Task<string> ResponderAsync(
        string pergunta, TextoDaReuniao texto, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _ocupado, 1) == 1)
            throw new InvalidOperationException(
                "o modelo responde uma pergunta por vez — espere a anterior terminar.");

        try
        {
            return await aoPerguntar(Montar(texto, pergunta), ct);
        }
        finally
        {
            // Sem isto, uma falha do motor — e ele falha, por VRAM — deixaria o
            // botão morto pelo resto da reunião.
            Volatile.Write(ref _ocupado, 0);
        }
    }

    private static string Linha(string quem, string texto) => $"{quem}: {texto.Trim()}";

    /// <summary>
    /// As linhas viram texto, de trás para frente, até o limite.
    /// </summary>
    /// <remarks>
    /// <b>O começo é que cai.</b> "O que aconteceu até agora" quer o fim — é o
    /// que ainda está na cabeça de quem pergunta, e é o que a reunião vai tratar
    /// a seguir.
    /// </remarks>
    private static TextoDaReuniao Montar(IEnumerable<string> linhas, int limiteDeCaracteres)
    {
        var todas = linhas.Where(l => l.Length > 0).ToList();

        var cabem = new List<string>();
        int usado = 0;
        for (int i = todas.Count - 1; i >= 0; i--)
        {
            int custo = todas[i].Length + (cabem.Count > 0 ? 1 : 0);
            if (usado + custo > limiteDeCaracteres) break;
            usado += custo;
            cabem.Add(todas[i]);
        }
        cabem.Reverse();

        // Nenhuma linha inteira coube: é uma pessoa falando sem parar, e a
        // legenda junta isso num turno só. Devolver vazio seria o pior desfecho
        // — o fim da fala é melhor que nada.
        if (cabem.Count == 0)
            return todas.Count == 0
                ? new TextoDaReuniao("", false)
                : new TextoDaReuniao(todas[^1][^limiteDeCaracteres..], true);

        return new TextoDaReuniao(string.Join('\n', cabem), cabem.Count < todas.Count);
    }
}
