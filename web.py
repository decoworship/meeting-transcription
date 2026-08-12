#!/usr/bin/env python3
"""Web interface entry point for Meeting Transcription."""

import logging
import tempfile

import gradio as gr


def main():
    log_format = "%(asctime)s - %(name)s - %(levelname)s - %(message)s"
    logging.basicConfig(
        level=logging.INFO,
        format=log_format,
        handlers=[
            logging.StreamHandler(),
            logging.FileHandler("transcription.log", mode="w"),
        ],
    )

    from src.web.gradio_app import create_app, get_logo_path
    from src.web.theme import build_css, build_theme

    app = create_app()
    app.launch(
        server_name="0.0.0.0",
        server_port=7860,
        # Base() rather than Soft(): Soft ships its own palette and fights the
        # design system for the same variables. Base is close to unstyled, so the
        # tokens in build_css() decide everything.
        theme=build_theme(),
        favicon_path=get_logo_path(),
        # Allow serving the extracted WAV files (in /tmp) via /gradio_api/file=...
        # so our custom <audio> element can fetch them.
        allowed_paths=[tempfile.gettempdir()],
        css=build_css(),
    )


if __name__ == "__main__":
    main()
