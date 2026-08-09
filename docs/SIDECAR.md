# O contrato do sidecar

O protocolo entre o núcleo (C#) e os motores (Python). Decidido na
[FASE2.md](FASE2.md), com o porquê da forma no [PLANO.md](PLANO.md) §5
("Correção: stdin/stdout, não HTTP").

Este documento é a especificação. Quem escrever um motor novo implementa isto e
nada mais; quem mexer no cliente não pode quebrar isto.

## Forma

Um processo por motor. Uma linha de JSON por mensagem, UTF-8, sem quebra de
linha dentro do JSON. O cliente escreve no `stdin` do motor e lê do `stdout`.

Por que não HTTP: nenhuma porta para alocar, nenhum diálogo do Firewall do
Windows na primeira execução (num app de bandeja isso parece malware), e a
morte do processo é detectável na hora pelo *pipe* fechado em vez de por
*timeout*. Nada escuta em rede.

## A regra que quebra tudo se for esquecida

**O `stdout` pertence ao protocolo. Nada mais pode escrever nele.**

Não é preciosismo: `torch`, `pyannote`, `transformers` e o próprio Python
imprimem avisos, barras de progresso e mensagens de download no `stdout` sem
pedir licença. Uma única linha dessas no meio do fluxo corrompe o protocolo, e
o sintoma — JSON inválido em algum ponto imprevisível — não aponta para a
causa.

Por isso todo motor duplica o descritor 1 no início, antes de importar
qualquer coisa, e redireciona o `stdout` do processo para o `stderr`:

```python
_protocolo = os.fdopen(os.dup(1), "w", encoding="utf-8", newline="\n")
os.dup2(2, 1)   # daqui em diante, print() vai para o stderr
```

O `stderr` é log livre: o cliente repassa para diagnóstico e nunca o
interpreta.

## Mensagens

Toda mensagem tem `tipo`. Toda mensagem ligada a uma requisição repete o `id`
dela. Campos desconhecidos são ignorados pelos dois lados — é o que permite
acrescentar sem quebrar (a mesma regra do `meta.json`).

### Motor → cliente, uma vez, ao subir

```json
{"tipo": "pronto", "motor": "diarizacao", "versao": "1"}
```

O cliente espera esta linha antes de enviar qualquer coisa. Se o processo
morrer antes dela, o erro é de inicialização — a distinção importa, porque
"não consegui subir o motor" e "o motor falhou nesta gravação" pedem reações
diferentes.

### Cliente → motor

```json
{"id": 1, "op": "diarizar", "audio": "C:\\...\\mix.wav"}
```

### Motor → cliente, durante

```json
{"id": 1, "tipo": "progresso", "pct": 0.4, "texto": "analisando falantes"}
```

Zero ou mais vezes. `pct` entre 0 e 1.

### Motor → cliente, no fim: um dos dois

```json
{"id": 1, "tipo": "resultado", "segmentos": [{"inicio": 0.5, "fim": 3.2, "falante": "SPEAKER_00"}]}
```

```json
{"id": 1, "tipo": "erro", "mensagem": "não foi possível ler o áudio: ..."}
```

Um `erro` encerra a requisição, não o motor: o processo continua vivo e
pronto para a próxima. Motor que morre é outra coisa, e o cliente detecta pelo
pipe fechado.

**Os rótulos de falante saem crus** (`SPEAKER_00`), como o pyannote os produz.
Traduzir para "Falante 1" é decisão de apresentação e vive no núcleo, junto com
o resto da nomeação de vozes.

## Ciclo de vida

- **O motor fica quente entre requisições.** É a razão de existir do processo
  separado: carregar o pyannote a cada gravação custaria mais que diarizar.
- **Cancelar é matar o processo.** Não há `op` de cancelamento e não deve
  haver: um cancelamento cooperativo depende do motor estar num ponto em que
  possa cooperar, e dentro de uma inferência ele não está. Matar libera a VRAM
  na hora (critério B da Fase 2) e conserta de graça o cancelamento cosmético
  da [AUDITORIA.md](AUDITORIA.md) §1.5.
- **Quem descarta é o cliente.** O motor não tem timeout de ociosidade próprio:
  ele não sabe se o usuário foi almoçar ou se o app fechou, e um processo que
  se mata sozinho no meio de uma sessão é um bug difícil de reproduzir.
- **`CREATE_NO_WINDOW` em todo spawn**, senão cada motor pisca um console preto
  no Windows.

## O que ainda não existe, deliberadamente

Sem manifesto, sem registro de motores, sem download, sem `ping`. São dois
motores hardcoded atrás de uma interface até o terceiro (resumo) chegar —
decisão registrada na [FASE2.md](FASE2.md). O pipe fechado já detecta motor
morto; um `ping` seria um segundo mecanismo para o mesmo fato.
