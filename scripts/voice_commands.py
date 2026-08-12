"""Exact, opt-in spoken commands for VoicePrompt."""

from __future__ import annotations

import os
import re
from collections.abc import Callable
from dataclasses import dataclass


@dataclass(frozen=True)
class VoiceCommand:
    name: str
    text: str | None


_COMMANDS = {
    "new line": VoiceCommand("new-line", "\n"),
    "nova vrstica": VoiceCommand("new-line", "\n"),
    "new paragraph": VoiceCommand("new-paragraph", "\n\n"),
    "nov odstavek": VoiceCommand("new-paragraph", "\n\n"),
    "bullet point": VoiceCommand("bullet-point", "• "),
    "alineja": VoiceCommand("bullet-point", "• "),
    "undo": VoiceCommand("undo", None),
    "razveljavi": VoiceCommand("undo", None),
    "cancel": VoiceCommand("cancel", None),
    "preklici": VoiceCommand("cancel", None),
    "prekliči": VoiceCommand("cancel", None),
}


def commands_enabled(value: object | None = None) -> bool:
    if value is None:
        value = os.environ.get("VOICEPROMPT_VOICE_COMMANDS", "")
    if isinstance(value, bool):
        return value
    return isinstance(value, str) and value.strip().lower() in {"1", "true", "yes", "on"}


def _normalize(text: str) -> str:
    # Whisper commonly adds terminal punctuation. Strip punctuation only at the
    # utterance edges, collapse whitespace, and require an exact full match.
    normalized = text.strip().casefold()
    normalized = re.sub(r"^[\s.,!?;:…\"'“”‘’]+|[\s.,!?;:…\"'“”‘’]+$", "", normalized)
    return re.sub(r"\s+", " ", normalized)


def resolve_voice_command(text: str, enabled: object | None = None) -> VoiceCommand | None:
    if not commands_enabled(enabled) or not isinstance(text, str):
        return None
    return _COMMANDS.get(_normalize(text))


def execute_voice_command(
    command: VoiceCommand,
    deliver: Callable[[str], str],
    undo: Callable[[], None],
) -> str:
    """Execute a resolved command through injected, easily tested effects."""
    if command.name == "undo":
        undo()
        return "command"
    if command.text is None:
        return "cancelled"
    return deliver(command.text)
