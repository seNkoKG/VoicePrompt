"""Safe, testable routing for VoicePrompt transcript delivery."""

from __future__ import annotations

import os
from collections.abc import Callable

PASTE = "paste"
CLIPBOARD = "clipboard"


def normalize_output_mode(value: object) -> str:
    """Return a known mode; hand-edited or missing values fail safe to paste."""
    if isinstance(value, str) and value.strip().lower() == CLIPBOARD:
        return CLIPBOARD
    return PASTE


def configured_output_mode() -> str:
    return normalize_output_mode(os.environ.get("VOICEPROMPT_OUTPUT_MODE"))


def deliver_text(
    text: str,
    copy_text: Callable[[str], None],
    paste_text: Callable[[str], None],
    mode: object | None = None,
) -> str:
    """Deliver exactly once and return the actual privacy-safe route name."""
    selected = configured_output_mode() if mode is None else normalize_output_mode(mode)
    if selected == CLIPBOARD:
        copy_text(text)
    else:
        paste_text(text)
    return selected
