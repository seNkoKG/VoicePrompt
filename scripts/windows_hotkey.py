"""Reliable Windows global-hotkey backend.

Windows' RegisterHotKey API owns the shortcut registration and posts a
WM_HOTKEY message to a dedicated message-loop thread.  Hold-mode release is
detected from the physical key state because WM_HOTKEY is a press-only event.
"""

from __future__ import annotations

import ctypes
import logging
import threading
from collections.abc import Callable
from ctypes import wintypes

log = logging.getLogger(__name__)

_WM_HOTKEY = 0x0312
_WM_QUIT = 0x0012
_PM_NOREMOVE = 0x0000
_MOD_NOREPEAT = 0x4000
_HOTKEY_ID = 0x5650
_KEY_DOWN_MASK = 0x8000
_RELEASE_POLL_SECONDS = 0.008

_MODIFIER_FLAGS = {
    "alt": 0x0001,
    "ctrl": 0x0002,
    "control": 0x0002,
    "shift": 0x0004,
}

_MODIFIER_VKS = {
    "alt": (0x12,),
    "ctrl": (0x11,),
    "control": (0x11,),
    "shift": (0x10,),
}

_NAMED_VKS = {
    "backspace": 0x08,
    "tab": 0x09,
    "enter": 0x0D,
    "pause": 0x13,
    "caps_lock": 0x14,
    "esc": 0x1B,
    "space": 0x20,
    "page_up": 0x21,
    "page_down": 0x22,
    "end": 0x23,
    "home": 0x24,
    "left": 0x25,
    "up": 0x26,
    "right": 0x27,
    "down": 0x28,
    "print_screen": 0x2C,
    "insert": 0x2D,
    "delete": 0x2E,
    "menu": 0x5D,
    "num_lock": 0x90,
    "scroll_lock": 0x91,
}


def _parse_binding(binding: str) -> tuple[tuple[str, ...], str]:
    parts = tuple(part.strip().lower() for part in binding.split("+") if part.strip())
    if not parts:
        raise ValueError("Hotkey binding cannot be empty")
    modifiers = parts[:-1]
    unknown = [modifier for modifier in modifiers if modifier not in _MODIFIER_FLAGS]
    if unknown:
        raise ValueError(f"Unsupported Windows hotkey modifier: {unknown[0]}")
    return modifiers, parts[-1]


def _virtual_key(key: str) -> int:
    if key in _NAMED_VKS:
        return _NAMED_VKS[key]
    if len(key) == 1 and key.isascii() and key.isalnum():
        return ord(key.upper())
    if key.startswith("f") and key[1:].isdigit():
        number = int(key[1:])
        if 1 <= number <= 24 and number != 12:
            return 0x70 + number - 1
    raise ValueError(f"Unsupported Windows hotkey key: {key}")


class WindowsHotkeyBackend:
    """Register and dispatch one system-wide Windows hotkey."""

    def __init__(
        self,
        binding: str,
        on_press: Callable[[], None],
        on_release: Callable[[], None],
        stop_event: threading.Event,
    ) -> None:
        modifiers, key = _parse_binding(binding)
        self.binding = binding
        self._modifier_names = modifiers
        self._modifier_flags = _MOD_NOREPEAT
        for modifier in modifiers:
            self._modifier_flags |= _MODIFIER_FLAGS[modifier]
        self._modifier_vks = tuple(_MODIFIER_VKS[modifier] for modifier in modifiers)
        self._target_vk = _virtual_key(key)
        self._on_press = on_press
        self._on_release = on_release
        self._stop_event = stop_event
        self._ready = threading.Event()
        self._startup_error: BaseException | None = None
        self._thread: threading.Thread | None = None
        self._thread_id: int | None = None
        self._user32 = None
        self._state_lock = threading.Lock()

    def start(self) -> None:
        with self._state_lock:
            if self._thread is not None and self._thread.is_alive():
                return
            self._thread = threading.Thread(
                target=self._message_loop,
                name="VoicePromptHotkey",
                daemon=True,
            )
            thread = self._thread
            thread.start()

        if not self._ready.wait(timeout=2.0):
            self.stop()
            raise RuntimeError("Windows hotkey registration timed out")
        if self._startup_error is not None:
            self.stop()
            raise RuntimeError(
                f"Could not register global hotkey {self.binding!r}: {self._startup_error}"
            ) from self._startup_error

    def stop(self) -> None:
        with self._state_lock:
            thread = self._thread
            thread_id = self._thread_id
            user32 = self._user32
        if thread_id is not None and user32 is not None:
            user32.PostThreadMessageW(thread_id, _WM_QUIT, 0, 0)
        if thread is not None and thread is not threading.current_thread():
            thread.join(timeout=2.0)
        with self._state_lock:
            if self._thread is thread:
                self._thread = None

    def _message_loop(self) -> None:
        registered = False
        user32 = ctypes.WinDLL("user32", use_last_error=True)
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self._configure_api(user32, kernel32)

        try:
            # Calling PeekMessage creates this thread's message queue before
            # another thread can attempt to wake it during shutdown.
            message = wintypes.MSG()
            user32.PeekMessageW(ctypes.byref(message), None, 0, 0, _PM_NOREMOVE)
            with self._state_lock:
                self._user32 = user32
                self._thread_id = int(kernel32.GetCurrentThreadId())

            if not user32.RegisterHotKey(
                None,
                _HOTKEY_ID,
                self._modifier_flags,
                self._target_vk,
            ):
                raise ctypes.WinError(ctypes.get_last_error())
            registered = True
            log.info("Hotkey listener started (Win32 native): %s", self.binding)
            self._ready.set()

            while not self._stop_event.is_set():
                result = user32.GetMessageW(ctypes.byref(message), None, 0, 0)
                if result == -1:
                    raise ctypes.WinError(ctypes.get_last_error())
                if result == 0:
                    break
                if message.message != _WM_HOTKEY or int(message.wParam) != _HOTKEY_ID:
                    continue
                self._safe_callback(self._on_press, "press")
                self._wait_for_release(user32)
        except BaseException as exc:
            if not self._ready.is_set():
                self._startup_error = exc
                self._ready.set()
            elif not self._stop_event.is_set():
                log.exception("Windows hotkey listener stopped unexpectedly")
        finally:
            if registered:
                user32.UnregisterHotKey(None, _HOTKEY_ID)
            with self._state_lock:
                self._thread_id = None
                self._user32 = None
            self._ready.set()

    def _wait_for_release(self, user32) -> None:
        while not self._stop_event.is_set() and self._binding_is_down(user32):
            self._stop_event.wait(_RELEASE_POLL_SECONDS)
        if not self._stop_event.is_set():
            self._safe_callback(self._on_release, "release")

    def _binding_is_down(self, user32) -> bool:
        if not user32.GetAsyncKeyState(self._target_vk) & _KEY_DOWN_MASK:
            return False
        return all(
            any(user32.GetAsyncKeyState(vk) & _KEY_DOWN_MASK for vk in alternatives)
            for alternatives in self._modifier_vks
        )

    @staticmethod
    def _safe_callback(callback: Callable[[], None], event_name: str) -> None:
        try:
            callback()
        except Exception:
            log.exception("Hotkey %s callback failed; listener is still active", event_name)

    @staticmethod
    def _configure_api(user32, kernel32) -> None:
        user32.RegisterHotKey.argtypes = [
            wintypes.HWND,
            ctypes.c_int,
            wintypes.UINT,
            wintypes.UINT,
        ]
        user32.RegisterHotKey.restype = wintypes.BOOL
        user32.UnregisterHotKey.argtypes = [wintypes.HWND, ctypes.c_int]
        user32.UnregisterHotKey.restype = wintypes.BOOL
        user32.GetMessageW.argtypes = [
            ctypes.POINTER(wintypes.MSG),
            wintypes.HWND,
            wintypes.UINT,
            wintypes.UINT,
        ]
        user32.GetMessageW.restype = ctypes.c_int
        user32.PeekMessageW.argtypes = [
            ctypes.POINTER(wintypes.MSG),
            wintypes.HWND,
            wintypes.UINT,
            wintypes.UINT,
            wintypes.UINT,
        ]
        user32.PeekMessageW.restype = wintypes.BOOL
        user32.PostThreadMessageW.argtypes = [
            wintypes.DWORD,
            wintypes.UINT,
            wintypes.WPARAM,
            wintypes.LPARAM,
        ]
        user32.PostThreadMessageW.restype = wintypes.BOOL
        user32.GetAsyncKeyState.argtypes = [ctypes.c_int]
        user32.GetAsyncKeyState.restype = ctypes.c_short
        kernel32.GetCurrentThreadId.argtypes = []
        kernel32.GetCurrentThreadId.restype = wintypes.DWORD
