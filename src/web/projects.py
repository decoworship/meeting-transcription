"""Persistence for clients/projects with per-project transcription settings.

Stores a single JSON at ``~/.meeting-transcription/projects.json`` with the
shape::

    {
      "clients": {
        "<Client name>": {
          "projects": {
            "<Project name>": { "language": ..., "initial_prompt": ..., ... }
          }
        }
      }
    }

The settings dict mirrors the keys used in the main app config so callers can
unpack it directly into UI components.
"""

import json
import logging
import os
import tempfile
from pathlib import Path
from typing import Optional

logger = logging.getLogger(__name__)

PROJECTS_PATH = Path.home() / ".meeting-transcription" / "projects.json"

# Settings keys that are saved per project (everything except hf_token, which
# is account-wide and stays in the global config).
SETTINGS_KEYS = (
    "language",
    "model_size",
    "engine",
    "diarization",
    "condition_on_previous_text",
    "diar_model",
    "initial_prompt",
)


def _empty() -> dict:
    return {"clients": {}}


def load_data() -> dict:
    """Return the full projects.json structure (creates an empty one if missing)."""
    if not PROJECTS_PATH.exists():
        return _empty()
    try:
        with open(PROJECTS_PATH, "r", encoding="utf-8") as f:
            data = json.load(f)
        if not isinstance(data, dict) or "clients" not in data:
            return _empty()
        return data
    except Exception as e:
        logger.warning(f"Failed to load projects.json: {e}")
        return _empty()


def save_data(data: dict) -> None:
    """Write projects.json atomically."""
    try:
        PROJECTS_PATH.parent.mkdir(parents=True, exist_ok=True)
        # Atomic write: temp file in same dir, then rename
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", dir=str(PROJECTS_PATH.parent),
            prefix=".projects.", suffix=".tmp", delete=False,
        ) as tmp:
            json.dump(data, tmp, ensure_ascii=False, indent=2)
            tmp_path = tmp.name
        os.replace(tmp_path, PROJECTS_PATH)
    except Exception as e:
        logger.warning(f"Failed to save projects.json: {e}")


def list_clients() -> list[str]:
    data = load_data()
    return sorted(data.get("clients", {}).keys())


def list_projects(client: str) -> list[str]:
    if not client:
        return []
    data = load_data()
    client_data = data.get("clients", {}).get(client) or {}
    return sorted((client_data.get("projects") or {}).keys())


def get_settings(client: str, project: str) -> Optional[dict]:
    """Return settings dict for (client, project), or None if not found."""
    if not client or not project:
        return None
    data = load_data()
    project_data = (
        data.get("clients", {})
        .get(client, {})
        .get("projects", {})
        .get(project)
    )
    if project_data is None:
        return None
    return {k: project_data.get(k) for k in SETTINGS_KEYS}


def save_settings(client: str, project: str, settings: dict) -> None:
    """Persist settings for (client, project), creating both if they don't exist."""
    if not client or not project:
        return
    data = load_data()
    clients = data.setdefault("clients", {})
    client_data = clients.setdefault(client, {})
    projects_map = client_data.setdefault("projects", {})
    # Keep only the recognised keys
    projects_map[project] = {k: settings.get(k) for k in SETTINGS_KEYS}
    save_data(data)


def delete_client(client: str) -> bool:
    if not client:
        return False
    data = load_data()
    clients = data.get("clients", {})
    if client not in clients:
        return False
    del clients[client]
    save_data(data)
    return True


def delete_project(client: str, project: str) -> bool:
    if not client or not project:
        return False
    data = load_data()
    client_data = data.get("clients", {}).get(client)
    if not client_data:
        return False
    projects_map = client_data.get("projects", {})
    if project not in projects_map:
        return False
    del projects_map[project]
    save_data(data)
    return True
