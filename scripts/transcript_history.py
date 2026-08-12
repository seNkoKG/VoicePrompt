"""Bounded local transcript recovery for VoicePrompt."""

from __future__ import annotations

import json
import logging
import os
import threading
import uuid
from datetime import datetime, timezone
from pathlib import Path

log = logging.getLogger(__name__)
_lock = threading.Lock()


def _data_dir() -> Path:
    override = os.environ.get("VOICEPROMPT_DATA_DIR")
    if override:
        return Path(override)
    appdata = os.environ.get("APPDATA")
    return Path(appdata) / "VoicePrompt" if appdata else Path.home() / ".voice-typing"


def _settings() -> tuple[bool, int]:
    path = _data_dir() / "history-settings.json"
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        enabled = bool(data.get("enabled", True))
        limit = max(5, min(100, int(data.get("limit", 20))))
        return enabled, limit
    except (OSError, ValueError, TypeError, json.JSONDecodeError):
        return True, 20


def remember_transcript(original_text: str, output_text: str) -> None:
    """Save final output locally before paste. Failures never block dictation."""
    enabled, limit = _settings()
    text = output_text.rstrip()
    if not enabled or not text:
        return

    original = original_text.rstrip()
    item = {
        "id": uuid.uuid4().hex,
        "createdAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "text": text,
        "originalText": original if original != text else "",
    }
    path = _data_dir() / "history.json"
    try:
        with _lock:
            items: list[dict[str, object]] = []
            if path.exists():
                try:
                    payload = json.loads(path.read_text(encoding="utf-8"))
                    if isinstance(payload, dict) and isinstance(payload.get("items"), list):
                        items = [entry for entry in payload["items"] if isinstance(entry, dict)]
                except (OSError, json.JSONDecodeError):
                    items = []

            path.parent.mkdir(parents=True, exist_ok=True)
            temp = path.with_name(f"{path.name}.{uuid.uuid4().hex}.tmp")
            temp.write_text(
                json.dumps({"version": 1, "items": [item, *items][:limit]}, ensure_ascii=False),
                encoding="utf-8",
            )
            os.replace(temp, path)
    except Exception:
        log.warning("Could not update local transcript recovery", exc_info=True)
