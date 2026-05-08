"""Standard OpenAI Whisper transcriber (CPU-focused)."""

import logging
from typing import Optional, Callable

from .base import BaseTranscriber, TranscriptionResult, TranscriptionSegment

logger = logging.getLogger(__name__)


class WhisperTranscriber(BaseTranscriber):
    """Transcriber using OpenAI's Whisper model."""

    def __init__(self, model_size: str = "base", device: str = "cpu"):
        super().__init__(model_size)
        self._device = device

    def load_model(self, progress_callback: Optional[Callable[[float, str], None]] = None) -> None:
        """Load the Whisper model."""
        if progress_callback:
            progress_callback(0.0, f"Loading Whisper {self.model_size} model...")

        try:
            import whisper

            logger.info(f"Loading Whisper model: {self.model_size} on {self._device}")
            self._model = whisper.load_model(self.model_size, device=self._device)

            if progress_callback:
                progress_callback(1.0, "Model loaded successfully")

            logger.info("Whisper model loaded successfully")

        except Exception as e:
            logger.error(f"Failed to load Whisper model: {e}")
            raise RuntimeError(f"Failed to load Whisper model: {e}")

    def transcribe(
        self,
        audio_path: str,
        language: Optional[str] = None,
        condition_on_previous_text: bool = False,
        initial_prompt: Optional[str] = None,
        progress_callback: Optional[Callable[[float, str], None]] = None
    ) -> TranscriptionResult:
        """
        Transcribe audio using Whisper.

        Args:
            audio_path: Path to audio file
            language: Optional language code (e.g. 'pt', 'en'). Auto-detects if None.
            condition_on_previous_text: If True, use previous output as context.
            progress_callback: Progress callback

        Returns:
            TranscriptionResult with segments
        """
        if not self.is_loaded:
            self.load_model(progress_callback)

        if progress_callback:
            progress_callback(0.0, "Starting transcription...")

        try:
            # Prepare transcription options
            options = {
                "verbose": False,
                "word_timestamps": False,
                "condition_on_previous_text": condition_on_previous_text,
            }

            if language:
                options["language"] = language
            if initial_prompt:
                options["initial_prompt"] = initial_prompt

            logger.info(f"Transcribing: {audio_path}")

            # Run transcription
            result = self._model.transcribe(audio_path, **options)

            # Convert to our format
            segments = []
            for seg in result["segments"]:
                segments.append(TranscriptionSegment(
                    start=seg["start"],
                    end=seg["end"],
                    text=seg["text"]
                ))

            if progress_callback:
                progress_callback(1.0, "Transcription complete")

            logger.info(f"Transcription complete: {len(segments)} segments")

            return TranscriptionResult(
                segments=segments,
                language=result.get("language"),
                duration=segments[-1].end if segments else 0.0
            )

        except Exception as e:
            logger.error(f"Transcription failed: {e}")
            raise RuntimeError(f"Transcription failed: {e}")

    @property
    def device_type(self) -> str:
        return self._device
