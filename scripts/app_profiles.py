"""Exact, local per-application overrides for VoicePrompt text delivery."""

from __future__ import annotations

import ctypes
import json
import os
import sys
from dataclasses import dataclass
from pathlib import Path

_WRITING_MODES = {"inherit", "off", "clean", "grammar", "prompt"}
_OUTPUT_MODES = {"inherit", "paste", "clipboard"}
_MAX_FILE_BYTES = 128 * 1024


@dataclass(frozen=True)
class AppProfile:
    executable: str
    writing_mode: str
    output_mode: str

    @property
    def writing_override(self) -> str | None:
        return None if self.writing_mode == "inherit" else self.writing_mode

    @property
    def output_override(self) -> str | None:
        return None if self.output_mode == "inherit" else self.output_mode


def _default_path() -> Path:
    override = os.environ.get("VOICEPROMPT_DATA_DIR")
    if override:
        return Path(override) / "app-profiles.json"
    appdata = os.environ.get("APPDATA")
    return (Path(appdata) / "VoicePrompt" if appdata else Path.home() / ".voice-typing") / "app-profiles.json"


def _valid_executable(value: str) -> bool:
    return (
        0 < len(value) <= 120
        and value.casefold().endswith(".exe")
        and value not in {".", ".."}
        and not any(character in value for character in '\\/:*?"<>|')
        and not any(ord(character) < 32 for character in value)
    )


def load_app_profiles(path: str | Path | None = None) -> dict[str, AppProfile]:
    """Load once at startup. Any malformed file fails closed to no overrides."""
    profile_path = Path(path) if path is not None else _default_path()
    try:
        if profile_path.stat().st_size > _MAX_FILE_BYTES:
            return {}
        payload = json.loads(profile_path.read_text(encoding="utf-8"))
        if not isinstance(payload, dict) or payload.get("version") != 1:
            return {}
        items = payload.get("items")
        if not isinstance(items, list) or len(items) > 50:
            return {}

        profiles: dict[str, AppProfile] = {}
        for item in items:
            if not isinstance(item, dict):
                return {}
            executable = item.get("executable")
            writing_mode = item.get("writingMode")
            output_mode = item.get("outputMode")
            if not all(isinstance(value, str) for value in (executable, writing_mode, output_mode)):
                return {}
            executable = executable.strip()
            writing_mode = writing_mode.strip().lower()
            output_mode = output_mode.strip().lower()
            key = executable.casefold()
            if (
                not _valid_executable(executable)
                or writing_mode not in _WRITING_MODES
                or output_mode not in _OUTPUT_MODES
                or key in profiles
            ):
                return {}
            profiles[key] = AppProfile(executable, writing_mode, output_mode)
        return profiles
    except (OSError, ValueError, TypeError, json.JSONDecodeError):
        return {}


def foreground_executable() -> str:
    """Return only the focused process filename. Fail closed without a path."""
    if sys.platform != "win32":
        return ""
    try:
        from ctypes import wintypes

        user32 = ctypes.windll.user32
        kernel32 = ctypes.windll.kernel32
        user32.GetForegroundWindow.argtypes = []
        user32.GetForegroundWindow.restype = wintypes.HWND
        user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
        user32.GetWindowThreadProcessId.restype = wintypes.DWORD
        kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel32.OpenProcess.restype = wintypes.HANDLE
        kernel32.QueryFullProcessImageNameW.argtypes = [
            wintypes.HANDLE,
            wintypes.DWORD,
            wintypes.LPWSTR,
            ctypes.POINTER(wintypes.DWORD),
        ]
        kernel32.QueryFullProcessImageNameW.restype = wintypes.BOOL
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL

        window = user32.GetForegroundWindow()
        if not window:
            return ""
        process_id = wintypes.DWORD()
        user32.GetWindowThreadProcessId(window, ctypes.byref(process_id))
        if not process_id.value:
            return ""
        handle = kernel32.OpenProcess(0x1000, False, process_id.value)
        if not handle:
            return ""
        try:
            size = wintypes.DWORD(32768)
            buffer = ctypes.create_unicode_buffer(size.value)
            if not kernel32.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(size)):
                return ""
            return Path(buffer.value).name.casefold()
        finally:
            kernel32.CloseHandle(handle)
    except (AttributeError, OSError, ValueError):
        return ""


_PROFILES = load_app_profiles()


def resolve_app_profile(
    executable: str | None = None,
    profiles: dict[str, AppProfile] | None = None,
) -> AppProfile | None:
    """Match an exact executable name; unmatched or inaccessible apps inherit globals."""
    selected = _PROFILES if profiles is None else profiles
    if not selected:
        return None
    active = foreground_executable() if executable is None else executable
    if not isinstance(active, str):
        return None
    return selected.get(Path(active.strip()).name.casefold())
