"""Fast optional transcript cleanup through an OpenAI-compatible text endpoint."""

from __future__ import annotations

import argparse
import base64
import ctypes
import json
import logging
import os
import sys
import threading
import time
from ctypes import wintypes
from pathlib import Path

import requests

log = logging.getLogger(__name__)

_ENTROPY = b"VoicePrompt AI v1"
_SYSTEM_PROMPTS = {
    "grammar": (
        "Polish this dictation conservatively. Fix punctuation, capitalization, and obvious grammar errors only. "
        "Never translate. Preserve every language and code-switched phrase exactly where it appears, including "
        "Slovenian slang. Do not paraphrase, summarize, remove details, or replace wording. Preserve names, code, "
        "commands, numbers, and URLs. Do not answer the transcript. Return only the polished text."
    ),
    "prompt": (
        "Turn this speech transcript into a concise, well-structured AI prompt. Preserve every "
        "requirement, constraint, name, code fragment, command, number, and URL. Remove filler and "
        "repetition. Never translate: preserve the transcript's language or language switches, including "
        "Slovenian slang. Do not solve the request or invent details. Return only the improved prompt."
    ),
}


def _default_config_path() -> Path:
    appdata = os.environ.get("APPDATA")
    if appdata:
        return Path(appdata) / "VoicePrompt" / "ai.json"
    return Path.home() / ".config" / "VoicePrompt" / "ai.json"


def _load_settings(path: Path) -> dict:
    defaults = {
        "mode": "off",
        "endpoint": "http://127.0.0.1:11434/v1/chat/completions",
        "model": "qwen2.5:3b",
        "timeout_ms": 900,
        "api_key_protected": "",
    }
    try:
        loaded = json.loads(path.read_text(encoding="utf-8"))
        if isinstance(loaded, dict):
            defaults.update(loaded)
    except FileNotFoundError:
        pass
    except (OSError, ValueError):
        log.warning("Could not load AI settings from %s", path, exc_info=True)

    defaults["mode"] = str(defaults.get("mode") or "off").strip().lower()
    if defaults["mode"] not in _SYSTEM_PROMPTS:
        defaults["mode"] = "off"
    defaults["endpoint"] = str(
        defaults.get("endpoint") or "http://127.0.0.1:11434/v1/chat/completions"
    ).strip()
    defaults["model"] = str(defaults.get("model") or "qwen2.5:3b").strip()
    try:
        defaults["timeout_ms"] = max(400, min(3000, int(defaults["timeout_ms"])))
    except (TypeError, ValueError):
        defaults["timeout_ms"] = 900
    return defaults


class _DataBlob(ctypes.Structure):
    _fields_ = [("size", wintypes.DWORD), ("data", ctypes.POINTER(ctypes.c_ubyte))]


def _blob(data: bytes) -> tuple[_DataBlob, ctypes.Array]:
    buffer = ctypes.create_string_buffer(data)
    return _DataBlob(len(data), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte))), buffer


def _unprotect_api_key(value: str) -> str:
    if not value or sys.platform != "win32":
        return ""
    cipher, cipher_buffer = _blob(base64.b64decode(value))
    entropy, entropy_buffer = _blob(_ENTROPY)
    output = _DataBlob()
    crypt32 = ctypes.windll.crypt32
    crypt32.CryptUnprotectData.argtypes = [
        ctypes.POINTER(_DataBlob), ctypes.c_void_p, ctypes.POINTER(_DataBlob),
        ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(_DataBlob),
    ]
    crypt32.CryptUnprotectData.restype = wintypes.BOOL
    kernel32 = ctypes.windll.kernel32
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p
    if not crypt32.CryptUnprotectData(
        ctypes.byref(cipher), None, ctypes.byref(entropy), None, None, 0, ctypes.byref(output)
    ):
        raise ctypes.WinError()
    try:
        return ctypes.string_at(output.data, output.size).decode("utf-8")
    finally:
        ctypes.memset(output.data, 0, output.size)
        kernel32.LocalFree(output.data)
        _ = cipher_buffer, entropy_buffer


class AiRewriter:
    def __init__(self, config_path: str | Path | None = None):
        self.settings = _load_settings(Path(config_path) if config_path else _default_config_path())
        self.session = requests.Session()
        self.session.headers.update({"User-Agent": "VoicePrompt/1.4.0"})
        self._lock = threading.Lock()
        self.last_error = ""
        self.last_latency_ms = 0
        self.used_fallback = False
        self._last_warning_at = 0.0

    @property
    def enabled(self) -> bool:
        return self.settings["mode"] in _SYSTEM_PROMPTS

    def rewrite(self, text: str) -> str:
        if not text or not self.enabled:
            return text

        leading = text[: len(text) - len(text.lstrip())]
        trailing = text[len(text.rstrip()) :]
        source = text.strip()
        if not source:
            return text

        started = time.perf_counter()
        self.last_error = ""
        self.used_fallback = False
        try:
            with self._lock:
                output = self._request(source)
            if not output:
                raise ValueError("provider returned empty text")
            output = output.strip()
            if len(output) > max(240, len(source) * 3):
                raise ValueError("provider returned unexpectedly long text")
            if self.settings["mode"] == "grammar" and len(output) < max(12, len(source) * 0.6):
                raise ValueError("provider returned an incomplete grammar edit")
            return leading + output + trailing
        except (requests.RequestException, KeyError, IndexError, TypeError, ValueError, OSError) as exc:
            self.last_error = str(exc)
            self.used_fallback = True
            now = time.monotonic()
            if now - self._last_warning_at >= 30:
                log.warning("AI cleanup failed; using original transcript: %s", exc)
                self._last_warning_at = now
            else:
                log.debug("AI cleanup failed; using original transcript: %s", exc)
            return text
        finally:
            self.last_latency_ms = round((time.perf_counter() - started) * 1000)
            if not self.used_fallback:
                log.info("AI %s cleanup completed in %d ms", self.settings["mode"], self.last_latency_ms)

    def _request(self, text: str) -> str:
        timeout_s = self.settings["timeout_ms"] / 1000.0
        connect_timeout = min(0.2, timeout_s / 2)
        read_timeout = max(0.2, timeout_s - connect_timeout)
        api_key = os.environ.get("VOICEPROMPT_AI_API_KEY", "")
        protected = str(self.settings.get("api_key_protected", ""))
        if not api_key and protected:
            api_key = _unprotect_api_key(protected)

        headers = {"Content-Type": "application/json"}
        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"
        payload = {
            "model": self.settings["model"],
            "messages": [
                {"role": "system", "content": _SYSTEM_PROMPTS[self.settings["mode"]]},
                {"role": "user", "content": text},
            ],
            "stream": False,
            "temperature": 0.1,
            # Grammar mode promises not to remove details. A 512-token ceiling
            # could truncate a two-to-three-minute dictation, so size the
            # allowance to the input while retaining a defensive upper bound.
            "max_tokens": max(128, min(4096, len(text) // 2 + 128)),
        }
        response = self.session.post(
            self.settings["endpoint"],
            headers=headers,
            json=payload,
            timeout=(connect_timeout, read_timeout),
        )
        response.raise_for_status()
        choice = response.json()["choices"][0]
        if choice.get("finish_reason") == "length":
            raise ValueError("provider truncated the rewritten transcript")
        content = choice["message"]["content"]
        if isinstance(content, list):
            content = "".join(part.get("text", "") for part in content if isinstance(part, dict))
        if not isinstance(content, str):
            raise TypeError("provider response content is not text")
        return content

    def close(self) -> None:
        self.session.close()


_rewriter = AiRewriter()


def rewrite_text(text: str) -> str:
    return _rewriter.rewrite(text)


def _main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--test", action="store_true")
    parser.add_argument("--config")
    args = parser.parse_args()
    if not args.test:
        parser.error("use --test")

    rewriter = AiRewriter(args.config)
    if not rewriter.enabled:
        print(json.dumps({"ok": False, "error": "AI cleanup is off", "latency_ms": 0}))
        return 1
    # A manual connection test may also warm a sleeping local model. Live
    # dictation always keeps the stricter configured deadline.
    rewriter.settings["timeout_ms"] = 6000
    sample = "um i need you to like fix my english and make this request clearer"
    result = rewriter.rewrite(sample)
    ok = not rewriter.used_fallback
    print(json.dumps({
        "ok": ok,
        "text": result if ok else "",
        "error": rewriter.last_error,
        "latency_ms": rewriter.last_latency_ms,
    }, ensure_ascii=False))
    rewriter.close()
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(_main())
