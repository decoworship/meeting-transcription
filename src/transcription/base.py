"""Abstract base class for transcription engines."""

import gc
from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import List, Optional, Callable


@dataclass
class TranscriptionSegment:
    """A single segment of transcribed text with timing."""
    start: float  # Start time in seconds
    end: float    # End time in seconds
    text: str     # Transcribed text
    speaker: Optional[str] = None  # Speaker label (filled by diarization)

    def to_dict(self) -> dict:
        return {
            "start": self.start,
            "end": self.end,
            "text": self.text,
            "speaker": self.speaker
        }


@dataclass
class TranscriptionResult:
    """Complete transcription result."""
    segments: List[TranscriptionSegment]
    language: Optional[str] = None
    duration: Optional[float] = None

    @property
    def full_text(self) -> str:
        """Get the complete transcribed text without timestamps."""
        return " ".join(seg.text.strip() for seg in self.segments)

    def to_formatted_text(self, include_speakers: bool = True) -> str:
        """Format transcription with timestamps and optional speaker labels."""
        lines = []
        for seg in self.segments:
            timestamp = f"[{self._format_time(seg.start)}]"
            speaker = f" {seg.speaker}:" if include_speakers and seg.speaker else ""
            lines.append(f"{timestamp}{speaker} {seg.text.strip()}")
        return "\n".join(lines)

    @staticmethod
    def _format_time(seconds: float) -> str:
        """Format seconds to HH:MM:SS."""
        hours = int(seconds // 3600)
        minutes = int((seconds % 3600) // 60)
        secs = int(seconds % 60)
        if hours > 0:
            return f"{hours:02d}:{minutes:02d}:{secs:02d}"
        return f"{minutes:02d}:{secs:02d}"


# Available model sizes
MODEL_SIZES = ["tiny", "base", "small", "medium", "large", "large-v2", "large-v3"]


class BaseTranscriber(ABC):
    """Abstract base class for transcription engines."""

    def __init__(self, model_size: str = "base"):
        if model_size not in MODEL_SIZES:
            raise ValueError(f"Invalid model size: {model_size}. Choose from: {MODEL_SIZES}")
        self.model_size = model_size
        self._model = None

    @abstractmethod
    def load_model(self, progress_callback: Optional[Callable[[float, str], None]] = None) -> None:
        """Load the transcription model."""
        pass

    @abstractmethod
    def transcribe(
        self,
        audio_path: str,
        language: Optional[str] = None,
        condition_on_previous_text: bool = False,
        initial_prompt: Optional[str] = None,
        progress_callback: Optional[Callable[[float, str], None]] = None
    ) -> TranscriptionResult:
        """
        Transcribe audio file.

        Args:
            audio_path: Path to audio file (WAV format recommended)
            language: Optional language code (e.g., 'en', 'fr'). Auto-detect if None.
            initial_prompt: Optional context/vocabulary to bias the model (names,
                jargon, technical terms). Improves recognition of domain-specific words.
            progress_callback: Optional callback(progress: float, status: str)

        Returns:
            TranscriptionResult with timestamped segments
        """
        pass

    @property
    @abstractmethod
    def device_type(self) -> str:
        """Return the device type being used (cpu/cuda)."""
        pass

    @property
    def is_loaded(self) -> bool:
        """Check if model is loaded."""
        return self._model is not None

    def unload_model(self) -> None:
        """Unload the model and actually release its VRAM.

        Dropping the reference is not enough: CTranslate2 frees device memory only
        when the object is collected, and torch keeps freed blocks in its caching
        allocator. Without the explicit collect + empty_cache the weights stay
        resident, and the next pipeline stage has to squeeze in beside them.
        """
        self._model = None
        gc.collect()
        try:
            import torch
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
        except ImportError:
            pass
