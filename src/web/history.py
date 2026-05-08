"""Persistence for transcription history.

Stores each completed transcription as a JSON file in
``~/.meeting-transcription/history/``. Newest entries are returned first; the
on-disk store is capped at ``MAX_ENTRIES`` to keep things bounded.
"""

import json
import logging
import time
from dataclasses import asdict
from pathlib import Path
from typing import Optional

from ..transcription.base import TranscriptionResult, TranscriptionSegment

logger = logging.getLogger(__name__)

HISTORY_DIR = Path.home() / ".meeting-transcription" / "history"
MAX_ENTRIES = 50


def _ensure_dir() -> None:
    HISTORY_DIR.mkdir(parents=True, exist_ok=True)


def save_entry(
    result: TranscriptionResult,
    speaker_names: dict[str, str],
    client: str,
    project: str,
    meeting_date: str,
    audio_path: Optional[str] = None,
    source_file: Optional[str] = None,
) -> str:
    """Persist a transcription to history. Returns the saved entry id."""
    _ensure_dir()

    entry_id = str(int(time.time() * 1000))
    payload = {
        "id": entry_id,
        "saved_at": time.time(),
        "client": client or "",
        "project": project or "",
        "date": meeting_date or "",
        "language": result.language,
        "duration": result.duration,
        "audio_path": audio_path,
        "source_file": source_file,
        "speaker_names": speaker_names or {},
        "segments": [asdict(s) for s in result.segments],
    }

    path = HISTORY_DIR / f"{entry_id}.json"
    try:
        with open(path, "w", encoding="utf-8") as f:
            json.dump(payload, f, ensure_ascii=False, indent=2)
    except Exception as e:
        logger.warning(f"Failed to save history entry: {e}")
        return ""

    _prune_old()
    return entry_id


def _prune_old() -> None:
    """Remove oldest entries beyond MAX_ENTRIES."""
    try:
        entries = sorted(HISTORY_DIR.glob("*.json"), key=lambda p: p.stat().st_mtime)
        excess = len(entries) - MAX_ENTRIES
        for old in entries[: max(excess, 0)]:
            old.unlink(missing_ok=True)
    except Exception as e:
        logger.warning(f"Failed to prune history: {e}")


def list_entries() -> list[dict]:
    """Return history entries as lightweight dicts (no segments). Newest first."""
    _ensure_dir()
    entries: list[dict] = []
    for path in HISTORY_DIR.glob("*.json"):
        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            entries.append({
                "id": data.get("id", path.stem),
                "saved_at": data.get("saved_at", path.stat().st_mtime),
                "client": data.get("client", ""),
                "project": data.get("project", ""),
                "date": data.get("date", ""),
                "language": data.get("language"),
                "duration": data.get("duration"),
                "n_segments": len(data.get("segments", [])),
                "n_speakers": len({s.get("speaker") for s in data.get("segments", []) if s.get("speaker")}),
            })
        except Exception as e:
            logger.warning(f"Failed to read history {path.name}: {e}")
    entries.sort(key=lambda e: e["saved_at"], reverse=True)
    return entries


def load_entry(entry_id: str) -> Optional[dict]:
    """Load a full history entry by id, returning the raw payload (or None)."""
    path = HISTORY_DIR / f"{entry_id}.json"
    if not path.exists():
        return None
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception as e:
        logger.warning(f"Failed to load history entry {entry_id}: {e}")
        return None


def entry_to_result(payload: dict) -> TranscriptionResult:
    """Reconstruct a TranscriptionResult from a history payload."""
    segments = [
        TranscriptionSegment(
            start=float(s.get("start", 0.0)),
            end=float(s.get("end", 0.0)),
            text=str(s.get("text", "")),
            speaker=s.get("speaker"),
        )
        for s in payload.get("segments", [])
    ]
    return TranscriptionResult(
        segments=segments,
        language=payload.get("language"),
        duration=payload.get("duration"),
    )


def delete_entry(entry_id: str) -> bool:
    """Remove an entry from history. Returns True on success."""
    path = HISTORY_DIR / f"{entry_id}.json"
    try:
        if path.exists():
            path.unlink()
            return True
    except Exception as e:
        logger.warning(f"Failed to delete history entry {entry_id}: {e}")
    return False
