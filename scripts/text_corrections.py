"""Explicit, deterministic personal corrections for VoicePrompt."""

from __future__ import annotations

import json
import logging
import os
import re
from pathlib import Path

log = logging.getLogger(__name__)


def _corrections_path() -> Path:
    override = os.environ.get("VOICEPROMPT_DATA_DIR")
    if override:
        return Path(override) / "corrections.json"
    appdata = os.environ.get("APPDATA")
    root = Path(appdata) / "VoicePrompt" if appdata else Path.home() / ".voice-typing"
    return root / "corrections.json"


def apply_corrections(text: str) -> str:
    """Apply approved phrase replacements, longest phrase first."""
    try:
        payload = json.loads(_corrections_path().read_text(encoding="utf-8"))
        items = payload.get("items", []) if isinstance(payload, dict) else []
    except (OSError, json.JSONDecodeError):
        return text

    pairs: list[tuple[str, str]] = []
    for item in items[:100]:
        if not isinstance(item, dict):
            continue
        heard = str(item.get("heard", "")).strip()
        replacement = str(item.get("replacement", "")).strip()
        if heard and replacement:
            pairs.append((heard, replacement))

    if not pairs:
        return text
    try:
        pairs.sort(key=lambda pair: len(pair[0]), reverse=True)
        replacements = {heard.casefold(): replacement for heard, replacement in pairs}
        alternatives = "|".join(re.escape(heard) for heard, _ in pairs)
        pattern = rf"(?<!\w)(?:{alternatives})(?!\w)"
        return re.sub(
            pattern,
            lambda match: replacements[match.group(0).casefold()],
            text,
            flags=re.IGNORECASE,
        )
    except (re.error, TypeError):
        log.warning("Could not apply personal corrections", exc_info=True)
        return text
