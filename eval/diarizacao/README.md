# Conjunto de avaliação de diarização (congelado)

Corpus padrão para toda medição de diarização do projeto. Os números das seções
3-A a 3-F do [FASE0-RESULTADOS](../../docs/FASE0-RESULTADOS.md) saíram daqui — se
o conjunto mudar, deixam de ser comparáveis com medições futuras.

Versionado aqui, e não em `data/`, por dois motivos: `data/` é ignorado pelo git,
e o `/tmp` foi limpo duas vezes durante a Fase 0 levando corpus e resultados
junto.

## O que é

10 reuniões do [`diarizers-community/ami`](https://huggingface.co/datasets/diarizers-community/ami),
configuração **`ihm`** — headset mix, cada pessoa no próprio microfone e mistura
depois, que é o análogo do nosso `system.wav`. Cinco minutos cada, 3 a 4
falantes, **12,5% de fala sobreposta**, turnos anotados à mão.

Em inglês, e isso é aceitável: diarização opera sobre características acústicas
de locutor, não sobre fonemas. Não existe corpus de diarização anotado em
português (procurado na Fase 0; o NURC brasileiro tem 290 h gravadas mas sem
anotação em RTTM).

## O que está versionado, e o que não

| | |
|---|---|
| `manifesto.json` | as **referências humanas** — turnos e falantes. É o que não se pode perder |
| `hipoteses/` | saídas já medidas do pyannote e do sherpa. Reproduzir custa horas de GPU |
| áudio | **não versionado** (92 MB). Regenerável, ver abaixo |

## Regenerar o áudio

```bash
curl -sL -o /tmp/ami.parquet \
  "https://huggingface.co/api/datasets/diarizers-community/ami/parquet/ihm/test/0.parquet"
python tools/benchmark_der.py preparar --parquet /tmp/ami.parquet \
    --saida data/eval-diarizacao/corpus10 --reunioes 10 --max-segundos 300
```

A seleção é determinística (as 10 primeiras reuniões do split, em ordem), então
o resultado é idêntico. Isso sobrescreve o `manifesto.json` em `data/` — o desta
pasta é a cópia de referência.

## Disciplina de ajuste e teste

- **ajuste**: `reuniao_00`, `reuniao_01`
- **teste (retido)**: `reuniao_02` a `reuniao_09`

Calibrar e reportar no mesmo conjunto foi o erro do resultado 3-B: o threshold
1,0 ganhava nas 2 de ajuste e perdia nas 8 retidas. Qualquer agrupamento novo
(AHC, eigengap) tem mais parâmetros que um threshold, então a disciplina vale com
mais força ainda.

## Como pontuar

```bash
python tools/benchmark_der.py pontuar --corpus data/eval-diarizacao/corpus10 \
    --hipoteses eval/diarizacao/hipoteses/*.json --por-item
```

Os arquivos `hip_cpu-*.json` são as mesmas varreduras de threshold rodadas em
CPU; a diferença entre eles e os de GPU é o **resultado 3-E** — o mesmo parâmetro
dá até 4,8 pontos de DER diferentes só por trocar o provider do onnxruntime.

## Ressalva permanente

Pyannote e sherpa tiveram AMI no treino. A comparação é simétrica e justa, mas os
DERs absolutos são otimistas em relação a uma chamada de Teams com microfone
doméstico. **O que decide é a diferença entre motores, não o valor.**
