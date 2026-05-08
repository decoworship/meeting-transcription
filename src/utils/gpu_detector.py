"""GPU detection utility for determining CUDA availability."""

import logging

logger = logging.getLogger(__name__)


def is_cuda_available() -> bool:
    """Check if CUDA is available for GPU acceleration."""
    try:
        import torch
        return torch.cuda.is_available()
    except ImportError:
        logger.warning("PyTorch not installed, CUDA detection unavailable")
        return False
    except Exception as e:
        logger.warning(f"Error checking CUDA availability: {e}")
        return False


def get_device_info() -> dict:
    """Get detailed information about available compute devices."""
    info = {
        "cuda_available": False,
        "device_count": 0,
        "devices": [],
        "recommended_device": "cpu"
    }

    try:
        import torch

        info["cuda_available"] = torch.cuda.is_available()

        if info["cuda_available"]:
            info["device_count"] = torch.cuda.device_count()

            for i in range(info["device_count"]):
                device_props = torch.cuda.get_device_properties(i)
                info["devices"].append({
                    "index": i,
                    "name": device_props.name,
                    "total_memory_gb": device_props.total_memory / (1024**3),
                    "compute_capability": f"{device_props.major}.{device_props.minor}"
                })

            info["recommended_device"] = "cuda"

    except ImportError:
        logger.warning("PyTorch not installed")
    except Exception as e:
        logger.warning(f"Error getting device info: {e}")

    return info


def get_optimal_compute_type() -> str:
    """Determine the optimal compute type for faster-whisper."""
    if not is_cuda_available():
        return "int8"  # CPU optimization

    try:
        import torch

        # Check compute capability for float16 support
        if torch.cuda.is_available():
            props = torch.cuda.get_device_properties(0)
            # float16 requires compute capability >= 7.0
            if props.major >= 7:
                return "float16"
            else:
                return "int8_float16"
    except Exception:
        pass

    return "int8"


def enable_gpu_optimizations() -> None:
    """Enable TF32 and high-precision matmul on supported GPUs (Ampere+).

    TF32 trades a tiny amount of bit-level reproducibility for substantial
    speedups on matrix operations (5-10x on RTX 30/40 series, A100, H100).
    For transcription/diarization workloads exact reproducibility is not
    needed, so this is a strict win.
    """
    try:
        import torch

        if not torch.cuda.is_available():
            return

        props = torch.cuda.get_device_properties(0)
        # TF32 requires compute capability >= 8.0 (Ampere or newer)
        if props.major >= 8:
            torch.backends.cuda.matmul.allow_tf32 = True
            torch.backends.cudnn.allow_tf32 = True
            # Modern PyTorch API equivalent
            try:
                torch.set_float32_matmul_precision("high")
            except Exception:
                pass
            logger.info(f"Enabled TF32 on {props.name} (compute {props.major}.{props.minor})")
        else:
            logger.info(
                f"TF32 not supported on {props.name} (compute {props.major}.{props.minor}, requires >= 8.0)"
            )
    except Exception as e:
        logger.warning(f"Failed to enable GPU optimizations: {e}")
