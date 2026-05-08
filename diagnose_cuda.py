"""Diagnostic script for CUDA/GPU detection issues."""

import sys

print("=" * 60)
print("CUDA/GPU Diagnostic Report")
print("=" * 60)

# Check Python version
print(f"\nPython: {sys.version}")

# Check PyTorch
try:
    import torch
    print(f"\nPyTorch version: {torch.__version__}")
    print(f"PyTorch CUDA compiled: {torch.version.cuda or 'NO (CPU-only build)'}")
    print(f"torch.cuda.is_available(): {torch.cuda.is_available()}")

    if torch.cuda.is_available():
        print(f"CUDA device count: {torch.cuda.device_count()}")
        for i in range(torch.cuda.device_count()):
            props = torch.cuda.get_device_properties(i)
            print(f"  Device {i}: {props.name}")
            print(f"    Memory: {props.total_memory / (1024**3):.1f} GB")
            print(f"    Compute capability: {props.major}.{props.minor}")
    else:
        print("\n>>> CUDA NOT AVAILABLE <<<")
        if torch.version.cuda is None:
            print("REASON: PyTorch was installed WITHOUT CUDA support (CPU-only build)")
            print("\nTo fix this, reinstall PyTorch with CUDA support:")
            print("  1. Remove current torch packages")
            print("  2. Install from PyTorch CUDA index")
        else:
            print(f"PyTorch was built with CUDA {torch.version.cuda}")
            print("But CUDA runtime is not accessible.")
            print("Check: NVIDIA drivers, CUDA toolkit installation")

except ImportError as e:
    print(f"PyTorch not installed: {e}")

# Check faster-whisper
print("\n" + "-" * 60)
try:
    import faster_whisper
    print(f"faster-whisper version: {faster_whisper.__version__}")
except ImportError:
    print("faster-whisper not installed")

# Check ctranslate2 (used by faster-whisper)
try:
    import ctranslate2
    print(f"ctranslate2 version: {ctranslate2.__version__}")
    print(f"ctranslate2 CUDA support: {ctranslate2.get_cuda_device_count() > 0}")
except ImportError:
    print("ctranslate2 not installed")
except Exception as e:
    print(f"ctranslate2 error: {e}")

print("\n" + "=" * 60)
print("RECOMMENDED FIX (if CUDA not available):")
print("=" * 60)
print("""
Run these commands in your project directory:

# Remove CPU-only PyTorch
uv pip uninstall torch torchaudio torchvision

# Install PyTorch with CUDA 12.4 (recommended for RTX 2060)
uv pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu124

# Or for CUDA 11.8 (if you have older CUDA toolkit):
# uv pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu118
""")
