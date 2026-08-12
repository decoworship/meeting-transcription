# syntax=docker/dockerfile:1.7

# CUDA 12.8 + cuDNN runtime on Ubuntu 22.04. Matches the PyTorch wheel built
# for CUDA 12.8 (torch==2.8.0+cu128). If your host doesn't expose a GPU, the
# image still runs (PyTorch falls back to CPU) — just omit `--gpus all`.
FROM nvidia/cuda:12.8.0-cudnn-runtime-ubuntu22.04

ENV DEBIAN_FRONTEND=noninteractive \
    PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    UV_LINK_MODE=copy \
    UV_COMPILE_BYTECODE=1 \
    PATH="/root/.local/bin:${PATH}"

# System deps:
#   ffmpeg     — required by AudioExtractor
#   curl, ca-certificates — for installing uv and HTTPS in general
#   git        — some pip dependencies fetch sources via git
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        ffmpeg \
        git \
    && rm -rf /var/lib/apt/lists/*

# Install uv (Python project/runtime manager). Pin the binary version so
# rebuilds are reproducible; bump as needed.
COPY --from=ghcr.io/astral-sh/uv:0.5.13 /uv /uvx /usr/local/bin/

WORKDIR /app

# Copy lockfiles first so dependency install layers are cached when only
# source files change.
COPY pyproject.toml uv.lock .python-version ./

# Install Python 3.13 (per .python-version) and all dependencies. Mount the
# uv cache so repeated builds reuse downloaded wheels.
RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --frozen --no-install-project

# Copy the rest of the project. Keep this AFTER the dep install layer so code
# changes don't invalidate the heavier dependency cache.
COPY src/ src/
COPY assets/ assets/
# LICENSE is required by the final `uv sync`: pyproject.toml declares
# license = { file = "LICENSE" }, and hatchling errors out if it is missing.
COPY web.py main.py README.md LICENSE ./

# Stamp the build so the running image can be identified from the UI. Placed
# after the source COPY on purpose: the layer cache invalidates whenever the
# code changes, so the timestamp tracks the code it was built from.
RUN date -u +"%Y-%m-%d %H:%M UTC" > /app/BUILD_INFO

# Final install step — package the project itself (no deps, already done).
RUN --mount=type=cache,target=/root/.cache/uv \
    uv sync --frozen --no-dev

EXPOSE 7860

# Volumes: user data + HuggingFace model cache. Both are persisted across
# container removals when bind-mounted from the host.
VOLUME ["/root/.meeting-transcription", "/root/.cache/huggingface"]

# Healthcheck — Gradio responds 200 on /
HEALTHCHECK --interval=30s --timeout=10s --start-period=120s --retries=3 \
    CMD curl -fsS http://localhost:7860/ >/dev/null || exit 1

CMD ["uv", "run", "--no-dev", "python", "web.py"]
