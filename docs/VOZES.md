# Vozes: qualidade de atribuição e gestão dos perfis

Complemento da [QUALIDADE.md](QUALIDADE.md) (seção 6), focado num problema
que ela só tangencia: **a mesma pessoa não soa igual em toda reunião** —
sala de conferência com reverberação, resfriado, headset diferente, campo
distante — e o perfil de voz precisa absorver essa variação sem absorver
também as amostras contaminadas (cross-talk, erro de diarização, ruído).

Não é prioridade de execução; é registro de desenho para quando chegar a
vez — e para que a reinscrição forçada pela migração (risco 4 do plano) já
nasça no formato certo.

---

## 1. O diagnóstico do modelo atual

O `voices.json` guarda, por pessoa, uma lista de até 25 vetores sem nenhum
contexto, e o match usa o **máximo** de cosseno contra qualquer um deles.
Três consequências:

- **um vetor contaminado envenena para sempre**: uma amostra aprendida de
  um segmento com cross-talk (ou que a diarização atribuiu errado) fica na
  lista e, como o match é pelo máximo, basta ela para gerar falso positivo
  — e não há como descobrir qual das 25 é a podre;
- **variação legítima e contaminação são indistinguíveis**: o vetor "Dimi
  na sala de conferência" e o vetor "Dimi com a voz do Carlos por cima"
  são ambos só listas de floats;
- **o threshold único não se sustenta entre condições**: o mesmo 0,65 que
  funciona headset-contra-headset rejeita a pessoa resfriada em campo
  distante — é por isso que o slider existe e ninguém sabe onde deixá-lo.

Tudo abaixo deriva de uma decisão: **cada amostra vira um objeto com
procedência, não um vetor solto.**

```json
{
  "vector": [...],
  "created_at": "...",
  "duration_s": 4.2,
  "source": {"recording": "2026-08-06_09-03-05", "track": "system", "t0": 312.4, "t1": 316.6},
  "device": "Headset AN01",
  "overlap_free": true,
  "snippet": "voices/snippets/dimi_0007.wav",
  "quarantined": false
}
```

Dois campos merecem justificativa:

- **`snippet`** — 3 a 5 segundos de WAV 16 kHz (~150 KB) recortados do
  trecho que gerou o embedding. Ninguém consegue julgar um vetor; qualquer
  um julga 4 segundos de áudio. É o que torna a limpeza humana possível —
  sem ele, a página de gestão da UI vira uma tabela de números.
- **`device`** — vem de graça: o `meta.json` do gravador já registra o
  nome do dispositivo de cada faixa. É o rótulo de condição mais barato e
  mais confiável que existe (ver seção 3).

---

## 2. Impedir a contaminação na entrada (o mais barato)

Regras de inscrição, na ordem em que cortam problema:

1. **A sua voz só entra pelo `mic.wav`** — limpa por construção; cross-talk
   seu é impossível nessa faixa.
2. **As dos outros só entram de segmentos sem sobreposição.** A saída
   powerset da segmentação diz exatamente onde duas pessoas falam juntas —
   hoje essa informação é jogada fora. Segmento com sobreposição não
   inscreve, ponto. (No pipeline atual, um proxy razoável: descartar
   segmentos cujo intervalo cruza turnos de falantes diferentes na
   diarização.)
3. **Piso de duração real** — implementar a concatenação que o
   `voices.py` promete (bug 1.3 da auditoria): juntar os trechos limpos da
   pessoa até somar ≥3s, em vez de embedar um segmento curto.
4. **Quarentena em vez de gravação cega**: antes de adicionar, comparar o
   candidato com o centróide do perfil. Muito distante (ex.: cos < 0,35)
   não é descartado — é marcado `quarantined` e aparece na fila de revisão
   da UI. O ponto: distância grande tanto pode ser contaminação quanto uma
   **condição nova legítima** (primeira vez na sala de conferência,
   resfriado). A máquina não sabe distinguir; o humano com o snippet no
   ouvido sabe em 4 segundos.
5. **Excluir o `user_label` do aprendizado automático** (bug 1.4) — hoje
   "You" vira perfil.

---

## 3. Representar a variação legítima: condições, não uma nuvem única

Três níveis, do mais simples ao mais correto. Recomendação: nível 2, com o
nível 3 anotado para o futuro.

**Nível 1 — centróide único.** Média dos vetores normalizados. Já elimina
a fragilidade do máximo, mas borra condições distintas numa média que não
é nenhuma delas: o centróide de {headset, sala} fica no meio do caminho e
combina mal com ambos.

**Nível 2 — sub-perfis por condição (recomendado).** Cada pessoa tem 1–3
centróides, e o match usa o melhor deles. Duas formas de obter os grupos,
complementares:

- **pelo rótulo que já existe**: agrupar por `device` (e por origem
  mic/system). "Dimi pelo headset de fulano na sala" e "Dimi no notebook
  dele" viram sub-perfis naturais, sem nenhum algoritmo;
- **por clustering dentro do perfil** (agglomerative, distância de cosseno,
  corte fixo): captura condições que o metadado não separa — o resfriado,
  a sala nova com o mesmo dispositivo. Rodar offline, na manutenção, não
  no caminho quente.

O match fica: `sim(x, pessoa) = max sobre sub-centróides` — máximo sobre
2–3 médias robustas é estável; máximo sobre 25 amostras cruas não é. E o
sub-perfil dá interpretabilidade de graça: a UI pode mostrar "reconhecido
como Dimi (condição: sala de conferência, 78%)".

**Nível 3 — normalização de score (AS-norm), quando os perfis crescerem.**
O problema do threshold que não vale entre condições tem solução clássica
em verificação de locutor: normalizar o score contra uma coorte de
impostores (os vetores das *outras* pessoas salvas servem de coorte).
`score_norm = (cos(x,p) − μ_coorte) / σ_coorte` — o threshold passa a ser
"quantos desvios acima do que impostores tiram", que é estável entre salas
e microfones. Custa ~30 linhas e umas dezenas de comparações extras por
match; vale quando houver >10 perfis ou quando o threshold do nível 2
continuar oscilando. (PLDA seria o passo seguinte da literatura — não vale
o custo nesta escala.)

**Sobre o resfriado especificamente**: é uma condição transitória — o
sub-perfil que ele cria deve **expirar**. Regra barata: sub-cluster que não
recebe amostra nova há N meses e tem poucas amostras é candidato a poda na
tela de manutenção. Não automatizar a exclusão; listar.

---

## 4. Limpar o que já existe (e o que existirá)

A migração descarta os embeddings atuais de qualquer forma (torch ≠ ONNX,
risco 4). Então a limpeza tem dois alvos distintos:

**4a. Reinscrição em lote a partir do acervo.** As gravações de duas
faixas em `data/recordings/` e os nomes confirmados no histórico são um
corpus de inscrição pronto. Uma ferramenta batch (`tools/reenroll.py`):

1. varre as gravações que têm entrada no histórico com nomes confirmados;
2. reextrai embeddings sob as regras da seção 2 (sem sobreposição, piso de
   duração, mic.wav para você);
3. propõe os perfis com os snippets ao lado;
4. o humano revisa uma vez e aceita.

Resultado: perfis novos já limpos, com procedência, sem depender de meses
de reaprendizado passivo. É a resposta concreta para "temos muitas
gravações das mesmas pessoas" — usar o acervo de uma vez, em lote, em vez
de amostra por amostra conforme as reuniões acontecem.

**4b. Manutenção contínua — os três detectores da tela de vozes:**

- **outlier interno**: amostra com similaridade média baixa contra as
  demais do próprio perfil → suspeita de contaminação;
- **confusão entre perfis**: amostra de A que está mais perto do centróide
  de B do que do de A → suspeita de rótulo errado (foi o cross-talk de B
  que entrou, ou a diarização trocou);
- **duplicata de pessoa**: dois perfis cujos centróides são mais próximos
  entre si do que o típico entre pessoas → provavelmente "Élio" e "Elio"
  (o `voices.json` é chaveado pelo nome cru; a normalização que
  `recordings.py` usa para vocabulário não existe aqui) → sugerir fusão.

Nenhum dos três apaga nada sozinho. Eles alimentam uma fila de revisão; o
snippet decide.

---

## 5. Melhorar o match na hora da reunião (independente dos perfis)

- **Vários embeddings por falante desconhecido, decisão pela mediana.**
  Hoje o match embeda só o segmento mais longo do falante — que pode ser
  justamente o segmento com cross-talk. Extrair 3 embeddings dos 3 maiores
  trechos *limpos* e usar a mediana das similaridades torna o match imune
  a um trecho sujo.
- **Atribuição conjunta, não gulosa**: dois falantes da mesma reunião não
  podem casar com o mesmo perfil. Resolver o pareamento
  falantes×perfis como atribuição (húngaro sobre a matriz de similaridade)
  em vez de cada falante escolher independente.
- **O calendário como reforço, nunca como filtro.** Os participantes do
  evento já estão no `meta.json`, mas a lista não é confiável como
  universo fechado: entra gente sem convite e falta convidado. Decisão
  registrada: **todos os perfis salvos continuam candidatos, sempre.** O
  convite entra como assimetria de exigência — quem está na lista casa
  com o threshold normal; quem não está precisa de evidência maior (ex.:
  threshold de auto-aplicação mais alto, ou cair para a faixa de
  *sugestão* em vez de aplicar direto). Um desconhecido de verdade
  continua virando "Speaker N" normalmente. Assim a lista dá a "garantia
  maior" na nomeação sem nunca excluir uma pessoa possível — e o erro
  residual é sempre do tipo barato (pedir uma confirmação a mais), nunca
  do caro (nome impossível de aparecer).
- **Três faixas de confiança em vez de um threshold binário**: acima do
  limiar alto, aplica o nome; na faixa média, mostra como *sugestão* na
  tabela de falantes ("Dimi? 58%") para confirmação de um clique — que
  por sua vez vira amostra nova de inscrição confirmada; abaixo, fica
  "Speaker N". O custo de errar para cima (nome errado numa ata) é muito
  maior que o de errar para baixo (um clique a mais).

---

## 6. A página de vozes na UI

O que ela precisa mostrar para a limpeza ser um gesto e não uma tarde:

```
Pessoas
├── Dimi Randel          14 amostras · 2 condições · consistência 0,81
│   ├── [▶] 2026-08-06 · system · 4,2s · Headset AN01        cos médio 0,84
│   ├── [▶] 2026-07-30 · system · 3,1s · Sala Conf B  ⚠ outlier (0,41)
│   │        [remover] [mover para outra pessoa] [manter assim]
│   └── ...
├── Fila de revisão (3)   ← quarentenas da seção 2 + flags da seção 4b
└── Sugestões: fundir "Élio" ↔ "Elio" (0,93)?
```

- **play do snippet em cada linha** — o requisito que define todo o resto
  do desenho (é ele que exige guardar o snippet na inscrição);
- ações por amostra: remover, mover para outra pessoa, tirar da
  quarentena; por perfil: renomear, fundir, apagar;
- indicador de saúde por perfil: nº de amostras, condições cobertas,
  consistência interna — diz de quem o sistema precisa de mais áudio;
- a fila de revisão como entrada principal: o usuário não caça problema,
  o sistema apresenta os 3 casos suspeitos da semana com o áudio pronto
  para ouvir.

No Gradio dá para fazer uma versão mínima (tabela + player por linha); a
versão boa converge com a Fase 3 do porte (UI própria no WebView2), e o
modelo de dados da seção 1 é o mesmo nos dois — por isso ele é a única
parte disto que vale fazer *antes* de precisar.

---

## 7. Ordem, quando chegar a vez

| # | o quê | por quê primeiro |
|---|---|---|
| 1 | Modelo de dados da seção 1 (amostra com procedência + snippet) | tudo depende dele, e a reinscrição da migração só acontece uma vez |
| 2 | Regras de inscrição da seção 2 | estancar a contaminação antes de limpar |
| 3 | `tools/reenroll.py` em lote sobre o acervo | perfis limpos imediatamente, sem esperar reuniões |
| 4 | Match: mediana de 3 + atribuição conjunta + prior do calendário | ganho de atribuição sem tocar na UI |
| 5 | Sub-perfis por condição (nível 2) | resolve sala/resfriado/microfone |
| 6 | Página de vozes com fila de revisão | a manutenção contínua |
| 7 | AS-norm (nível 3) | só se o threshold continuar instável com >10 perfis |

Calibração de tudo (thresholds das três faixas, corte de quarentena,
limiar de outlier): o gold set da [QUALIDADE.md](QUALIDADE.md) já tem os
falantes rotulados — a curva falso-aceite × falso-rejeite sai dele.
