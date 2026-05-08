"""WhisperX transcriber - batched inference with word-level alignment."""

import logging
from typing import Optional, Callable

from .base import BaseTranscriber, TranscriptionResult, TranscriptionSegment
from ..utils.gpu_detector import is_cuda_available, get_optimal_compute_type

logger = logging.getLogger(__name__)


class WhisperXTranscriber(BaseTranscriber):
    """Transcriber using WhisperX for batched inference and word-level timestamps."""

    def __init__(self, model_size: str = "base", device: str = "auto", batch_size: int = 16):
        super().__init__(model_size)

        if device == "auto":
            self._device = "cuda" if is_cuda_available() else "cpu"
        else:
            self._device = device

        self._compute_type = get_optimal_compute_type() if self._device == "cuda" else "int8"
        self._batch_size = batch_size

        logger.info(
            f"WhisperX initialized: device={self._device}, "
            f"compute_type={self._compute_type}, batch_size={batch_size}"
        )

    def load_model(self, progress_callback: Optional[Callable[[float, str], None]] = None) -> None:
        """Load the WhisperX model."""
        if progress_callback:
            progress_callback(0.0, f"Loading WhisperX {self.model_size} model...")

        try:
            import whisperx

            logger.info(f"Loading WhisperX model: {self.model_size} on {self._device}")

            self._model = whisperx.load_model(
                self.model_size,
                device=self._device,
                compute_type=self._compute_type,
            )

            if progress_callback:
                progress_callback(1.0, "WhisperX model loaded")

            logger.info("WhisperX model loaded successfully")

        except Exception as e:
            logger.error(f"Failed to load WhisperX model: {e}")
            raise RuntimeError(f"Failed to load WhisperX model: {e}")

    def transcribe(
        self,
        audio_path: str,
        language: Optional[str] = None,
        condition_on_previous_text: bool = False,
        initial_prompt: Optional[str] = None,
        progress_callback: Optional[Callable[[float, str], None]] = None,
    ) -> TranscriptionResult:
        """
        Transcribe audio using WhisperX with optional word-level alignment.

        Args:
            audio_path: Path to audio file
            language: Optional language code (e.g. 'en'). Auto-detects if None.
            progress_callback: Progress callback

        Returns:
            TranscriptionResult with segments
        """
        if not self.is_loaded:
            self.load_model(progress_callback)

        if progress_callback:
            progress_callback(0.0, "Loading audio for WhisperX...")

        try:
            import whisperx

            audio = whisperx.load_audio(audio_path)

            if progress_callback:
                progress_callback(0.1, "Transcribing with WhisperX...")

            transcribe_kwargs = {"batch_size": self._batch_size}
            if language:
                transcribe_kwargs["language"] = language
            if initial_prompt:
                # WhisperX wraps faster-whisper; initial_prompt is supported via asr_options
                transcribe_kwargs["asr_options"] = {"initial_prompt": initial_prompt}

            result = self._model.transcribe(audio, **transcribe_kwargs)
            detected_language = result.get("language", "unknown")

            if progress_callback:
                progress_callback(0.6, f"Aligning timestamps (language: {detected_language})...")

            # Attempt word-level alignment for better timestamps
            try:
                align_model, metadata = whisperx.load_align_model(
                    language_code=detected_language,
                    device=self._device,
                )
                aligned = whisperx.align(
                    result["segments"],
                    align_model,
                    metadata,
                    audio,
                    self._device,
                    return_char_alignments=False,
                )
                segments_data = aligned["segments"]
            except Exception as e:
                logger.warning(f"Alignment failed, using unaligned segments: {e}")
                segments_data = result["segments"]

            if progress_callback:
                progress_callback(0.95, "Processing segments...")

            segments = []
            total_duration = 0.0
            for seg in segments_data:
                start = seg.get("start", 0.0)
                end = seg.get("end", 0.0)
                text = seg.get("text", "").strip()
                if text:
                    segments.append(TranscriptionSegment(start=start, end=end, text=text))
                    total_duration = max(total_duration, end)

            if progress_callback:
                progress_callback(1.0, "WhisperX transcription complete")

            logger.info(f"WhisperX transcription complete: {len(segments)} segments")

            return TranscriptionResult(
                segments=segments,
                language=detected_language,
                duration=total_duration,
            )

        except Exception as e:
            logger.error(f"WhisperX transcription failed: {e}")
            raise RuntimeError(f"WhisperX transcription failed: {e}")

    @property
    def device_type(self) -> str:
        return self._device
