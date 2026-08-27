# Auditoria do acervo — o que se vê sem fonte paralela

**Data da varredura:** 21/08/2026 · **Acervo:** 31 gravações, 28 com ata ·
**Ferramenta:** [`tools/auditar_atas.py`](../tools/auditar_atas.py)

> **Este documento é evidência, não decisão.** Ele mede **quantas vezes** cada
> defeito acontece no acervo. Ele não decide **qual lado está certo** quando o
> defeito é de conteúdo — para isso a régua continua sendo a
> [FASE6.md §5](FASE6.md#5-a-tarefa-que-não-espera-esta-fase-comparar-com-outras-fontes):
> comparação com fontes paralelas, e hoje o acervo tem **três** (Notion em 13/08,
> Gemini/Meet em 20 e 21/08) contra as três a cinco que a carta pede.
>
> A ordem de conserto fica pendente de mais pares com o Gemini. O que está aqui
> serve para dizer *o que vale medir* quando esses pares existirem — e para
> separar, desde já, o que **não precisa** de par nenhum.
>
> **A exceção é a §6.** Ali os pares existem e a medição foi feita: em duas
> reuniões, **11–12% dos segmentos chegam ao prompt da ata com o falante
> errado**, e o erro é de atribuição, não de fronteira.

---

## 1. Por que uma auditoria sem referência

A comparação com uma segunda fonte é cara: depende de a reunião ter sido gravada
em paralelo, e isso não se faz retroativamente. Mas boa parte do que a análise de
20/08 encontrou **não precisou de segunda fonte nenhuma** — são contradições
internas, visíveis olhando só o que já está no disco:

- uma ata cujo conteúdo foi gerado e **descartado em silêncio** pelo redator;
- uma ata que escreve uma sigla que o `meta.json` da mesma pasta contradiz — e
  que a própria ata escreve certo no cabeçalho;
- uma transcrição que diz dois valores para a mesma grandeza com 75 s de
  distância, e uma ata que escolheu o errado;
- 19 de 28 itens duplicados numa ata só.

O acervo tem 31 gravações e nunca tinha sido varrido. É o corpus mais barato que
o projeto tem: já está pago, não precisa de anotação, e roda em segundos.

```bash
python tools/auditar_atas.py                        # tabela
python tools/auditar_atas.py --detalhe              # cada ocorrência
python tools/auditar_atas.py --so secao_descartada  # um achado só
python tools/auditar_atas.py --json                 # para outra ferramenta
```

---

## 2. Resultado

```
achado                           gravidade  gravações  ocorr.  incidência
----------------------------------------------------------------------------
porcentagem_invisivel            PERDA              9       9  32% de 28
secao_descartada                 PERDA              5      20  17% de 28
fragmentacao                     suspeita          26      26  83% de 31
lacuna_de_vad                    suspeita          22      22  70% de 31
porcentagem_contraditoria        suspeita          10      10  32% de 31 [h]
vocativo_do_proprio_falante      suspeita          10      10  32% de 31 [h]
quebra_meio_frase                suspeita           9       9  29% de 31
todas_do_cliente                 suspeita           9       9  32% de 28
sigla_corrompida                 suspeita           7       9  25% de 28 [h]
dono_fora_da_agenda              suspeita           1       1  3% de 28
secao_duplicada                  ruído              7      23  25% de 28
eco_de_pendencia                 ruído              6       7  21% de 28 [h]
repeticao                        ruído              5       9  17% de 28
situacao_e_o_titulo              ruído              1       5  3% de 28
bullet_no_valor                  ruído              1       4  3% de 28
```

`[h]` = heurística; erra para o lado de reportar demais. A precisão medida de
cada uma está na §4.

**Gravidade** quer dizer:

- **PERDA** — informação que existiu e não chegou ao arquivo. Não depende de
  julgamento e não depende de fonte paralela: é conferível contra o próprio
  `ata.json`;
- **suspeita** — indício forte de erro, mas quem confirma é o áudio ou uma
  segunda fonte;
- **ruído** — a ata sai pior de ler, e nada se perde.

---

## 3. Achado a achado

### 3.1 `secao_descartada` — PERDA, 5 gravações, **59 itens**

O modelo devolve decisões, ações, riscos e observações como **seções** em
`secoes` em vez de nos campos próprios. O redator descarta a seção
([RedatorDeAta.cs:84](../app-net/Nucleo/Atas/RedatorDeAta.cs#L84)) porque "o app
escreve essa a partir do campo" — e o campo está vazio. Nada disso vira linha em
Observações, então a ata sai parecendo completa.

| gravação | o que foi gerado e descartado |
|---|---|
| `2026-08-17_15-29-18` | decisões, ações, pontos em aberto, riscos **e** observações |
| `2026-08-10_11-50-26` | decisões, ações, pontos em aberto, riscos |
| `2026-08-20_14-00-06` | decisões, ações, pontos em aberto |
| `2026-08-20_15-29-34` | decisões, ações, pontos em aberto, riscos **e** observações |
| `2026-08-14_10-30-32` | uma decisão |

A de `2026-08-17_15-29-18` é a pior: 2 KB de `ata.md`, só Resumo e a seção livre,
e o `ata.json` ao lado com quatro pendências com dono e prazo. Uma daily de 38
minutos que aparenta não ter gerado ação nenhuma.

**Não depende de fonte paralela.** É bug determinístico, com prova em disco.

### 3.2 `porcentagem_invisivel` — PERDA, 9 gravações

[`RoteiroDeFatos.Normalizar`](../app-net/Nucleo/Atas/RoteiroDeFatos.cs#L167)
descarta tudo que não é letra ou dígito, e `NaoIncorporados` pula chave com menos
de três caracteres:

```csharp
private static string Normalizar(string t) =>
    string.Concat(t.Where(char.IsLetterOrDigit)).ToLowerInvariant();
...
if (chave.Length < 3) continue;
```

`"2%"` vira `"2"`, `"55%"` vira `"55"`, `"86%"` vira `"86"` — todos abaixo de três
caracteres, todos pulados. **Toda porcentagem menor que 100% é invisível para a
rede de omissões**, que numa reunião de negócio é quase toda porcentagem que
importa.

Na varredura: 9 gravações têm porcentagem dita na reunião, ausente da ata, e que
o verificador nunca chegou a conferir — de uma a cinco por gravação.

Há um segundo defeito no mesmo método: a comparação é `Contains` por substring,
então `"2"` casaria dentro de `"12%"` mesmo se o guarda de três caracteres não
existisse. Precisa de fronteira de token, e o guarda precisa olhar o texto
original — `"2%"` tem dois caracteres e é informação; `"2"` solto é ruído, e a
diferença está no símbolo que o `Normalizar` joga fora.

**Não depende de fonte paralela.**

### 3.3 `porcentagem_contraditoria` — 8 gravações `[h]`

Duas porcentagens diferentes ditas em até três minutos. Nasceu do caso medido em
20/08: a transcrição diz `menos de 2%` aos 02:05 e `de 12%, menos de 10%` aos
03:20, para a mesma grandeza. A ata escolheu a segunda e a repetiu cinco vezes; a
fonte paralela mostrou que a certa era a primeira.

**Aqui a auditoria só levanta a contradição.** Quem decide qual valor é o certo é
o áudio ou a segunda fonte — é exatamente a fronteira entre este documento e a
§5 da carta.

### 3.4 `fragmentacao` e `quebra_meio_frase` — 83% e 29%

A mediana do acervo é **38% dos segmentos com quatro palavras ou menos** e **7%
de quebras no meio de frase com troca de falante**. As gravações de 20 e 21/08
estão bem acima: 49–50% de fragmentos e 13–17% de quebras.

O padrão é sempre o mesmo — a fronteira do segmento cai no meio da frase e a
segunda metade recebe o falante seguinte.

Quando escrevi este item pela primeira vez, ele terminava dizendo que a métrica
prova que a segmentação é fina, não que ela está errada, e que só uma fonte
paralela decidiria. **As fontes paralelas chegaram e decidiram: a segmentação
fina não é o problema principal** — a fronteira mal posta responde por 0,6% dos
segmentos, contra 11–12% de rótulo simplesmente errado. Ver §6.

### 3.5 `vocativo_do_proprio_falante` — 10 gravações `[h]`

Ninguém chama a si mesmo pelo nome. Um segmento atribuído a alguém que é chamado
pelo nome dentro dele é quase sempre dois turnos fundidos num falante só:

```
[00:03] <Fulano>: Alô, e aí <Fulano>, tudo bem?
[03:03] <Fulano>: Boa tarde, <Fulano>. Tudo bom?
[13:09] <Fulano>: Ô <Fulano>, você tá ficando em <cidade>?
```

**Erro de diarização provável e conferível sem áudio**, em 35% do acervo. É
também a heurística mais barata de virar conserto: se o segmento contém o
vocativo de um participante, o falante não pode ser ele.

Falsos positivos medidos: 2 em 10 — auto-referência (`eu sou o Fulano`) e
terceira pessoa (`o Fulano tá com...`). Ver §4.

### 3.6 `eco_de_pendencia` — 22% `[h]`

Risco ou ponto em aberto que é a pendência reescrita. Na ata de `2026-08-20_15-59-20`:
7 de 8 pontos em aberto e 4 de 5 riscos são as pendências parafraseadas como
pergunta (`Como implementar X`) e como ameaça (`X pode ser complexo`).

**`decisoes` fica fora do teste, de propósito.** Decidir fazer X e ter a pendência
de fazer X é a ata funcionando, não eco. Com `decisoes` dentro, o achado marcava
48% do acervo e 13 das 17 ocorrências eram esse par legítimo — o número parecia
grave e não era. Tirando-o, sobram 6 gravações e 7 ocorrências, e todas são o
defeito de verdade.

Isto revela um buraco no
[`ConferirRiscos`](../app-net/Nucleo/Atas/VerificadorDeAta.cs): ele exige ≥50% de
eco lexical na transcrição para um risco sobreviver. Um risco construído a partir
do texto da própria ata **passa por construção** — as palavras estão todas lá. O
teste está certo contra invenção do nada e indefeso contra reciclagem. A régua
precisa de um segundo braço: risco com alta similaridade a uma pendência já
listada é eco, não risco.

**Não depende de fonte paralela** para o mecanismo; depende dela para saber se o
risco *verdadeiro* da reunião ficou de fora.

### 3.7 `todas_do_cliente` — 33%

Em 9 atas o modelo pôs **todas** as pendências do lado do cliente. O
`ConferirLados` corrige pelo domínio do e-mail e registra — mas registra como
rodapé em Observações, com a mesma voz de uma nota qualquer.

"10 de 10 pendências trocaram de lado" não é uma nota de rodapé: é o modelo com
um mapa errado da sala. Numa reunião cujo projeto é marcado como interno, todas
as pessoas com e-mail da casa, o modelo deduziu "cliente" do assunto da conversa
— que é precisamente o erro que o `Organizacoes` existe para impedir.

### 3.8 `sigla_corrompida` — 25% `[h]`, **precisão baixa**

A ata escreve uma sigla a uma ou duas letras de uma que o `meta.json` da mesma
pasta conhece. O caso motivador: uma ata que compara o cliente da própria reunião
contra uma sigla de quatro letras que difere dele por um caractere — enquanto o
cabeçalho, três linhas acima, escreve a forma certa.

Três filtros, cada um medido contra o ruído que tirou:

1. sigla contra sigla, mesmo tamanho, 1–2 letras trocadas. A versão larga
   (qualquer token parecido) disparava em 62% do acervo, quase tudo flexão —
   `agentes`/`Agente`, `usuários`/`Usuario`. Diferença só no sufixo é morfologia;
2. a candidata não pode ser entidade conhecida em **nenhuma** reunião do acervo.
   Sem isto, `B2C` vira corrupção de `B2B` e `API` de `APP`;
3. sobra o que interessa: sigla que só existe nesta ata, a uma letra de uma
   conhecida.

Mesmo assim a precisão é ~1 em 9 (§4). **É lista de candidatos, não de achados** —
mas nove candidatos em 27 gravações custam um minuto de leitura por varredura.

### 3.9 `repeticao`, `secao_duplicada`, `bullet_no_valor`, `situacao_e_o_titulo` — ruído

- **`repeticao`** (14%): loop de repetição do modelo. Numa ata, 5 de 12
  observações, 5 de 10 riscos e 4 de 18 descobertas eram repetição quase literal.
  Dois consertos independentes e baratos: penalidade de repetição na chamada do
  llama.cpp, e dedup por similaridade no verificador — que já é o lugar certo,
  porque já sabe registrar o que mudou;
- **`secao_duplicada`** (25%, 23 ocorrências): o inverso da §3.1 — o modelo
  preencheu o campo **e** mandou a seção, e a ata diz tudo duas vezes;
- **`bullet_no_valor`** (3%): o modelo emite `"- texto"` dentro do valor e o
  redator prefixa outro `- `, saindo `- -`. Raro, e um `TrimStart` resolve;
- **`situacao_e_o_titulo`** (3%): o modelo preenche `situacao` com o próprio
  título da seção, e o redator escreve `**Situação:** Decisoes`.

---

## 4. Precisão medida das heurísticas

Conferido à mão contra a transcrição, na varredura de 21/08:

| heurística | candidatos | verdadeiros | precisão |
|---|---|---|---|
| `vocativo_do_proprio_falante` | 10 | 8 | 80% |
| `eco_de_pendencia` | 7 | 7 | 100%, amostra pequena |
| `porcentagem_contraditoria` | 8 | não conferido | — |
| `sigla_corrompida` | 9 | 1 | ~11% |

A `sigla_corrompida` é a única que não se sustenta como achado. Fica como
candidato porque o custo de ler nove linhas é menor que o de escrever o nome do
cliente errado numa ata que vai para o cliente.

Uma melhora possível, não implementada: em vez de similaridade, usar
**frequência** — a forma corrompida aparece pouco e a correta aparece muito, no
mesmo documento. Precisa de mais acervo para calibrar o corte.

---

## 5. Métricas de transcrição, por gravação

```
gravação                segs  ≤4pal  quebras  lacunas     /h
------------------------------------------------------------------------
2026-08-10_11-50-26      387    41%      10%        1    2.4
2026-08-10_15-00-15     2058    35%       5%       40   19.7
2026-08-11_08-02-40      246    10%       9%        0    0.0
2026-08-11_11-01-10      553    45%       5%        0    0.0
2026-08-11_14-14-56      332    35%       4%        0    0.0
2026-08-11_15-59-44      964    40%       2%        6    8.6
2026-08-12_09-59-50      533    38%       7%        8   14.1
2026-08-12_14-00-20      117    23%       0%        2    6.5
2026-08-12_15-29-38      391    38%       7%        2    5.6
2026-08-13_14-30-15      205    30%       0%        7   14.4
2026-08-13_15-29-40      491    47%      10%        3    8.5
2026-08-14_09-29-44      815    33%       5%        3    4.6
2026-08-14_10-30-32       76     8%       0%        6   21.6
2026-08-14_14-30-10      228    27%       0%        5   11.8
2026-08-14_15-29-44      145    44%      16%        0    0.0
2026-08-14_15-59-46      928    37%       2%        1    1.7
2026-08-17_14-00-48      303    36%       3%        0    0.0
2026-08-17_15-29-18     1040    43%      12%        0    0.0
2026-08-17_16-29-34     1623    41%       8%        5    4.8
2026-08-18_10-00-10     1341    39%       6%        7    7.7
2026-08-18_11-00-50      117    46%       3%        1   12.6
2026-08-18_14-00-48      572    54%       7%        0    0.0
2026-08-18_15-29-49      295    32%       7%        0    0.0
2026-08-18_15-59-53     1033    36%       4%        5    7.3
2026-08-20_09-59-49      411    38%      16%        5    9.1
2026-08-20_14-00-06       64    39%      17%        0    0.0
2026-08-20_15-29-34      305    50%      13%        1    4.7
2026-08-20_15-59-20      702    49%      17%        8   15.0
2026-08-21_09-32-59      365    46%       8%        2    8.3
2026-08-21_10-00-32      409    54%      12%        2    6.8
2026-08-21_11-00-33      145    49%      10%        1    7.9
------------------------------------------------------------------------
mediana                         39%       7%
```

A coluna `/h` existe porque contagem bruta de lacuna cresce com a duração, e sem
normalizar a reunião de duas horas sempre parece a pior. Por hora, as duas
extremas são `2026-08-10_15-00-15` (19,7/h) e `2026-08-20_15-59-20` (15,0/h).

Sobre a primeira, que tem **40 buracos acima de 8 s**: a explicação fácil seria
faixa muda, e ela **não se sustenta** — o `meta.json` dela dá `usable_pct: 99,9`
e `total_silent_s: 8,6` na faixa de sistema, que é de onde a transcrição saiu. O
microfone é que ficou mudo em 84% do tempo, e microfone mudo não abre buraco na
transcrição porque a fala dos outros continua na outra faixa. Ou seja: 40 lacunas
com faixa saudável, e a causa continua em aberto. É item para a §5 da carta, não
para este documento.

---

## 6. Diarização medida contra referência, em duas reuniões

**Ferramenta:** [`tools/comparar_com_gemini.py`](../tools/comparar_com_gemini.py) ·
**Referência:** export do Gemini/Meet, guardado como `gemini.md` na pasta da
gravação

Os rótulos de falante do Meet **não são estimativa**: cada participante entra
pelo próprio canal, então quem falou é dado do sistema de conferência. É a
referência que a [FASE6.md §5](FASE6.md) chama de mais valiosa, e até aqui DER só
era medível no acervo AMI da Fase 0.

A métrica não é DER — é sobre **palavras alinhadas**, e o alinhamento é por
texto, não por relógio, porque os timestamps das duas fontes estão deslocados em
~1min30. A pergunta que ela responde é a que importa para a ata: *o texto que
chega ao prompt está atribuído a quem?*

| | `2026-08-21_11-00-33` | `2026-08-20_15-59-20` |
|---|---|---|
| duração, falantes | 7min41s, 6 | 32min08s, 3 |
| assunto | logística, fala curta | brainstorm, fala corrida |
| nossos segmentos | 145 | 702 |
| palavras alinhadas | 684 (88%) | 3.605 (89%) |
| **palavra com falante certo** | **95,0%** | **92,6%** |

Por segmento, que é a unidade que o prompt da ata enxerga:

| classe do segmento | 21/08 | 20/08 |
|---|---|---|
| falante certo, sem respingo | 82,8% | 83,6% |
| falante certo, ponta do vizinho junto (**fronteira**) | 0,7% | 0,6% |
| fala inteira no falante errado (**atribuição**) | **9,7%** | **11,8%** |
| duas ou mais pessoas no mesmo segmento (**fusão**) | 1,4% | 1,0% |
| sem par no Gemini (fora da conta) | 5,5% | 3,0% |

E os dois números que **não dependem do corte** entre "atribuição" e "fusão":

| | 21/08 | 20/08 |
|---|---|---|
| segmentos com fala de mais de uma pessoa dentro | 2,1% | 1,9% |
| **segmentos cujo rótulo não é o falante dominante** | **11,0%** | **12,0%** |

### O que isso quer dizer, e o que eu tinha concluído errado

**A primeira versão desta seção dizia o contrário, e estava errada.** Ela
classificava o erro pela *posição da palavra* dentro do segmento e concluiu "33
de 34 erros na borda, logo o modelo acústico está certo e o problema é a
segmentação". O defeito estava na métrica: com 38% dos segmentos tendo quatro
palavras ou menos (§5), **toda** palavra está a menos de três de uma borda, e
"erro de borda" dava 97% por construção — inclusive para segmentos inteiramente
atribuídos à pessoa errada. Medir posição não distingue nada quando o segmento é
do tamanho da janela de medida.

Classificando pelo **rótulo do segmento contra o falante que domina as palavras
dele**, o quadro se inverte e fica estável entre duas reuniões muito diferentes:

- **fronteira mal posta é 0,6–0,7%.** Praticamente não existe;
- **rótulo errado é 11–12%.** É o modo de falha dominante, e é atribuição:
  a fala inteira está na boca da pessoa errada;
- **fusão de falantes é ~2%.** Existe, é o pior para a ata quando acontece, e é
  raro.

Ou seja: **costurar segmentos não é o conserto.** O que erra é a atribuição, e
isso é o modelo acústico — que é o caminho caro. Um em cada nove segmentos que
chegam ao prompt da ata tem o falante errado, e é consistente entre uma reunião
de 7 minutos com seis pessoas e uma de 32 minutos com três.

Exemplos, com os nomes trocados:

```
trocado:  <A> -> <B>   "alo e ai diego tudo bem"      (dois turnos num rótulo só)
trocado:  <A> -> <B>   "e muito constante"
trocado:  <C> -> <B>   "e uma questao"
fusão:    <D> -> <E>   "ola tudo cha e voce"
```

### A fala do dono, que não deveria depender de estimativa nenhuma

**A diarização já não usa o mix.** Ela roda só no `system.wav`
([Transcritor.cs:383](../app-net/Nucleo/Transcritor.cs#L383)) — o mix existe para
o ASR ouvir a conversa inteira, sobreposição inclusive. Quem decide o que é do
dono é `Montagem.AtribuirDono`
([Transcricao.cs:435](../app-net/Nucleo/Transcricao.cs#L435)), pela energia das
duas faixas:

```csharp
if (rmsMic >= RmsMinimoDoDono && rmsMic > rmsSistema * MargemDoDono)
```

Ou seja: `rmsMic >= 5e-3` **e** `rmsMic > rmsSistema * 2,0`. E o dono é, medido,
o falante que **menos** acertamos — ele, que é o único conhecido por construção:

| falante | 20/08 (32 min) | 21/08 (7 min) |
|---|---|---|
| **o dono** | **88,3%** | 93,7% |
| remoto mais falante | 97,2% | 99,5% |
| demais remotos | 91,1% | 90,5–96,1% |

Isolando os segmentos que o Gemini atribui ao dono e medindo o RMS das duas
faixas em cada um:

| | acertamos | erramos |
|---|---|---|
| segmentos (20/08) | 172 | 25 |
| duração mediana | 1,9 s | 2,0 s |
| rms do microfone | 0,0997 | 0,0839 |
| rms do sistema | 0,0002 | 0,0762 |
| razão mic/sistema | **539** | **0,94** |
| reprovados pelo mínimo `5e-3` | 1 | 1 |
| reprovados pela margem `2,0` | 2 | **25 de 25** |

**Todos os erros são a margem, e nenhum é o mínimo.** No segmento que erra, o
microfone está em 0,084 — o dono está falando alto e claro. O que mudou é que
**alguém está falando junto**, e a regra é vencedor-leva-tudo: em sobreposição
não há vencedor, o teste falha, e o segmento cai para o rótulo que o pyannote deu
ao `system.wav` — que é, necessariamente, outra pessoa. O mesmo padrão nas duas
gravações, e o destino da fala perdida é sempre um participante remoto.

O número que fecha o diagnóstico é a separação do microfone sozinho:

| | 20/08 | 21/08 |
|---|---|---|
| rms do mic quando o dono fala (p10) | 0,0636 | 0,0291 |
| rms do mic quando **outro** fala (p90) | 0,0024 | 0,0013 |
| separação | **26×  (28 dB)** | **22×  (27 dB)** |

**O microfone sozinho separa o dono do resto por ~27 dB**, e o
`RmsMinimoDoDono = 5e-3` de hoje já cai no meio dessa folga. A comparação com o
`system.wav` não acrescenta informação — ela só destrói a certeza que o desenho
de duas faixas tinha comprado, e destrói exatamente onde a fala é mais difícil.

A margem existe por um motivo real, registrado no código: *"2,0 (~6 dB) tolera o
vazamento de quem usa caixas em vez de fone"*. Com caixas, o microfone capta a
sala e a regra absoluta atribuiria ao dono a fala dos outros. Mas **o vazamento é
medível na própria gravação**: a mediana do rms do microfone enquanto só o
sistema tem fala foi 0,0002 nas duas — fone, sem vazamento nenhum. Com caixas
esse número sobe para uma fração do nível do sistema, e a diferença entre os dois
casos é de ordens de grandeza.

### O conserto, e o que ele rendeu de verdade

**A estimativa acima estava errada, e o número certo é menor.** Ela contava os
segmentos que se recuperaria e não os que se quebraria. Medido: trocar a margem
por um limiar absoluto no microfone recupera 24 segmentos do dono e **rouba 40 de
outros** — saldo −16. Um segmento em sobreposição contém fala das duas pessoas, e
atribuí-lo por inteiro a qualquer uma delas erra. **Escolher é o erro; o que
resolve é cortar.**

O conserto entregue ([`Nucleo/VozDoDono.cs`](../app-net/Nucleo/VozDoDono.cs)) põe
o dono na linha do tempo da diarização, para o `Montagem.RepartirPorFalante` —
que já existia e já corta segmento por palavra — cortar também na fronteira dele.
Medido com [`tools/simular_voz_do_dono.py`](../tools/simular_voz_do_dono.py),
contra a mesma referência:

| | `2026-08-20_15-59-20` (32 min) | `2026-08-21_11-00-33` (7 min) |
|---|---|---|
| **fala do dono, por palavra** | 92,6% → **96,6%** | 94,1% → 94,9% |
| palavras erradas do dono | 67 → **31** | 8 → 7 |
| palavra certa, geral | 92,7% → 93,3% | 95,0% → 95,0% |
| segmento com rótulo certo | 88,5% → 89,3% | 92,4% → 92,1% |
| segmentos produzidos | 766 → 863 | 144 → 151 |

**O erro de palavra da fala do dono cai pela metade** onde há sobreposição de
verdade, e não muda onde não há — o que é a assinatura esperada, e é o que dá
confiança de que o mecanismo diagnosticado é o certo.

**O custo é fragmentação:** os segmentos com quatro palavras ou menos vão de 46%
para 55% na gravação longa, e a mediana cai de 5 para 4 palavras. Mais confete no
prompt da ata, em troca de metade do erro na fala de quem grava. O piso de
`MinimoS = 0,8 s` foi escolhido varrendo de 0,25 a 2,0: é o maior valor que não
custa acerto nenhum, e ele evita que um "uhum" abra trecho.

**O ganho geral é modesto** — 0,6 ponto de palavra na longa, zero na curta. Os
11–12% de rótulo errado continuam sendo, na maior parte, diarização de verdade.

---

### Ressalvas

- **duas reuniões.** A concordância entre elas é o que dá confiança, não o
  tamanho de cada uma;
- **12% e 11% das nossas palavras não casaram com nada do Gemini** (443 e 85).
  São divergência de ASR ou de VAD e ficam fora da conta de diarização de
  propósito — mas se a divergência for enviesada por falante, ela contamina o
  resto;
- o Gemini **não é verdade absoluta**: ele erra fronteira e o export dele traz
  ruído (fragmentos em cirílico e tailandês em backchannels). O que ele tem de
  sólido é *de quem é o canal*, e é só isso que esta medição usa;
- o corte de 0,8 entre "atribuição" e "fusão" é arbitrário em segmento curto, e é
  por isso que a tabela sem corte está logo acima. Se houver um número para levar
  daqui, é **11–12% de rótulo errado**.

### O que fecharia

Uma terceira reunião, de preferência longa e com sobreposição pesada. Se os
11–12% se repetirem, a conclusão passa a ser que **melhorar a diarização é
pré-requisito da ata**, e não acabamento — o que é uma decisão de arquitetura,
não de ajuste.

---

## 7. O VAD, medido por três lados

**Ferramentas:** [`wer_contra_gemini.py`](../tools/wer_contra_gemini.py),
[`palavras_no_silencio.py`](../tools/palavras_no_silencio.py),
[`texto_nas_pausas.py`](../tools/texto_nas_pausas.py)

A [FASE6.md](FASE6.md) deixou "reavaliar o VAD" em aberto porque faltava régua.
Com a transcrição paralela do Meet ela existe pela metade — mede cobertura, não
alucinação —, e as outras duas ferramentas fecham o resto.

### O que cada lado mede, e por que são três

| | mede | precisa de |
|---|---|---|
| divergência contra o Gemini | **cobertura** — o que faltou | transcrição paralela |
| palavras no silêncio | **invenção sobre ausência de sinal** | nada |
| texto nas pausas | **invenção sobre ruído de sala** | nada |

A terceira existe por um caso concreto: a gravação de 122 minutos do acervo tem
**22 minutos de pausa** e **8,6 segundos** de silêncio digital. As pausas são
ruído baixo, não ausência de sinal, e a segunda medida é cega ali.

### Cobertura: afrouxar ganha, mas o ótimo não replicou

Divergência contra o Gemini, cinco configurações, dois áudios:

| `threshold` | 7 min | **32 min** |
|---|---|---|
| 0,35 (padrão) | 27,5% | 29,8% |
| 0,25 | **26,4%** | 29,6% |
| 0,15 | 27,4% | **28,6%** |
| `min_silence` 200 ms | 27,5% | 29,8% |
| sem VAD | 29,1% | 30,1% |

**As duas reuniões discordam sobre onde está o ótimo.** Na de 7 minutos o 0,25
ganha e o 0,15 piora; na de 32, o 0,15 é o melhor de todos. A de 32 pesa mais —
o dono do produto observou que a maioria das reuniões fica entre 25 e 45
minutos, e a de 7 é caso atípico (logística, seis pessoas, fala picotada).

Três coisas **replicaram** nas duas, e essas valem como decididas:

- **0,35, o padrão de hoje, é a pior configuração com VAD.** Afrouxar melhora;
- **`min_silence_duration_ms` é parâmetro morto.** 200 e 500 dão resultado
  idêntico ao dígito, nas duas. Não vale mexer nele nunca;
- **desligar o VAD é pior que qualquer configuração com VAD.** Contraria o
  resultado 6 da Fase 0 — e pelo motivo que o próprio `sweep_vad.py` previa: lá
  a medição foi sobre fala concatenada, quase sem silêncio; em gravação real é
  no silêncio que o VAD ganha o salário.

### O sinal de alerta, e o que ele custou

Na de 32 minutos o 0,15 ganha **120 palavras que faltavam** e ganha **46
trocadas**. Transcrever mais áudio marginal recupera fala baixa e também produz
erro — e é por isso que cobertura sozinha não decide.

Invenção sobre ausência de sinal, na de 7 minutos (24% dela abaixo do piso):

| | palavras no mudo |
|---|---|
| 0,35 · 0,25 · 0,15 | **0** |
| sem VAD | 4 |

**Afrouxar até 0,15 não produziu uma única palavra sobre silêncio.** Só desligar
o VAD inventou. É a primeira metade da resposta, e ela é favorável a afrouxar.

### As gravações longas, e a decisão

O terceiro lado, nas duas gravações com pausa de verdade. A referência de pausa é
a transcrição no padrão de hoje:

| 61 min (3 min de pausa) | palavras nas pausas | min de pausa |
|---|---|---|
| 0,35 | 0 | 3 |
| **0,25** | 11 | **2** — encurtou |
| 0,15 | 15 | 3 — não encurtou |
| sem VAD | 44 | **4** — aumentou |

| 122 min (22 min de pausa) | palavras, total | nas pausas | min de pausa |
|---|---|---|---|
| 0,35 | 12.561 | 0 | 22 |
| **0,25** | **12.653 (+92)** | 113 | 20 |
| 0,15 | **12.492 (−69)** | 113 | 20 |

**O discriminador não é a pausa — é o saldo.** Na de 122 minutos os dois
recuperaram os mesmos 2 minutos de pausa e as mesmas 113 palavras dentro delas.
Mas o 0,15 ficou 69 palavras **abaixo** do padrão no total: achou texto na pausa
e perdeu em outro lugar. O 0,25 ficou 92 acima.

**Placar final, quatro gravações:**

| | 7 min | 32 min | 61 min | 122 min |
|---|---|---|---|---|
| **0,25** | melhor | 2º, melhor que o padrão | recupera | +92 |
| 0,15 | pior | melhor | não encurta | −69 |

**0,25 ganha em três de quatro, e é o único que nunca piora.** É o valor que
entrou no `motores/asr/motor.py`.

### Uma régua que não serviu, e onde

A medida de invenção sobre ausência de sinal deu **zero para todas** as
configurações na gravação de 61 minutos — não porque estejam boas, mas porque
aquela gravação tem **0% de silêncio digital**. A limitação estava prevista para
a de 122 minutos e apareceu também onde eu não esperava.

É o motivo de as três réguas existirem: cada uma é cega em algum lugar, e só o
cruzamento decide.

---

## 8. O que esta auditoria **não** consegue ver

Registrado para que a lista não seja lida como completa. Tudo abaixo precisa de
fonte paralela, e é por isso que a §5 da carta continua sendo a régua:

- **palavra errada no ASR.** A auditoria vê que a transcrição se contradiz; não
  vê que `<sigla A>` deveria ser `<sigla B>` quando as duas são plausíveis, nem
  que um nome próprio virou outra palavra;
- **número que o ASR simplesmente perdeu.** Se o valor nunca chegou à nossa
  transcrição, nenhuma auditoria interna sente falta dele. Na comparação de
  20/08, o único número de desempenho do produto estava só na fonte paralela;
- **dono trocado entre quem pede e quem executa.** Precisa de entender a
  conversa, ou de comparar com uma ata que outro sistema escreveu;
- **hipótese promovida a decisão.** O maior defeito de conteúdo encontrado até
  agora, e completamente invisível daqui: uma ata bem formada com cinco decisões
  que ninguém tomou passa em todas as checagens deste documento;
- **fala omitida.** A auditoria mede buraco de tempo, não conteúdo perdido dentro
  de fala transcrita.

---

## 9. Estado do corpus paralelo

| data | reunião | fonte paralela | o que ela permitiu decidir |
|---|---|---|---|
| 13/08/2026 | `2026-08-13_14-30-15` | Notion | [FASE6.md §1.6](FASE6.md) — omissão de números, ausência de dono |
| 20/08/2026 | `2026-08-20_15-59-20` | Gemini/Meet | valor certo de uma grandeza, sigla do cliente, dono real de 5 pendências, hipótese × decisão |
| 21/08/2026 | `2026-08-21_11-00-33` | Gemini/Meet | **a §6**: 95,0% de palavras com falante certo; 9,7% dos segmentos com rótulo errado |
| 20/08/2026 | `2026-08-20_15-59-20` | Gemini/Meet | **a §6**: 92,6% de palavras; 11,8% dos segmentos com rótulo errado |

**A régua de "três a cinco" está no piso.** O que falta agora não é contagem, é
**diversidade** — e uma reunião longa, porque as três que existem têm 33, 32 e 8
minutos, e a de 21/08, que é a única com medição de diarização, é a mais curta e
a menos representativa (sete minutos de logística, quase sem fala corrida). O acervo é
dominado por update de cliente. Ainda não há par paralelo de:

- reunião em que o dono do app **fala muito** — a de 20/08 teve o microfone mudo
  em 98,8% do tempo, e toda a diarização veio de uma faixa só. É o caso extremo,
  não o típico, e a §4.3 da carta depende de ter o outro;
- **daily** e **1:1** — padrões de fala e de estrutura diferentes de update;
- reunião com **sobreposição pesada**, para calibrar a costura de segmentos com
  casos fáceis e difíceis, e não só difíceis.

O Gemini/Meet é a fonte mais valiosa das quatro listadas na carta, e por um
motivo que muda o que se pode medir: os rótulos de falante dele vêm do canal de
cada participante e **não são estimativa**. Com dois ou três pares a mais, o DER
da nossa diarização passa a ser medível em reunião real, o que hoje só acontece
no acervo anotado da Fase 0.

**Obstáculo prático medido e resolvido:** os timestamps do Gemini estão
deslocados ~1min30 em relação aos nossos. O
[`comparar_com_gemini.py`](../tools/comparar_com_gemini.py) alinha por texto e
não por relógio, então o deslocamento deixou de ser problema.

**Como guardar um par novo:** salve o export do Gemini como `gemini.md` dentro da
pasta da gravação — é o que a §5 da carta pede ("guardar as saídas junto da
gravação") e é onde a ferramenta procura por padrão.

```bash
python tools/comparar_com_gemini.py <pasta-da-gravacao>
```

---

## 10. O que o acervo ainda não tem, e é o insumo caro

Fonte paralela responde *"qual dos dois está certo?"*. Não responde *"a ata
ficou melhor depois do conserto?"* — para isso é preciso um alvo.

**Duas ou três atas corrigidas à mão, congeladas como referência.** É a única
peça do corpus que compõe: sem ela, cada conserto é avaliado por leitura, e
leitura não é repetível. Duas bastam; não vinte.

---

## 11. Convenção deste documento

Nomes de cliente, de pessoas e valores financeiros ficam **fora** — o repositório
é público. Onde o exemplo precisa da forma da palavra, a forma está anonimizada
(`<sigla A>`, `<Fulano>`); onde precisa do número, o número está inteiro, porque
`2%` não identifica ninguém. As gravações são citadas pela pasta, que já é a
convenção da [FASE6.md](FASE6.md).

Para reproduzir com os dados reais, a ferramenta lê direto do acervo em disco e
não escreve nada:

```bash
python tools/auditar_atas.py --detalhe
```
