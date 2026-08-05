"""Confere se o ambiente do gravador está completo. Rodado pelo setup_windows.ps1."""

import sys

print("python       :", sys.version.split()[0])
print("local        :", sys.executable)

falhas = []
for mod in ("numpy", "soxr", "pyaudiowpatch", "pystray", "PIL"):
    try:
        m = __import__(mod)
        print(f"{mod:13}: OK {getattr(m, '__version__', '')}")
    except Exception as e:
        print(f"{mod:13}: FALHOU -> {e}")
        falhas.append(mod)

sys.exit(1 if falhas else 0)
