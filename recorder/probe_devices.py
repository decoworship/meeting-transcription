"""Sonda de dispositivos de áudio do Windows — rode ANTES de escrever o gravador.

Responde três perguntas, nesta ordem de risco:
  1. O loopback WASAPI está disponível nesta máquina?
  2. Qual é o dispositivo de saída padrão (o que o Teams toca) e seu loopback?
  3. Qual é o microfone padrão?

Uso (no PowerShell do Windows, NÃO no WSL):
    pip install PyAudioWPatch
    python probe_devices.py
"""

import sys

# O console do Windows usa code page ANSI por padrao e embaralha acentos.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def main() -> int:
    try:
        import pyaudiowpatch as pyaudio
    except ImportError:
        print("ERRO: PyAudioWPatch não instalado.\n")
        print("  pip install PyAudioWPatch\n")
        print("Se falhar a compilação, instale antes o 'Microsoft C++ Build Tools'.")
        return 1

    p = pyaudio.PyAudio()
    try:
        try:
            wasapi = p.get_host_api_info_by_type(pyaudio.paWASAPI)
        except OSError:
            print("ERRO: host API WASAPI não encontrada. Windows 10+ é necessário.")
            return 1

        print(f"WASAPI disponível — {wasapi['deviceCount']} dispositivos\n")

        # --- saída padrão e seu loopback ---
        speakers = p.get_device_info_by_index(wasapi["defaultOutputDevice"])
        print("SAÍDA PADRÃO (o que os outros participantes falam)")
        print(f"  nome : {speakers['name']}")
        print(f"  taxa : {int(speakers['defaultSampleRate'])} Hz")
        print(f"  canais: {speakers['maxOutputChannels']}")

        loopback = None
        for lb in p.get_loopback_device_info_generator():
            if speakers["name"] in lb["name"]:
                loopback = lb
                break

        if loopback:
            print("  LOOPBACK: OK")
            print(f"    index : {loopback['index']}")
            print(f"    nome  : {loopback['name']}")
            print(f"    canais: {loopback['maxInputChannels']}")
            print(f"    taxa  : {int(loopback['defaultSampleRate'])} Hz")
        else:
            print("  LOOPBACK: NÃO ENCONTRADO para este dispositivo")
            print("  (o gravador não conseguirá capturar o áudio da reunião)")

        # --- microfone padrão ---
        print("\nENTRADA PADRÃO (seu microfone)")
        try:
            mic = p.get_device_info_by_index(wasapi["defaultInputDevice"])
            print(f"  index : {mic['index']}")
            print(f"  nome  : {mic['name']}")
            print(f"  canais: {mic['maxInputChannels']}")
            print(f"  taxa  : {int(mic['defaultSampleRate'])} Hz")
        except Exception as e:
            print(f"  ERRO ao ler o microfone padrão: {e}")

        # --- todos os loopbacks, caso queira escolher outro ---
        print("\nTODOS OS LOOPBACKS DISPONÍVEIS")
        for lb in p.get_loopback_device_info_generator():
            print(f"  [{lb['index']:3}] {lb['name']}")

        print("\nTODAS AS ENTRADAS (microfones)")
        for i in range(p.get_device_count()):
            d = p.get_device_info_by_index(i)
            if d["maxInputChannels"] > 0 and d["hostApi"] == wasapi["index"]:
                if "loopback" not in d["name"].lower():
                    print(f"  [{i:3}] {d['name']}")

        print("\n--- me mande esta saída inteira ---")
        return 0 if loopback else 2
    finally:
        p.terminate()


if __name__ == "__main__":
    sys.exit(main())
