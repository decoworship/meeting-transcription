# Modelos de ata, comparados

Medido em 17/08/2026 na máquina do usuário — RTX 2060 de 6 GB, driver 595.97 —
a pedido do dono do produto, que queria avaliar o Qwen3.5 4B e o Gemma 4 contra
o Qwen3 4B que está em produção desde a Fase 3.

Ferramenta: `tools/comparar_modelos_de_ata.py`, que roda **o pipeline de
verdade** (`Sidecar.exe --ata`) e não uma reconstrução do prompt.

---

## O desenho da medição, e por que ele é assim

A primeira tentativa rodou **uma** reunião com 5 números e deu 80% contra 40%.
Parecia um veredito e não era: a diferença eram *dois números*, e o mesmo modelo
repetido na mesma reunião deu 60% numa rodada e 80% na outra. **A variância do
modelo era maior que a diferença entre modelos.**

O desenho final:

| | |
|---|---|
| reuniões | 5, escolhidas por densidade de números (14 a 35 cada, 117 ao todo) |
| duração | de 19 min a **122 min** — a de 2 h entra para testar contexto |
| modelos | 3 |
| rodadas | 2 por modelo e reunião |
| atas geradas | 30 |
| tipo de ata | "Sessão de trabalho" em todas, para não misturar variáveis |

**A régua principal é o recall de números**, porque o defeito que a Fase 3 nomeou
no 4B não é inventar — é **omitir** ([FASE3-HANDOFF.md](FASE3-HANDOFF.md) §6:
"não inventou nada e omitiu metade"). Omissão não se vê lendo a ata; se vê
contando o que ficou de fora.

---

## O resultado

| modelo | n | falhas | tempo | recall | pior seção | tem Pendências |
|---|---|---|---|---|---|---|
| Qwen3 4B Instruct *(atual)* | 9 | 1 | **114 s** | 67% ± 18% | 2.477 | 9/9 |
| Qwen3.5 4B | 9 | 1 | **41 s** | 68% ± 11% | 1.978 | **1/9** |
| Gemma 4 E4B | 10 | **0** | **63 s** | 65% ± 15% | 1.959 | 10/10 |

### O recall não distingue os três

67%, 68%, 65% — com desvios de 11 a 18 pontos entre rodadas do **mesmo** modelo
na **mesma** reunião. Os três pontos que separam o primeiro do último cabem
inteiros dentro do ruído.

**Isto é um resultado, não uma falta dele.** Ele diz que trocar de modelo nesta
faixa não compra fidelidade, e que quem quiser mais recall tem que mexer em
outra coisa — no prompt, no roteiro de fatos, ou no tamanho do modelo.

### O Qwen3.5 está fora, e não por pouco

**Ele não escreve pendências.** Em 8 das 9 rodadas a ata saiu sem a seção — e
não é a régua se enganando: os arquivos têm Resumo, Problema trabalhado,
Descobertas e Referências citadas, e simplesmente não têm Pendências.

Uma ata de reunião sem "quem faz o quê até quando" não é uma ata para este
produto. Nenhum ganho de velocidade compra isso.

*(E ele exigiu `enable_thinking: false` para funcionar: é modelo de raciocínio, e
com o padrão do template gastava os 8.192 tokens de saída inteiros pensando.)*

### O Gemma 4 E4B é a alternativa real

Mesmo recall, **45% mais rápido** que o atual (63 s contra 114 s), **zero
falhas** em 10 rodadas — incluindo a reunião de 2 horas, onde o atual levou
300 s e o Qwen3.5 falhou — e seções menores, que é o inverso da mania de se
alongar.

O que ele custa:

- **4,98 GB contra 2,50 GB.** O "E4B" é efetivo, não bruto. Dobra o download e
  aperta a placa de 6 GB;
- **contexto de 131k contra 262k.** Sobra para reunião de 2 h, mas o teto é
  metade;
- ele só cabe porque usa **janela deslizante** — 512 tokens de cache na maioria
  das camadas, e 18 das 42 compartilhando KV. Foi isso que enganou o
  dimensionamento e obrigou a mudança de desenho registrada abaixo.

---

## O que a medição mudou no código

**A estimativa de contexto deixou de ser porteiro.** O dimensionamento recusou o
Gemma dizendo "cabem ~17.281 tokens"; subindo o `llama-server` à mão com 32.768
ele carregou sem reclamar. A fórmula trata todas as camadas como iguais, e errou
por ~5×.

Modelar cada arquitetura é corrida que se perde — a próxima família traz outro
truque. Quem conhece a arquitetura é o llama.cpp. Então a conta escolhe a
quantização do cache, e a alocação de verdade decide se cabe.

**`enable_thinking: false` em toda requisição.** Para escrever ata, o que garante
o resultado é o esquema e o verificador, não a deliberação do modelo — e num
motor de saída constrangida por gramática JSON o pensamento não tem onde caber.

---

## O que esta medição **não** diz

- **Se a ata é boa de ler.** Estrutura, recall e formato se contam; fidelidade de
  prosa, não. Alguém tem que ler as 30 atas em
  `C:\Users\andre\ata-comparacao` antes de qualquer troca de padrão;
- **se vale para outros tipos de ata.** Tudo rodou como "Sessão de trabalho";
- **nada sobre modelos maiores.** Um 12B em placa maior é outra conversa, e é a
  saída que a [FASE3-HANDOFF.md](FASE3-HANDOFF.md) §6 já apontava: se a omissão
  incomodar, o caminho é subir de modelo, não apertar o de agora.

---

---

## A reviravolta: quase tudo era defeito nosso

> Acrescentado em 17/08/2026, depois de o dono do produto questionar a métrica e
> pedir uma avaliação de qualidade. As conclusões acima **não sobreviveram**.

A pergunta que abriu tudo: *"será que todos os números são mesmo importantes? Pode
ser que os modelos decidiram certo quais colocar."*

**Ele estava certo.** Dos 9 números que o Qwen3 "perdeu" numa reunião, os 9 eram
hipótese ("não sei se seria 50, 70 ou 80"), conta em voz alta ("6299 mais 32 dá
194") ou leitura de tela. Nenhum era fato a acompanhar. A régua punia o modelo
por filtrar certo.

Lendo as atas contra a transcrição inteira, o que eles perdem de verdade é outra
coisa — e é o que importa:

- **"Não corrija antes de falar comigo"**, dito com todas as letras pela líder do
  cliente, sumiu das duas atas. É restrição, e restrição é decisão;
- **100% das ações foram para quem mais falou.** A coordenadora se atribuiu
  trabalho em voz alta e não apareceu em nenhuma ata;
- e as duas atas traziam **uma segunda ata inteira dentro de uma seção**.

### Quando dois modelos erram igual, a causa é o prompt

Foram quatro defeitos, todos nossos:

| # | defeito | causa |
|---|---|---|
| 1 | ata inteira dentro de uma seção | o recorte do `SKILL.md` levava o Passo 3 ("Toda ata começa assim") e o Passo 4 ("Saída em Markdown, direto na conversa") — o prompt pedia documento, o esquema pedia JSON |
| 2 | lista de pendências escrita em `secoes` **e** no campo `acoes` | o esqueleto do tipo mostrava "## Ações" como seção. **Regra em texto não vence exemplo em estrutura** |
| 3 | a lista de pendências saía **duas vezes** no documento | `RedatorDeAta` deduplicava pela chave de *entrada*: "Ações" e "Pendências" são entradas diferentes para a mesma saída |
| 4 | **"Decisões técnicas" era engolida** | `EhCanonica` casava por prefixo `decis`, e substituía o raciocínio (por quê, alternativas descartadas, implicações) pela lista de uma linha |

O 4 é o mais grave, e estava escondido atrás do 1: enquanto a ata duplicada
existia, o modelo escrevia "Decisões técnicas" dentro dela e a seção aparecia.
Consertar o 1 a fez sumir — e só aí ficou visível que **a seção mais valiosa da
ata de sessão de trabalho nunca tinha chegado ao usuário pelo caminho certo**.

### O que os consertos mudaram

Medido nas mesmas 5 reuniões, 3 modelos, 2 rodadas:

| | antes | depois |
|---|---|---|
| atas com pendências duplicadas | 30 de 30 | **0** |
| atas com "Decisões técnicas" | 0 | **28** |
| ações do Qwen3.5 por ata | **0** | 4,3 |
| falhas por estouro de saída | 2 | 2 |
| donos distintos por ata | ~1 | 2,6 a 2,9 |

**O Qwen3.5 não estava desqualificado.** A conclusão de que "ele não escreve
pendências" era um defeito nosso: o esqueleto mostrava "## Ações" como seção e o
esquema pedia o campo `acoes`; ele escolhia a seção, e o redator não a escrevia.

### O quadro final

| modelo | n | falhas | tempo | recall | ações | donos |
|---|---|---|---|---|---|---|
| Qwen3 4B *(atual)* | 9 | 1 | 122 s | 79% ± 32% | 6,3 | 2,9 |
| Qwen3.5 4B | 9 | 1 | 64 s | 69% ± 23% | 4,3 | 2,7 |
| Gemma 4 E4B | 10 | **0** | 61 s | 67% ± 21% | 5,0 | 2,6 |

Com os desvios em 21 a 32 pontos, **os três são indistinguíveis em qualidade**.
Sobram duas diferenças que resistem ao ruído: o Qwen3 é **duas vezes mais lento**,
e o Gemma é o único que não falhou nenhuma vez.

### A lição que vale mais que a escolha do modelo

Passamos de "qual modelo é melhor" para "quatro defeitos nossos custavam mais que
qualquer diferença entre modelos". Antes de trocar de modelo, vale sempre ler a
saída contra a entrada — a régua automática mediu 30 atas e não viu nenhum dos
quatro.

---

## A recomendação

> Revista em 17/08/2026, depois dos consertos.

**Trocar o padrão para o Gemma 4 E4B**, e manter o Qwen3 4B como opção.

O argumento mudou de lado com os consertos. Antes, o Gemma ganhava só em
velocidade — que não era o que doía. Agora que os quatro defeitos saíram e os
três modelos ficaram indistinguíveis em qualidade, o que resta a decidir é
exatamente velocidade e confiabilidade, e nos dois o Gemma ganha: **metade do
tempo** do atual (61 s contra 122 s) e **zero falhas em 10 rodadas**, incluindo
a reunião de duas horas onde o atual falhou.

O que a troca custa, e não é pouco: **4,98 GB contra 2,50 GB** de download e de
VRAM. Numa placa de 6 GB isso é apertado, e é o motivo de o Qwen3 continuar
sendo oferecido.

**O Qwen3.5 volta à mesa** — a desqualificação dele era defeito nosso. Ele é tão
rápido quanto o Gemma e ocupa metade do disco. Ficou de fora da recomendação por
uma razão só: com 4,3 ações por ata contra 5,0 e 6,3, é o que menos registra
pendência — e pendência é o que o produto existe para não deixar cair.

**Nada disso se decide sem ler.** Estrutura, recall e formato se contam; se a ata
é boa de ler, não. As 30 atas de cada geração estão em
`C:\Users\andre\ata-comparacao` e `...-antes`.

Acrescentar qualquer um deles ao catálogo é publicar uma versão nova do app — o
catálogo é código —, e é por isso que a rota de atualização
([ATUALIZACAO.md](ATUALIZACAO.md)) veio antes desta medição.
