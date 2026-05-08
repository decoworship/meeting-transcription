"""Voice fingerprinting / speaker recognition across recordings.

Stores per-person speaker embeddings in
``~/.meeting-transcription/voices/voices.json``. When a user confirms a speaker
name via Apply Names, the corresponding voice embedding is appended to that
person's profile. On future transcriptions, detected speakers are matched
against saved profiles using cosine similarity.

Embedding extraction uses pyannote's pretrained model. The model is loaded
lazily on first use and cached in memory.
"""

from __future__ import annotations

import json
import logging
import os
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

import numpy as np

from ..transcription.base import TranscriptionSegment

logger = logging.getLogger(__name__)

VOICES_DIR = Path.home() / ".meeting-transcription" / "voices"
VOICES_PATH = VOICES_DIR / "voices.json"

# Tunable: cosine similarity above this counts as a match.
DEFAULT_MATCH_THRESHOLD = 0.65

# Pyannote embedding model (newer than pyannote/embedding, recommended in 4.x).
# Requires accepting terms at https://huggingface.co/pyannote/wespeaker-voxceleb-resnet34-LM
EMBEDDING_MODEL_ID = "pyannote/wespeaker-voxceleb-resnet34-LM"

# Minimum total speaking time (seconds) needed to extract a usable embedding.
MIN_SPEECH_SECONDS = 1.5

# In-memory cache for the model — lazy-loaded once per process.
_inference_cache: dict[str, object] = {}


# ── Data model ──────────────────────────────────────────────────────


@dataclass
class VoiceProfile:
    """All saved embeddings for a single named person."""
    name: str
    embeddings: list[list[float]] = field(default_factory=list)
    created_at: float = 0.0
    updated_at: float = 0.0

    def to_dict(self) -> dict:
        return {
            "embeddings": self.embeddings,
            "created_at": self.created_at,
            "updated_at": self.updated_at,
        }

    @classmethod
    def from_dict(cls, name: str, data: dict) -> "VoiceProfile":
        return cls(
            name=name,
            embeddings=data.get("embeddings", []),
            created_at=data.get("created_at", 0.0),
            updated_at=data.get("updated_at", 0.0),
        )

    def vectors(self) -> np.ndarray:
        """Return embeddings as a (N, D) numpy array."""
        if not self.embeddings:
            return np.zeros((0, 0), dtype=np.float32)
        return np.asarray(self.embeddings, dtype=np.float32)


# ── Persistence ─────────────────────────────────────────────────────


def _ensure_dir() -> None:
    VOICES_DIR.mkdir(parents=True, exist_ok=True)


def load_profiles() -> dict[str, VoiceProfile]:
    if not VOICES_PATH.exists():
        return {}
    try:
        with open(VOICES_PATH, "r", encoding="utf-8") as f:
            raw = json.load(f)
        return {name: VoiceProfile.from_dict(name, data) for name, data in raw.items()}
    except Exception as e:
        logger.warning(f"Failed to load voice profiles: {e}")
        return {}


def save_profiles(profiles: dict[str, VoiceProfile]) -> None:
    _ensure_dir()
    raw = {name: p.to_dict() for name, p in profiles.items()}
    try:
        with open(VOICES_PATH, "w", encoding="utf-8") as f:
            json.dump(raw, f, ensure_ascii=False, indent=2)
    except Exception as e:
        logger.warning(f"Failed to save voice profiles: {e}")


def list_profiles() -> list[dict]:
    """Return summary rows for the UI."""
    profiles = load_profiles()
    rows = []
    for name in sorted(profiles.keys()):
        p = profiles[name]
        rows.append({
            "name": name,
            "samples": len(p.embeddings),
            "created_at": p.created_at,
            "updated_at": p.updated_at,
        })
    return rows


def delete_profile(name: str) -> bool:
    profiles = load_profiles()
    if name not in profiles:
        return False
    del profiles[name]
    save_profiles(profiles)
    return True


def add_embedding(name: str, embedding: np.ndarray) -> None:
    """Append a new embedding under `name`, creating the profile if needed."""
    if embedding is None or embedding.size == 0:
        return
    profiles = load_profiles()
    now = time.time()
    if name not in profiles:
        profiles[name] = VoiceProfile(name=name, created_at=now, updated_at=now)
    profile = profiles[name]
    profile.embeddings.append(embedding.astype(np.float32).tolist())
    profile.updated_at = now
    # Cap stored embeddings per person to avoid runaway growth
    if len(profile.embeddings) > 25:
        profile.embeddings = profile.embeddings[-25:]
    save_profiles(profiles)


# ── Embedding extraction ────────────────────────────────────────────


def _get_inference(hf_token: Optional[str]):
    """Load the pyannote embedding model lazily and cache it."""
    if "inf" in _inference_cache:
        return _inference_cache["inf"]

    try:
        import torch
        from pyannote.audio import Model, Inference
    except ImportError as e:
        raise RuntimeError(f"pyannote.audio not available: {e}")

    token = hf_token or os.environ.get("HF_TOKEN")
    if not token:
        raise RuntimeError(
            "HuggingFace token required for voice fingerprinting. "
            "Set HF_TOKEN or provide it in the UI."
        )

    model = Model.from_pretrained(EMBEDDING_MODEL_ID, token=token)
    if torch.cuda.is_available():
        model = model.to(torch.device("cuda"))
    inference = Inference(model, window="whole")
    _inference_cache["inf"] = inference
    return inference


def extract_speaker_embedding(
    audio_path: str,
    segments: list[TranscriptionSegment],
    hf_token: Optional[str] = None,
) -> Optional[np.ndarray]:
    """Extract a single embedding vector for a speaker from their segments.

    Picks the longest segment for best signal quality. Returns None if there's
    not enough audio or extraction fails.
    """
    if not segments or not audio_path or not os.path.exists(audio_path):
        return None

    # Pick the longest segment (best chance of clean speech)
    longest = max(segments, key=lambda s: s.end - s.start)
    duration = longest.end - longest.start
    if duration < MIN_SPEECH_SECONDS:
        # Try concatenating all segments if individual ones are too short
        total = sum(s.end - s.start for s in segments)
        if total < MIN_SPEECH_SECONDS:
            logger.info(f"Skipping embedding: only {total:.1f}s of speech available")
            return None

    try:
        from pyannote.core import Segment

        inference = _get_inference(hf_token)
        # Crop the audio to the longest segment and embed it as a whole window
        emb = inference.crop(audio_path, Segment(longest.start, longest.end))
        if emb is None:
            return None
        # pyannote returns a 1-D ndarray (or compatible). Force flat float32.
        arr = np.asarray(emb, dtype=np.float32).flatten()
        if arr.size == 0:
            return None
        return arr
    except Exception as e:
        logger.warning(f"Embedding extraction failed: {e}")
        return None


# ── Matching ────────────────────────────────────────────────────────


def _cosine_similarity(a: np.ndarray, b: np.ndarray) -> float:
    if a.size == 0 or b.size == 0:
        return 0.0
    na = float(np.linalg.norm(a))
    nb = float(np.linalg.norm(b))
    if na == 0 or nb == 0:
        return 0.0
    return float(np.dot(a, b) / (na * nb))


def best_match(
    embedding: np.ndarray,
    profiles: Optional[dict[str, VoiceProfile]] = None,
    threshold: float = DEFAULT_MATCH_THRESHOLD,
) -> Optional[tuple[str, float]]:
    """Return (name, similarity) of the best matching saved profile, or None.

    Uses the maximum cosine similarity to any embedding within each profile.
    Returns None if the best similarity is below the threshold or there are
    no saved profiles.
    """
    if embedding is None or embedding.size == 0:
        return None
    if profiles is None:
        profiles = load_profiles()
    if not profiles:
        return None

    best_name: Optional[str] = None
    best_sim = -1.0
    for name, profile in profiles.items():
        vectors = profile.vectors()
        if vectors.size == 0:
            continue
        # Compare against each stored embedding, take the best
        sims = [_cosine_similarity(embedding, v) for v in vectors]
        sim = max(sims) if sims else 0.0
        if sim > best_sim:
            best_sim = sim
            best_name = name

    if best_name is None or best_sim < threshold:
        return None
    return best_name, best_sim


def match_speakers(
    audio_path: str,
    speaker_to_segments: dict[str, list[TranscriptionSegment]],
    hf_token: Optional[str] = None,
    threshold: float = DEFAULT_MATCH_THRESHOLD,
) -> dict[str, tuple[str, float]]:
    """For each speaker in the dict, return its best match (or skip if no match).

    Returns: {speaker_label: (matched_name, similarity)}. Speakers without a
    confident match are absent from the result.
    """
    profiles = load_profiles()
    if not profiles:
        return {}

    matches: dict[str, tuple[str, float]] = {}
    for label, segments in speaker_to_segments.items():
        emb = extract_speaker_embedding(audio_path, segments, hf_token=hf_token)
        if emb is None:
            continue
        match = best_match(emb, profiles=profiles, threshold=threshold)
        if match is not None:
            matches[label] = match
    return matches


def learn_speakers(
    audio_path: str,
    speaker_to_name: dict[str, str],
    speaker_to_segments: dict[str, list[TranscriptionSegment]],
    hf_token: Optional[str] = None,
) -> int:
    """Save embeddings for each (label → user-confirmed name) pair.

    Skips entries where the name still looks like an auto-generated label
    ("Speaker 1", "Speaker N", etc.) since those aren't useful identifiers.
    Returns the number of profiles updated.
    """
    saved = 0
    import re as _re
    auto_pattern = _re.compile(r"^Speaker\s*\d+$", _re.IGNORECASE)
    for label, name in speaker_to_name.items():
        clean_name = (name or "").strip()
        if not clean_name or auto_pattern.match(clean_name):
            continue
        segments = speaker_to_segments.get(label) or []
        emb = extract_speaker_embedding(audio_path, segments, hf_token=hf_token)
        if emb is None:
            continue
        add_embedding(clean_name, emb)
        saved += 1
    return saved
