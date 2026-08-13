"""Bounded, deterministic text snippets for VoicePrompt."""

from __future__ import annotations

import json
import os
import re
from dataclasses import dataclass
from pathlib import Path

_MAX_FILE_BYTES = 512 * 1024


@dataclass(frozen=True)
class TextSnippet:
    name: str
    content: str


def _snippets_path() -> Path:
    override = os.environ.get("VOICEPROMPT_DATA_DIR")
    if override:
        return Path(override) / "snippets.json"
    appdata = os.environ.get("APPDATA")
    root = Path(appdata) / "VoicePrompt" if appdata else Path.home() / ".voice-typing"
    return root / "snippets.json"


def _normalize_name(value: str) -> str:
    normalized = value.strip().casefold()
    normalized = re.sub(r"^[\s.,!?;:…\"'“”‘’]+|[\s.,!?;:…\"'“”‘’]+$", "", normalized)
    return re.sub(r"\s+", " ", normalized)


def load_snippets(path: str | Path | None = None) -> dict[str, TextSnippet]:
    """Load at most 50 valid snippets; malformed files fail closed."""
    try:
        snippet_path = Path(path or _snippets_path())
        if snippet_path.stat().st_size > _MAX_FILE_BYTES:
            return {}
        payload = json.loads(snippet_path.read_text(encoding="utf-8"))
        items = payload.get("items", []) if isinstance(payload, dict) else []
        if not isinstance(items, list):
            return {}
    except (OSError, UnicodeError, json.JSONDecodeError, TypeError):
        return {}

    snippets: dict[str, TextSnippet] = {}
    for item in items[:50]:
        if not isinstance(item, dict):
            continue
        name_value = item.get("name", "")
        content_value = item.get("content", "")
        if not isinstance(name_value, str) or not isinstance(content_value, str):
            continue
        name = name_value.strip()
        content = content_value.strip()
        normalized_name = _normalize_name(name)
        if normalized_name and len(name) <= 60 and content and len(content) <= 4_000:
            snippets.setdefault(normalized_name, TextSnippet(name, content))
    return snippets


def resolve_snippet(text: str, snippets: dict[str, TextSnippet]) -> TextSnippet | None:
    """Require an exact command prefix plus an exact saved snippet name."""
    if not isinstance(text, str) or not snippets:
        return None
    normalized = _normalize_name(text)
    for prefix in ("insert snippet ", "vstavi predlogo "):
        if normalized.startswith(prefix):
            return snippets.get(normalized[len(prefix) :])
    return None
