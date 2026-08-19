"""Explicit spoken command-mode routing for transforming selected text."""

from __future__ import annotations

import re

from .voice_commands import commands_enabled

_PREFIX = re.compile(r"(?is)^\s*(?:command|ukaz)\s+(.+?)\s*[.!?]*\s*$")


def resolve_selection_command(text: str, enabled: object | None = None) -> str | None:
    """Return the instruction only for an explicit full-utterance command prefix."""
    if not commands_enabled(enabled) or not isinstance(text, str):
        return None
    match = _PREFIX.fullmatch(text)
    if match is None:
        return None
    instruction = re.sub(r"\s+", " ", match.group(1)).strip()
    return instruction if 1 <= len(instruction) <= 240 else None
