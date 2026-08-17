# Fase 4 — handoff: o instalador

O que a fase de fato entregou, medido, e o que ela deixa para as seguintes. A
carta está em [FASE4.md](FASE4.md); o texto para quem recebe o app, em
[INSTALAR.md](INSTALAR.md).

Executada entre 14 e 15/08/2026, na branch `feat/fase-4-instalador`.

**A fase fechou pelo critério G**: o dono do produto mandou o instalador para um
amigo, numa máquina que não é a de quem compilou, e funcionou. Nenhum outro
critério substitui esse.

---

## 1. Os números

| | antes | depois |
|---|---|---|
| como se instalava | copiar um `.exe` e uma DLL numa pasta | um instalador |
| tamanho entregue | — | **1,59 GB** |
| payload bruto | 5,4 GB | 4,1 GB |
| compressão | 24,5 min | 6,8 min |
| segredo no binário | token do HuggingFace embutido | **nenhum** |
| diarização na 1ª execução | baixava 32 MB de um repositório com portão | já está em disco |
| testes | 279 | **289** |

---

## 2. O que a fase entregou, item a item

| # | o quê | como ficou |
|---|---|---|
| 1 | a versão | `0.1.0` em `Directory.Build.props`, bloco "Sobre" com diagnóstico copiável, `CHANGELOG.md` |
| 2 | o fim do token do HuggingFace | os 57 MB de pesos viajam dentro do app, CC-BY-4.0 com atribuição |
| 3 | emagrecer o payload | 5,4 → 4,1 GB, com o motor de ata saindo para download sob demanda |
| 5 | a primeira execução | cai em Modelos, falha legível sem modelo, checagem de disco, aviso de CPU |
| 4+6 | o instalador e o script | Inno Setup por usuário, `montar_instalador.sh` com nove réguas |

E três coisas que **não estavam na carta** e entraram porque o uso cobrou:

- **a régua de privacidade** (`tools/conferir_privacidade.py`), pedida pelo dono
  do produto antes de o instalador ir para alguém: nada de cliente, projeto, voz
  ou reunião pode viajar. Ela é régua de build, não conferência de uma vez;
- **o motor de ata como pacote baixável**, que era otimização de tamanho e virou
  a primeira aplicação real de "motores como pacotes" ([PLANO.md](PLANO.md) §5)
  ao lado dos modelos;
- **a família `diarizacao` saiu do catálogo**, porque o item 2 a tornou mentira:
  o cartão media o cache do HuggingFace, que o motor deixou de ler.

---

## 3. O que foi medido, e onde

Tudo na máquina do usuário, com as gravações reais dela.

| pergunta | resposta |
|---|---|
| carregar o pyannote de pasta local muda a diarização? | **não.** 602 segmentos idênticos, mesmos falantes, mesmos instantes |
| quanto custa a rede na 1ª diarização? | 64 s — a diferença entre 184 s e 120 s |
| dá para cortar o `cudnn_engines_precompiled` (589 MB)? | **não.** A diarização morre; o cuDNN não cai para o runtime-compiled |
| e o `cufft`, `cusparse`, `cusolver`, `cublas`? | **não.** Estão na tabela de importações do `torch_cuda.dll` |
| e o `curand`, o `cusolverMg`? | **sim**, 141 MB. Saída idêntica |
| `sympy`, `pandas`, `matplotlib` são órfãos? | **não.** 86, 7 e 14 arquivos os importam |
| algum dado meu entra no instalador? | **não.** 17.526 arquivos, 5,66 GB, duas réguas independentes |

Ferramentas que ficam: `tools/conferir_diarizacao_local.py` (o critério E),
`tools/conferir_motores_curto.py` (1 min por corte, contra 15 de uma reunião
inteira) e `tools/conferir_privacidade.py`.

---

## 4. As decisões que valem revisitar

**O token saiu; o segredo do Google ficou.** O do HuggingFace era secreto de
verdade — dava acesso a uma conta — e a medição mostrou que substituí-lo custava
57 MB. O do Google é a credencial do *aplicativo*, que a própria documentação do
Google trata como não-confidencial em app instalado. A conta do usuário
(`google_token.json`) nunca viajou, e a régua de privacidade confere isso.

**A régua de privacidade tem uma lista de homônimos.** "Vivo" é cliente e também
é um demuxer do ffmpeg. A exceção é por caminho e por termo, com o motivo
escrito, e **continua aparecendo na saída como "ignorado"** — silenciar sem
explicar transformaria a régua num carimbo.

**Reprovar em vez de sincronizar.** Quando o `motor.py` do repositório diverge do
que está na árvore de motores, o `montar_instalador.sh` para. A alternativa —
sincronizar sozinho — faria montar um instalador mexer, de lado, na instalação
que o usuário está usando para trabalhar.

**Instalação por usuário, sem escolha.** `PrivilegesRequiredOverridesAllowed`
saiu: deixar escolher "para todos os usuários" instalaria em `{localappdata}` do
administrador, e o app abriria para a pessoa errada — ou não abriria.

---

## 5. As armadilhas medidas

Todas custaram uma tentativa, nenhuma aparece na documentação com o sintoma que
produz:

- **`const` dentro de função não existe no Pascal Script.** O erro é
  `'BEGIN' expected`, na linha do `const`;
- **`#` na primeira coluna vira diretiva do pré-processador**, mesmo dentro de
  string do `[Code]`. Uma linha continuada com `#13#10` aborta a compilação;
- **o interop do WSL escapa as aspas** do primeiro token. Como o `ISCC.exe` mora
  em `Inno Setup 6` — com espaço —, chamar direto falha; a saída é um `.cmd`
  escrito daqui;
- **`MsgBox` trava instalação silenciosa.** `WizardSilent` e `UninstallSilent`
  existem para isso, e é assim que o instalador é exercitado antes de ir a
  alguém;
- **`Path()` é o diretório atual, não "nada".** A régua de privacidade varreu o
  repositório inteiro, incluindo os 2 GB de instalador em `dist/`;
- **regex por termo não escala.** 55 termos × 5,4 GB gastaram 20 min de CPU sem
  terminar. Com `grep -F` como pré-filtro (Aho-Corasick em C) e o Python só
  confirmando fronteira, são 6 min;
- **fronteira de palavra não é detalhe.** Sem ela o cliente "Vivo" casa dentro de
  `FaixaAoVivo`, um tipo do C#;
- **o stderr do torch não é UTF-8.** Um byte 0xE7 derruba a leitura depois de o
  motor já ter respondido certo;
- **`publicar.sh --so-build` não sincroniza os sidecars.** Achado no fim da fase,
  ao responder "gerar uma versão nova é só rodar o build?". Virou régua.

---

## 6. O que a Fase 5 (e a 6) herdam

### 6.1 A rota de atualização não existe — e agora ela é obrigatória

A carta ([FASE4.md](FASE4.md) §10) dispensou atualização automática com um
argumento que era verdadeiro e deixou de ser: *"não há servidor de update, e não
vai haver por causa de três amigos"*. **Havia um amigo; agora há um amigo com uma
instalação de 0.1.0 numa máquina que não é a nossa.** Toda correção da Fase 5 ou
6 precisa de um caminho até ele.

O que **já funciona**, e não é pouco:

- rodar o instalador novo por cima atualiza no lugar (mesmo `AppId`);
- o `AppMutex` pede para fechar o app antes, em vez de falhar no meio da cópia;
- gravações, transcrições, atas, notas, projetos, vozes e modelos baixados moram
  fora da pasta do app e sobrevivem;
- os modelos já baixados não são rebaixados.

O que **não existe**:

- **nenhum aviso de que há versão nova.** O amigo só sabe se alguém contar;
- nenhuma verificação de versão, nenhum canal, nenhum changelog na tela;
- nenhum caminho de download dentro do app.

**O dado que desenha a solução:** o `MeetingApp.exe` tem 18,5 MB e os motores têm
4,1 GB — e os motores quase nunca mudam. **A maioria das versões novas é um
arquivo de 18,5 MB.** Uma rota de atualização que reconheça isso entrega
correção em segundos; uma que reempacote 1,59 GB a cada correção de texto de
botão não vai ser usada.

Três degraus, do mais barato ao mais caro, para a fase que pegar isto decidir:

1. **avisar** — o app consulta um JSON com a versão corrente e mostra um aviso
   em Ajustes › Sobre, com o que mudou. Um dia de trabalho, e resolve o pior do
   problema, que é o amigo não saber;
2. **atualizar só o app** — o aviso vira botão: baixa o `.exe` novo (18,5 MB),
   confere, troca e reabre. Exige assinar o que se baixa ou conferir hash;
3. **instalador completo por dentro** — só quando os motores mudarem. É o que
   existe hoje, apenas disparado de dentro do app.

Ferramentas prontas para o degrau 2 e 3 (Velopack, Squirrel, WinSparkle) fazem
delta e assinatura; entrar nelas é decisão a tomar sabendo que o payload grande
raramente muda.

### 6.2 O resto

- **assinatura de código.** Sem ela o SmartScreen avisa, e o `INSTALAR.md` ensina
  a passar. Reabre quando a audiência crescer — e é pré-requisito do degrau 2
  acima, que baixa e executa um binário;
- **Vulkan e máquina sem NVIDIA.** A primeira máquina sem placa reabre a decisão
  (4) da carta. O que existe é o aviso honesto na tela de Modelos;
- **o critério C nunca foi exercitado.** Atualizar 0.1.0 → 0.1.1 por cima está
  desenhado e não foi feito, porque não houve 0.1.1. É o primeiro teste da
  próxima versão;
- **os 3,46 GB de `torch`,** que existem só para o pyannote e são dois terços do
  que sobrou. Trocá-los por ONNX levaria o instalador para ~300 MB — é a
  [FASE6.md](FASE6.md) §3.1, e é troca de stack, não empacotamento;
- **a agenda do Google na máquina dos outros.** Se o app OAuth estiver em modo
  *Testing*, só contas cadastradas autorizam e o token expira em 7 dias. O
  `INSTALAR.md` já avisa; conferir o estado no console é tarefa aberta.

---

## 7. O ciclo de release, para quem for gerar a próxima

```bash
# 1. a versão, num lugar só
$EDITOR app-net/Directory.Build.props     # <Version>0.1.1</Version>
$EDITOR CHANGELOG.md                      # escrito para quem usa

# 2. se algum motor.py mudou, sincronizar antes (a régua reprova se não)
tools/publicar.sh

# 3. o instalador
tools/montar_instalador.sh
```

O `AppId` do `.iss` **nunca muda**: é por ele que o Windows sabe que a versão
nova é atualização, e não um segundo programa.

As nove réguas que param o build antes de produzir artefato: tamanho do binário,
ausência de token, versão batendo com o `Directory.Build.props`, sidecars iguais
aos do repositório, Python embarcado presente, pesos de diarização presentes,
atribuição presente, `ata\bin` excluído, e a privacidade dos 17.526 arquivos.
