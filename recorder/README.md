# Gravador de reuniões (Windows)

App de bandeja que grava a reunião em **duas faixas separadas** — o que sai pelos
alto-falantes (os outros participantes) e o seu microfone — e entrega numa pasta
que o app de transcrição enxerga.

## Por que é um app Windows separado

O WSL só enxerga o microfone e a saída dos próprios apps do WSL, via WSLg:

```
/dev/snd     -> só 'timer'
/mnt/wslg/   -> PulseAudioRDPSink, PulseAudioRDPSource
```

O que o Teams toca no Windows não passa por aí. A captura de loopback precisa da
API WASAPI, nativa do Windows — daí o gravador rodar fora do container.

## Por que duas faixas

- **Redução de ruído independente por canal.** Conversa paralela vazando da sala
  do cliente não contamina o seu canal.
- **Diarização quase de graça do seu lado.** Tudo em `mic.wav` é você, com
  certeza — o pyannote só precisa separar os outros no `system.wav`.
- **Detecção de falha na hora.** Se um canal ficar mudo (microfone no mudo, cabo
  solto), o ícone fica amarelo durante a gravação em vez de você descobrir depois.

## Instalação

Não precisa instalar Python no Windows. O setup baixa o `uv` e cria tudo dentro
de `%USERPROFILE%\.meeting-recorder` — sem registro, sem PATH, sem `.msi`:

```powershell
.\setup_windows.ps1
```

Para remover por completo:

```powershell
Remove-Item -Recurse -Force "$env:USERPROFILE\.meeting-recorder"
```

## Uso

**Executável** (recomendado) — gere uma vez e use como qualquer app:

```powershell
.\build_exe.ps1
# resultado em %USERPROFILE%\.meeting-recorder\dist\MeetingRecorder\
```

**Direto do fonte:**

```powershell
& "$env:USERPROFILE\.meeting-recorder\.venv\Scripts\python.exe" tray.py
```

### O ícone da bandeja

| cor | significado |
|---|---|
| cinza | parado |
| vermelho | gravando |
| laranja | gravando com o microfone mudo **pela bandeja** |
| amarelo | gravando, mas um canal está sem áudio (ver limiares abaixo) |

> **O mute do Teams não afeta o gravador.** Mutar no Teams só faz o Teams parar
> de *transmitir* — o microfone continua ligado e legível por outros programas,
> o gravador inclusive. Para tirar sua voz da gravação é preciso usar o mute
> desta bandeja. Os dois são caminhos de captura independentes do mesmo
> dispositivo.

### O clique no ícone

| estado | o que o clique faz |
|---|---|
| parado | inicia a gravação |
| gravando | **muta / desmuta o microfone** |

**Parar a gravação só pelo menu** (botão direito → "Parar gravacao"), nunca pelo
clique. É deliberado: um clique acidental que encerra a gravação perde a reunião
inteira; um que muta você percebe na hora e desfaz.

O menu também permite escolher o microfone e a saída a capturar, e abrir a pasta
das gravações. A escolha de dispositivo fica travada durante a gravação — trocar
no meio exigiria reabrir o stream e realinhar as faixas.

O mute **escreve silêncio** em vez de parar a escrita: parar deslocaria a faixa
em relação à do sistema. E note que mutar no Teams *não* impede o áudio de chegar
aqui — são caminhos de captura independentes, por isso este mute existe.

## Saída

```
data/recordings/2026-08-05_14-57-23/
    system.wav    16 kHz mono — os outros participantes
    mic.wav       16 kHz mono — você
    meta.json     duração, dispositivos, deriva corrigida, campos da reunião
```

## Diagnóstico

```powershell
python probe_devices.py                     # os dispositivos existem?
python capture.py --seconds 30              # teste de captura sem UI
```

## Nota técnica: deriva de clock

Os dois dispositivos têm clocks de hardware independentes. Medido nesta máquina:

```
system  +0.103%  ->  +3.7s de desalinhamento em 1h se não corrigido
mic     +0.145%  ->  +5.2s
```

Cada escrita é ancorada no relógio de parede, inserindo ou descartando amostras.
Resultado medido: **1,5 ms** de desalinhamento após 5 minutos — e o erro fica
limitado em vez de acumular, porque cada correção reancora as faixas.
