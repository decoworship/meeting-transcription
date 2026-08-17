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

## A recomendação

**Manter o Qwen3 4B como padrão e oferecer o Gemma 4 E4B como opção**, para quem
tem placa folgada e quer ata em metade do tempo.

O recall não distingue os dois, e trocar o padrão é uma decisão que atinge todo
mundo por um ganho — velocidade — que não é o que dói hoje. O que de fato
recomenda o Gemma é a **confiabilidade**: zero falhas em 10 rodadas contra uma em
9, e nunca a seção gigante.

Acrescentar qualquer um deles ao catálogo é publicar uma versão nova do app — o
catálogo é código —, e é por isso que a rota de atualização
([ATUALIZACAO.md](ATUALIZACAO.md)) veio antes desta medição.
