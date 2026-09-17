# Mudanças

Escrito para quem usa o app, não para quem o compila. O histórico técnico está
nos commits e nos `docs/*-HANDOFF.md`.

## 0.7.0-rc14 — 17/09/2026

**Dá para perguntar sobre a reunião enquanto ela acontece.** No Gravador, um
bloco novo: o botão **"O que já rolou?"** devolve os assuntos em lista, do mais
recente para o mais antigo, e a caixa ao lado responde qualquer pergunta sobre o
que já foi dito — quem ficou de quê, o que ficou decidido, sobre o que estão
falando agora. As respostas se acumulam, e você pode puxar o detalhe de um
assunto sem perder o que perguntou antes.

Nasce **desligado**, em Ajustes › Transcrição. Cada pergunta leva cerca de vinte
segundos, e enquanto o modelo responde a legenda engasga — ele ocupa a placa e a
devolve logo depois.

**O modelo lê no máximo a última hora de reunião.** Acima disso o começo sai, e a
resposta diz que saiu — passada uma hora, o início costuma não ser o que alguém
precisa para se situar, e ler a reunião inteira não caberia na placa junto com a
legenda.

**Você escolhe o modelo da pergunta separado do da ata**, em Ajustes › Modelos.
São tarefas diferentes: a ata roda com a reunião encerrada e a placa livre, e
pode pagar um modelo grande; a pergunta divide a placa com a legenda e tem vinte
segundos para responder.

**E dá para deixar o modelo pronto entre perguntas**, se você vai perguntar
várias coisas seguidas. A primeira continua levando os vinte segundos; as
seguintes respondem quase na hora. Em troca ele segura cerca de 3 GB da placa
enquanto espera, então essa chave também nasce desligada.

**A legenda ficou mais rápida e parou de tomar a máquina.** Ela pedia ao sistema
mais threads de processador do que o computador tem núcleos, e perdia tempo na
disputa. Medido numa reunião real: **36% mais rápida usando 43% menos
processador**, com o mesmo texto. Em máquina que grava, abre o Teams e o
navegador ao mesmo tempo, é a diferença entre o texto acompanhar a fala ou
atrasar.

## 0.7.0-rc13 — 15/09/2026

**A legenda ao vivo escreve.** Ela congelava no terceiro pedaço de áudio por uma
diferença de uma amostra — pedia um pedaço de tamanho exato, recebia um a menos,
e ficava pedindo o mesmo para sempre. Agora ela acompanha a fala do começo ao
fim da reunião.

**O texto leva cerca de 15 segundos para começar a firmar.** O modelo só confirma
uma palavra quando tem certeza, e essa certeza vem de ouvir o que veio depois.
Antes disso aparece o texto em cinza, que ainda pode mudar.

## 0.7.0-rc12 — 14/09/2026

**A legenda ao vivo funciona.** Ela lia o arquivo de áudio do jeito certo para
uma gravação já encerrada — esperando o cabeçalho dizer quanto áudio existe —, e
esse cabeçalho só é atualizado a cada dez segundos. Resultado: ela via um arquivo
vazio, concluía que a reunião tinha acabado, e encerrava sem escrever nada.
Agora ela lê o áudio que está em disco.

## 0.7.0-rc11 — 14/09/2026

**A legenda desistia antes de começar.** Ela sobe junto com a gravação, e o
arquivo de áudio ainda não existe nesse instante — a legenda lia isso como "a
reunião acabou" e encerrava com zero texto, no mesmo segundo em que o modelo
terminava de carregar. Agora ela espera o áudio chegar.

## 0.7.0-rc10 — 14/09/2026

**A legenda ao vivo não ligava nunca.** Com a prévia em blocos desligada — que é
exatamente a configuração que a legenda precisa —, o app desistia antes de
tentar. Ela agora tem caminho próprio, e nada dela depende da outra.

## 0.7.0-rc9 — 14/09/2026

**O painel dizia "em blocos de 3 minutos" mesmo quando a legenda era o que
estava ligado.** Agora ele diz o que está de fato rodando — e, quando nada está
ligado, diz isso em vez de prometer um texto que não vem.

## 0.7.0-rc8 — 14/09/2026

**A legenda ao vivo não ligava se a prévia em blocos estivesse ligada.** A tela
dizia que ligar uma desligava a outra, e não desligava — as duas ficavam
ativas, o bloco ganhava, e a legenda não acontecia sem nada explicar. Agora as
chaves se excluem de verdade, e quando alguma coisa impede a legenda o motivo
vai para o registro.

## 0.7.0-rc7 — 12/09/2026

**Baixar o modelo da legenda funcionava e não servia para nada.** O download ia
até o fim, demorava os 750 MB, e o botão "Baixar" voltava como se nada tivesse
acontecido — o arquivo era escrito no caminho da pasta em vez de dentro dela.
Quem já baixou não precisa baixar de novo.

## 0.7.0-rc6 — 12/09/2026

**A legenda fica para ler depois.** Encerrada a reunião, o que ela ouviu aparece
na tela de transcrever, num bloco que você abre — dá para conferir se o que foi
dito está lá antes de decidir gastar a placa com a passada inteira.

**É rascunho, e a tela diz isso.** O texto vem do modelo rápido, sem separação de
falantes; a transcrição continua sendo outra coisa, e é a que fica. Ela roda
normalmente, com o modelo inteiro, quando você mandar.

**Ele é escrito a cada fala, não no fim.** Se a máquina desligar no meio da
reunião, o que já foi dito continua lá.

## 0.7.0-rc5 — 12/09/2026

**A legenda ao vivo chegou.** Enquanto você grava, o texto aparece quase no
instante da fala — não a cada três minutos. Ele entra na mesma conversa da
prévia: a sua fala de um lado, a dos outros do outro.

**O que ainda pode mudar aparece diferente.** O motor firma o texto aos poucos, e
o que ele ainda pode reescrever fica apagado e em itálico, com borda tracejada.
Quando firma, vira texto normal. Sem isso a tela pareceria trocar palavra
sozinha.

**A legenda e a prévia em blocos não ligam juntas, e o app diz por quê.** As duas
não cabem na mesma placa: medindo numa reunião real, a legenda sozinha acompanha
a fala com folga e, com a prévia em blocos junto, passa a atrasar sem parar.
Ligando a legenda, a de cima fica desligada.

**Ela nasce desligada**, em Ajustes › Transcrição, como tudo o que roda durante a
gravação.

## 0.7.0-rc4 — 10/09/2026

**A transcrição durante a reunião finalmente aparece.** Ela existia desde a rc2,
ligava certo e transcrevia certo — e a tela ficava em branco de qualquer jeito,
inclusive a dica dos três primeiros minutos. Eram dois defeitos em sequência, um
escondendo o outro, e nenhum dava erro visível.

**Ela agora tem forma de conversa: a sua fala de um lado, a dos outros do
outro.** Antes ela dizia "Falante 1", "Falante 4" — e isso era uma afirmação que
o app não tinha como sustentar ao vivo, porque quem separa as pessoas só enxerga
três minutos por vez e acaba inventando gente demais. O que ele sabe com certeza
é o que passou pelo seu microfone, e agora é só isso que ele afirma. Quem é cada
um dos outros continua vindo na transcrição do fim, que vê a reunião inteira.
De quebra, sem a coluna de nome sobra largura para o texto.

**E ela funciona com os dois motores.** Antes exigia o MOSS, porque precisava do
falante junto; sem falante, o motor de sempre serve igual. Quem nunca baixou o
MOSS pode ligar a prévia do mesmo jeito.

**O Gravador voltou a ter duas colunas de verdade.** A agenda e os outros
blocos escorregavam para debaixo da transcrição.

**O app passou a anotar em que etapa estava quando parou.** Se a máquina
desligar no meio de uma transcrição, o próximo início diz qual etapa estava
rodando — no registro e no bloco de diagnóstico. É para o computador que desliga
sozinho durante a transcrição: até agora, descobrir onde ele caía dependia de
perguntar.

## 0.7.0-rc3 — 09/09/2026

**"6 participantes" virou "6 convidados", no Gravador e nos Ajustes.** A lista
vem do convite da agenda: ela diz quem foi **chamado**, não quem apareceu. Numa
reunião de seis convidados em que três entram, o número antigo era simplesmente
falso — e na tela do app que está gravando a reunião. A palavra certa já estava
em uso em dois outros lugares; estes dois ficaram para trás.

**A prévia ao vivo agora começa de verdade.** No rc2 ela nunca ligava: a prévia
se prendia ao botão de gravar da janela, e gravar tem três portas — o botão, o
ícone da bandeja e o menu dela. Quem começava pela bandeja gravava a reunião
inteira com o painel vazio, sem erro nenhum na tela. Agora ela se prende ao
gravador em si, e não à porta por onde se entrou.

**E não fica mais vazia se você chegar no meio.** Abrir o Gravador no minuto 20
mostrava um painel em branco até o bloco seguinte fechar, porque os blocos de
antes já tinham passado. Eles ficam guardados e aparecem de uma vez.

**Dá para juntar duas vozes que são a mesma pessoa.** "André Yuri" e "Andre
Yuri" viravam dois perfis, e cada um reconhecia pior do que um só reconheceria —
quanto mais a pessoa era nomeada, pior ficava. Em Ajustes › Vozes, cada pessoa
tem agora um "Juntar com…" que move as amostras para o outro perfil, sem perder
nenhuma e sem perder de onde vieram. O destino se escolhe numa lista de quem já
existe, e não digitando: digitar de novo é justamente o gesto que criou a segunda
grafia.

**Cada voz abre e fecha.** Quem tem muitas amostras rolava a página inteira para
chegar na pessoa seguinte. Agora cada uma é uma linha com "N amostras · M
aguardando revisão", e só abre se você clicar — ou sozinha, quando há algo
esperando revisão.

## 0.7.0-rc2 — 09/09/2026

> Ainda um candidato. O `versao.json` continua intocado: ninguém recebe aviso
> de atualização por causa dele.

**Dá para ver a transcrição durante a própria reunião.** Enquanto você grava, o
que já foi dito aparece no Gravador, ao lado dos controles. **Não é legenda**: são
blocos de 3 minutos, então uma frase dita no minuto 10 aparece entre o 12 e o 13
— o bloco precisa fechar antes de ser transcrito. A tela diz isso em vez de
esconder.

O texto é **provisório** e não substitui nada: quando a reunião acaba, a
transcrição roda como sempre rodou. Cada bloco vem marcado com o horário e o
estado, os últimos minutos ficam para a passada final, e a lista rola sozinha até
você rolar para trás — aí aparece um "voltar ao vivo".

Nasce **desligada**, em Ajustes › Transcrição. Ocupa cerca de um décimo da placa
e hoje só funciona com o motor MOSS.

**A escolha do motor saiu do arquivo e virou tela.** Ajustes › Transcrição tem
agora um seletor entre "o de sempre (dois modelos)" e "MOSS (uma passada)" — não
é mais preciso fechar o app e editar o `app.json` à mão.

**Sua própria voz voltou a ser reconhecida com o MOSS.** Havia um defeito: a
faixa do microfone identificava você corretamente e o resultado era descartado,
porque o mecanismo que dava a você a preferência dependia de uma informação que o
motor novo não fornece. Você aparecia como um participante qualquer.

**O modelo do MOSS baixa no lugar certo.** O download funcionava e gravava o
arquivo por cima da pasta onde ele deveria ficar, então a tela seguia dizendo
"ausente" sobre 0,70 GB que estavam no disco.

**Some a caixa "Entry Point Not Found" do Windows.** Uma biblioteca que este app
nunca usou vinha junto dos motores e não casava com a versão do PyTorch; o
carregador do Windows abria um diálogo que travava a transcrição até alguém
clicar em OK.

**O bloco de diagnóstico diz de onde o app rodou.** Uma linha nova, `executável:`
— sem ela não havia como saber qual das instalações produziu o bloco.

## 0.7.0-rc1 — 04/09/2026

> **É um candidato, não uma versão.** Serve para testar, e o `versao.json` não
> foi mexido de propósito: ninguém que tem o app instalado recebe aviso de
> atualização por causa dele.

**Um segundo motor de transcrição, e ele é opcional.** O de sempre são dois
modelos — um escreve o texto, outro separa quem falou, e o app cruza os dois pelo
tempo. O novo faz as duas coisas de uma vez, e nas suas gravações ele saiu na
frente nos dois quesitos. Ele **não é o padrão** e nada muda até você escolhê-lo.

Para experimentar, em dois passos:

1. Ajustes › Modelos, bloco "Transcrição em uma passada", botão Baixar — são
   0,70 GB, e ele não vem dentro do instalador de propósito;
2. feche o app e acrescente `"motor_de_transcricao": "moss"` ao arquivo
   `C:\Users\<você>\.meeting-transcription\app.json`.

Ainda não há chave na tela para isso — ela vem depois. A gravação fica salva,
então dá para transcrever a mesma reunião nos dois motores e comparar; voltar
atrás é trocar a palavra para `"classico"`.

**O que muda quando ele está ligado, e vale saber antes:** o vocabulário do
projeto funciona diferente. O motor novo não aceita a lista de termos na entrada
— isso não existe nele —, então os nomes e siglas só são corrigidos depois, na
revisão. Termo que ele não ouviu não volta.

**A transcrição não é jogada fora se a máquina cair no meio.** O texto já era
salvo assim que existia; agora ele também guarda **com qual motor** foi feito, e
transcrever de novo com o outro motor refaz o trabalho em vez de reaproveitar o
texto errado em silêncio.

**A revisão para de engasgar nas reuniões grandes.** Cada letra digitada na busca
redesenhava a transcrição inteira — na sua reunião de duas horas, são 2.058
trechos refeitos por tecla. Agora ela desenha o que cabe na tela e o resto chega
conforme você rola.

**As cores dos falantes passam a ser legíveis no tema escuro.** Cinco das seis
eram fixas e não acompanhavam o tema — só o seu nome ficava legível, e o de todo
mundo não. São oito cores novas, feitas para os dois temas, e o ciclo só recomeça
na nona pessoa em vez de na sexta.

**As caixas de "tem certeza?" passam a ser do app.** Eram do Edge, com o título do
executável e fora do desenho do app — e, pior, elas travavam o laço de mensagens
que a bandeja usa. As três que destroem trabalho (apagar gravação, transcrever de
novo, desfazer correções) agora dizem o que se perde antes de perguntar.

**Trocar de tela passa a existir para quem usa teclado ou leitor de tela.** O foco
segue o título, e a troca é anunciada.

**Termos escritos com o espaço no lugar errado são consertados.** "next best"
vira "NextBest" e "lifecycle" vira "life cycle" quando é assim que o seu
vocabulário escreve. Vale para os dois motores.

**O bloco de diagnóstico de Ajustes › Sobre passa a levar o registro junto.** As
últimas linhas do log vão dentro dele, então relatar um problema deixa de exigir
uma segunda ida atrás do arquivo.

## 0.6.2 — 28/08/2026

**O vocabulário do projeto parava de existir quando você abria uma reunião
antiga.** A tela de transcrição mostrava a caixa de termos vazia mesmo quando o
projeto tinha uma lista cheia — e a primeira coisa que você mexesse ali gravava
esse vazio por cima. Não era só o vocabulário: iam junto o modelo, o idioma, a
escolha de separar falantes e o pipeline de diarização escolhido em Ajustes ›
Clientes. Trocar o modelo na tela apagava tudo isso, em silêncio.

Agora a tela lê o que o projeto guardou antes de deixar gravar qualquer coisa.

Se algum projeto seu está com o vocabulário vazio e você lembra de tê-lo
preenchido, foi isto. A lista não tem como ser recuperada — vale conferir os
projetos que você usa antes da próxima transcrição.

## 0.6.1 — 27/08/2026

**Nomes e siglas do projeto param de sair errados na transcrição.** O
reconhecimento de fala troca nome próprio pela palavra comum que soa parecido — o
cliente "GCCB" saía "G6CB" no meio da reunião enquanto o cabeçalho da ata, três
linhas acima, escrevia certo. Agora o app compara o que foi escrito com o que ele
já sabe (o vocabulário do projeto, o cliente e quem a agenda convidou) e conserta
o que for claramente a mesma coisa. Cada troca fica marcada com ✎, e um clique
desfaz.

**E dá para pedir ajuda ao modelo, se você quiser.** Em Ajustes › Transcrição há
uma chave nova, desligada: com ela, o modelo lê a transcrição e acha também o que
a regra não alcança — o sistema "Kenan" que saiu "Kina" ou "Kino". Numa medição
com dez erros reais de reuniões suas, a regra pegou 3 e o modelo 8. Custa o modelo
de ata baixado e meio minuto por reunião; sem ela, a correção por regra continua.

**A transcrição passa a pegar mais do que foi dito.** O detector de fala estava
apertado demais e cortava fala baixa. Medido em quatro gravações suas, de 7 a 122
minutos: o ajuste novo recupera 2 minutos de pausa numa reunião de duas horas, sem
inventar uma palavra sequer sobre trecho mudo.

**A ata diz quem falou, e não só quem foi convidado.** O cabeçalho listava a lista
inteira da agenda — numa reunião com onze convidados e seis falantes, cinco pessoas
que nunca abriram a boca apareciam como participantes. Agora são duas linhas: os
convidados em cima, quem falou embaixo, na mesma ordem.

**Os nomes param de sair pela metade, e o responsável para de sair como e-mail.**
Quando a agenda não traz o nome de alguém, o app o deduzia do endereço — e um
e-mail sem ponto virava uma palavra só. Isso fazia a pessoa aparecer ao mesmo
tempo como quem não falou e como "falante não identificado". Pior: às vezes o app
misturava os dois estilos, não reconhecia mais a pessoa e **apagava um responsável
certo**.

**Decisões e pendências param de sumir.** Duas coisas: seções inteiras eram
descartadas em silêncio quando o modelo respondia por um caminho diferente do
esperado — 52 itens perdidos em 5 das suas reuniões —, e decisões reais eram
rebaixadas para "pontos em aberto" porque a conferência comparava palavra por
palavra, e a reunião diz "tem que ser casado" enquanto a ata escreve "de forma
casada". Das 19 rebaixadas nas suas atas, 12 eram legítimas.

**A ata avisa quando um compromisso não virou pendência.** Já havia aviso quando
um número sumia; agora há também quando some um item de ação. Uma ata sem
pendência numa reunião que teve nove é pior que uma ata feia — parece completa, e
ninguém é cobrado de nada.

## 0.6.0 — 26/08/2026

**Você escolhe qual reunião está gravando, antes de gravar.** O app decidia
sozinho, e decidia bem no caso fácil: a reunião que está acontecendo agora. No
caso difícil ele não tinha como acertar — duas reuniões no mesmo horário, ou a
que começa daqui a vinte minutos e é a que você vai gravar de verdade. E o erro
não aparecia na hora: aparecia dias depois, na ata, com o título e os
participantes de outra reunião. A tela do Gravador agora lista as reuniões da
sua agenda, marca qual delas seria usada se você apertasse o gravar neste
instante, e deixa você apontar outra com um clique.

**A reunião que atrasou não some mais da tela.** Reunião marcada para 14:00 em
que todo mundo entra às 14:40 já terminou no papel quando alguém lembra de
gravar — e ela sumia da lista exatamente aí, junto com a chance de a gravação
sair com o nome certo. Pior: sem ela por perto, o app pegava a reunião
*seguinte* como rótulo, porque era a mais próxima que ele enxergava. A lista
agora guarda as três horas anteriores, e as que já terminaram ficam ali,
recuadas e ainda escolhíveis.

**A escolha vale para uma gravação, não para o dia.** Ao parar, ela é solta
sozinha — uma escolha que sobrevivesse carimbaria a próxima reunião com o título
da anterior, em silêncio, que é o problema que isto veio resolver. Trocar no meio
da gravação também funciona: é quando se percebe que a reunião é outra, e o
arquivo com os dados da reunião só é escrito no fim.

## 0.5.0 — 25/08/2026

**A ata diz quem falou, e não só quem foi convidado.** O cabeçalho listava a
lista inteira da agenda como "Participantes" — numa reunião com onze convidados
e seis falantes, cinco pessoas que nunca abriram a boca apareciam como se
tivessem participado. Agora são duas linhas: os convidados em cima, quem falou
embaixo, na mesma ordem, e a diferença é a sua leitura. Falante que a separação
não conseguiu nomear vira uma contagem, para a ata não dizer que menos gente
falou do que falou.

**Os nomes param de sair pela metade.** Quando a agenda não traz o nome de
alguém, o app o deduzia do e-mail — e `lilianioshimoto@telefonica.com` virava
"Lilianioshimoto", uma palavra só. Isso não era só feio: com o nome grudado ela
não casava com a fala dela na transcrição, então saía ao mesmo tempo como quem
não falou e como "falante não identificado". O nome que a agenda tem é usado
quando existe, e o e-mail continua decidindo de que lado da mesa a pessoa está.

**O responsável para de sair como endereço de e-mail.** A agenda mistura nome
próprio e e-mail na mesma lista, e a ata copiava o que via: pendências saíam
atribuídas a "dimi.randel" ou "andre.monlevade". Pior, às vezes o app misturava
os dois estilos, não reconhecia mais a pessoa e **apagava um responsável certo** —
trocando uma pendência com dono por uma sem dono, que ninguém cobra.

**Decisões e pendências param de sumir da ata.** Quando o app escrevia a ata por
um caminho e o modelo respondia por outro, seções inteiras eram descartadas em
silêncio. Uma daily de 38 minutos saiu com resumo e mais nada, enquanto quatro
pendências com responsável e prazo tinham sido geradas e jogadas fora. Varrendo
as gravações desta máquina: **52 itens perdidos em 5 reuniões**. Agora eles são
recuperados, passam pelas mesmas conferências do resto, e a ata avisa que houve
remontagem.

**A ata para de arquivar do lado errado a pendência que é sua.** Quando o
responsável não estava claro, a ata chutava o lado — e uma entrega nossa
arquivada como do cliente não é cobrada por ninguém, some das duas listas ao
mesmo tempo. Agora ela procura na transcrição quem se comprometeu ("eu vou te
mandar isso") e usa o e-mail da agenda para dizer de que lado essa pessoa está.
A frase que decidiu fica escrita nas observações, para você conferir.

**Números param de ganhar unidade inventada.** "129 mil registros" virava
"R$ 129 mil" — o número certo com a unidade errada, que num documento feito para
ser citado é pior que a omissão. A palavra dita na reunião agora acompanha o
número.

**A seção "Observações sobre a transcrição" para de se contradizer.** Ela
afirmava que tudo tinha sido registrado e, logo abaixo, listava onze números que
faltavam. Agora ela traz só a conferência — o que foi conferido e o que ficou de
fora —, sem comentário do modelo por cima.

**Riscos ditos na reunião chegam à ata.** A seção de riscos saía vazia mesmo
quando alguém tinha falado de dependência, bloqueio ou incidente aberto: essas
falas nunca chegavam ao modelo. Agora chegam, marcadas.

**Há um segundo modelo de separação de falantes para escolher.** Em Ajustes →
Clientes, o campo "Modelo de diarização" agora oferece o *pyannote 3.1* além do
padrão. O padrão continua sendo o melhor dos dois nas medições — o 3.1 está ali
para comparar numa reunião de verdade, que é a única régua que vale.

**As vozes aprendidas até aqui foram aposentadas.** Elas foram colhidas por um
caminho que podia deixar entrar, no perfil de uma pessoa, um pedaço em que outra
falava — e não há como saber quais foram afetadas. O app volta a aprender do
zero: da próxima vez que você nomear alguém, a voz dele é guardada limpa e passa
a ser reconhecida de novo. As antigas não foram apagadas, continuam na tela de
Vozes, marcadas e sem efeito — apagá-las é escolha sua.

**As vozes aprendidas passaram a saber de qual modelo vieram.** O app reconhece
quem já foi nomeado comparando uma "impressão digital" da voz — e essa impressão
só faz sentido dentro do modelo que a produziu. Se o modelo mudar, a comparação
não daria erro: daria um nome errado com cara de certeza. Agora cada voz guarda
o seu, e um modelo novo simplesmente aprende todo mundo de novo, sem apagar o
que já existia. Nada muda hoje — o modelo é o mesmo desde sempre.

**A escolha do modelo de diarização passou a valer.** Em Ajustes → Clientes há
um campo "Modelo de diarização" por projeto. Ele existia, guardava o que você
escolhesse, e o app transcrevia com outro — a escolha nunca saía do disco. Agora
ela chega ao motor, e a lista mostra o que está de fato instalado na sua
máquina, em vez de uma opção vazia. Hoje há um modelo só, então na prática nada
muda para você; o que muda é que o campo parou de mentir, e um modelo novo
aparece sozinho ali no dia em que vier junto com o app.

**Da ata dá para ir direto à transcrição.** Dentro da ata aberta, ao lado de
Copiar e Exportar, há agora "Ver a transcrição". A ata afirma coisas, e conferir
onde elas foram ditas obrigava a sair para Reuniões e achar na lista a mesma
reunião que já estava na tela.

**As vozes param de aprender áudio com mais de uma pessoa dentro.** Quando o app
guarda a voz de alguém para reconhecê-la nas próximas reuniões, ele escolhe
trechos em que só essa pessoa fala. A conferência olhava o que vinha antes e
depois do trecho, mas não o trecho por dentro — e um pedaço em que você falou por
cima entrava no perfil da outra pessoa. Agora entra na conta o seu microfone, que
diz com certeza quando você estava falando, e o trecho analisado é o mesmo que
fica guardado para você poder ouvir.

> As vozes **já aprendidas** continuam como estão: um perfil não se conserta
> depois de formado. Se alguém passar a ser reconhecido errado com frequência, o
> caminho é apagar e ensinar de novo.

## 0.4.0 — o app agora se chama PulseMeet, e a transcrição não se perde mais

**Nome novo, símbolo novo.** O app se chama **PulseMeet**, e o ícone deixou de
ser o monograma A para ser um M — o mesmo círculo, o mesmo traço, as linhas de
dentro redesenhadas. O que muda para você é o que está escrito: o título da
janela, o balão da bandeja, os atalhos e o texto do instalador. O que **não**
muda é onde o app mora nem como ele se atualiza: a pasta continua
`...\Programs\MeetingApp`, o executável continua `MeetingApp.exe`, e o Windows
continua reconhecendo esta versão como atualização da anterior — sem segunda
entrada em "Aplicativos Instalados" e sem baixar os 4,3 GB de motores de novo.
O nome ainda pode mudar de novo; nada do que você tem instalado depende dele.

**O texto é salvo assim que fica pronto.** Até agora a transcrição só ia para o
disco no fim de tudo — depois de separar os falantes, de procurar as vozes
conhecidas, de montar o arquivo. Se alguma coisa acontecesse no meio, o texto,
que já estava pronto havia minutos, ia junto. Agora ele é gravado no instante em
que existe: você abre a gravação e lê a reunião, mesmo que o resto não tenha
terminado. A lista avisa quando é uma transcrição pela metade.

**Transcrever de novo aproveita o que já foi feito.** Se a primeira tentativa
chegou a transcrever o texto, a segunda pula direto para a separação de
falantes, em vez de passar o áudio inteiro pelo modelo outra vez. Numa reunião
de uma hora isso é a diferença entre alguns minutos e alguns segundos. Se você
mudar o modelo, o idioma ou o vocabulário, ela refaz tudo — porque aí o texto
sairia diferente.

Isto saiu de um caso real: um usuário cujo computador desliga sozinho durante a
separação de falantes, e que perdia a reunião inteira toda vez. A causa do
desligamento continua em investigação; o que esta versão conserta é o app jogar
fora um trabalho que deu certo.

**O áudio para quando você sai da reunião.** Ouvir os trechos para conferir quem
fala e depois ir para o Gravador, para as Atas ou para os Ajustes deixava a
gravação tocando por cima da tela nova — sem nenhum botão à vista para pará-la.
Abrir os falantes ou as notas continua não cortando o áudio, que é o que se
espera de quem só quer mexer em algo sem perder o lugar no texto.

**Quem falou para de sumir dentro dos trechos longos.** Quando duas ou três
pessoas falavam sem pausa, o modelo juntava tudo num trecho só — às vezes de
quarenta segundos — e o trecho inteiro ficava no nome de uma pessoa. As outras
sumiam da transcrição e, com ela, da ata. Agora o trecho é cortado onde a
separação de falantes diz que a voz mudou, na palavra exata.

**O vocabulário do projeto deixou de ser sussurrado ao modelo.** Ele era usado
duas vezes: durante a transcrição e depois dela, para corrigir a grafia. As duas
medições dizem que os nomes se recuperam igual pelos dois caminhos — e o
primeiro cobrava caro, juntando a fala em blocos longos, que é exatamente o
problema do parágrafo acima. Na mesma reunião: 787 trechos em vez de 207, mais
fala aproveitada, e quatro vezes menos tempo para transcrever. A correção da
grafia continua igual. Quem quiser o comportamento antigo, a chave está em
Ajustes → Transcrição.

## 0.3.0 — o tema escuro, e a tela de ler fica maior

**O app tem tema escuro.** Em Ajustes → Geral → Aparência: claro, escuro, ou
igual ao Windows. Ele já estava desenhado desde o começo — a mesma paleta de
areia, em carvão, porque cinza neutro tiraria a cara do app — e simplesmente não
havia como chegar nele. Continua abrindo no claro se você não escolher nada.

**A tela de revisão mostra mais texto.** A barra de cima comia quase metade da
janela: agora a transcrição começa quase 90 pixels mais acima, e cabem três
trechos a mais sem rolar. O texto também ficou maior — é a tela em que se passa
mais tempo lendo, e ela estava com tamanho de nota de rodapé.

**"Apagar gravação" saiu do meio da barra de ferramentas.** Ficava entre a busca
e os filtros, no caminho do que se clica todo dia. Foi para a direita, separada
do resto.

**A ata volta a ter títulos.** As seções da ata estavam saindo do mesmo tamanho
dos parágrafos, o que fazia um documento de duas páginas parecer um bloco só.

**Dois acertos pequenos:** o gravador parado não escreve mais "Parado" duas
vezes, e os cartões da tela de Atas param de dançar de linha para linha.

## 0.2.1 — dá para saber o que aconteceu

Uma transcrição travou o computador de um usuário, e não havia nada para olhar
depois. Esta versão é sobre isso.

**O app passa a manter um registro** em `%USERPROFILE%\.meeting-transcription\registro.log`:
o que ele fez, em qual placa, e o que os motores disseram. Nada de transcrição,
nome de cliente ou de participante entra ali — e o arquivo só sai da sua máquina
se você mandar. O caminho dele aparece no bloco de diagnóstico.

**A transcrição diz onde está rodando** — "transcrevendo em NVIDIA GeForce
RTX 2060" — em vez de deixar você adivinhar.

**E recusa rodar na CPU sem você mandar.** Transcrever pela CPU leva horas e
consome muita memória; num computador apertado, o suficiente para travá-lo. Se a
sua máquina não tem placa NVIDIA e você quer mesmo assim, ligue "Transcrever sem
placa" em Ajustes → Transcrição.

## 0.2.0 — a ata fica inteira

Esta versão é quase toda sobre a **ata**. Quatro defeitos faziam com que boa
parte do trabalho do modelo nunca chegasse até você.

**A ata deixa de sair repetida.** Havia uma segunda ata inteira dentro dela, e a
lista de pendências aparecia duas vezes. As duas coisas eram erro nosso.

**"Decisões técnicas" volta a aparecer.** Nas atas de sessão de trabalho, o
raciocínio por trás de cada decisão — por que se escolheu aquilo, o que foi
descartado e o que isso trava daqui para frente — estava sendo apagado antes de
chegar ao arquivo. É a parte que faz a ata valer alguma coisa três meses depois.

**As pendências param de cair todas no colo da mesma pessoa.** A ata passa a
percorrer os participantes um a um; "me manda o número que eu falo com o fulano"
agora vira duas tarefas, uma de cada lado.

**O que se combinou _não_ fazer também vira decisão.** "Não corrija antes de
falar comigo" é o tipo de combinado que some da ata e faz alguém agir sem ele.

**Reuniões longas param de falhar.** A ata calculava o espaço necessário pelo
relógio, e reunião com conversa densa estourava a conta — uma sessão de 39
minutos falhava. Agora a conta é feita pelo texto de verdade, e uma reunião de
duas horas cabe.

**Dois modelos novos de ata**, em Ajustes → Modelos, medidos em 30 atas:

- **Gemma 4 E4B** — o mais rápido e o único que não falhou nenhuma vez. São 5 GB;
- **Qwen3.5 4B** — rápido e menor, mas registra menos pendências.

O padrão continua sendo o Qwen3 4B.

**O app avisa quando sai versão nova**, em Ajustes → Geral → Sobre e no alto da
lista de reuniões. Ele só avisa: não baixa nada sozinho. Dá para desligar.

## 0.1.0 — a primeira versão instalável

A primeira que se instala em vez de se copiar. O app faz, nesta ordem, o que uma
reunião pede:

- **grava** em duas faixas separadas, o seu microfone e o áudio do sistema, com
  correção de deriva de relógio — uma reunião de duas horas continua sincronizada
  no fim;
- **transcreve** com o Whisper large-v3 na sua placa, e **separa quem falou**;
- **aprende as vozes** entre reuniões: quem já foi identificado uma vez volta com
  nome na próxima;
- **notas** escritas durante a reunião, guardadas junto dela, alimentando o
  vocabulário da transcrição;
- **ata** escrita por um modelo que roda na sua máquina — nada de transcrição de
  cliente saindo daqui;
- **agenda**: o Google Calendar diz qual reunião está acontecendo, e os
  participantes viram vocabulário;
- exportação em txt, srt, vtt e docx.

Novo nesta versão, e é o que a torna entregável:

- **versão à vista e bloco de diagnóstico** nos Ajustes, em Geral. O botão copia
  versão, placa, modelos instalados e pasta das gravações — é o que resolve um
  problema à distância sem vinte perguntas;
- **o que é grande e opcional se baixa quando faz falta.** O modelo de
  transcrição, o motor de ata e o modelo de ata ficam fora do instalador, em
  Ajustes → Modelos, com barra e tamanho à vista. Quem não usa ata nunca baixa os
  3,1 GB dela;
- **separar quem falou funciona sem internet**, desde a primeira reunião: os
  modelos de diarização vêm dentro do app.

O que ainda **não** existe, dito com todas as letras:

- **sem placa NVIDIA o app funciona, mas devagar** — uma reunião de uma hora pode
  levar horas. Esta versão só traz o caminho CUDA;
- **não há atualização automática.** Atualizar é rodar o instalador novo;
- o instalador **não é assinado**, então o Windows mostra um aviso de editor
  desconhecido na primeira execução.
