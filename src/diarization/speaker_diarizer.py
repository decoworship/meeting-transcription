"""Speaker diarization using pyannote-audio."""

import logging
import os
from dataclasses import dataclass
from typing import List, Optional, Callable

from dotenv import load_dotenv

# Load .env file from project root
load_dotenv()

from ..transcription.base import TranscriptionResult, TranscriptionSegment

logger = logging.getLogger(__name__)


@dataclass
class DiarizationSegment:
    """A segment with speaker identification."""
    start: float
    end: float
    speaker: str


DIARIZATION_MODELS = {
    "community-1": "pyannote/speaker-diarization-community-1",
    "3.1": "pyannote/speaker-diarization-3.1",
}


class SpeakerDiarizer:
    """Speaker diarization using pyannote-audio."""

    def __init__(self, hf_token: Optional[str] = None, model: str = "community-1"):
        """
        Initialize diarizer.

        Args:
            hf_token: HuggingFace token for accessing pyannote models.
                     Falls back to HF_TOKEN env var.
            model: Diarization model to use ('community-1' or '3.1').
        """
        self._pipeline = None
        self._model_id = DIARIZATION_MODELS.get(model, DIARIZATION_MODELS["community-1"])
        # Priority: provided token > env var
        self._hf_token = hf_token or os.environ.get("HF_TOKEN")

        if not self._hf_token:
            raise ValueError(
                "HuggingFace token required for speaker diarization. "
                "Set HF_TOKEN in .env file or provide token in the GUI."
            )

    def load_model(self, progress_callback: Optional[Callable[[float, str], None]] = None) -> None:
        """Load the pyannote diarization pipeline."""
        if progress_callback:
            progress_callback(0.0, "Loading speaker diarization model...")

        try:
            from pyannote.audio import Pipeline
            import torch

            logger.info(f"Loading pyannote diarization pipeline: {self._model_id}")

            # Use GPU if available
            device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

            # Load the pretrained pipeline
            self._pipeline = Pipeline.from_pretrained(
                self._model_id,
                token=self._hf_token
            )

            # Move to GPU if available
            self._pipeline.to(device)

            if progress_callback:
                progress_callback(1.0, "Diarization model loaded")

            logger.info(f"Diarization pipeline loaded on {device}")

        except Exception as e:
            logger.error(f"Failed to load diarization model: {e}")
            raise RuntimeError(
                f"Failed to load diarization model: {e}\n"
                "Make sure you have a valid HuggingFace token and have accepted "
                "the model terms at https://huggingface.co/pyannote/speaker-diarization-3.1"
            )

    @property
    def is_loaded(self) -> bool:
        return self._pipeline is not None

    def diarize(
        self,
        audio_path: str,
        progress_callback: Optional[Callable[[float, str], None]] = None
    ) -> List[DiarizationSegment]:
        """
        Perform speaker diarization on audio file.

        Args:
            audio_path: Path to audio file
            progress_callback: Progress callback

        Returns:
            List of DiarizationSegment with speaker labels
        """
        if not self.is_loaded:
            self.load_model(progress_callback)

        if progress_callback:
            progress_callback(0.0, "Analyzing speakers...")

        try:
            logger.info(f"Running diarization on: {audio_path}")
            diarization = self._pipeline(audio_path)

            # Convert to our format
            segments = []

            # In pyannote-audio 3.1+, the pipeline returns a DiarizeOutput object
            # The actual annotation is in the speaker_diarization attribute
            if hasattr(diarization, 'speaker_diarization'):
                annotation = diarization.speaker_diarization
            elif hasattr(diarization, 'itertracks'):
                # Fallback for older versions that return Annotation directly
                annotation = diarization
            else:
                raise RuntimeError(
                    f"Unexpected diarization result type: {type(diarization)}"
                )

            # Iterate over the annotation timeline
            for segment, track, speaker in annotation.itertracks(yield_label=True):
                segments.append(DiarizationSegment(
                    start=segment.start,
                    end=segment.end,
                    speaker=speaker
                ))

            if progress_callback:
                progress_callback(1.0, "Speaker analysis complete")

            # Create speaker mapping (SPEAKER_00 -> Speaker 1, etc.)
            speaker_map = self._create_speaker_map(segments)
            for seg in segments:
                seg.speaker = speaker_map.get(seg.speaker, seg.speaker)

            logger.info(f"Diarization complete: {len(segments)} segments, {len(speaker_map)} speakers")

            return segments

        except Exception as e:
            logger.error(f"Diarization failed: {e}")
            raise RuntimeError(f"Diarization failed: {e}")

    def _create_speaker_map(self, segments: List[DiarizationSegment]) -> dict:
        """Create human-readable speaker labels."""
        unique_speakers = sorted(set(seg.speaker for seg in segments))
        return {
            spk: f"Speaker {i+1}"
            for i, spk in enumerate(unique_speakers)
        }

    def assign_speakers(
        self,
        transcription: TranscriptionResult,
        diarization_segments: List[DiarizationSegment]
    ) -> TranscriptionResult:
        """
        Assign speaker labels to transcription segments.

        Uses overlap-based matching: each transcription segment is assigned
        the speaker who speaks the most during that segment.

        Args:
            transcription: TranscriptionResult to annotate
            diarization_segments: Speaker diarization results

        Returns:
            TranscriptionResult with speaker labels filled in
        """
        for trans_seg in transcription.segments:
            # Find overlapping diarization segments
            overlaps = []
            for diar_seg in diarization_segments:
                overlap_start = max(trans_seg.start, diar_seg.start)
                overlap_end = min(trans_seg.end, diar_seg.end)
                overlap_duration = max(0, overlap_end - overlap_start)

                if overlap_duration > 0:
                    overlaps.append((diar_seg.speaker, overlap_duration))

            # Assign speaker with most overlap
            if overlaps:
                # Group by speaker and sum overlaps
                speaker_overlaps = {}
                for speaker, duration in overlaps:
                    speaker_overlaps[speaker] = speaker_overlaps.get(speaker, 0) + duration

                # Get speaker with maximum overlap
                trans_seg.speaker = max(speaker_overlaps.keys(), key=lambda s: speaker_overlaps[s])
            else:
                trans_seg.speaker = "Unknown"

        return transcription


def merge_transcription_with_diarization(
    transcription: TranscriptionResult,
    audio_path: str,
    hf_token: Optional[str] = None,
    num_speakers: Optional[int] = None,
    progress_callback: Optional[Callable[[float, str], None]] = None
) -> TranscriptionResult:
    """
    Convenience function to add speaker labels to transcription.

    Args:
        transcription: Transcription result to annotate
        audio_path: Path to audio file for diarization
        hf_token: HuggingFace token
        num_speakers: Optional speaker count hint
        progress_callback: Progress callback

    Returns:
        TranscriptionResult with speaker labels
    """
    diarizer = SpeakerDiarizer(hf_token=hf_token)
    diarization_segments = diarizer.diarize(
        audio_path,
        num_speakers=num_speakers,
        progress_callback=progress_callback
    )
    return diarizer.assign_speakers(transcription, diarization_segments)
