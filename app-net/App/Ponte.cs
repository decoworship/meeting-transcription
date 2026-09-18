using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingApp.App.Nativo;
using MeetingApp.Nucleo;
using MeetingApp.Nucleo.Atas;
using MeetingApp.Sidecar;

namespace MeetingApp.App;

/// <summary>
/// O que a página pede ao núcleo, e o que o núcleo devolve.
/// </summary>
/// <remarks>
/// Mesma forma do contrato com os motores (docs/SIDECAR.md): JSON com um campo
/// <c>op</c> e um <c>id</c> que volta na resposta. Ter um só formato de
/// mensagem no projeto inteiro significa um só jeito de depurar quando algo não
/// chega do outro lado.
/// </remarks>
internal sealed class Pedido
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("op")] public string? Op { get; init; }
    [JsonPropertyName("gravacao")] public string? Gravacao { get; init; }
    [JsonPropertyName("vocabulario")] public string? Vocabulario { get; init; }
    [JsonPropertyName("idioma")] public string? Idioma { get; init; }
    [JsonPropertyName("modelo")] public string? Modelo { get; init; }
    [JsonPropertyName("cliente")] public string? Cliente { get; init; }
    [JsonPropertyName("projeto")] public string? Projeto { get; init; }
    [JsonPropertyName("data")] public string? Data { get; init; }

    /// <summary>Separar quem falou. Ausente equivale a sim.</summary>
    [JsonPropertyName("diarizar")] public bool? Diarizar { get; init; }

    /// <summary>
    /// Qual pipeline separa os falantes. Ausente usa o padrão do app.
    /// </summary>
    /// <remarks>
    /// Vinha das preferências do projeto e <b>não saía delas</b>: era colhido na
    /// tela, salvo em disco e ignorado até 20/08/2026. Ver FASE6 §4.6.
    /// </remarks>
    [JsonPropertyName("diar_model")] public string? DiarModel { get; init; }
    [JsonPropertyName("prefs")] public PreferenciasDoProjeto? Prefs { get; init; }

    /// <summary>Rótulo do falante e o nome dado a ele, para aprender a voz.</summary>
    [JsonPropertyName("falante")] public string? Falante { get; init; }
    [JsonPropertyName("nome")] public string? Nome { get; init; }

    /// <summary>"txt", "srt", "vtt" ou "docx".</summary>
    [JsonPropertyName("formato")] public string? Formato { get; init; }
    [JsonPropertyName("com_falantes")] public bool? ComFalantes { get; init; }

    /// <summary>Também salvar uma cópia numa pasta escolhida pelo usuário.</summary>
    [JsonPropertyName("copiar")] public bool? Copiar { get; init; }

    [JsonPropertyName("config")] public ConfiguracoesDoApp? Config { get; init; }

    /// <summary>A transcrição inteira, como a página a tem depois de editada.</summary>
    [JsonPropertyName("conteudo")] public string? Conteudo { get; init; }

    /// <summary>Qual pessoa e qual amostra dela, na tela de vozes.</summary>
    [JsonPropertyName("pessoa")] public string? Pessoa { get; init; }
    [JsonPropertyName("indice")] public int? Indice { get; init; }

    /// <summary>De onde o diálogo de pasta começa.</summary>
    [JsonPropertyName("pasta")] public string? Pasta { get; init; }

    /// <summary>"mic" ou "loopback", nas operações do gravador.</summary>
    [JsonPropertyName("faixa")] public string? Faixa { get; init; }

    /// <summary>O id WASAPI escolhido, ou nulo para o padrão do Windows.</summary>
    [JsonPropertyName("dispositivo")] public string? Dispositivo { get; init; }

    /// <summary>Liga/desliga, nas opções do gravador.</summary>
    [JsonPropertyName("ligado")] public bool? Ligado { get; init; }

    /// <summary>O id do evento da agenda a fixar; vazio solta a escolha.</summary>
    [JsonPropertyName("evento")] public string? Evento { get; init; }

    /// <summary>O que se quer saber da reunião em curso, em português.</summary>
    [JsonPropertyName("pergunta")] public string? Pergunta { get; init; }

    /// <summary>
    /// <c>true</c> no botão de resumo, que tem forma; ausente na caixa livre.
    /// </summary>
    [JsonPropertyName("resumo")] public bool? Resumo { get; init; }
}

internal sealed class Resposta
{
    [JsonPropertyName("id")] public int Id { get; init; }

    /// <summary>
    /// <c>"progresso"</c> numa mensagem intermediária; ausente na final.
    /// </summary>
    /// <remarks>
    /// É o que permite uma operação longa reportar andamento sem inventar um
    /// segundo canal: a página só resolve a promessa quando o tipo não vem.
    /// </remarks>
    [JsonPropertyName("tipo")] public string? Tipo { get; init; }
    [JsonPropertyName("etapa")] public string? Etapa { get; init; }
    [JsonPropertyName("fracao")] public double? Fracao { get; init; }
    [JsonPropertyName("texto")] public string? Texto { get; init; }

    /// <summary>Um bloco da prévia ao vivo. Só no evento <c>id: 0</c>.</summary>
    [JsonPropertyName("aovivo")] public BlocoDaPrevia? AoVivo { get; init; }

    /// <summary>Um pedaço da legenda ao vivo. Só no evento <c>id: 0</c>.</summary>
    [JsonPropertyName("legenda")] public PedacoDaLegendaJson? Legenda { get; init; }

    /// <summary>O que a legenda deixou numa gravação já encerrada.</summary>
    [JsonPropertyName("legenda_gravada")] public List<TurnoJson>? LegendaGravada { get; init; }

    /// <summary>
    /// A diarização já passou por esta legenda. <c>false</c> = ainda vem.
    /// </summary>
    /// <remarks>
    /// Nulo quando não há legenda, e aí a tela não afirma nada. É o que separa
    /// "ficou sem falante" de "ainda está separando".
    /// </remarks>
    [JsonPropertyName("legenda_falantes")] public bool? LegendaFalantes { get; init; }

    /// <summary>O que impede a prévia, ou nulo quando ela pode acontecer.</summary>
    [JsonPropertyName("aovivo_impedimento")] public string? AoVivoImpedimento { get; init; }

    /// <summary>Qual dos dois modos ao vivo está ligado: legenda, bloco ou nada.</summary>
    [JsonPropertyName("aovivo_modo")] public string? AoVivoModo { get; init; }

    /// <summary>Os blocos já entregues, para a tela que chegou no meio.</summary>
    [JsonPropertyName("aovivo_ate")] public List<BlocoDaPrevia>? AoVivoAte { get; init; }

    /// <summary>
    /// O que impede a caixa de perguntar, ou nulo quando ela pode existir.
    /// </summary>
    /// <remarks>
    /// A tela pergunta ao montar. Sem isto a caixa apareceria ligada com a chave
    /// desligada, e o erro só viria depois de a pessoa escrever a pergunta.
    /// </remarks>
    [JsonPropertyName("perguntar_impedimento")] public string? PerguntarImpedimento { get; init; }

    /// <summary>O que o modelo respondeu sobre a reunião em curso.</summary>
    [JsonPropertyName("resposta")] public string? RespostaDoModelo { get; init; }

    /// <summary>
    /// O modelo só viu a parte final da reunião — o começo não coube.
    /// </summary>
    /// <remarks>
    /// Vai para a tela, e não só para o prompt: quem perguntou tem direito de
    /// saber que a resposta não considerou a reunião inteira.
    /// </remarks>
    [JsonPropertyName("cortado")] public bool? Cortado { get; init; }

    [JsonPropertyName("erro")] public string? Erro { get; init; }
    [JsonPropertyName("gravacoes")] public List<GravacaoResumo>? Gravacoes { get; init; }
    [JsonPropertyName("transcricao")] public string? Transcricao { get; init; }

    /// <summary>Onde o arquivo exportado foi parar.</summary>
    [JsonPropertyName("arquivo")] public string? Arquivo { get; init; }

    /// <summary>A cópia, quando pedida.</summary>
    [JsonPropertyName("copia")] public string? Copia { get; init; }

    [JsonPropertyName("config")] public ConfiguracoesDoApp? Config { get; init; }

    /// <summary>O que aconteceu ao aprender uma voz, para a UI poder dizer.</summary>
    [JsonPropertyName("voz")] public string? Voz { get; init; }

    /// <summary>Cliente → seus projetos. A UI precisa dos dois para o cadastro.</summary>
    [JsonPropertyName("clientes")] public Dictionary<string, List<string>>? Clientes { get; init; }

    /// <summary>O vínculo desta gravação, na resposta a <c>reuniao</c>.</summary>
    [JsonPropertyName("cliente")] public string? Cliente { get; init; }
    [JsonPropertyName("projeto")] public string? Projeto { get; init; }

    /// <summary>O que está escrito em notas.md.</summary>
    [JsonPropertyName("notas")] public string? Notas { get; init; }

    /// <summary>Nomes e siglas achados nas notas, para sugerir como vocabulário.</summary>
    [JsonPropertyName("termos")] public List<string>? Termos { get; init; }

    /// <summary>Os tipos de ata que a tela pode oferecer.</summary>
    [JsonPropertyName("tipos")] public List<TipoDeAtaResumo>? Tipos { get; init; }

    /// <summary>A ata em Markdown, ou nulo quando ainda não existe.</summary>
    [JsonPropertyName("ata")] public string? Ata { get; init; }

    /// <summary>A transcrição mudou depois de a ata ter sido escrita.</summary>
    [JsonPropertyName("ata_velha")] public bool AtaVelha { get; init; }
    [JsonPropertyName("prefs")] public PreferenciasDoProjeto? Prefs { get; init; }

    /// <summary>Os pacotes de modelo com o estado de cada um.</summary>
    [JsonPropertyName("catalogo")] public List<PacoteComEstado>? Catalogo { get; init; }

    /// <summary>
    /// Os pipelines de diarização que existem em disco, pelo nome da pasta.
    /// </summary>
    /// <remarks>
    /// Vai junto do catálogo e não dentro dele: a diarização deixou de ser um
    /// download na Fase 4, e um cartão de catálogo prometeria "Baixar" e
    /// "Remover" sobre arquivos que vieram no instalador. O que a tela precisa
    /// saber daqui é só o que dá para escolher.
    /// </remarks>
    [JsonPropertyName("diarizadores")] public List<string>? Diarizadores { get; init; }

    /// <summary>A biblioteca de vozes como a tela precisa vê-la.</summary>
    [JsonPropertyName("vozes")] public List<PessoaResumo>? Vozes { get; init; }

    /// <summary>A pasta escolhida no diálogo, ou nulo se foi cancelado.</summary>
    [JsonPropertyName("pasta")] public string? Pasta { get; init; }

    /// <summary>O gravador como a tela precisa vê-lo.</summary>
    [JsonPropertyName("gravador")] public EstadoDoGravador? Gravador { get; init; }

    /// <summary>O que está sendo transcrito, e o que acabou de terminar.</summary>
    [JsonPropertyName("transcricoes")] public EstadoDasTranscricoes? Transcricoes { get; init; }

    /// <summary>Os dispositivos de áudio, para a tela poder escolher.</summary>
    [JsonPropertyName("dispositivos")] public DispositivosDisponiveis? Dispositivos { get; init; }

    /// <summary>O estado desta instalação, para quem vai relatar um problema.</summary>
    [JsonPropertyName("diagnostico")] public Diagnostico? Diagnostico { get; init; }

    /// <summary>O motor de ata, que desde a Fase 4 se baixa em vez de vir junto.</summary>
    [JsonPropertyName("motor_de_ata")] public EstadoDoMotorDeAta? MotorDeAta { get; init; }

    /// <summary>Saiu versão nova? É o único caminho até quem já instalou.</summary>
    [JsonPropertyName("atualizacao")] public EstadoDaAtualizacao? Atualizacao { get; init; }

    /// <summary>As próximas reuniões da agenda, para escolher qual gravar.</summary>
    [JsonPropertyName("proximas")] public ProximasReunioes? Proximas { get; init; }
}

/// <summary>Uma reunião da agenda, como a tela precisa vê-la.</summary>
/// <remarks>
/// Só o que a tela desenha: o horário para ordenar e situar, o título para
/// reconhecer, e a <b>contagem</b> de participantes — a lista inteira de nomes
/// e e-mails de doze reuniões atravessaria a ponte cinco vezes por dia para
/// caber num rótulo de duas palavras.
/// </remarks>
internal sealed class ReuniaoDaAgenda
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("titulo")] public required string Titulo { get; init; }

    /// <summary>ISO 8601 com fuso, como veio do Google. A tela formata.</summary>
    [JsonPropertyName("inicio")] public string? Inicio { get; init; }
    [JsonPropertyName("fim")] public string? Fim { get; init; }
    [JsonPropertyName("participantes")] public int Participantes { get; init; }
    [JsonPropertyName("organizador")] public string? Organizador { get; init; }
}

/// <summary>O que a tela do Gravador mostra do calendário antes de gravar.</summary>
internal sealed class ProximasReunioes
{
    /// <summary>
    /// O <see cref="MeetingRecorder.Agenda.StatusDaAgenda"/> em minúsculas.
    /// </summary>
    /// <remarks>
    /// A tela precisa dele inteiro, e não de um booleano: agenda nunca conectada
    /// pede um convite discreto; token morto pede um aviso. Ver o comentário do
    /// enum, que é onde essa distinção foi paga.
    /// </remarks>
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("detalhe")] public string? Detalhe { get; init; }
    [JsonPropertyName("eventos")] public List<ReuniaoDaAgenda> Eventos { get; init; } = [];

    /// <summary>Qual seria escolhida se a gravação começasse agora.</summary>
    [JsonPropertyName("pre_definido")] public string? PreDefinido { get; init; }
}

/// <summary>
/// O gravador num instante: o que a bandeja diz num tooltip, aberto em campos.
/// </summary>
/// <remarks>
/// Chega à página de dois jeitos — como resposta a <c>gravador</c> e empurrado a
/// cada 200 ms enquanto a janela está aberta e gravando. É o mesmo objeto nos
/// dois casos de propósito: a tela desenha do estado que recebeu, sem precisar
/// saber se pediu ou se foi avisada.
/// </remarks>
internal sealed class EstadoDoGravador
{
    [JsonPropertyName("gravando")] public bool Gravando { get; init; }
    [JsonPropertyName("mudo")] public bool Mudo { get; init; }

    /// <summary>Há quanto tempo está mudo. Mute esquecido é o modo de falha mais provável.</summary>
    [JsonPropertyName("mudo_ha_s")] public double MudoHaS { get; init; }

    /// <summary>"cinza", "vermelho", "laranja" ou "amarelo" — a cor da bandeja.</summary>
    /// <remarks>
    /// A tela usa a <b>mesma</b> escala de cor do ícone, e não uma própria:
    /// laranja é você tendo mutado, amarelo é um canal sem áudio sem ninguém ter
    /// pedido. Inventar outra linguagem na janela obrigaria a traduzir de cabeça
    /// entre dois lugares que mostram a mesma coisa.
    /// </remarks>
    [JsonPropertyName("cor")] public required string Cor { get; init; }

    /// <summary>O texto de status pronto, do EstadoDaBandeja.</summary>
    [JsonPropertyName("status")] public required string Status { get; init; }

    [JsonPropertyName("duracao_s")] public double DuracaoS { get; init; }
    [JsonPropertyName("pasta")] public required string Pasta { get; init; }

    /// <summary>
    /// A pasta <b>desta</b> gravação, e não a raiz onde elas moram.
    /// </summary>
    /// <remarks>
    /// É o endereço para onde as notas escritas durante a reunião vão. Sem ele
    /// a tela do Gravador saberia que está gravando e não saberia onde — a raiz
    /// não serve, porque nota pertence a uma reunião, não à coleção delas.
    /// </remarks>
    [JsonPropertyName("gravacao")] public string? Gravacao { get; init; }

    /// <summary>A reunião da agenda que está sendo gravada, quando há uma.</summary>
    [JsonPropertyName("titulo")] public string? Titulo { get; init; }
    [JsonPropertyName("participantes")] public List<string>? Participantes { get; init; }

    /// <summary>
    /// O id da reunião fixada à mão, quando há uma.
    /// </summary>
    /// <remarks>
    /// Vai no estado, e não só na resposta da listagem, porque quem fixa pela
    /// bandeja ou por outra tela tem de aparecer aqui — o estado é empurrado, a
    /// listagem só chega quando alguém pede.
    /// </remarks>
    [JsonPropertyName("fixado")] public string? Fixado { get; init; }

    [JsonPropertyName("notificacoes")] public bool Notificacoes { get; init; }
    [JsonPropertyName("usar_agenda")] public bool UsarAgenda { get; init; }
    [JsonPropertyName("conta")] public string? Conta { get; init; }
    [JsonPropertyName("agenda_configurada")] public bool AgendaConfigurada { get; init; }

    [JsonPropertyName("faixas")] public List<FaixaAoVivo> Faixas { get; init; } = [];
}

/// <summary>Uma faixa enquanto grava — é daqui que sai o medidor de nível.</summary>
internal sealed class FaixaAoVivo
{
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("dispositivo")] public required string Dispositivo { get; init; }

    /// <summary>RMS instantâneo, 0 a 1. Ver WasapiTrackCapture.Nivel.</summary>
    [JsonPropertyName("nivel")] public double Nivel { get; init; }
    [JsonPropertyName("ja_ouviu")] public bool JaOuviu { get; init; }
    [JsonPropertyName("mudo")] public bool Mudo { get; init; }
    [JsonPropertyName("silencio_s")] public double SilencioS { get; init; }
    [JsonPropertyName("desconectado")] public bool Desconectado { get; init; }
    [JsonPropertyName("falha")] public string? Falha { get; init; }
}

/// <summary>
/// As transcrições como a página precisa vê-las.
/// </summary>
/// <remarks>
/// Chega de dois jeitos, como o gravador: como resposta a <c>transcricoes</c> e
/// empurrado a cada aviso de andamento do pipeline. É o mesmo objeto nos dois
/// casos, para a tela desenhar do que recebeu sem saber se pediu ou foi avisada.
/// </remarks>
internal sealed class EstadoDasTranscricoes
{
    /// <summary>A que está rodando agora, ou nulo. É ela que acende a bolinha.</summary>
    [JsonPropertyName("atual")] public TranscricaoResumo? Atual { get; init; }

    /// <summary>A última que terminou, para a tela poder mostrar como acabou.</summary>
    [JsonPropertyName("ultimo")] public TranscricaoResumo? Ultimo { get; init; }
}

internal sealed class TranscricaoResumo
{
    /// <summary>A pasta da gravação: é por ela que a tela sabe se é a sua.</summary>
    [JsonPropertyName("gravacao")] public required string Gravacao { get; init; }
    [JsonPropertyName("nome")] public required string Nome { get; init; }

    /// <summary>
    /// "transcricao" ou "ata".
    /// </summary>
    /// <remarks>
    /// A tela precisa saber qual dos dois está rodando: os dois usam o mesmo
    /// registro (disputam a mesma placa), e sem isto a lista dizia
    /// "Transcrevendo…" numa reunião cuja ata estava sendo escrita, com a
    /// bolinha acesa no destino errado.
    /// </remarks>
    [JsonPropertyName("tarefa")] public required string Tarefa { get; init; }

    [JsonPropertyName("etapa")] public required string Etapa { get; init; }
    [JsonPropertyName("fracao")] public double Fracao { get; init; }
    [JsonPropertyName("texto")] public required string Texto { get; init; }
    [JsonPropertyName("comecou_em")] public required string ComecouEm { get; init; }
    [JsonPropertyName("terminou")] public bool Terminou { get; init; }
    [JsonPropertyName("erro")] public string? Erro { get; init; }

    /// <summary>Parou a pedido. A tela trata diferente de falha.</summary>
    [JsonPropertyName("cancelada")] public bool Cancelada { get; init; }
}

/// <summary>Um tipo de reunião, como a tela o oferece.</summary>
internal sealed class TipoDeAtaResumo
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("nome")] public required string Nome { get; init; }

    /// <summary>Veio da pasta do perfil: a tela oferece "voltar ao original".</summary>
    [JsonPropertyName("do_usuario")] public bool DoUsuario { get; init; }
}

internal sealed class DispositivosDisponiveis
{
    [JsonPropertyName("entradas")] public List<DispositivoResumo> Entradas { get; init; } = [];
    [JsonPropertyName("saidas")] public List<DispositivoResumo> Saidas { get; init; } = [];

    /// <summary>Os escolhidos, ou nulo para "padrão do Windows".</summary>
    [JsonPropertyName("mic_id")] public string? MicId { get; init; }
    [JsonPropertyName("loopback_id")] public string? LoopbackId { get; init; }
}

internal sealed class DispositivoResumo
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("padrao")] public bool Padrao { get; init; }
}

/// <summary>Uma pessoa conhecida, e as amostras que o sistema guarda dela.</summary>
/// <remarks>
/// O vetor <b>não</b> vem junto de propósito: são 256 floats por amostra que a
/// tela não tem como usar e que só engordariam cada mensagem. O que a tela
/// precisa é da procedência — é ela que permite tocar o trecho, e é ouvindo
/// quatro segundos que uma pessoa julga o que nenhum número mostra.
/// </remarks>
internal sealed class PessoaResumo
{
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("amostras")] public List<AmostraResumo> Amostras { get; init; } = [];
}

internal sealed class AmostraResumo
{
    /// <summary>A posição dentro do perfil: é por ela que se aprova ou esquece.</summary>
    [JsonPropertyName("indice")] public int Indice { get; init; }
    [JsonPropertyName("criada_em")] public required string CriadaEm { get; init; }
    [JsonPropertyName("duracao_s")] public double DuracaoS { get; init; }
    [JsonPropertyName("gravacao")] public required string Gravacao { get; init; }
    [JsonPropertyName("faixa")] public required string Faixa { get; init; }
    [JsonPropertyName("t0")] public double T0 { get; init; }
    [JsonPropertyName("t1")] public double T1 { get; init; }
    [JsonPropertyName("dispositivo")] public string? Dispositivo { get; init; }
    [JsonPropertyName("quarentena")] public bool Quarentena { get; init; }

    /// <summary>
    /// A amostra veio de um modelo de voz diferente do que o app usa hoje.
    /// </summary>
    /// <remarks>
    /// Ela não é apagada nem entra em nenhuma comparação — vetores de modelos
    /// diferentes não se comparam. Precisa aparecer porque, sem isso, a tela
    /// mostraria cinco amostras de alguém que o app não reconhece, e nada
    /// explicaria a contradição. Ver <see cref="Vozes.ModeloDe"/>.
    /// </remarks>
    [JsonPropertyName("outro_modelo")] public bool OutroModelo { get; init; }

    /// <summary>
    /// A amostra foi colhida sob regras de inscrição que já não valem.
    /// </summary>
    /// <remarks>
    /// Mesmo tratamento do <see cref="OutroModelo"/> e pelo mesmo motivo: ela
    /// não entra em conta nenhuma, e precisa aparecer para a tela não mostrar
    /// amostras de alguém que o app não reconhece sem explicar por quê. O texto
    /// é diferente porque a causa é diferente — e a diferença importa para quem
    /// decide se apaga ou espera. Ver <see cref="Vozes.RegrasAtuais"/>.
    /// </remarks>
    [JsonPropertyName("regras_antigas")] public bool RegrasAntigas { get; init; }

    /// <summary>
    /// O caminho do trecho relativo à pasta de vozes, ou nulo se não houver.
    /// </summary>
    /// <remarks>
    /// Relativo, e não absoluto: a página o concatena em <c>vozes.local</c>,
    /// que é a pasta mapeada. Mandar o caminho absoluto obrigaria a tela a
    /// conhecer a estrutura de disco do app.
    /// </remarks>
    [JsonPropertyName("trecho")] public string? Trecho { get; init; }
}

/// <summary>Uma gravação como a lista precisa mostrá-la.</summary>
/// <remarks>
/// Os avisos vêm prontos do núcleo, e não como campos crus para a página
/// interpretar: decidir que 3% de conteúdo útil é um problema é regra de
/// produto, e regra de produto fica de um lado só.
/// </remarks>
internal sealed class GravacaoResumo
{
    [JsonPropertyName("nome")] public required string Nome { get; init; }
    [JsonPropertyName("caminho")] public required string Caminho { get; init; }
    [JsonPropertyName("duracao_s")] public double DuracaoS { get; init; }
    [JsonPropertyName("titulo")] public string? Titulo { get; init; }
    /// <summary>Quantos a agenda listou — convidados, não presentes.</summary>
    [JsonPropertyName("convidados")] public int Convidados { get; init; }
    [JsonPropertyName("transcrita")] public bool Transcrita { get; init; }

    /// <summary>O vínculo escolhido na tela de preparo, que sobrevive a ela.</summary>
    [JsonPropertyName("cliente")] public string? Cliente { get; init; }
    [JsonPropertyName("projeto")] public string? Projeto { get; init; }

    /// <summary>Alguém escreveu notas nesta reunião.</summary>
    [JsonPropertyName("com_notas")] public bool ComNotas { get; init; }

    [JsonPropertyName("avisos")] public List<string> Avisos { get; init; } = [];
}

/// <summary>Uma fala da legenda gravada, como a tela a recebe.</summary>
internal sealed class TurnoJson
{
    [JsonPropertyName("dono")] public required bool Dono { get; init; }
    [JsonPropertyName("texto")] public required string Texto { get; init; }
}

/// <summary>Um pedaço da legenda ao vivo, como a tela o recebe.</summary>
/// <remarks>
/// <b>Só o que firmou agora</b> — ver <c>Nucleo/LegendaAoVivo.cs</c>. Mandar o
/// acumulado a cada parcial seria O(n²) ao longo da reunião.
/// </remarks>
internal sealed class PedacoDaLegendaJson
{
    [JsonPropertyName("novo")] public required string Novo { get; init; }
    [JsonPropertyName("tentativo")] public required string Tentativo { get; init; }
    [JsonPropertyName("dono")] public required bool Dono { get; init; }
}

/// <summary>Um bloco de 3 minutos da prévia, como a tela o recebe.</summary>
/// <remarks>
/// <b>Chega inteiro e uma vez.</b> Nada de trecho a trecho: o produto é de
/// blocos de três minutos, e fingir granularidade menor é fingir tempo real.
/// Ver docs/FASE7-FRONTEND.md §8.
/// </remarks>
internal sealed class BlocoDaPrevia
{
    [JsonPropertyName("n")] public int Numero { get; init; }
    [JsonPropertyName("inicio_s")] public double InicioS { get; init; }
    [JsonPropertyName("fim_s")] public double FimS { get; init; }

    /// <summary><c>"provisorio"</c> ou <c>"mudo"</c>.</summary>
    [JsonPropertyName("estado")] public required string Estado { get; init; }

    [JsonPropertyName("trechos")] public required List<TrechoDaPrevia> Trechos { get; init; }
}

/// <summary>Um trecho da prévia.</summary>
/// <remarks>
/// O <c>speaker</c> é o rótulo <b>local ao bloco</b> (<c>b3_S1</c>) ou
/// <c>You</c>, que vem da faixa do microfone e é certeza. A tela nunca mostra
/// nome de pessoa aqui: a costura só acontece na passada final, e um nome que
/// troca sozinho é pior que "Falante 2" (docs/FASE7-RESULTADOS.md §5.3).
/// </remarks>
internal sealed class TrechoDaPrevia
{
    [JsonPropertyName("start")] public double Start { get; init; }
    [JsonPropertyName("end")] public double End { get; init; }
    [JsonPropertyName("text")] public required string Text { get; init; }
    [JsonPropertyName("speaker")] public string? Speaker { get; init; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Pedido))]
[JsonSerializable(typeof(Resposta))]
[JsonSerializable(typeof(BlocoDaPrevia))]
[JsonSerializable(typeof(PedacoDaLegendaJson))]
[JsonSerializable(typeof(List<TurnoJson>))]
[JsonSerializable(typeof(PacoteComEstado))]
[JsonSerializable(typeof(EstadoDoGravador))]
[JsonSerializable(typeof(EstadoDasTranscricoes))]
[JsonSerializable(typeof(DispositivosDisponiveis))]
[JsonSerializable(typeof(PessoaResumo))]
[JsonSerializable(typeof(PreferenciasDoProjeto))]
[JsonSerializable(typeof(ConfiguracoesDoApp))]
[JsonSerializable(typeof(Diagnostico))]
[JsonSerializable(typeof(EstadoDoMotorDeAta))]
[JsonSerializable(typeof(EstadoDaAtualizacao))]
[JsonSerializable(typeof(ProximasReunioes))]
internal sealed partial class PonteJsonBase : JsonSerializerContext;

internal static class PonteJson
{
    // Acento literal, como em todo JSON deste projeto: o nome do dispositivo e o
    // título da reunião passam por aqui.
    public static readonly PonteJsonBase Default = new(new JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

/// <summary>Atende os pedidos da página.</summary>
/// <param name="responder">
/// Envia uma mensagem à página. Chamado mais de uma vez por pedido quando há
/// progresso, e sempre na thread da UI — quem passa o delegate garante isso.
/// </param>
/// <param name="gravador">
/// O gravador do mesmo processo. A ponte não o comanda de longe: chama métodos,
/// e o efeito aparece na bandeja e na janela pelo mesmo evento.
/// </param>
/// <param name="avisar">
/// Um balão da bandeja. Serve à transcrição que termina com a janela escondida —
/// que é o caso normal, já que ela passou a rodar sem ninguém olhando.
/// </param>
internal sealed class Ponte(string pastaDasGravacoes, Action<string> responder,
                            Bandeja.Gravador gravador, Action<string> avisar)
{
    private readonly Transcritor _transcritor = new(Motores.AoLadoDoExecutavel());
    private readonly Projetos _projetos = new();

    /// <summary>
    /// O que está sendo transcrito. Vive na ponte, e não na página, porque a
    /// página troca de tela e o pipeline não pode saber disso (FASE3.md §2).
    /// </summary>
    private readonly RegistroDeTranscricoes _transcricoes = new();

    /// <summary>
    /// A prévia da reunião em curso, quando ligada. Nula o resto do tempo.
    /// </summary>
    /// <remarks>
    /// Uma por gravação, criada no <c>gravar</c> e descartada no
    /// <c>parar-gravacao</c>. Ela não é dona de nada que a gravação precise: se
    /// morrer, some da tela e o áudio continua sendo escrito.
    /// </remarks>
    private SessaoAoVivo? _aoVivo;
    private LegendaAoVivo? _legenda;

    /// <summary>
    /// A pasta da gravação em curso, para a pergunta saber sobre o que é.
    /// </summary>
    /// <remarks>
    /// Guardada mesmo quando nem a legenda nem a prévia ligaram: é o que separa
    /// "não há reunião acontecendo" de "há, mas ninguém está transcrevendo ela"
    /// — duas frases que pedem coisas diferentes de quem lê.
    /// </remarks>
    private string? _pastaAoVivo;

    /// <summary>
    /// Quem leva a pergunta ao modelo, uma de cada vez.
    /// </summary>
    /// <remarks>
    /// Uma instância só para a ponte inteira, e é ela que guarda a vez: duas
    /// subidas do <c>llama-server</c> ao mesmo tempo são dois modelos na placa
    /// durante uma reunião que está sendo gravada.
    /// </remarks>
    private readonly PerguntaDaReuniao _pergunta = new(PerguntarAoMotorAsync);

    /// <summary>
    /// O motor de pé entre perguntas, quando a chave <c>modelo_quente</c> liga.
    /// </summary>
    /// <remarks>
    /// <b>Nulo é o estado normal</b>: com a chave desligada, cada pergunta sobe
    /// e mata o seu próprio motor. Quando existe, ele morre ao parar a gravação
    /// (<see cref="EncerrarAPrevia"/>), por ociosidade, e quando a chave é
    /// desligada em Ajustes.
    /// </remarks>
    private MotorQuente? _motorQuente;

    public async Task AtenderAsync(string mensagem)
    {
        Pedido? p;
        try
        {
            p = JsonSerializer.Deserialize(mensagem, PonteJson.Default.Pedido);
        }
        catch (JsonException e)
        {
            Responder(new Resposta { Id = 0, Erro = $"pedido ilegível: {e.Message}" });
            return;
        }
        if (p is null)
        {
            Responder(new Resposta { Id = 0, Erro = "pedido vazio" });
            return;
        }

        try
        {
            switch (p.Op)
            {
                case "gravacoes":
                    Responder(new Resposta { Id = p.Id, Gravacoes = Listar() });
                    break;

                case "clientes":
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "prefs":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Prefs = _projetos.Preferencias(p.Cliente ?? "", p.Projeto ?? ""),
                    });
                    break;

                // O vínculo da reunião com cliente/projeto, guardado na hora em
                // que se escolhe — e não só quando se transcreve. Ver
                // DadosDaReuniao: o defeito que originou isto era sair da tela
                // de preparo e voltar com os campos em branco.
                case "salvar-reuniao":
                {
                    if (p.Gravacao is not { Length: > 0 } onde)
                        throw new InvalidOperationException("sem gravação");
                    new DadosDaReuniao { Cliente = p.Cliente, Projeto = p.Projeto }
                        .Salvar(onde);
                    Responder(new Resposta { Id = p.Id });
                    break;
                }

                // ─────────────────────────────────────────────── atas

                case "modelos-de-ata":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Tipos = [.. ModelosDeAta.Todos().Select(m => new TipoDeAtaResumo
                        {
                            Id = m.Id, Nome = m.Nome, DoUsuario = m.DoUsuario,
                        })],
                    });
                    break;

                case "ata":
                {
                    if (p.Gravacao is not { Length: > 0 } onde)
                        throw new InvalidOperationException("sem gravação");
                    string caminho = Path.Combine(onde, "ata.md");
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Ata = File.Exists(caminho) ? File.ReadAllText(caminho) : null,
                        // Ata mais velha que a transcrição significa que alguém
                        // corrigiu o texto depois — e a ata ficou desatualizada
                        // sem ninguém avisar.
                        AtaVelha = File.Exists(caminho)
                                   && File.Exists(Path.Combine(onde, "transcricao.json"))
                                   && File.GetLastWriteTimeUtc(caminho)
                                      < File.GetLastWriteTimeUtc(Path.Combine(onde, "transcricao.json")),
                    });
                    break;
                }

                case "gerar-ata":
                    GerarAta(p);
                    break;

                case "perguntar-ao-vivo":
                    await PerguntarAoVivoAsync(p);
                    break;

                case "exportar-ata":
                    Responder(new Resposta { Id = p.Id, Arquivo = ExportarAta(p) });
                    break;

                case "customizar-tipo-de-ata":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Arquivo = ModelosDeAta.Customizar(p.Modelo ?? ""),
                    });
                    break;

                case "restaurar-tipo-de-ata":
                    ModelosDeAta.VoltarAoOriginal(p.Modelo ?? "");
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Tipos = [.. ModelosDeAta.Todos().Select(m => new TipoDeAtaResumo
                        {
                            Id = m.Id, Nome = m.Nome, DoUsuario = m.DoUsuario,
                        })],
                    });
                    break;

                // ─────────────────────────────────── notas da reunião

                case "notas":
                {
                    if (p.Gravacao is not { Length: > 0 } onde)
                        throw new InvalidOperationException("sem gravação");
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Notas = Notas.Ler(onde),
                        Termos = Notas.TermosSugeridos(Notas.Ler(onde)),
                    });
                    break;
                }

                case "salvar-notas":
                {
                    if (p.Gravacao is not { Length: > 0 } onde)
                        throw new InvalidOperationException("sem gravação");
                    Notas.Salvar(onde, p.Conteudo);
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Termos = Notas.TermosSugeridos(p.Conteudo ?? ""),
                    });
                    break;
                }

                case "reuniao":
                {
                    if (p.Gravacao is not { Length: > 0 } onde)
                        throw new InvalidOperationException("sem gravação");
                    var d = DadosDaReuniao.Ler(onde);
                    Responder(new Resposta
                    {
                        Id = p.Id, Cliente = d.Cliente, Projeto = d.Projeto,
                    });
                    break;
                }

                case "salvar-projeto":
                    // Cliente e projeto novos nascem aqui: digitar um nome
                    // inédito e transcrever é o fluxo do app Python que o
                    // usuário pediu para manter.
                    _projetos.Salvar(p.Cliente ?? "", p.Projeto ?? "",
                                     p.Prefs ?? new PreferenciasDoProjeto());
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "config":
                    Responder(new Resposta { Id = p.Id, Config = ConfiguracoesDoApp.Carregar() });
                    break;

                case "diagnostico":
                    // Fora da thread da UI: o nvidia-smi é um processo filho, e
                    // esperar por ele aqui congelaria a janela por até 5 s na
                    // máquina em que ele estiver lento.
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Diagnostico = await Task.Run(() =>
                            Diagnostico.Coletar(ConfiguracoesDoApp.Carregar(), pastaDasGravacoes)),
                    });
                    break;

                case "escolher-pasta":
                    Responder(new Resposta { Id = p.Id, Pasta = EscolherPasta(p.Pasta) });
                    break;

                case "renomear-cliente":
                    _projetos.RenomearCliente(p.Cliente ?? "", p.Nome ?? "");
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "renomear-projeto":
                    _projetos.RenomearProjeto(p.Cliente ?? "", p.Projeto ?? "", p.Nome ?? "");
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "apagar-cliente":
                    _projetos.ApagarCliente(p.Cliente ?? "");
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "apagar-projeto":
                    _projetos.ApagarProjeto(p.Cliente ?? "", p.Projeto ?? "");
                    Responder(new Resposta { Id = p.Id, Clientes = MapaDeClientes() });
                    break;

                case "apagar-gravacao":
                    ApagarGravacao(p.Gravacao);
                    Responder(new Resposta { Id = p.Id, Gravacoes = Listar() });
                    break;

                case "catalogo":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Catalogo = Catalogo.Listar(ConfiguracoesDoApp.Carregar()),
                        Diarizadores =
                            [.. Motores.AoLadoDoExecutavel().ModelosDeDiarizacao()],
                    });
                    break;

                case "baixar-pacote":
                    await BaixarPacoteAsync(p);
                    break;

                case "atualizacao":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Atualizacao = await Atualizacao.ProcurarAsync(
                            ConfiguracoesDoApp.Carregar(), ct: CancellationToken.None),
                    });
                    break;

                case "motor-de-ata":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        MotorDeAta = PacoteDoMotorDeAta.Estado(),
                    });
                    break;

                case "baixar-motor-de-ata":
                    // 641 MB de duas releases do GitHub, extraídos em
                    // motores/ata/bin. Ver PacoteDoMotorDeAta.
                    await PacoteDoMotorDeAta.BaixarAsync(
                        (fracao, texto) => Responder(new Resposta
                        {
                            Id = p.Id,
                            Tipo = "progresso",
                            Etapa = "baixando",
                            Fracao = fracao,
                            Texto = texto,
                        }),
                        CancellationToken.None);
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        MotorDeAta = PacoteDoMotorDeAta.Estado(),
                    });
                    break;

                case "remover-pacote":
                    RemoverPacote(p.Modelo);
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Catalogo = Catalogo.Listar(ConfiguracoesDoApp.Carregar()),
                    });
                    break;

                case "vozes":
                    Responder(new Resposta { Id = p.Id, Vozes = VozesConhecidas() });
                    break;

                case "aprovar-voz":
                    new Vozes().Aprovar(p.Pessoa ?? "", p.Indice ?? -1);
                    Responder(new Resposta { Id = p.Id, Vozes = VozesConhecidas() });
                    break;

                case "esquecer-voz":
                    new Vozes().Esquecer(p.Pessoa ?? "", p.Indice ?? -1);
                    Responder(new Resposta { Id = p.Id, Vozes = VozesConhecidas() });
                    break;

                case "juntar-vozes":
                    // Duas grafias do mesmo nome viram dois perfis, e dois
                    // perfis reconhecem pior que um: o centroide de cada um é
                    // mais fraco. Ver Vozes.Juntar.
                    new Vozes().Juntar(p.Pessoa ?? "", p.Nome ?? "");
                    Responder(new Resposta { Id = p.Id, Vozes = VozesConhecidas() });
                    break;

                case "salvar-config":
                    p.Config?.Salvar();
                    // **Desligar a chave devolve a placa na hora.** Sem isto, a
                    // pessoa desliga porque precisa da máquina para outra coisa
                    // e o processo de 3,2 GB continua lá até a gravação parar —
                    // que é o oposto do que ela pediu ao desligar.
                    if (ConfiguracoesDoApp.Carregar() is { ModeloQuente: false })
                    {
                        _motorQuente?.Dispose();
                        _motorQuente = null;
                    }
                    Responder(new Resposta { Id = p.Id, Config = ConfiguracoesDoApp.Carregar() });
                    break;

                case "exportar":
                {
                    var (arquivo, copia) = Exportar(p);
                    Responder(new Resposta { Id = p.Id, Arquivo = arquivo, Copia = copia });
                    break;
                }

                case "aprender-voz":
                    await AprenderVozAsync(p);
                    break;

                case "salvar-transcricao":
                    SalvarTranscricao(p.Gravacao, p.Conteudo);
                    Responder(new Resposta { Id = p.Id });
                    break;

                case "transcricao":
                    Responder(new Resposta { Id = p.Id, Transcricao = LerTranscricao(p.Gravacao) });
                    break;

                // O que a legenda ao vivo deixou para ler. **Não é transcrição**
                // e não entra no lugar dela: serve para conferir, antes de
                // gastar a placa, se o que foi dito está lá.
                case "legenda-gravada":
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        LegendaFalantes = p.Gravacao is { Length: > 0 } gf
                            ? LegendaAoVivo.Ler(gf)?.FalantesProntos : null,
                        LegendaGravada = p.Gravacao is { Length: > 0 } g
                            ? LegendaAoVivo.Ler(g)?.Turnos.Select(t => new TurnoJson
                            { Dono = t.Dono, Texto = t.Texto }).ToList()
                            : null,
                    });
                    break;

                // Não espera o pipeline: responde "aceita" na hora, e o
                // andamento passa a fluir pelo canal de eventos. É o que faz a
                // transcrição sobreviver a trocar de tela — quem desenha a barra
                // deixa de ser o dono da promessa (FASE3.md §2).
                case "transcrever":
                    Transcrever(p);
                    break;

                case "transcricoes":
                    Responder(new Resposta { Id = p.Id, Transcricoes = Instantaneo() });
                    break;

                // Só pede para parar; quem tira do registro é a tarefa que
                // estava rodando, quando os motores de fato morrerem.
                case "cancelar-transcricao":
                    _transcricoes.Cancelar(p.Gravacao);
                    Responder(new Resposta { Id = p.Id, Transcricoes = Instantaneo() });
                    break;

                case "esquecer-transcricao":
                    _transcricoes.EsquecerUltimo();
                    Responder(new Resposta { Id = p.Id, Transcricoes = Instantaneo() });
                    break;

                // ─────────────────────────────────── gravador
                //
                // Todas devolvem o estado inteiro, e não um "ok": a tela desenha
                // do estado que recebeu, e uma resposta vazia a obrigaria a
                // adivinhar o que mudou — ou a pedir de novo logo em seguida.

                case "gravador":
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "gravar":
                    // A prévia NÃO é ligada aqui: quem avisa é o próprio
                    // gravador, pelo AoComecar. Gravar tem três portas — este
                    // botão, o ícone da bandeja e o menu dela — e pendurar-se
                    // numa delas é pendurar-se em nenhuma. Ver Gravador.AoComecar.
                    gravador.Iniciar();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "parar-gravacao":
                    gravador.Parar();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "aovivo":
                    // A tela pergunta ao montar, e recebe DUAS coisas: o motivo,
                    // quando a prévia não pode acontecer, e o que já aconteceu —
                    // porque o Gravador pode ser aberto no minuto 20, e os blocos
                    // de antes já passaram pelo canal de eventos.
                    // **E o modo**, porque a tela não pode adivinhá-lo. A dica
                    // do painel estava cravada em "blocos de 3 minutos" e mentia
                    // quando a legenda era o que rodava — visto em uso em
                    // 14/09/2026, com a legenda desligada e a tela prometendo
                    // blocos que também não vinham.
                    var cfgAv = ConfiguracoesDoApp.Carregar();
                    var motoresAv = Motores.AoLadoDoExecutavel();
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        AoVivoModo = cfgAv.LegendaAoVivo ? "legenda"
                                   : cfgAv.TranscricaoAoVivo ? "bloco" : "nada",
                        AoVivoImpedimento = cfgAv.LegendaAoVivo
                            ? LegendaAoVivo.OQueImpede(motoresAv, cfgAv)
                            : cfgAv.TranscricaoAoVivo
                                ? SessaoAoVivo.OQueImpede(motoresAv, cfgAv)
                                : "nem a legenda nem a prévia em blocos estão ligadas "
                                  + "em Ajustes › Transcrição.",
                        AoVivoAte = [.. (_aoVivo?.Entregues ?? []).Select(Resumir)],
                        PerguntarImpedimento = PerguntaDaReuniao.OQueImpede(
                            cfgAv, CaminhosDoMotorDeAta.AoLadoDoExecutavel(cfgAv.ModeloParaPergunta)),
                    });
                    break;

                case "mutar":
                    gravador.AlternarMudo();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "dispositivos":
                    Responder(new Resposta { Id = p.Id, Dispositivos = Disponiveis(gravador) });
                    break;

                case "escolher-dispositivo":
                    gravador.EscolherDispositivo(p.Faixa ?? "mic",
                        p.Dispositivo is { Length: > 0 } ? p.Dispositivo : null);
                    Responder(new Resposta
                    {
                        Id = p.Id,
                        Gravador = Instantaneo(gravador),
                        Dispositivos = Disponiveis(gravador),
                    });
                    break;

                case "pasta-das-gravacoes":
                    DefinirPastaDasGravacoes(p.Pasta);
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "notificacoes":
                    if ((p.Ligado ?? true) != gravador.Estado.NotificacoesLigadas)
                        gravador.AlternarNotificacoes();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "agenda-proximas":
                    Responder(new Resposta { Id = p.Id, Proximas = await ProximasAsync() });
                    break;

                case "fixar-evento":
                    gravador.Fixar(p.Evento);
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "usar-agenda":
                    gravador.UsarAgenda(p.Ligado ?? true);
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "conectar-agenda":
                    // Abre o navegador e volta na hora: a autorização acontece
                    // fora do app, e travar a tela até o usuário terminar de
                    // clicar num site seria travá-la por minutos.
                    gravador.Autorizar();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                case "desconectar-agenda":
                    MeetingRecorder.Agenda.ClienteDaAgenda.Desconectar();
                    Responder(new Resposta { Id = p.Id, Gravador = Instantaneo(gravador) });
                    break;

                default:
                    Responder(new Resposta { Id = p.Id, Erro = $"operação desconhecida: {p.Op}" });
                    break;
            }
        }
        catch (Exception e)
        {
            // A página precisa poder mostrar o erro; derrubar a janela por causa
            // de uma pasta ilegível seria pior que a falha original.
            Responder(new Resposta { Id = p.Id, Erro = e.Message });
        }
    }

    // ───────────────────────────────────────────────────────── gravador

    /// <summary>
    /// O evento empurrado à página: o mesmo estado, com <c>id</c> zero.
    /// </summary>
    /// <remarks>
    /// Zero porque não responde a pedido nenhum, e a página casa pedidos e
    /// respostas pelo id — um id inventado casaria com uma promessa alheia. O
    /// <c>tipo</c> é o que a <c>ponte.js</c> usa para roteá-lo aos assinantes em
    /// vez de a uma promessa.
    /// </remarks>
    public static string EventoDoGravador(Bandeja.Gravador g) =>
        JsonSerializer.Serialize(
            new Resposta { Id = 0, Tipo = "gravador", Gravador = Instantaneo(g) },
            PonteJson.Default.Resposta);

    private static EstadoDoGravador Instantaneo(Bandeja.Gravador g)
    {
        var faixas = new List<FaixaAoVivo>();
        foreach (var c in g.Capturas)
            faixas.Add(new FaixaAoVivo
            {
                Nome = c.Stats.Nome,
                Dispositivo = c.NomeDispositivo,
                Nivel = c.Nivel,
                JaOuviu = c.Stats.JaOuviu,
                Mudo = c.Mudo,
                SilencioS = c.Stats.SilencioAtualS,
                Desconectado = c.Desconectado,
                Falha = c.FalhaDeEscrita ? c.MotivoDaFalha : null,
            });

        return new EstadoDoGravador
        {
            Gravando = g.Estado.Gravando,
            Mudo = g.Estado.Mudo,
            MudoHaS = g.Estado.MudoHaS(DateTime.UtcNow),
            Cor = g.Estado.Cor.ToString().ToLowerInvariant(),
            Status = g.Estado.TextoDeStatus(g.DuracaoAtual, null).Split('\n')[0],
            DuracaoS = g.DuracaoAtual,
            Pasta = g.PastaDeSaida,
            // Só enquanto grava: o PastaAtual guarda também a da última
            // gravação, e oferecer o bloco de notas depois de parar faria
            // escrever numa reunião que já acabou sem a tela dizer qual é.
            Gravacao = g.Estado.Gravando ? g.PastaAtual : null,
            Titulo = g.Evento?.Titulo,
            Participantes = g.Evento?.NomesDosParticipantes().ToList(),
            Fixado = g.Fixado?.Id,
            Notificacoes = g.Estado.NotificacoesLigadas,
            UsarAgenda = g.Cfg.UseCalendar,
            AgendaConfigurada = MeetingRecorder.Agenda.ClienteDaAgenda.EstaConfigurado(),
            Conta = MeetingRecorder.Agenda.ClienteDaAgenda.EstaAutorizado()
                ? MeetingRecorder.Agenda.ClienteDaAgenda.EmailDaConta() : null,
            Faixas = faixas,
        };
    }

    /// <summary>
    /// Pergunta as próximas reuniões ao Google e as traduz para a tela.
    /// </summary>
    /// <remarks>
    /// Nunca lança — o <see cref="MeetingRecorder.Agenda.ClienteDaAgenda"/> já
    /// converte falha em status, e a tela mostra o motivo no lugar da lista.
    /// </remarks>
    private async Task<ProximasReunioes> ProximasAsync()
    {
        var r = await gravador.ProximasAsync();
        return new ProximasReunioes
        {
            Status = r.Status.ToString().ToLowerInvariant(),
            Detalhe = r.Detalhe is { Length: > 0 } ? r.Detalhe : null,
            PreDefinido = r.PreDefinido,
            Eventos = [.. r.Eventos.Select(e => new ReuniaoDaAgenda
            {
                Id = e.Id,
                Titulo = e.Titulo,
                Inicio = e.Inicio?.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                Fim = e.Fim?.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                Participantes = e.NomesDosParticipantes().Count,
                Organizador = e.Organizador,
            })],
        };
    }

    private static DispositivosDisponiveis Disponiveis(Bandeja.Gravador g)
    {
        var cat = g.Dispositivos.Atual;
        static List<DispositivoResumo> Mapear(
            IReadOnlyList<MeetingRecorder.Capture.Dispositivo> lista) =>
            [.. lista.Select(d => new DispositivoResumo
            {
                Id = d.Id, Nome = d.Nome, Padrao = d.EhPadrao,
            })];

        return new DispositivosDisponiveis
        {
            Entradas = Mapear(cat.Entradas),
            Saidas = Mapear(cat.Saidas),
            MicId = g.Cfg.MicId,
            LoopbackId = g.Cfg.LoopbackId,
        };
    }

    /// <summary>
    /// Troca a pasta onde o gravador salva — a mesma que o app lê.
    /// </summary>
    /// <remarks>
    /// Confere a escrita antes de aceitar, como o menu da bandeja: descobrir que
    /// a pasta é somente leitura no meio de uma reunião seria tarde demais. Uma
    /// pasta vazia restaura o padrão.
    /// </remarks>
    private void DefinirPastaDasGravacoes(string? pasta)
    {
        if (gravador.Estado.Gravando)
            throw new InvalidOperationException(
                "não dá para trocar a pasta durante uma gravação");

        if (gravador.PastaForcada is not null)
            throw new InvalidOperationException(
                "esta sessão foi aberta com --gravacoes; a pasta está fixa");

        if (pasta is { Length: > 0 } && !Bandeja.Bandeja.PodeEscrever(pasta, out string? erro))
            throw new InvalidOperationException($"não dá para escrever nessa pasta: {erro}");

        gravador.DefinirPastaDeSaida(pasta);
    }

    // ──────────────────────────────────────────────────── transcrições

    /// <summary>O registro como a página o vê.</summary>
    private EstadoDasTranscricoes Instantaneo() => new()
    {
        Atual = Resumir(_transcricoes.Atual),
        Ultimo = Resumir(_transcricoes.Ultimo),
    };

    private static TranscricaoResumo? Resumir(TrabalhoDeTranscricao? t) => t is null ? null : new()
    {
        Gravacao = t.Gravacao,
        Nome = t.Nome,
        Tarefa = t.Tarefa,
        Etapa = t.Etapa,
        Fracao = t.Fracao,
        Texto = t.Texto,
        ComecouEm = t.ComecouEm.ToString("o"),
        Terminou = t.Terminou,
        Erro = t.Erro,
        Cancelada = t.Cancelada,
    };

    /// <summary>
    /// Liga a prévia desta gravação, quando a chave permite.
    /// </summary>
    /// <remarks>
    /// <b>Nunca lança.</b> Ela é acionada no mesmo caminho que acabou de iniciar
    /// uma gravação: uma exceção aqui derrubaria o pedido e deixaria o usuário
    /// sem saber se está gravando. O pior desfecho aceitável é a prévia não
    /// acontecer — e ela ser um extra é exatamente o que permite tratá-la assim.
    /// </remarks>
    /// <summary>
    /// Passa a escutar o gravador. Chamado uma vez, na subida.
    /// </summary>
    /// <remarks>
    /// Assinar o gravador e não interceptar o botão: as três portas de gravar
    /// convergem nele, e uma quarta amanhã não exigiria lembrar deste arquivo.
    /// </remarks>
    public void AcompanharOGravador(Bandeja.Gravador gravador)
    {
        gravador.AoComecar += ComecarAPrevia;
        gravador.AoTerminar += EncerrarAPrevia;
    }

    private void ComecarAPrevia(string pasta)
    {
        _pastaAoVivo = pasta;
        try
        {
            _aoVivo?.Dispose();
            _aoVivo = null;

            var cfg = ConfiguracoesDoApp.Carregar();
            var motores = Motores.AoLadoDoExecutavel();

            // **A legenda primeiro, e com guarda própria.** Ela ficou uma semana
            // sem ligar porque o `if (!cfg.TranscricaoAoVivo) return;` — que é
            // do caminho dos blocos — vinha antes dela: com a prévia em blocos
            // desligada, o método saía na segunda linha e a legenda nunca era
            // tentada. Cada modo tem a sua condição, e nenhuma delas fala pelo
            // outro.
            if (cfg.LegendaAoVivo)
            {
                if (LegendaAoVivo.OQueImpede(motores, cfg) is { } porque)
                {
                    Registro.Escrever("legenda", $"legenda não ligou: {porque}");
                    return;
                }

                _legenda = new LegendaAoVivo(pasta, motores, Motores.Ambiente(),
                                             EmpurrarLegenda);
                _legenda.Comecar();
                return;
            }

            if (!cfg.TranscricaoAoVivo) return;

            if (SessaoAoVivo.OQueImpede(motores, cfg) is { } impede)
            {
                Registro.Escrever("aovivo", $"prévia não ligou: {impede}");
                return;
            }

            _aoVivo = new SessaoAoVivo(pasta, motores, Motores.Ambiente(),
                                       EmpurrarBlocoAoVivo,
                                       cfg.MotorDeTranscricao, cfg.ModeloPadrao);
            _aoVivo.Comecar();
        }
        catch (Exception e)
        {
            Registro.Escrever("aovivo", $"prévia não ligou: {e.Message}");
            _aoVivo = null;
        }
    }

    /// <summary>Descarta a prévia. Nunca lança, pela mesma razão.</summary>
    private void EncerrarAPrevia()
    {
        // **A pasta é capturada ANTES de zerar o campo**, e a ordem é o defeito
        // que a primeira reunião com o VIVO-2 encontrou: o `_pastaAoVivo = null`
        // vinha primeiro, a separação de falantes recebia nulo e nunca era
        // chamada — sem erro, sem linha no registro, e a tela prometendo
        // "separando falantes…" para sempre. Visto em 18/09/2026.
        string? pastaDaLegenda = _pastaAoVivo;
        _pastaAoVivo = null;

        // **A placa volta com a gravação.** Um motor de 3,2 GB órfão depois da
        // reunião é o pior desfecho desta chave, e é o que ninguém notaria.
        _motorQuente?.Dispose();
        _motorQuente = null;

        // **Fecha com graça, e em segundo plano.** O `finalize()` do motor é
        // quem devolve o texto quando nada firmou durante a reunião, e esperá-lo
        // aqui seguraria quem acabou de parar a gravação. O arquivo cai na pasta
        // um instante depois, que é cedo o bastante: ninguém lê a legenda
        // gravada antes de a tela de transcrever abrir.
        if (_legenda is { } legenda)
        {
            _legenda = null;
            _ = Task.Run(async () =>
            {
                try { await legenda.EncerrarAsync(TimeSpan.FromSeconds(30)); }
                catch (Exception e)
                {
                    Registro.Escrever("legenda", $"encerramento: {e.Message}");
                }

                // **Só depois do encerramento**, porque é ele que escreve o
                // legenda.json final — inclusive o texto que só o finalize()
                // solta quando nada firmou durante a reunião.
                if (pastaDaLegenda is { Length: > 0 }) SepararFalantes(pastaDaLegenda);
            });
        }

        try { _aoVivo?.Dispose(); }
        catch (Exception) { /* a gravação não pode parar por causa da prévia */ }
        _aoVivo = null;
    }

    /// <summary>
    /// Empurra um bloco da prévia ao vivo à página.
    /// </summary>
    /// <remarks>
    /// <b>Incremental por construção</b>, e isso é decisão de contrato e não
    /// otimização: os outros dois eventos empurrados — nível de áudio e registro
    /// de transcrições — mandam o estado inteiro a cada vez, e está certo, porque
    /// os dois cabem em centenas de bytes. <b>Uma transcrição não é pequena.</b>
    /// Mandar o transcrito inteiro a cada bloco é O(n²) ao longo da reunião, com
    /// o JSON.parse na thread que desenha — na tela do app que está gravando.
    /// Aqui vai só o bloco que chegou. Ver docs/FASE7-FRONTEND.md §F-12.
    /// </remarks>
    private void EmpurrarBlocoAoVivo(BlocoAoVivo b) =>
        Responder(new Resposta { Id = 0, Tipo = "aovivo", AoVivo = Resumir(b) });

    /// <summary>Empurra um pedaço da legenda. Mesmo canal do bloco, outro campo.</summary>
    private void EmpurrarLegenda(PedacoDaLegenda p) =>
        Responder(new Resposta
        {
            Id = 0, Tipo = "aovivo",
            Legenda = new PedacoDaLegendaJson
            {
                Novo = p.Novo, Tentativo = p.Tentativo, Dono = p.Dono,
            },
        });

    /// <summary>Um bloco, como a tela o recebe. O mesmo no evento e na pergunta.</summary>
    private static BlocoDaPrevia Resumir(BlocoAoVivo b) => new()
    {
        Numero = b.Numero,
        InicioS = b.InicioS,
        FimS = b.FimS,
        Estado = b.Estado,
        Trechos = [.. b.Trechos.Select(t => new TrechoDaPrevia
        {
            Start = t.Start,
            End = t.End,
            Text = t.Text,
            Speaker = t.Speaker,
        })],
    };

    /// <summary>Empurra o registro à página, sem ela ter pedido.</summary>
    private void EmpurrarTranscricoes() =>
        Responder(new Resposta { Id = 0, Tipo = "transcricoes", Transcricoes = Instantaneo() });

    /// <summary>
    /// Aceita a transcrição e devolve o controle na hora.
    /// </summary>
    /// <remarks>
    /// <para>
    /// O pipeline roda solto: a resposta a este pedido diz apenas que foi
    /// aceito, e etapa, fração e fim chegam pelo canal de eventos. Foi assim que
    /// a transcrição deixou de morrer ao trocar de tela — antes, quem desenhava
    /// a barra era o dono da promessa, e trocar de tela jogava fora o DOM em que
    /// ela escrevia (FASE3.md §2).
    /// </para>
    /// <para>
    /// <c>Task.Run</c> porque o pipeline bloquearia a thread da UI, que é a
    /// mesma que desenha a janela <b>e</b> a que atende a bandeja: sem isto, a
    /// barra congelaria justamente enquanto há progresso a mostrar, e o menu da
    /// bandeja não abriria durante uma transcrição.
    /// </para>
    /// </remarks>
    private void Transcrever(Pedido p)
    {
        if (p.Gravacao is not { Length: > 0 } pasta)
        {
            Responder(new Resposta { Id = p.Id, Erro = "sem gravação" });
            return;
        }

        // O que a tela mandou tem precedência, mas o silêncio dela não apaga o
        // que já estava: retranscrever com os campos em branco apagava o cliente
        // e o projeto guardados, e essa era a metade invisível do defeito.
        var vinculo = DadosDaReuniao.Ler(pasta);
        string? cliente = p.Cliente is { Length: > 0 } ? p.Cliente : vinculo.Cliente;
        string? projeto = p.Projeto is { Length: > 0 } ? p.Projeto : vinculo.Projeto;
        if (cliente != vinculo.Cliente || projeto != vinculo.Projeto)
            new DadosDaReuniao { Cliente = cliente, Projeto = projeto }.Salvar(pasta);

        // Lidas uma vez, aqui: dentro da tarefa elas seriam relidas do disco
        // enquanto o pipeline roda, e mudar a chave no meio de uma transcrição
        // não pode mudar o que aquela transcrição está fazendo.
        var cfgDaTranscricao = ConfiguracoesDoApp.Carregar();

        // Lança quando já há uma em curso, e a mensagem nomeia qual. O catch do
        // AtenderAsync a transforma na resposta de erro que a tela mostra.
        var trabalho = _transcricoes.Comecar(pasta, NomeDaGravacao(pasta));
        Responder(new Resposta { Id = p.Id, Transcricoes = Instantaneo() });
        EmpurrarTranscricoes();

        _ = Task.Run(async () =>
        {
            try
            {
                await _transcritor.ExecutarAsync(
                    pasta, p.Vocabulario, p.Idioma,
                    // A chave existe em Ajustes › Transcrição desde 14/08; até
                    // então o filtro só ligava por linha de comando, e a tela
                    // dizia isso num recado que ninguém podia agir.
                    filtrarSilencio: cfgDaTranscricao.FiltrarSilencio,
                    modelo: p.Modelo, cliente: cliente, projeto: projeto,
                    diarizar: p.Diarizar ?? true,
                    corrigirFonetica: cfgDaTranscricao.CorrecaoFonetica,
                    revisarComModelo: cfgDaTranscricao.RevisaoComModelo,
                    motorDeAta: CaminhosDoMotorDeAta.AoLadoDoExecutavel(cfgDaTranscricao.ModeloDeAta),
                    usarHotwords: cfgDaTranscricao.UsarHotwords,
                    // O projeto manda; sem preferência, o padrão do app. Os dois
                    // eram guardados e nunca lidos — ver Pedido.DiarModel.
                    modeloDeDiarizacao: p.DiarModel is { Length: > 0 } doProjeto
                        ? doProjeto : cfgDaTranscricao.DiarizacaoPadrao,
                    progresso: e =>
                    {
                        _transcricoes.Progredir(pasta, e.Etapa, e.Fracao, e.Texto);
                        EmpurrarTranscricoes();
                    },
                    ct: trabalho.Token);
                _transcricoes.Terminar(pasta);
                Avisar($"Transcrição pronta: {trabalho.Nome}");
            }
            catch (OperationCanceledException)
            {
                // Parar a pedido não é falha: a tela não mostra alerta vermelho,
                // e a bandeja não avisa — quem clicou em parar sabe que parou.
                _transcricoes.Terminar(pasta, cancelada: true);
            }
            catch (Exception e)
            {
                // O erro vira estado, e não mensagem perdida: quem saiu da tela
                // no meio precisa poder descobrir, ao voltar, que falhou.
                _transcricoes.Terminar(pasta, e.Message);
                Avisar($"A transcrição de {trabalho.Nome} falhou.");
            }
            EmpurrarTranscricoes();
        });
    }

    /// <summary>
    /// Copia a ata para a pasta de atas, com um nome que se acha depois.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pasta própria, separada da de transcrições</b> (pedido do dono do
    /// produto em 14/08/2026): os dois arquivos têm destino e finalidade
    /// diferentes. A transcrição é material de trabalho; a ata é o que se manda
    /// para fora — cliente, time, pasta do projeto.
    /// </para>
    /// <para>
    /// O nome carrega data, cliente e título porque a pasta de destino junta
    /// atas de reuniões diferentes: "ata.md" ali dentro seria inencontrável na
    /// segunda exportação, e sobrescreveria a primeira.
    /// </para>
    /// </remarks>
    private static string ExportarAta(Pedido p)
    {
        if (p.Gravacao is not { Length: > 0 } pasta)
            throw new InvalidOperationException("sem gravação");

        string origem = Path.Combine(pasta, "ata.md");
        if (!File.Exists(origem))
            throw new InvalidOperationException("esta reunião ainda não tem ata");

        var cfg = ConfiguracoesDoApp.Carregar();
        if (cfg.PastaDeAtas is not { Length: > 0 } destino)
            throw new InvalidOperationException(
                "escolha a pasta das atas em Ajustes › Geral antes de exportar");

        Directory.CreateDirectory(destino);

        var dados = DadosDaReuniao.Ler(pasta);
        string titulo = p.Nome is { Length: > 0 } ? p.Nome : Path.GetFileName(pasta);

        var partes = new[] { dados.Cliente, titulo }.Where(x => x is { Length: > 0 });
        string arquivo = Exportacao.NomeDeArquivo(
            string.Join(" - ", partes) + " - ata", "md", Transcritor.DataDaReuniao(pasta));

        string caminho = Path.Combine(destino, arquivo);
        File.Copy(origem, caminho, overwrite: true);
        return caminho;
    }

    /// <summary>
    /// Aceita a ata e devolve o controle, como a transcrição faz.
    /// </summary>
    /// <remarks>
    /// Mesmo registro da transcrição, e não um segundo: os dois trabalhos
    /// disputam a mesma placa, e a trava de um por vez é o que garante que o
    /// modelo de ata só carregue com a VRAM do ASR liberada. A bolinha do trilho
    /// acende para os dois pelo mesmo caminho.
    /// </remarks>
    /// <summary>
    /// Separa os falantes da legenda, em segundo plano, depois da reunião.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>É o <c>VIVO-2</c>.</b> Entrega <i>quem falou</i> no rascunho enquanto
    /// a pessoa ainda decide se vai transcrever — a diarização custa 32× o tempo
    /// real, ~2 min numa reunião de uma hora, contra a passada inteira.
    /// </para>
    /// <para>
    /// <b>Sobre o <c>system.wav</c>, e não sobre o mix</b>, pela mesma razão do
    /// <c>Transcritor</c>: o dono já veio da faixa do microfone, com certeza, e
    /// não precisa de estimativa por cima.
    /// </para>
    /// <para>
    /// <b>Nunca levanta, e nunca bloqueia.</b> O pior desfecho é a legenda
    /// ficar sem falante — que é como ela era até hoje.
    /// </para>
    /// </remarks>
    private void SepararFalantes(string pasta)
    {
        LegendaGravada? legenda;
        try
        {
            legenda = LegendaAoVivo.Ler(pasta);
        }
        catch (Exception) { return; }

        // Sem trecho não há o que sobrepor: é legenda de antes do VIVO-1, ou
        // reunião em que nada firmou.
        if (legenda is not { FalantesProntos: false, Trechos.Count: > 0 }) return;

        string sistema = Path.Combine(pasta, "system.wav");
        if (!File.Exists(sistema)) return;

        // **O registro recusa duas tarefas ao mesmo tempo, e recusar é legítimo:**
        // as duas disputariam a placa. O que não pode é morrer em silêncio — este
        // método roda dentro de um Task.Run, e a exceção não teria quem a
        // observasse.
        TrabalhoDeTranscricao trabalho;
        try
        {
            trabalho = _transcricoes.Comecar(pasta, NomeDaGravacao(pasta), "falantes");
        }
        catch (InvalidOperationException e)
        {
            // Sem separação, e a legenda não fica prometendo: marca como feita,
            // sem falante. O registro diz por quê.
            LegendaAoVivo.Gravar(pasta, legenda.Turnos, legenda.Trechos, prontos: true);
            Registro.Escrever("legenda", $"falantes não separados: {e.Message}");
            return;
        }
        EmpurrarTranscricoes();

        _ = Task.Run(async () =>
        {
            try
            {
                var cfg = ConfiguracoesDoApp.Carregar();
                var motores = Motores.AoLadoDoExecutavel();
                using var motor = await MotorSidecar.IniciarAsync(
                    motores.Python, [motores.ScriptDiarizacao],
                    trabalho.Token, Motores.Ambiente());
                // O SUP-1 pede que toda carga de GPU deixe rastro, e esta é a
                // terceira — depois do ASR e do reconhecimento de vozes.
                motor.AoRegistrar += l => Registro.Escrever("diarizacao", l);

                var diarizacao = await motor.DiarizarAsync(
                    sistema,
                    (f, t) =>
                    {
                        _transcricoes.Progredir(pasta, "falantes", f, t);
                        EmpurrarTranscricoes();
                    },
                    cfg.DiarizacaoPadrao, trabalho.Token);

                var comFalante = FalantesDaLegenda.Atribuir(legenda.Trechos, diarizacao);

                // **O mesmo banco de vozes da passada final**, e é o que torna
                // isto um pipeline só: os rótulos do pyannote são locais à
                // reunião, e quem os transforma em pessoa é o banco — que
                // melhora a cada reunião, porque é aí que ele ganha amostra.
                //
                // **Nunca derruba a separação.** Não reconhecer é o estado
                // normal de quem nunca foi apresentado, e um rótulo sem nome é
                // melhor que trecho nenhum.
                int nomeados = 0;
                try
                {
                    _transcricoes.Progredir(pasta, "falantes", 0.8,
                                            "procurando vozes conhecidas");
                    EmpurrarTranscricoes();

                    var faixas = Faixas.Ler(Path.Combine(pasta, "mic.wav"), sistema);
                    var conhecidos = await new AprendizadoDeVozes(motores, new Vozes())
                        .ReconhecerAsync(pasta, ComoSegmentos(comFalante), faixas.Mic,
                                         trabalho.Token);

                    if (conhecidos.Count > 0)
                    {
                        comFalante = [.. comFalante.Select(t =>
                            t.Falante is { } r && conhecidos.TryGetValue(r, out string? nome)
                                ? new TrechoDaLegenda
                                {
                                    InicioMs = t.InicioMs, FimMs = t.FimMs,
                                    Dono = t.Dono, Texto = t.Texto, Falante = nome,
                                }
                                : t)];
                        nomeados = conhecidos.Count;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    Registro.Escrever("legenda", $"vozes conhecidas: {e.Message}");
                }

                LegendaAoVivo.Gravar(pasta, legenda.Turnos, comFalante, prontos: true);

                _transcricoes.Terminar(pasta);
                Registro.Escrever("legenda",
                    $"falantes separados: {comFalante.Count} trechos, "
                    + $"{diarizacao.Count} segmentos de diarização, "
                    + $"{nomeados} reconhecidos pelo banco de vozes.");
            }
            catch (OperationCanceledException)
            {
                _transcricoes.Terminar(pasta, cancelada: true);
            }
            catch (Exception e)
            {
                // **A tela para de prometer.** Marcar como feita sem falante é
                // honesto — tentou-se e não saiu —, e deixar `false` faria o
                // aviso "separando falantes…" ficar para sempre, que é a tela
                // prometendo um resultado que não vem.
                try
                {
                    LegendaAoVivo.Gravar(pasta, legenda.Turnos, legenda.Trechos, prontos: true);
                }
                catch (Exception) { /* disco: o aviso fica, e é o menor dos males */ }

                _transcricoes.Terminar(pasta, e.Message);
                Registro.Escrever("legenda", $"falantes não separados: {e.Message}");
            }
            EmpurrarTranscricoes();
        });
    }

    /// <summary>
    /// Os trechos da legenda como o reconhecimento de vozes os espera.
    /// </summary>
    /// <remarks>
    /// <b>Conversão e não cópia de regra.</b> O <c>ReconhecerAsync</c> trabalha
    /// sobre <see cref="SegmentoFinal"/> porque é o que a passada final produz;
    /// dar a ele a mesma forma é o que permite os dois caminhos usarem o
    /// <b>mesmo</b> banco de vozes, em vez de dois que divergem.
    /// </remarks>
    private static List<SegmentoFinal> ComoSegmentos(IEnumerable<TrechoDaLegenda> trechos) =>
        [.. trechos.Select(t => new SegmentoFinal
        {
            Start = t.InicioMs / 1000.0,
            End = t.FimMs / 1000.0,
            Text = t.Texto,
            Speaker = t.Falante,
        })];

    /// <summary>
    /// Pergunta ao modelo o que já aconteceu na reunião.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>É a única operação que sobe um modelo enquanto se grava</b>, e por
    /// isso ela nasce curta: sobe, pergunta, morre. O motor quente a reunião
    /// inteira é o terceiro contexto CUDA que derrubou a legenda de 2,46× para
    /// 0,45× em 11/09/2026 (docs/FASE7-ROTA.md §4) — ver
    /// <see cref="PerguntaDaReuniao"/>.
    /// </para>
    /// <para>
    /// <b>O <c>gravacao</c> é opcional, e é o banco de ensaio</b>: apontando
    /// para uma pasta do acervo, a mesma pergunta roda sobre o
    /// <c>legenda.json</c> dela sem precisar de reunião acontecendo. É como as
    /// instruções do <see cref="PromptDeReuniao"/> se ajustam.
    /// </para>
    /// </remarks>
    private async Task PerguntarAoVivoAsync(Pedido p)
    {
        if (p.Pergunta is not { Length: > 0 } pergunta)
        {
            Responder(new Resposta { Id = p.Id, Erro = "sem pergunta" });
            return;
        }

        // O motor de ata não vem no instalador, e "não está lá" é o estado
        // normal de quem acabou de instalar. A frase do OQueFalta já diz onde
        // baixar — e dizê-la **antes** de montar o prompt evita a espera inútil.
        var cfg = ConfiguracoesDoApp.Carregar();
        var caminhos = CaminhosDoMotorDeAta.AoLadoDoExecutavel(cfg.ModeloParaPergunta);
        if (PerguntaDaReuniao.OQueImpede(cfg, caminhos) is { } falta)
        {
            Responder(new Resposta { Id = p.Id, Erro = falta });
            return;
        }

        string? pasta = p.Gravacao is { Length: > 0 } g ? g : _pastaAoVivo;
        if (pasta is null)
        {
            Responder(new Resposta
            {
                Id = p.Id,
                Erro = "não há reunião acontecendo — comece a gravar, ou escolha uma "
                     + "gravação já feita.",
            });
            return;
        }

        int limite = PerguntaDaReuniao.LimiteDeCaracteres(MetadadosDoGguf.Ler(caminhos.Modelo));
        var texto = LerAReuniaoAteAgora(pasta, limite);

        if (texto.Texto.Length == 0)
        {
            Responder(new Resposta
            {
                Id = p.Id,
                Erro = "ainda não há nada transcrito desta reunião. A legenda ao vivo ou a "
                     + "prévia em blocos precisam estar ligadas em Ajustes › Transcrição, "
                     + "e leva alguns segundos até a primeira fala firmar.",
            });
            return;
        }

        // **O botão e a caixa livre pedem coisas diferentes.** A instrução vai
        // depois da transcrição, que foi o ajuste que fez o modelo acertar o
        // assunto do fim (docs/ESTUDO-RESUMO-AO-VIVO.md §8).
        bool resumo = p.Resumo == true;
        string pedido = resumo
            ? PerguntaDaReuniao.Instrucao(resumo: true)
            : $"{pergunta}\n\n{PerguntaDaReuniao.Instrucao(resumo: false)}";

        // Um progresso só, e fixo. Não há o que medir: o modelo leva de 6 a 9
        // segundos para carregar e depois escreve de uma vez. Inventar uma
        // barra que anda sozinha seria mentir sobre o que está acontecendo.
        Responder(new Resposta
        {
            Id = p.Id, Tipo = "progresso", Etapa = "modelo", Fracao = 0.1,
            Texto = cfg.ModeloQuente && _motorQuente?.Aberto == true
                ? "lendo a reunião…" : "carregando o modelo e lendo a reunião…",
        });

        string resposta = cfg.ModeloQuente
            ? await PerguntarQuenteAsync(cfg, pedido, texto)
            : await _pergunta.ResponderAsync(pedido, texto, CancellationToken.None);

        Responder(new Resposta
        {
            Id = p.Id, RespostaDoModelo = resposta, Cortado = texto.Cortado,
        });
    }

    /// <summary>
    /// Pergunta ao motor que fica de pé, subindo-o na primeira vez.
    /// </summary>
    /// <remarks>
    /// <b>O contexto é pedido pelo pior caso da janela</b>, e não pelo tamanho
    /// da transcrição de agora: a sessão não sabe que pergunta virá, e subir de
    /// novo a cada minuto de reunião anularia o motivo de ela existir.
    /// </remarks>
    private async Task<string> PerguntarQuenteAsync(
        ConfiguracoesDoApp cfg, string pedido, TextoDaReuniao texto)
    {
        _motorQuente ??= new MotorQuente(ct =>
        {
            var motor = new MotorDeAta(
                CaminhosDoMotorDeAta.AoLadoDoExecutavel(cfg.ModeloParaPergunta));
            return motor.AbrirSessaoAsync(
                PromptDeReuniao.Sistema, nomeDoEsquema: "", esquema: "",
                PerguntaDaReuniao.JanelaMaximaCaracteres, PerguntaDaReuniao.TokensDeSaida, ct);
        }, MotorQuente.OciosoPadrao);

        // A ociosidade é conferida na própria pergunta: é o único momento em que
        // se sabe que há alguém olhando, e evita um relógio a mais no processo.
        _motorQuente.FecharSeOcioso(DateTime.UtcNow);

        return await _motorQuente.PerguntarAsync(
            PerguntaDaReuniao.Montar(texto, pedido), CancellationToken.None);
    }

    /// <summary>
    /// A reunião até agora, na melhor fonte que existir.
    /// </summary>
    /// <remarks>
    /// <b>A legenda primeiro, e o disco é a fonte</b> — não a instância viva.
    /// O <c>legenda.json</c> é reescrito a cada trecho que firma, então ele é
    /// tão fresco quanto a memória, e o mesmo caminho serve à gravação já
    /// encerrada do banco de ensaio.
    /// <para>
    /// <b>A repetição não é paranoia.</b> O <c>Gravar()</c> da legenda usa
    /// <c>File.WriteAllText</c>, que <b>trunca antes de escrever</b>; uma leitura
    /// no instante errado pega o arquivo pela metade, e o <c>Ler</c> devolve
    /// <c>null</c> para qualquer erro. Sem repetir, a pergunta feita no momento
    /// de um commit responderia "ainda não há nada transcrito" numa reunião
    /// cheia de texto.
    /// </para>
    /// </remarks>
    private TextoDaReuniao LerAReuniaoAteAgora(string pasta, int limite)
    {
        for (int tentativa = 0; tentativa < 3; tentativa++)
        {
            if (LegendaAoVivo.Ler(pasta) is { Turnos.Count: > 0 } legenda)
                return PerguntaDaReuniao.DaLegenda(legenda.Turnos, limite);

            if (!File.Exists(Path.Combine(pasta, LegendaAoVivo.Arquivo))) break;
            Thread.Sleep(50);
        }

        return PerguntaDaReuniao.DosBlocos(_aoVivo?.Entregues ?? [], limite);
    }

    /// <summary>
    /// Leva o prompt ao <c>llama-server</c> e devolve o texto da resposta.
    /// </summary>
    /// <remarks>
    /// Reusa o <c>ResponderAsync</c> do motor de ata, que já sobe, pergunta e
    /// mata — nenhum código de processo novo. O esquema de um campo só existe
    /// pela razão medida em 25/08: sem ele, um modelo de raciocínio delibera
    /// até estourar o limite sem emitir nada.
    /// </remarks>
    private static async Task<string> PerguntarAoMotorAsync(string prompt, CancellationToken ct)
    {
        var cfg = ConfiguracoesDoApp.Carregar();
        var motor = new MotorDeAta(
            CaminhosDoMotorDeAta.AoLadoDoExecutavel(cfg.ModeloParaPergunta));

        // **Sem esquema, e com as regras no sistema.** O esquema tem medição por
        // trás: ele custou quatro dos seis modelos comparados
        // (docs/ESTUDO-RESUMO-AO-VIVO.md §2). As regras, idem — cada linha delas
        // saiu de um defeito visto rodando.
        var respostas = await motor.ResponderAsync(
            PromptDeReuniao.Sistema, [prompt], nomeDoEsquema: "", esquema: "",
            PerguntaDaReuniao.TokensDeSaida, progresso: null, ct);

        return respostas[0] is { Length: > 0 } texto
            ? texto
            : "o modelo devolveu uma resposta vazia.";
    }

    private void GerarAta(Pedido p)
    {
        if (p.Gravacao is not { Length: > 0 } pasta)
        {
            Responder(new Resposta { Id = p.Id, Erro = "sem gravação" });
            return;
        }

        var tipo = ModelosDeAta.Buscar(p.Modelo)
            ?? throw new InvalidOperationException($"tipo de ata desconhecido: {p.Modelo}");

        string json = LerTranscricao(pasta)
            ?? throw new InvalidOperationException("esta reunião ainda não foi transcrita");
        var dados = ResultadoDaTranscricao.DeJson(json)
            ?? throw new InvalidOperationException("transcrição ilegível");

        var trabalho = _transcricoes.Comecar(pasta, NomeDaGravacao(pasta), "ata");
        Responder(new Resposta { Id = p.Id, Transcricoes = Instantaneo() });
        EmpurrarTranscricoes();

        _ = Task.Run(async () =>
        {
            try
            {
                var vinculo = DadosDaReuniao.Ler(pasta);
                var cfg = ConfiguracoesDoApp.Carregar();
                var (convidados, emails) = ConvidadosDaAgenda.Ler(pasta);
                // Quem é da casa e quem é do cliente sai do domínio do e-mail, e
                // não de dedução do modelo: ver Nucleo/Atas/Organizacoes.cs.
                // Nome de exibição e e-mail juntos: o e-mail diz o lado, o nome
                // diz como a pessoa é chamada. Ver Organizacoes.Classificar.
                var pessoas = Organizacoes.Classificar(
                    convidados, emails, cfg.DominiosDaCasa);
                // A partir daqui vale o nome canônico, e não o cru do meta.json.
                // Ele mistura nome próprio com local-part de e-mail na mesma
                // lista ("Andre Yuri" ao lado de "dimi.randel"), e o modelo copia
                // o que vê: numa ata gerada de ponta a ponta em 25/08 três
                // responsáveis saíram como "dimi.randel", "andre.monlevade" e
                // "thiago.souza". Ver Organizacoes.Classificar.
                if (pessoas.Count > 0) convidados = [.. pessoas.Select(p => p.Nome)];

                var ctx = new ContextoDaReuniao
                {
                    Titulo = Listar().FirstOrDefault(g => g.Caminho == pasta)?.Titulo,
                    Convidados = convidados,
                    Pessoas = pessoas,
                    Cliente = vinculo.Cliente ?? dados.Client,
                    Projeto = vinculo.Projeto ?? dados.Project,
                    Data = dados.Date ?? Transcritor.DataDaReuniao(pasta),
                    DuracaoS = dados.Duration ?? 0,
                    Falantes = [.. dados.Segments.Select(s => s.Speaker)
                        .Where(s => s is { Length: > 0 }).Distinct()!],
                    Notas = Notas.Ler(pasta),
                    Vocabulario = _projetos.Preferencias(
                        vinculo.Cliente ?? "", vinculo.Projeto ?? "")?.InitialPrompt ?? "",
                };

                var roteiro = RoteiroDeFatos.De(dados.Segments);
                string prompt = PromptDeAta.Montar(tipo, ctx, dados.Segments, roteiro);

                var motor = new MotorDeAta(
                    CaminhosDoMotorDeAta.AoLadoDoExecutavel(cfg.ModeloDeAta));

                var ata = await motor.GerarAsync(prompt, ctx.DuracaoS, e =>
                {
                    _transcricoes.Progredir(pasta, e.Etapa, e.Fracao, e.Texto);
                    EmpurrarTranscricoes();
                }, trabalho.Token);

                VerificadorDeAta.Conferir(ata, dados.Segments,
                    [.. ctx.Convidados.Concat(ctx.Falantes)], roteiro, pessoas);

                File.WriteAllText(Path.Combine(pasta, "ata.md"),
                                  RedatorDeAta.Escrever(ata, tipo, ctx));
                File.WriteAllText(Path.Combine(pasta, "ata.json"), ata.ParaJson());

                _transcricoes.Terminar(pasta);
                Avisar($"Ata pronta: {trabalho.Nome}");
            }
            catch (OperationCanceledException)
            {
                _transcricoes.Terminar(pasta, cancelada: true);
            }
            catch (Exception e)
            {
                _transcricoes.Terminar(pasta, e.Message);
                Avisar($"A ata de {trabalho.Nome} falhou.");
            }
            EmpurrarTranscricoes();
        });
    }

    /// <summary>
    /// Os convidados que a agenda gravou, com os e-mails quando existirem.
    /// </summary>
    /// <remarks>
    /// <c>attendee_emails</c> é chave nova (14/08/2026): as gravações anteriores
    /// só têm os nomes, e nelas a organização de cada um fica desconhecida — o
    /// que é melhor que fingir saber.
    /// </remarks>

    /// <summary>
    /// Como chamar a reunião numa frase: o título da agenda, ou a pasta.
    /// </summary>
    /// <remarks>
    /// O mesmo nome que a lista mostra, para o aviso de "já estou transcrevendo
    /// X" citar o que a pessoa vê na tela, e não um caminho de disco.
    /// </remarks>
    private string NomeDaGravacao(string pasta)
    {
        string nome = Path.GetFileName(pasta.TrimEnd(Path.DirectorySeparatorChar));
        foreach (var g in Listar())
            if (g.Caminho == pasta) return g.Titulo is { Length: > 0 } t ? t : g.Nome;
        return nome;
    }

    private Dictionary<string, List<string>> MapaDeClientes()
    {
        var mapa = new Dictionary<string, List<string>>();
        foreach (string c in _projetos.ListarClientes()) mapa[c] = _projetos.ListarProjetos(c);
        return mapa;
    }

    /// <summary>
    /// Escreve a transcrição no formato pedido, ao lado da gravação.
    /// </summary>
    /// <remarks>
    /// Ao lado da gravação, e não em Downloads: o arquivo pertence àquela
    /// reunião, e quem procurar por ele daqui a um mês vai procurar na pasta
    /// dela. O caminho volta para a UI poder mostrar onde ficou.
    /// </remarks>
    private static (string Arquivo, string? Copia) Exportar(Pedido p)
    {
        if (p.Gravacao is not { Length: > 0 } pasta)
            throw new InvalidOperationException("sem gravação");

        string json = LerTranscricao(pasta)
            ?? throw new InvalidOperationException("esta gravação ainda não foi transcrita");
        var dados = ResultadoDaTranscricao.DeJson(json)
            ?? throw new InvalidOperationException("transcrição ilegível");

        bool comFalantes = p.ComFalantes ?? true;
        string titulo = p.Nome is { Length: > 0 } ? p.Nome : Path.GetFileName(pasta);
        string formato = p.Formato ?? "txt";

        // O que a tela mandou tem precedência: ela mostra o que estava
        // guardado e deixa corrigir, então o valor que chega aqui é o que a
        // pessoa acabou de confirmar. E o que ela preencher volta para o
        // arquivo, senão precisaria digitar de novo na próxima exportação.
        string? cliente = p.Cliente is { Length: > 0 } ? p.Cliente : dados.Client;
        string? projeto = p.Projeto is { Length: > 0 } ? p.Projeto : dados.Project;
        string? data = dados.Date ?? Transcritor.DataDaReuniao(pasta);

        if (cliente != dados.Client || projeto != dados.Project || data != dados.Date)
        {
            dados.Client = cliente;
            dados.Project = projeto;
            dados.Date = data;
            SalvarTranscricao(pasta, dados.ParaJson());
        }

        var cabecalho = Cabecalho.De(dados, titulo, cliente, projeto, data);

        string destino = Path.Combine(pasta,
            Exportacao.NomeDeArquivo(titulo, formato, cabecalho.Data));

        switch (formato)
        {
            case "txt": File.WriteAllText(destino, Exportacao.Txt(dados, comFalantes, cabecalho)); break;
            case "srt": File.WriteAllText(destino, Exportacao.Srt(dados, comFalantes, cabecalho)); break;
            case "vtt": File.WriteAllText(destino, Exportacao.Vtt(dados, comFalantes, cabecalho)); break;
            case "docx": Exportacao.Docx(dados, destino, titulo, comFalantes, cabecalho); break;
            default: throw new InvalidOperationException($"formato desconhecido: {formato}");
        }

        // A cópia é secundária de propósito: o original fica sempre junto da
        // gravação, e a pasta escolhida é para levar o arquivo a outro lugar —
        // rede, nuvem, Downloads. Se a cópia falhar, a exportação já aconteceu.
        string? copia = null;
        if (p.Copiar == true)
        {
            var cfg = ConfiguracoesDoApp.Carregar();
            string? escolhida = cfg.PastaDeExportacao;

            if (escolhida is not { Length: > 0 } || !Directory.Exists(escolhida))
                escolhida = SeletorDePasta.Escolher(IntPtr.Zero,
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Onde salvar a cópia");

            if (escolhida is { Length: > 0 })
            {
                copia = Path.Combine(escolhida, Path.GetFileName(destino));
                File.Copy(destino, copia, overwrite: true);

                // Lembrar a escolha: quem exporta uma vez para a pasta do
                // cliente costuma exportar as próximas para lá também.
                cfg.PastaDeExportacao = escolhida;
                cfg.Salvar();
            }
        }
        return (destino, copia);
    }

    /// <summary>
    /// Aprende a voz de um falante recém-nomeado.
    /// </summary>
    /// <remarks>
    /// Roda fora da thread da UI e nunca lança para fora: o nome já foi
    /// aplicado à transcrição, e falhar em aprender a voz não pode desfazer
    /// isso nem travar a janela.
    /// </remarks>
    private async Task AprenderVozAsync(Pedido p)
    {
        if (p.Gravacao is not { Length: > 0 } pasta
            || p.Falante is not { Length: > 0 } falante
            || p.Nome is not { Length: > 0 } nome)
        {
            Responder(new Resposta { Id = p.Id, Voz = "" });
            return;
        }

        string? json = LerTranscricao(pasta);
        if (json is null)
        {
            Responder(new Resposta { Id = p.Id, Voz = "" });
            return;
        }

        var dados = ResultadoDaTranscricao.DeJson(json);
        if (dados is null)
        {
            Responder(new Resposta { Id = p.Id, Voz = "" });
            return;
        }

        // O microfone vai junto, e é lido aqui e não no núcleo: inscrever uma
        // voz é a decisão que persiste entre reuniões, e um bloco em que o dono
        // estava falando envenena o perfil em silêncio. LerUma e não Ler — só a
        // energia do microfone interessa, e a outra faixa seria uma cópia de
        // centenas de MB para nada. Ver AprendizadoDeVozes.TrechosDe.
        var amostra = await Task.Run(() =>
        {
            string caminhoDoMic = Path.Combine(pasta, "mic.wav");
            float[]? mic = File.Exists(caminhoDoMic) ? Faixas.LerUma(caminhoDoMic) : null;
            return new AprendizadoDeVozes(Motores.AoLadoDoExecutavel(), new Vozes())
                // O motor vai junto: nomear um falante de uma transcrição do
                // MOSS grava um vetor de identidade COSTURADA, e a costura pode
                // ter fundido duas pessoas (docs/FASE7-RESULTADOS.md §11.4). A
                // origem não impede a inscrição — permite desfazê-la em bloco.
                .AprenderAsync(pasta, dados.Segments, falante, nome, mic,
                               motor: dados.Engine);
        });

        Responder(new Resposta
        {
            Id = p.Id,
            Voz = amostra is null ? "pouca fala limpa para aprender a voz"
                : amostra.Quarentena ? $"voz de {nome} guardada, aguardando revisão"
                : $"voz de {nome} aprendida",
        });
    }

    /// <summary>Abre o diálogo de pasta do Windows e devolve o que foi escolhido.</summary>
    /// <remarks>
    /// Digitar caminho à mão é onde os erros moram — barra invertida trocada,
    /// espaço no fim, pasta que não existe. O diálogo do sistema não erra
    /// nenhum dos três, e é o mesmo que a exportação já usava.
    /// </remarks>
    private static string? EscolherPasta(string? inicial)
    {
        string ponto = inicial is { Length: > 0 } && Directory.Exists(inicial)
            ? inicial
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return SeletorDePasta.Escolher(IntPtr.Zero, ponto, "Escolher pasta");
    }

    /// <summary>
    /// Apaga uma gravação inteira: os WAVs, o meta e a transcrição.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pasta é conferida contra a raiz das gravações antes de qualquer coisa.
    /// A página manda um caminho, e caminho vindo da tela que chega direto a um
    /// <c>Directory.Delete(recursive)</c> é um apagador de disco controlado pelo
    /// HTML — basta um <c>..\..\</c> para virar outra coisa.
    /// </para>
    /// <para>
    /// É a operação mais destrutiva do app: leva junto o áudio original, que não
    /// se refaz. Quem chama é responsável por confirmar antes.
    /// </para>
    /// </remarks>
    private void ApagarGravacao(string? pasta)
    {
        if (pasta is not { Length: > 0 })
            throw new InvalidOperationException("sem gravação");

        string alvo = Path.GetFullPath(pasta);
        string raiz = Path.GetFullPath(pastaDasGravacoes);

        // O separador no fim impede que "…/gravacoes-antigas" passe por estar
        // sob "…/gravacoes" por prefixo de texto.
        if (!alvo.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "esta pasta não está na pasta das gravações");

        if (alvo.TrimEnd(Path.DirectorySeparatorChar)
                .Equals(raiz.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("isso apagaria todas as gravações");

        if (!Directory.Exists(alvo))
            throw new InvalidOperationException("a gravação não está mais lá");

        Directory.Delete(alvo, recursive: true);
    }

    /// <summary>
    /// Baixa um pacote de modelo, relatando andamento à tela.
    /// </summary>
    /// <remarks>
    /// O motor de modelos sobe e cai por download: diferente do ASR e da
    /// diarização, não há nada quente para preservar entre chamadas, e um
    /// processo Python parado à toa é memória sem contrapartida.
    /// </remarks>
    private async Task BaixarPacoteAsync(Pedido p)
    {
        var pacote = Catalogo.Pacotes.FirstOrDefault(x => x.Id == p.Modelo)
            ?? throw new InvalidOperationException($"não conheço o pacote {p.Modelo}");

        var motores = Motores.AoLadoDoExecutavel();
        if (!File.Exists(motores.ScriptModelos))
            throw new MotorException(
                $"o motor de modelos não está em {motores.ScriptModelos}");

        // Cabe no disco? A margem de 10% cobre o que o cache do HuggingFace
        // gasta além do peso do modelo — blobs mais links, mais o arredondamento
        // do sistema de arquivos. Sem esta pergunta, o pior desfecho é acabar o
        // espaço no meio de 3 GB: fica um pacote parcial, e a falha aparece na
        // próxima transcrição, longe de onde foi causada.
        long livre = Catalogo.LivreNoDestino(pacote);
        long preciso = (long)(pacote.TamanhoEsperadoBytes * 1.1);
        if (livre >= 0 && livre < preciso)
            throw new InvalidOperationException(
                $"não cabe: {pacote.Nome} precisa de {preciso / 1_000_000_000.0:0.#} GB "
                + $"e há {livre / 1_000_000_000.0:0.#} GB livres no disco de destino.");

        using (var motor = await MotorSidecar.IniciarAsync(
                   motores.Python, [motores.ScriptModelos], CancellationToken.None,
                   Motores.Ambiente()))
        {
            await motor.BaixarAsync(
                pacote.Repositorio,
                // **Onde o GGUF avulso cai, e o erro que isto já cometeu.** Nas
                // famílias de arquivo único o destino é o ARQUIVO, e não a pasta
                // de cache: quem o abre é o llama.cpp (ata) ou o transcribe.cpp
                // (moss), os dois por caminho.
                //
                // Esta linha dizia `Familia == "ata"` e ficou para trás quando a
                // família "moss" chegou, em 04/09/2026. O download funcionou —
                // 0,70 GB, íntegros — e gravou o modelo NO LUGAR da pasta:
                // `motores/moss/modelos` virou um arquivo de 700 MB, o motor
                // procurou `modelos/MOSS-...gguf`, não achou, e a tela continuou
                // dizendo "ausente" sobre um download que deu certo.
                //
                // O `Catalogo.EhArquivoAvulso` é o lugar único dessa pergunta;
                // repeti-la aqui foi o que permitiu as duas discordarem.
                Catalogo.EhArquivoAvulso(pacote)
                    ? Catalogo.ArquivoDoPacote(pacote) : Catalogo.PastaDoPacote(pacote),
                pacote.TamanhoEsperadoBytes,
                (pct, texto) =>
                Responder(new Resposta
                {
                    Id = p.Id,
                    Tipo = "progresso",
                    Etapa = "baixando",
                    Fracao = pct,
                    Texto = texto,
                }),
                arquivo: pacote.Arquivo);
        }

        // O catálogo relê o disco: a tela nunca acredita no que o download
        // disse ter feito, só no que está lá.
        Responder(new Resposta
        {
            Id = p.Id,
            Catalogo = Catalogo.Listar(ConfiguracoesDoApp.Carregar()),
        });
    }

    /// <summary>Apaga um pacote do cache.</summary>
    /// <remarks>
    /// Só apaga pasta de pacote que está no catálogo, e o caminho é montado
    /// aqui a partir do repositório conhecido — nunca vem da página. Um
    /// caminho vindo da tela seria um apagador recursivo controlado pelo HTML.
    /// </remarks>
    private static void RemoverPacote(string? id)
    {
        var pacote = Catalogo.Pacotes.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException($"não conheço o pacote {id}");

        string pasta = Catalogo.PastaDoPacote(pacote);
        if (Directory.Exists(pasta)) Directory.Delete(pasta, recursive: true);
    }

    /// <summary>
    /// A biblioteca de vozes achatada para a tela.
    /// </summary>
    /// <remarks>
    /// A quarentena aparece <b>junto</b> das demais amostras da pessoa, e não
    /// numa lista à parte: quem decide se a amostra estranha é contaminação ou
    /// uma condição nova legítima precisa ver as outras amostras da mesma pessoa
    /// ao lado. Separar em duas telas obrigaria a decidir sem a comparação, que
    /// é justamente o que a decisão exige.
    /// </remarks>
    private static List<PessoaResumo> VozesConhecidas()
    {
        var vozes = new Vozes();
        var lista = new List<PessoaResumo>();

        foreach (string pessoa in vozes.Pessoas())
        {
            var perfil = vozes.Perfil(pessoa);
            if (perfil is null) continue;

            var resumo = new PessoaResumo { Nome = pessoa };
            for (int i = 0; i < perfil.Amostras.Count; i++)
            {
                var a = perfil.Amostras[i];
                resumo.Amostras.Add(new AmostraResumo
                {
                    Indice = i,
                    CriadaEm = a.CriadaEm,
                    DuracaoS = a.DuracaoS,
                    Gravacao = a.Origem.Gravacao,
                    Faixa = a.Origem.Faixa,
                    T0 = a.Origem.T0,
                    T1 = a.Origem.T1,
                    Dispositivo = a.Origem.Dispositivo,
                    Quarentena = a.Quarentena,
                    OutroModelo = Vozes.ModeloDe(a) != Vozes.ModeloDeVozPadrao,
                    RegrasAntigas = Vozes.RegrasDe(a) != Vozes.RegrasAtuais,
                    // Conferir que o arquivo existe, e não só que o campo está
                    // preenchido: amostra antiga pode apontar para um recorte
                    // que foi apagado, e a tela precisa desabilitar o play em
                    // vez de oferecer um som que não vem.
                    Trecho = a.Trecho is { Length: > 0 }
                             && File.Exists(vozes.CaminhoDoTrecho(a.Trecho))
                                 ? a.Trecho.Replace('\\', '/') : null,
                });
            }
            lista.Add(resumo);
        }
        return lista;
    }

    /// <summary>
    /// Grava a transcrição editada por cima da que estava lá.
    /// </summary>
    /// <remarks>
    /// Escrita atômica pelo mesmo motivo do resto do projeto: a alternativa é
    /// um desligamento no meio deixar o arquivo pela metade, e aqui isso
    /// custaria a revisão inteira de uma reunião.
    /// </remarks>
    private static void SalvarTranscricao(string? pasta, string? conteudo)
    {
        if (pasta is not { Length: > 0 } || conteudo is not { Length: > 0 })
            throw new InvalidOperationException("nada para salvar");

        // Conferir que é JSON antes de gravar: escrever lixo aqui apagaria a
        // transcrição, e o erro só apareceria na próxima abertura.
        using (JsonDocument.Parse(conteudo)) { }

        string destino = Path.Combine(pasta, "transcricao.json");
        string tmp = destino + ".tmp";
        File.WriteAllText(tmp, conteudo);
        File.Move(tmp, destino, overwrite: true);
    }

    private static string? LerTranscricao(string? pasta)
    {
        if (pasta is not { Length: > 0 }) return null;
        string caminho = Path.Combine(pasta, "transcricao.json");
        return File.Exists(caminho) ? File.ReadAllText(caminho) : null;
    }

    private void Responder(Resposta r) =>
        responder(JsonSerializer.Serialize(r, PonteJson.Default.Resposta));

    /// <summary>
    /// Um balão da bandeja, e nunca uma exceção que suba.
    /// </summary>
    /// <remarks>
    /// O aviso é conveniência; a transcrição já terminou quando ele sai. Deixar
    /// uma falha de Shell_NotifyIcon derrubar a tarefa perderia o
    /// <c>EmpurrarTranscricoes</c> que vem depois — e aí a tela ficaria com a
    /// barra parada para sempre, que é justamente o defeito que esta fase
    /// conserta.
    /// </remarks>
    private void Avisar(string texto)
    {
        try { avisar(texto); }
        catch { /* a bandeja pode estar indo embora; o estado já foi registrado */ }
    }

    /// <summary>As gravações que o gravador deixou, mais recentes primeiro.</summary>
    private List<GravacaoResumo> Listar()
    {
        if (!Directory.Exists(pastaDasGravacoes)) return [];

        var lista = new List<GravacaoResumo>();
        foreach (string pasta in Directory.EnumerateDirectories(pastaDasGravacoes))
        {
            string meta = Path.Combine(pasta, "meta.json");
            if (!File.Exists(meta)) continue;

            try
            {
                lista.Add(LerResumo(pasta, meta));
            }
            catch (Exception)
            {
                // Uma gravação ilegível não pode esconder as outras da lista.
            }
        }
        return [.. lista.OrderByDescending(g => g.Nome)];
    }

    private static GravacaoResumo LerResumo(string pasta, string caminhoMeta)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(caminhoMeta));
        var raiz = doc.RootElement;

        double duracao = raiz.TryGetProperty("duration_s", out var d) ? d.GetDouble() : 0;
        var avisos = new List<string>();

        if (raiz.TryGetProperty("tracks", out var faixas))
        {
            foreach (var faixa in faixas.EnumerateObject())
            {
                string nome = faixa.Name == "mic" ? "microfone" : "áudio do sistema";
                var t = faixa.Value;

                if (t.TryGetProperty("no_audio", out var sem) && sem.GetBoolean())
                    avisos.Add($"O {nome} não teve áudio nenhum.");
                else if (t.TryGetProperty("usable_pct", out var util) && util.GetDouble() < 20)
                    avisos.Add($"O {nome} tem só {util.GetDouble():F0}% de conteúdo útil.");

                // Campo novo do gravador nativo, que até agora ninguém lia.
                if (t.TryGetProperty("disconnected", out var caiu) && caiu.GetBoolean())
                    avisos.Add($"O dispositivo do {nome} caiu durante a gravação.");
            }
        }

        string? titulo = null;
        int convidados = 0;
        if (raiz.TryGetProperty("meeting", out var reuniao))
        {
            if (reuniao.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String)
                titulo = t.GetString();
            if (reuniao.TryGetProperty("attendees", out var a) && a.ValueKind == JsonValueKind.Array)
                convidados = a.GetArrayLength();
        }

        // O vínculo com cliente/projeto vem junto na lista, e não por pedido
        // separado: são dois campos por gravação, e um pedido por cartão faria a
        // lista piscar preenchendo-se aos poucos.
        var dados = DadosDaReuniao.Ler(pasta);

        // Um parcial é texto de verdade — e é meia transcrição. A gravação conta
        // como transcrita porque a reunião está lá para ler, mas quem abrir
        // precisa saber por que ninguém tem nome. Ver Nucleo/Retomada.cs.
        if (Retomada.EstaPendente(pasta))
            avisos.Add("A transcrição foi interrompida antes de separar os falantes. "
                       + "Transcreva de novo para completá-la — o texto já pronto é aproveitado.");

        return new GravacaoResumo
        {
            Nome = Path.GetFileName(pasta),
            Caminho = pasta,
            DuracaoS = duracao,
            Titulo = titulo,
            Convidados = convidados,
            Transcrita = File.Exists(Path.Combine(pasta, "transcricao.json")),
            Cliente = dados.Cliente,
            Projeto = dados.Projeto,
            ComNotas = Notas.Existem(pasta),
            Avisos = avisos,
        };
    }
}
