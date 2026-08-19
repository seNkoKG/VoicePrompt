"""Bounded, local context capture for application-aware dictation formatting."""

from __future__ import annotations

import ctypes
import os
import sys
from ctypes import wintypes
from dataclasses import dataclass

from .app_profiles import foreground_executable

_MAX_NEIGHBOR_CHARS = 500
_SMTO_ABORTIFHUNG = 0x0002
_WM_GETTEXT = 0x000D
_WM_GETTEXTLENGTH = 0x000E
_EM_GETSEL = 0x00B0
_GWL_STYLE = -16
_ES_PASSWORD = 0x0020


@dataclass(frozen=True)
class DictationContext:
    executable: str = ""
    window_title: str = ""
    before_text: str = ""
    selected_text: str = ""
    after_text: str = ""
    app_kind: str = "general"


def context_awareness_enabled(value: object | None = None) -> bool:
    if value is None:
        value = os.environ.get("VOICEPROMPT_CONTEXT_AWARENESS", "1")
    if isinstance(value, bool):
        return value
    return isinstance(value, str) and value.strip().lower() in {"1", "true", "yes", "on"}


def classify_application(executable: str, title: str = "") -> str:
    """Classify without retaining a process path or other application data."""
    name = executable.strip().casefold()
    heading = title.strip().casefold()
    if name in {"code.exe", "devenv.exe", "rider64.exe", "pycharm64.exe", "idea64.exe"}:
        return "code"
    if name in {"windowsterminal.exe", "powershell.exe", "pwsh.exe", "cmd.exe", "wt.exe"}:
        return "terminal"
    if name in {"outlook.exe", "olk.exe", "thunderbird.exe"} or "gmail" in heading:
        return "email"
    if name in {"slack.exe", "discord.exe", "teams.exe", "ms-teams.exe", "whatsapp.exe", "telegram.exe"}:
        return "chat"
    if name in {"winword.exe", "wordpad.exe", "notepad.exe", "onenote.exe"}:
        return "document"
    return "general"


def _send_timeout(hwnd: int, message: int, wparam: int = 0, lparam: int = 0) -> int | None:
    result = ctypes.c_size_t()
    send = ctypes.windll.user32.SendMessageTimeoutW
    send.argtypes = [
        wintypes.HWND,
        wintypes.UINT,
        wintypes.WPARAM,
        wintypes.LPARAM,
        wintypes.UINT,
        wintypes.UINT,
        ctypes.POINTER(ctypes.c_size_t),
    ]
    send.restype = wintypes.LPARAM
    ok = send(
        hwnd,
        message,
        wparam,
        lparam,
        _SMTO_ABORTIFHUNG,
        40,
        ctypes.byref(result),
    )
    return int(result.value) if ok else None


def _window_title(hwnd: int) -> str:
    user32 = ctypes.windll.user32
    length = min(512, max(0, int(user32.GetWindowTextLengthW(hwnd))))
    if length == 0:
        return ""
    buffer = ctypes.create_unicode_buffer(length + 1)
    return buffer.value if user32.GetWindowTextW(hwnd, buffer, length + 1) else ""


def _focused_control(hwnd: int) -> int:
    class GuiThreadInfo(ctypes.Structure):
        _fields_ = [
            ("cbSize", wintypes.DWORD),
            ("flags", wintypes.DWORD),
            ("hwndActive", wintypes.HWND),
            ("hwndFocus", wintypes.HWND),
            ("hwndCapture", wintypes.HWND),
            ("hwndMenuOwner", wintypes.HWND),
            ("hwndMoveSize", wintypes.HWND),
            ("hwndCaret", wintypes.HWND),
            ("rcCaret", wintypes.RECT),
        ]

    process_id = wintypes.DWORD()
    thread_id = ctypes.windll.user32.GetWindowThreadProcessId(hwnd, ctypes.byref(process_id))
    info = GuiThreadInfo(cbSize=ctypes.sizeof(GuiThreadInfo))
    if not thread_id or not ctypes.windll.user32.GetGUIThreadInfo(thread_id, ctypes.byref(info)):
        return 0
    return int(info.hwndFocus or 0)


def _edit_neighbors(hwnd: int) -> tuple[str, str, str]:
    if not hwnd:
        return "", "", ""
    user32 = ctypes.windll.user32
    class_name = ctypes.create_unicode_buffer(64)
    if not user32.GetClassNameW(hwnd, class_name, len(class_name)) or class_name.value.casefold() != "edit":
        return "", "", ""
    if int(user32.GetWindowLongPtrW(hwnd, _GWL_STYLE)) & _ES_PASSWORD:
        return "", "", ""
    length = _send_timeout(hwnd, _WM_GETTEXTLENGTH)
    if length is None or length < 0 or length > 65_535:
        return "", "", ""
    buffer = ctypes.create_unicode_buffer(length + 1)
    copied = _send_timeout(hwnd, _WM_GETTEXT, length + 1, ctypes.cast(buffer, ctypes.c_void_p).value or 0)
    selection = _send_timeout(hwnd, _EM_GETSEL)
    if copied is None or selection is None:
        return "", "", ""
    text = buffer.value
    start = min(len(text), selection & 0xFFFF)
    end = min(len(text), (selection >> 16) & 0xFFFF)
    return (
        text[max(0, start - _MAX_NEIGHBOR_CHARS) : start],
        text[start:end],
        text[end : end + _MAX_NEIGHBOR_CHARS],
    )


def capture_context(enabled: object | None = None) -> DictationContext:
    """Capture bounded edit-control neighbors. Password and nonstandard controls fail closed."""
    executable = foreground_executable()
    if not context_awareness_enabled(enabled) or sys.platform != "win32":
        return DictationContext(executable=executable, app_kind=classify_application(executable))
    try:
        hwnd = int(ctypes.windll.user32.GetForegroundWindow() or 0)
        title = _window_title(hwnd) if hwnd else ""
        before, selected, after = _edit_neighbors(_focused_control(hwnd)) if hwnd else ("", "", "")
        return DictationContext(
            executable=executable,
            window_title=title,
            before_text=before,
            selected_text=selected,
            after_text=after,
            app_kind=classify_application(executable, title),
        )
    except (AttributeError, OSError, TypeError, ValueError):
        return DictationContext(executable=executable, app_kind=classify_application(executable))
