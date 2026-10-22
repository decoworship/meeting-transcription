# Integração com o Claude

O PulseMeet como servidor MCP: o Claude Desktop abre `PulseMeet.exe --mcp` e lê
as reuniões por ali — atas, transcrições e notas — sem ninguém copiar nada. É o
`UI-13` do [BACKLOG](BACKLOG.md). Construído em 22/10/2026.

## 1. Por que existe

O pedido do dono, 22/10/2026: usar a ata ou a transcrição numa tarefa **sem
copiar à mão**, manter tudo local, e conseguir tarefas que atravessam várias
reuniões — a linha do tempo de um cliente era o exemplo, não o objetivo.

Copiar funcionava para uma reunião e não escalava para cinquenta. E a resposta
não era subir o acervo para lugar nenhum: o Claude Desktop já sabe abrir um
processo local e conversar com ele pelo protocolo MCP, e o app já sabe ler o
acervo.

## 2. As três peças

O MCP tem três primitivas, e cada uma resolve uma parte do pedido:

| peça | para quê | onde aparece no Claude Desktop |
|---|---|---|
| **recursos** | anexar uma reunião sem copiar | no menu **+**, como um arquivo |
| **ferramentas** | o Claude buscar sozinho o que uma tarefa de várias reuniões pede | ele chama quando precisa |
| **prompts** | as tarefas que se repetem, com a instrução já escrita | no menu **+**, como receitas |

**Ferramentas** (todas só leem, e dizem isso em `readOnlyHint`):

| nome | o que faz |
|---|---|
| `listar_clientes` | clientes, projetos de cada um, quantas reuniões e o período |
| `listar_reunioes` | id, data, título, duração, vínculo e o que cada uma tem; filtra por cliente, projeto e datas |
| `ler_ata` | o `ata.md`, as observações do verificador e as notas, sem as imagens |
| `ler_transcricao` | uma fala por linha, `[mm:ss] Falante: texto`, inteira ou por trecho |
| `buscar` | palavras nas atas, nas notas e nas transcrições, da reunião mais nova para a mais antiga |
| `reuniao_aberta` | a última reunião aberta no app — o "esta reunião" |

**Recursos**: `pulsemeet://reuniao/<id>/ata` e `.../transcricao` de cada
reunião, da mais nova para a mais antiga, e `pulsemeet://reuniao-aberta/ata` e
`.../transcricao` na frente de todos.

**Prompts**: `timeline` (cliente, projeto, desde), `follow-up` (reunião; vazio
é a aberta) e `preparar-reuniao` (cliente, projeto).

## 3. As decisões

**A ata é o índice, a transcrição é a prova.** Medido em 22/10/2026 no acervo
do dono: as atas do cliente com mais reuniões (49) somavam 224 KB, e cabem numa
conversa; as transcrições, 4,8 MB, e não cabem. Por isso a transcrição sai como
texto corrido por falante (os 362 segmentos de uma reunião de 20 minutos viram
algumas dezenas de falas), por trecho de tempo, com teto de 150 mil caracteres
e o instante de onde continuar; e a busca devolve trechos, não reuniões.

**Tudo devolve texto, e não JSON.** Quem lê é um modelo. Uma linha
`[12:03] Carla Nunes: …` custa uma fração do segmento em JSON.

**Só lê.** O acervo é a coisa mais valiosa do app. Um caminho novo até ele que
também escreve é um caminho novo para estragá-lo. A escrita de volta (seção 7)
fica para quando fizer falta.

**Os mesmos leitores que a tela.** `DadosDaReuniao.Ler` para o vínculo, com o
recuo para a transcrição nas gravações antigas; `Notas.SemImagens`;
`EstadoDaAta.EstaVelha`; `GeracaoDeAta.LerObservacoes`. Uma segunda leitura dos
formatos é como uma delas fica para trás.

**Cliente casa por pedaço, sem acento.** "coca" acha as três unidades de um
cliente, e quem decide se junta ou pergunta é o Claude — o prompt da timeline
manda perguntar.

**A busca pede todas as palavras na mesma fala.** A frase exata perde o que um
modelo costuma mandar ("prazo faturamento B2B"); qualquer palavra devolveria a
reunião inteira. O **cabeçalho da ata fica fora** da busca: ele traz cliente e
projeto, e casava com toda busca pelo nome do projeto (50 achados contra 41,
medido na primeira passada).

**O id é o nome da pasta, e nada mais.** Ele vem de fora, escrito por um
modelo, e vira caminho no disco. Barra, `..`, `:` e caminho absoluto são
recusados antes de qualquer acesso (`Acervo.Achar`).

## 4. Como roda

```
Claude Desktop  ──stdio──>  PulseMeet.exe --mcp  ──lê──>  pasta das gravações
```

- **O `--mcp` desvia antes do mutex** (`App/Program.cs`). O Claude abre este
  processo com o app de pé, e pela regra da instância única ele pediria a
  janela à outra e sairia — o Claude veria o servidor morrer ao nascer. Sem
  bandeja, sem WebView2, sem gravador: funciona com o app fechado.
- **`WinExe` tem stdin e stdout**, porque quem abre o processo com os canais
  redirecionados os entrega. UTF-8 **sem BOM**, escrito à mão: um BOM no
  começo da primeira resposta seria JSON inválido.
- **O stdout é o protocolo.** Um `Console.WriteLine` perdido no caminho
  corrompe a conversa, como o torch faz com os sidecars. Diagnóstico vai para o
  stderr, que o Claude Desktop guarda no log dele.
- A pasta é resolvida como o app resolve (`PastaDasGravacoes.Resolver`);
  `--gravacoes <pasta>` também vale aqui.

### Escrito à mão, e não com o SDK

O protocolo que o app precisa é JSON-RPC 2.0, uma mensagem por linha, e nove
métodos (`Nucleo/Claude/ServidorMcp.cs`). O SDK C# descobre as ferramentas por
reflexão, e o binário publicado sai com `PublishTrimmed`, que desliga a
reflexão do `System.Text.Json`. Tudo aqui é `JsonNode`.

**E mesmo assim o trimming pegou.** `JsonArray.Add(umJsonObject)` cai no
`Add<T>` genérico, que tem `RequiresUnreferencedCode`. Compilou, passou nos 905
testes, e o `publicar.sh` recusou com IL2026. A expressão de coleção
`JsonArray x = [a, b]` cai no mesmo `Add<T>`, e essa nem o analisador do núcleo
pega: só o ILLink do publish, como aviso. A regra é **`(JsonNode)` no `Add` e
`new JsonArray(a, b)` no lugar de `[a, b]`**. Desde então o núcleo compila com
`EnableTrimAnalyzer`, que pega a primeira forma no build de todo dia.

### Versões do protocolo

Aceita `2025-11-25`, `2025-06-18`, `2025-03-26` e `2024-11-05`, devolvendo a
que o cliente pediu; outra recebe a mais nova. O servidor só usa o que as
quatro têm em comum. Responde a lote (o 2025-03-26 permitia) e ignora
notificações.

## 5. O que sai da máquina

O acervo não sai. O processo só lê o disco desta máquina e responde ao que o
Claude pede. **Mas o que o Claude lê vai para a conversa**, e uma conversa com o
Claude roda na Anthropic. Ler uma ata manda aquela ata; buscar manda os
trechos achados. Isso é dito nos Ajustes, atrás do ⓘ, e não só aqui: a ata
local, no llama.cpp, nunca saiu daqui, e esta é a primeira funcionalidade em que
texto de reunião vai para fora por um caminho do app.

## 6. A reunião aberta

O app escreve `~\.meeting-transcription\reuniao-aberta.json` cada vez que abre
uma reunião — no pedido `transcricao` da ponte, que é feito pelos três lugares
que abrem uma reunião, e só por eles. O MCP só lê. É **a última aberta**, e
não a que está na tela: voltar para a lista não apaga, e a hora em que ela foi
aberta vai junto para o Claude não confundir a de ontem com a de agora.

## 7. Conectar

Ajustes › Geral › **Claude**: um botão que acrescenta, ou tira, a entrada no
`claude_desktop_config.json`:

```json
"mcpServers": { "pulsemeet": { "command": "C:\\…\\PulseMeet.exe", "args": ["--mcp"] } }
```

**O arquivo não é nosso.** Na versão da Store ele guarda as preferências do
Claude inteiro (sessões, pastas confiáveis, extensões), e por isso
`ConexaoComClaude` mexe numa chave só. Um JSON que não se lê **não é
reescrito**: o erro vai para a tela e o arquivo fica como estava. Antes da
primeira mudança fica uma cópia, `claude_desktop_config.json.antes-do-pulsemeet`.
A escrita passa por um arquivo temporário.

**Onde ele mora**, na ordem procurada (vale o primeiro arquivo que existe; sem
nenhum, a primeira pasta):

1. `%LOCALAPPDATA%\Claude-Data\claude_*\` — a Store, com os dados fora do
   pacote. É o caso da máquina do dono em 22/10/2026;
2. `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\` — a Store
   antiga, **pulada se tiver a marca `.user-data-left*`**, que é o que a nova
   deixa ao se mudar;
3. `%APPDATA%\Claude\` — o instalador clássico.

O Claude **só lê a configuração ao abrir**, e fechar a janela dele não o
encerra. A tela diz para sair pela bandeja e abrir de novo.

O estado distingue *conectado* de *conectado a este executável*: uma entrada
que aponta para um `PulseMeet.exe` que mudou de lugar aparece como
"Reconectar".

## 8. Medido em 22/10/2026

Com o binário publicado (trimmed, single-file), no Windows, sobre o acervo do
dono (187 gravações, 170 transcritas, 329 recursos), oito pedidos levaram
**3,4 s no total**: initialize, listar clientes, listar reuniões filtradas, duas
buscas (uma sobre o acervo inteiro, com 507 achados contados) e um trecho de
transcrição. Pelo WSL, lendo `/mnt/c`, o mesmo levou 50 s. Medir pelo WSL não
diz nada sobre o uso.

Para repetir à mão, de uma pasta no disco do Windows:

```bat
PulseMeet.exe --mcp < pedidos.jsonl > respostas.jsonl 2> erros.txt
```

com uma mensagem JSON-RPC por linha em `pedidos.jsonl` (o `initialize`
primeiro).

## 9. O que fica para depois

- **Escrever de volta**: salvar o que o Claude produziu (a timeline, um
  dossiê do cliente) como algo que o app mostra. Só quando fizer falta, e com o
  cuidado da seção 3.
- **Claude Code**: o mesmo executável serve —
  `claude mcp add pulsemeet -- "C:\…\PulseMeet.exe" --mcp`. Não conferido.
- **Extensão `.mcpb`**: instalar com um clique pelo próprio Claude, no lugar
  do botão. O botão resolve sem depender do formato.
- **Paginação dos recursos**: hoje sai a lista inteira (329 itens). Se o menu
  do Claude ficar lento com o acervo crescendo, o protocolo tem cursor.
- **Filtrar por pessoa** ("todas as reuniões com a Carla"): os convidados já
  estão na lista; falta o filtro.
