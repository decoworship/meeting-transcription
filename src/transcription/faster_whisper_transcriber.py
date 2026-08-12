"""GPU-accelerated transcriber using faster-whisper (CTranslate2)."""

import logging
from typing import Optional, Callable

from .base import BaseTranscriber, TranscriptionResult, TranscriptionSegment
from ..utils.gpu_detector import is_cuda_available, get_optimal_compute_type

logger = logging.getLogger(__name__)


class FasterWhisperTranscriber(BaseTranscriber):
    """Transcriber using faster-whisper for GPU acceleration."""

    def __init__(self, model_size: str = "base", device: str = "auto"):
        super().__init__(model_size)

        # Determine device
        if device == "auto":
            self._device = "cuda" if is_cuda_available() else "cpu"
        else:
            self._device = device

        # Get optimal compute type for the device
        self._compute_type = get_optimal_compute_type() if self._device == "cuda" else "int8"

        logger.info(f"FasterWhisper initialized: device={self._device}, compute_type={self._compute_type}")

    def load_model(self, progress_callback: Optional[Callable[[float, str], None]] = None) -> None:
        """Load the faster-whisper model."""
        if progress_callback:
            progress_callback(0.0, f"Loading faster-whisper {self.model_size} model...")

        try:
            from faster_whisper import WhisperModel

            logger.info(
                f"Loading faster-whisper model: {self.model_size} "
                f"on {self._device} with {self._compute_type}"
            )

            self._model = WhisperModel(
                self.model_size,
                device=self._device,
                compute_type=self._compute_type
            )

            if progress_callback:
                progress_callback(1.0, "Model loaded successfully")

            logger.info("faster-whisper model loaded successfully")

        except Exception as e:
            logger.error(f"Failed to load faster-whisper model: {e}")
            raise RuntimeError(f"Failed to load faster-whisper model: {e}")

    def transcribe(
        self,
        audio_path: str,
        language: Optional[str] = None,
        condition_on_previous_text: bool = False,
        initial_prompt: Optional[str] = None,
        progress_callback: Optional[Callable[[float, str], None]] = None
    ) -> TranscriptionResult:
        """
        Transcribe audio using faster-whisper.

        Args:
            audio_path: Path to audio file
            language: Optional language code (e.g. 'pt', 'en'). Auto-detects if None.
            condition_on_previous_text: If True, use previous output as context (risks
                hallucination cascades). False recommended for meetings.
            progress_callback: Progress callback

        Returns:
            TranscriptionResult with segments
        """
        if not self.is_loaded:
            self.load_model(progress_callback)

        if progress_callback:
            progress_callback(0.0, "Starting transcription...")

        try:
            logger.info(
                f"Transcribing: {audio_path} | language={language or 'auto'} "
                f"condition_on_previous_text={condition_on_previous_text}"
            )

            # Run transcription
            transcribe_kwargs = dict(
                language=language,
                beam_size=5,
                condition_on_previous_text=condition_on_previous_text,
                # Word-level timestamps split long utterances into short segments.
                # Essential for diarization: assign_speakers() matches by temporal
                # overlap, so a segment spanning several speaker turns is guaranteed
                # to be misattributed.
                word_timestamps=True,
                hallucination_silence_threshold=2.0,
                vad_filter=True,
                vad_parameters=dict(
                    min_silence_duration_ms=500,
                    # Caps runaway segments; without it a dense stretch of speech
                    # can collapse into a single block and lose content.
                    max_speech_duration_s=25,
                    threshold=0.35,
                ),
            )
            if initial_prompt:
                # Passed as `hotwords`, not `initial_prompt`. faster-whisper resets
                # the prompt after every 30s window when condition_on_previous_text
                # is False, so initial_prompt only ever biases the first window --
                # and it is truncated to the last 223 tokens, silently dropping
                # whatever comes first (typically the speaker names). `hotwords` is
                # re-injected into every window instead.
                transcribe_kwargs["hotwords"] = initial_prompt

            segments_gen, info = self._model.transcribe(audio_path, **transcribe_kwargs)

            # Convert generator to list with progress updates
            segments = []
            duration = info.duration if info.duration else 0

            for seg in segments_gen:
                segments.append(TranscriptionSegment(
                    start=seg.start,
                    end=seg.end,
                    text=seg.text
                ))

                # Update progress based on segment end time
                if progress_callback and duration > 0:
                    progress = min(seg.end / duration, 0.99)
                    progress_callback(progress, f"Transcribing: {progress*100:.1f}%")

            if progress_callback:
                progress_callback(1.0, "Transcription complete")

            logger.info(f"Transcription complete: {len(segments)} segments")

            return TranscriptionResult(
                segments=segments,
                language=info.language,
                duration=duration
            )

        except Exception as e:
            logger.error(f"Transcription failed: {e}")
            raise RuntimeError(f"Transcription failed: {e}")

    @property
    def device_type(self) -> str:
        return self._device
