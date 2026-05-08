#!/usr/bin/env python3
"""
Meeting Transcription Application

A desktop GUI application for transcribing meeting video recordings
with speaker diarization support.

Usage:
    python main.py

Requirements:
    - FFmpeg installed and in PATH
    - Python packages from requirements.txt
    - Optional: CUDA for GPU acceleration
    - Optional: HuggingFace token for speaker diarization
"""

import sys
import logging


def check_dependencies():
    """Check that required dependencies are installed."""
    missing = []

    try:
        import customtkinter
    except ImportError:
        missing.append("customtkinter")

    try:
        import torch
    except ImportError:
        missing.append("torch")

    if missing:
        print("Missing required dependencies:")
        for dep in missing:
            print(f"  - {dep}")
        print("\nInstall with: pip install -r requirements.txt")
        sys.exit(1)


def check_ffmpeg():
    """Check that FFmpeg is available."""
    import subprocess
    import os

    try:
        result = subprocess.run(
            ['ffmpeg', '-version'],
            capture_output=True,
            text=True,
            creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
        )
        if result.returncode != 0:
            raise RuntimeError()
    except (FileNotFoundError, RuntimeError):
        print("FFmpeg not found!")
        print("Please install FFmpeg and add it to your PATH.")
        print("\nDownload from: https://ffmpeg.org/download.html")
        sys.exit(1)


def main():
    """Main entry point."""
    # Configure logging
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
    )

    logger = logging.getLogger(__name__)
    logger.info("Starting Meeting Transcription Application")

    # Check dependencies
    check_dependencies()
    check_ffmpeg()

    # Import and run the app
    from src.gui.app import MeetingTranscriptionApp

    app = MeetingTranscriptionApp()
    app.mainloop()


if __name__ == "__main__":
    main()
