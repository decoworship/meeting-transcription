# Meeting Transcription

Web app for transcribing meeting recordings (audio or video) with speaker diarization, voice fingerprinting, and per-project settings.

Released under the [MIT License](LICENSE).

## Features

- Transcribe audio/video files (mp4, mkv, avi, mov, webm, wav, mp3, m4a, flac, ogg)
- Speaker diarization (who said what)
- Voice fingerprinting — auto-recognize people across recordings
- Per-project settings (vocabulary, language, model)
- Audio player synced with transcript (click a segment to seek)
- Inline editing of segments and speakers
- Export to TXT / SRT / VTT / DOCX
- History of past transcriptions
- GPU acceleration with faster-whisper

## HuggingFace setup (required)

Speaker diarization and voice fingerprinting use gated pyannote models. Before
the app can run, you need a HuggingFace token AND you must accept the model
terms in your browser.

1. **Create a token** at https://huggingface.co/settings/tokens (Read scope is
   enough). Copy it — it starts with `hf_`.
2. **Accept the terms** for both models (you must be logged in; click "Agree
   and access repository" on each page):
   - https://huggingface.co/pyannote/speaker-diarization-community-1
   - https://huggingface.co/pyannote/wespeaker-voxceleb-resnet34-LM
3. **Provide the token** to the app via a `.env` file (see below) or by
   pasting it into the UI on first run.

Without these steps you will hit a 401/403 from HuggingFace when the app tries
to load the diarization pipeline. See `Huggingface access guide.md` for
detailed troubleshooting.

## Run with Docker (recommended)

The Docker image bundles Python 3.13 + CUDA 12.8 + all dependencies. Models are downloaded on first use into a mounted volume.

### Prerequisites

- Docker 24+ and Docker Compose
- For GPU: NVIDIA driver + [nvidia-container-toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html) on the host
- A HuggingFace token with the model terms accepted (see "HuggingFace setup" above)

### Setup

```bash
# 1. Set your HF token
echo "HF_TOKEN=hf_xxxxxxxxxxxxxxxxxxxx" > .env

# 2. Build and start
docker compose up -d --build

# 3. Open in browser
# http://localhost:7860
```

First transcription downloads the Whisper + pyannote models (~5–10 GB) into `./data/huggingface`. Subsequent runs reuse them.

### Without GPU

Edit `docker-compose.yml` and remove the `deploy.resources.reservations.devices` block. PyTorch will fall back to CPU. `tiny` and `base` Whisper models are usable on CPU; `large-v3` is much slower without a GPU.

### Stopping / updating

```bash
docker compose down            # stop
docker compose up -d --build   # rebuild after code changes
docker compose logs -f         # follow logs
```

User data persists in `./data/meeting-transcription/` (config, history, projects, voice profiles). This directory is gitignored — your transcripts, voice fingerprints, and project list never leave your machine.

A starter `config.json` is not shipped; the app creates one with sensible defaults the first time you save settings in the UI. See `config.example.json` for the schema and field names.

## Run locally without Docker

### Requirements

- Python 3.13 (via `uv` or system)
- FFmpeg in PATH
- Optional: CUDA 12.8 for GPU
- HuggingFace token (set `HF_TOKEN` env var or paste in the UI)

### Install

```bash
uv sync
```

### Usage

```bash
# Web UI (recommended)
uv run python web.py

# Desktop UI (legacy CustomTkinter — works under WSL/native Linux)
uv run python main.py
```

Web UI opens at http://localhost:7860.
