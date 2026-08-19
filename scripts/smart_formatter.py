"""Fast deterministic formatting for raw dictation, with no model dependency."""

from __future__ import annotations

import os
import re

from .windows_context import DictationContext

_SPOKEN_MARKS = (
    (r"new paragraph|nov odstavek", "\n\n"),
    (r"new line|nova vrstica", "\n"),
    (r"question mark|vprašaj", "?"),
    (r"exclamation mark|klicaj", "!"),
    (r"semicolon|podpičje", ";"),
    (r"colon|dvopičje", ":"),
    (r"full stop|pika", "."),
    (r"comma|vejica", ","),
)
_FILLERS = r"um+|uh+|erm+|hmm+"


def smart_formatting_enabled(value: object | None = None) -> bool:
    if value is None:
        value = os.environ.get("VOICEPROMPT_SMART_FORMATTING", "1")
    if isinstance(value, bool):
        return value
    return isinstance(value, str) and value.strip().lower() in {"1", "true", "yes", "on"}


def _replace_spoken_marks(text: str) -> str:
    value = text
    for phrase, mark in _SPOKEN_MARKS:
        value = re.sub(rf"(?i)(?<![\w-])(?:{phrase})(?![\w-])", mark, value)
    return value


def _capitalize_sentences(text: str) -> str:
    chars = list(text)
    capitalize_next = True
    for index, character in enumerate(chars):
        if capitalize_next and character.isalpha():
            chars[index] = character.upper()
            capitalize_next = False
        elif character in ".!?\n":
            capitalize_next = True
        elif not character.isspace() and character not in "\"'([{“‘":
            capitalize_next = False
    return "".join(chars)


def _join_with_context(text: str, context: DictationContext) -> str:
    if not text or not context.before_text:
        return text
    previous = context.before_text[-1]
    first = text[0]
    if previous.isspace() or previous in "([{/-—\n" or first.isspace() or first in ".,!?;:)]}":
        return text
    return " " + text


def _join_before_following_text(text: str, context: DictationContext) -> str:
    if not text or not context.after_text:
        return text
    following = context.after_text[0]
    last = text[-1]
    if following.isspace() or following in ".,!?;:)]}" or last.isspace() or last in "([{/-—\n":
        return text
    return text + " "


def format_dictation(
    text: str,
    context: DictationContext | None = None,
    enabled: object | None = None,
) -> str:
    """Apply bounded transformations that never invent or translate content."""
    if not isinstance(text, str) or not text or not smart_formatting_enabled(enabled):
        return text
    context = context or DictationContext()
    leading = text[: len(text) - len(text.lstrip())]
    trailing = text[len(text.rstrip()) :]
    value = text.strip()
    if not value:
        return text

    value = _replace_spoken_marks(value)
    value = re.sub(rf"(?i)(^|(?<=[.!?])\s+)(?:{_FILLERS})[,\s]+", r"\1", value)
    value = re.sub(rf"(?i)\b({_FILLERS})(?:[,\s]+\1)+\b", r"\1", value)
    value = re.sub(r"[ \t]+([,.;:!?])", r"\1", value)
    value = re.sub(r"([,;:!?])(?=\S)", r"\1 ", value)
    value = re.sub(r"\.(?=[^\s.])", ". ", value)
    value = re.sub(r"[ \t]*\n[ \t]*", "\n", value)
    value = re.sub(r"[ \t]{2,}", " ", value).strip()

    if context.app_kind not in {"code", "terminal"}:
        value = _capitalize_sentences(value)
    if (
        context.app_kind in {"document", "email"}
        and len(value) >= 12
        and value[-1] not in ".!?;:)]}"
    ):
        value += "."

    value = _join_with_context(value, context)
    value = _join_before_following_text(value, context)
    return ("" if context.before_text else leading) + value + ("" if context.after_text else trailing)
