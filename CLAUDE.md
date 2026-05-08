# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Desktop GUI application for transcribing meeting recordings (video or audio) with speaker diarization. Built with CustomTkinter for the UI, Whisper/faster-whisper for transcription, and pyannote-audio for speaker identification.

## Commands

```bash
# Install dependencies and create virtual environment
uv sync

# Run the application
uv run python main.py
```

## Prerequisites

- FFmpeg must be installed and in PATH
- For GPU acceleration: CUDA toolkit
- For speaker diarization: HuggingFace token (accept terms at huggingface.co/pyannote/speaker-diarization-3.1)

## Architecture

### Processing Pipeline

The transcription flow in `src/gui/app.py:_transcription_worker` follows this sequence:
1. **Audio Processing** (0-20%): FFmpeg extracts/normalizes to 16kHz mono WAV (from video or audio input)
2. **Model Loading** (20-30%): Load Whisper or faster-whisper model
3. **Transcription** (30-70%): Generate timestamped segments
4. **Diarization** (70-95%): pyannote identifies speakers, assigns labels via overlap matching
5. **Output** (95-100%): Format with `[timestamp] Speaker N: text`

### Transcriber Strategy Pattern

Two interchangeable transcribers extend `BaseTranscriber`:
- `WhisperTranscriber`: Uses openai-whisper (CPU-focused)
- `FasterWhisperTranscriber`: Uses faster-whisper with CTranslate2 (GPU-accelerated)

Selection is automatic based on CUDA availability, with manual override in GUI.

### Thread-Safe UI Updates

Background processing uses a queue pattern (`_progress_queue`) to safely update the GUI from worker threads. The main thread polls this queue via `_process_queue()` scheduled with `self.after(100, ...)`.

### Speaker Assignment

`SpeakerDiarizer.assign_speakers()` uses overlap-based matching: each transcription segment gets the speaker label with the maximum temporal overlap from diarization results.

## Key Data Types

- `TranscriptionSegment`: Single utterance with start/end times, text, optional speaker
- `TranscriptionResult`: Collection of segments with formatting methods
- `DiarizationSegment`: Speaker turn with timing
