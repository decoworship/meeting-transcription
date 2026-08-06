# Plano de trabalho

Três frentes, em ordem de dependência: validar o áudio gravado, melhorar o
gravador, redesenhar a interface.

---

## 0. Bloqueio: o microfone da gravação de 06/08 está mudo

A gravação `2026-08-06_10-31-03` tem a faixa do microfone **95,3% em silêncio**:

```
mic.wav     2204s, 2099s de zeros exatos
            maior trecho: 1400s (23 min) a partir de 0:81
            segundo:       690s (11,5 min) a partir de 24:57
system.wav  saudável, 0,5% de zeros
```

São **zeros exatos**, não fala baixa. Duas causas possíveis:

1. **Mute pela bandeja.** O clique no ícone agora muta em vez de parar. Um
   clique para "conferir o estado" muta a faixa por toda a reunião.
2. **Mute no hardware** do headset AN01, ou o dispositivo capturado em modo
   exclusivo por outro app.

O `meta.json` não denuncia nenhuma das duas: diz `no_audio: false`, porque o
canal produziu áudio nos primeiros 81 segundos e o campo só marca "nunca teve
áudio". **Essa é uma falha de instrumentação, não só de operação** — a
gravação parecia saudável pelos metadados.

### Correções necessárias antes da próxima gravação

- Registrar no `meta.json` o **maior trecho silencioso** e o **tempo total
  mudo** por faixa, não apenas o booleano.
- Registrar quanto tempo a gravação passou **mutada pela bandeja**, separando
  mute deliberado de canal morto.
- Avisar na bandeja quando o mute passar de N minutos — mute esquecido é agora
  o modo de falha mais provável, dado o novo comportamento do clique.

---

## 1. Testes de validação do áudio gravado

### O que esta gravação consegue e não consegue validar

| pergunta | possível hoje? |
|---|---|
| O áudio do sistema do gravador serve tão bem quanto o do OBS? | **sim** |
| A deriva de clock aguenta 36 minutos? | **sim** |
| As duas faixas melhoram a atribuição de falante? | não — mic morto |
| A minha voz precisa de supressão de ruído? | não — mic morto |

### Teste A — gravador vs OBS (executável agora)

Mesma reunião, duas capturas independentes: `system.wav` do gravador e a faixa
de áudio do MP4 do OBS. Transcrever ambas com config idêntica e comparar.

Métricas, todas sem verdade de referência:

- cobertura: contagem de palavras, número de segmentos, maior lacuna
- acertos de vocabulário: nomes do projeto e jargão conhecidos
- vazamento de idioma: trechos em inglês/espanhol no meio do português
- divergência: alinhar por tempo e extrair só os pontos onde discordam

Critério de decisão: se o gravador empatar ou ganhar, o OBS sai de cena. Se
perder, investigar antes de abandonar o OBS.

### Teste B — deriva ao longo de 36 minutos (executável agora)

Correlacionar `system.wav` com o áudio do MP4 do OBS em janelas ao longo da
gravação e medir o deslocamento em cada uma. Se o deslocamento crescer ao longo
do tempo, a âncora de relógio não está segurando em duração real — só foi
verificada em 5 minutos até agora.

### Teste C — duas faixas vs faixa única (precisa de gravação boa)

Processar a mesma reunião pelos dois caminhos e comparar a atribuição de
falante com o proxy já construído: quanto tempo de fala fica atribuído ao
falante errado porque o segmento atravessa turnos.

Também: quantos segmentos o `assign_owner` reivindica e quantos estão certos.
É o que calibra o `OWNER_MARGIN` (hoje 2.0, escolhido por raciocínio, não
medido).

### Teste D — supressão de ruído (precisa de gravação boa)

**Não adicionar processamento por fé.** O método:

1. medir o piso de ruído do `mic.wav` nos trechos em que você está calado
2. se o piso for desprezível, não há o que suprimir — encerrar aqui
3. se não for, transcrever a faixa crua e a tratada, comparar

Candidatos, do mais barato ao mais caro: gate por energia, `afftdn` do ffmpeg,
RNNoise, DeepFilterNet.

Critério: só entra se melhorar métrica de transcrição. Supressão agressiva
costuma comer consoantes e piorar o resultado.

Nota: com fone (o seu caso) o vazamento acústico do sistema para o microfone é
mínimo, então cancelamento de eco provavelmente é desnecessário. Confirmar
medindo a correlação entre as faixas nos trechos em que só o sistema fala.

### Próxima gravação: o que fazer diferente

Uma reunião em que **você fale bastante**. A de 06/08 não serviria nem com o
microfone funcionando — sem fala sua não há o que avaliar em atribuição de
falante nem em supressão de ruído.

---

## 2. Melhorias no gravador

### 2.1 Integração com o Teams

Espelhar o estado de mute do Teams no gravador, para não existirem dois mutes
independentes na cabeça de quem usa.

**Mecanismo:** WebSocket local em `ws://127.0.0.1:8124` — a mesma API que
plugins de Stream Deck usam. Ela reporta estado por eventos, incluindo
`isMuted` e se há reunião ativa.

**Escopo:**

- conectar ao subir; token emitido pelo Teams na primeira conexão, com prompt
  de pareamento que você aprova uma vez, e persistido
- assinar os eventos de estado e espelhar o mute na faixa do microfone
- indicar na bandeja se a ponte está ativa, para o estado do ícone ser legível
- reconectar sozinho quando o Teams reiniciar

**Degradação:** sem Teams, sem token ou sem resposta, o gravador funciona
exatamente como hoje, com o mute manual. A ponte é bônus, nunca dependência.

**Riscos:** só Teams novo; API não documentada pela Microsoft, pode mudar entre
versões; só reporta estado dentro de uma reunião.

**Teste:** exige reunião real. Verificar mute nos dois sentidos, entrada e
saída da reunião, e o Teams fechando no meio da gravação.

### 2.2 Integração com o Google Calendar

Registrar, no início da gravação, qual reunião da agenda está acontecendo.

**Escopo:**

- OAuth de aplicativo desktop, escopo somente leitura
  (`calendar.readonly`), token guardado em `%USERPROFILE%\.meeting-recorder`
- ao iniciar, localizar o evento que cobre o instante atual (ou o mais próximo
  dentro de ±15 min)
- preencher o `meta.json`, que já tem os campos reservados: `title`,
  `attendees`, `calendar_event_id`
- se houver mais de um candidato, escolher pela bandeja em vez de adivinhar

**O ganho que fecha um ciclo:** os participantes do evento alimentam o
vocabulário customizado do transcritor. O caso "Dimi → Jimmy" que originou toda
essa investigação deixa de depender de alguém lembrar de digitar o nome.

**Mapeamento para cliente/projeto:** o app já guarda configurações por projeto,
incluindo o vocabulário. Uma regra simples (por participante, por domínio de
e-mail ou por palavra no título) pode sugerir o projeto; confirmação manual na
primeira vez, memorizada depois.

**Degradação:** falha de rede, token expirado ou nenhum evento encontrado nunca
podem impedir ou atrasar a gravação. A associação com o calendário é
posterior e opcional — inclusive editável depois no app.

---

## 3. Redesign da interface com o AA Design System

### O que o design system é

CSS puro com custom properties, um único `styles.css` como entrada, fontes
auto-hospedadas (Fraunces e Hanken Grotesk), tema escuro por
`data-tema="escuro"`, tokens em `tokens.json`. Componentes em React ou como
classes CSS. Português como língua padrão. Sem biblioteca de ícones.

### O conflito estrutural

O app é Gradio, que gera o próprio HTML e traz o próprio CSS. Isso limita o
alcance do redesign, mas de forma desigual:

- **Blocos de HTML escritos à mão** (transcrição, cartões de falante, barra de
  etapas, cabeçalho) — controle total, fidelidade total possível
- **Widgets do Gradio** (dropdowns, sliders, accordions, upload) — dá para
  tematizar via tokens e CSS, mas o DOM é dele; alguns detalhes não se dobram
- **Componentes React do design system** — inutilizáveis dentro do Gradio

### Estratégia em três fases

**Fase 1 — tokens e tema.** Trazer `tokens.css` e as fontes para dentro da
imagem (o CSP e o modo offline impedem depender de CDN) e mapear para um
`gr.themes.Base` customizado: cores, tipografia, raios, espaçamentos. Risco
baixo, ganho visual grande, nada de estrutura muda.

**Fase 2 — blocos próprios.** Reescrever o HTML que já é nosso usando as
classes do design system: a transcrição, os cartões de falante, a barra de
etapas, o cabeçalho, o painel de tempos. É onde a identidade de fato aparece.

**Fase 3 — decidir sobre o Gradio.** Com as fases 1 e 2 prontas, avaliar quanto
ainda destoa. Se for pouco, parar. Se incomodar, aí sim considerar uma
interface própria (FastAPI servindo estático + os componentes React do design
system), sabendo que é reescrever a camada de UI inteira.

### Decisões necessárias antes de começar

1. **Idioma.** A interface hoje é inglês; o design system é português-primeiro,
   inclusive nos nomes de token e no tom de voz. Traduzir a UI para pt-BR faz
   parte do redesign ou fica para depois?
2. **Como consumir o design system.** Submódulo git, cópia versionada dentro do
   repositório, ou pacote publicado? A imagem Docker precisa dos arquivos
   embutidos de qualquer forma.
3. **Tema escuro.** O app hoje segue o tema do Gradio. Passar a expor o toggle
   `data-tema` do design system, ou seguir a preferência do sistema?
4. **Ordem.** Antes ou depois das integrações do gravador? O redesign é o item
   mais longo e o que menos muda a qualidade da transcrição.

---

## 4. Correções pequenas

- **Campo de data não preenche ao escolher uma gravação.** O
  `extract_date_from_filename` está ligado apenas ao `file_input`; o seletor de
  gravações não dispara nada. O nome da pasta (`2026-08-06_10-31-03`) já traz a
  data, e o `meta.json` traz `recorded_at` — a segunda fonte é melhor, por ser
  o instante real e não o nome do arquivo.
- **`user_label` não persiste** entre sessões, volta para "You" a cada reload.
