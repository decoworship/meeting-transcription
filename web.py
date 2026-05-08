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

    app = create_app()
    app.launch(
        server_name="0.0.0.0",
        server_port=7860,
        theme=gr.themes.Soft(),
        favicon_path=get_logo_path(),
        # Allow serving the extracted WAV files (in /tmp) via /gradio_api/file=...
        # so our custom <audio> element can fetch them.
        allowed_paths=[tempfile.gettempdir()],
        css="""
            .gradio-container {
                max-width: 1100px !important;
                margin-left: auto !important;
                margin-right: auto !important;
            }
            .timing-box textarea {
                font-family: 'Consolas', 'Courier New', monospace !important;
                font-size: 12px !important;
            }
            .mt-segment:hover {
                background: rgba(59, 130, 246, 0.08) !important;
            }
            .mt-segment.mt-active {
                background: rgba(59, 130, 246, 0.18) !important;
                box-shadow: inset 3px 0 0 #3b82f6;
            }
            /* Hide the helper textbox visually but keep it in the DOM and event-targetable */
            .mt-hidden-input {
                position: absolute !important;
                width: 1px !important;
                height: 1px !important;
                overflow: hidden !important;
                clip: rect(0 0 0 0) !important;
                white-space: nowrap !important;
                pointer-events: none !important;
                opacity: 0 !important;
            }
        """,
    )


if __name__ == "__main__":
    main()
