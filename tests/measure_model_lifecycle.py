"""Measure VoicePrompt's real local model load, unload, and reload lifecycle."""

from __future__ import annotations

import argparse
import ctypes
import json
import subprocess
import time
from ctypes import wintypes
from types import SimpleNamespace


class _MemoryCounters(ctypes.Structure):
    _fields_ = [
        ("cb", wintypes.DWORD),
        ("PageFaultCount", wintypes.DWORD),
        ("PeakWorkingSetSize", ctypes.c_size_t),
        ("WorkingSetSize", ctypes.c_size_t),
        ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
        ("QuotaPagedPoolUsage", ctypes.c_size_t),
        ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
        ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
        ("PagefileUsage", ctypes.c_size_t),
        ("PeakPagefileUsage", ctypes.c_size_t),
        ("PrivateUsage", ctypes.c_size_t),
    ]


def memory() -> dict[str, float]:
    kernel32 = ctypes.windll.kernel32
    psapi = ctypes.windll.psapi
    kernel32.GetCurrentProcess.argtypes = []
    kernel32.GetCurrentProcess.restype = wintypes.HANDLE
    psapi.GetProcessMemoryInfo.argtypes = [
        wintypes.HANDLE,
        ctypes.POINTER(_MemoryCounters),
        wintypes.DWORD,
    ]
    psapi.GetProcessMemoryInfo.restype = wintypes.BOOL
    counters = _MemoryCounters(cb=ctypes.sizeof(_MemoryCounters))
    if not psapi.GetProcessMemoryInfo(
        kernel32.GetCurrentProcess(),
        ctypes.byref(counters),
        counters.cb,
    ):
        raise ctypes.WinError()
    return {
        "private_mb": round(counters.PrivateUsage / 1024 / 1024, 1),
        "working_set_mb": round(counters.WorkingSetSize / 1024 / 1024, 1),
    }


def gpu_memory_mb() -> int:
    result = subprocess.run(
        ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
        capture_output=True,
        text=True,
        timeout=30,
        check=False,
    )
    try:
        return int(result.stdout.strip().splitlines()[0])
    except (IndexError, ValueError):
        return -1


def event(name: str, seconds: float = 0.0) -> None:
    print(json.dumps({
        "event": name,
        "seconds": round(seconds, 3),
        "gpu_memory_mb": gpu_memory_mb(),
        **memory(),
    }), flush=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default="Systran/faster-whisper-large-v3")
    parser.add_argument("--device", default="cuda")
    parser.add_argument("--compute-type", default="float16")
    args = parser.parse_args()

    from whisper_dictation.engine.local import LocalEngine

    engine = LocalEngine(
        SimpleNamespace(
            model=args.model,
            language="",
            prompt="",
            temperature=0.0,
            hotwords="",
        ),
        SimpleNamespace(device=args.device, compute_type=args.compute_type),
        SimpleNamespace(
            threshold=0.6,
            silence_ms=250,
            min_speech_ms=250,
            max_speech_s=180.0,
        ),
    )
    event("created")

    started = time.perf_counter()
    engine.prepare()
    event("loaded", time.perf_counter() - started)

    engine._last_used = 0.0
    started = time.perf_counter()
    if not engine.release_if_idle(1.0):
        raise RuntimeError("model did not unload")
    event("unloaded", time.perf_counter() - started)

    started = time.perf_counter()
    engine.prepare()
    event("reloaded", time.perf_counter() - started)
    engine.close()
    event("closed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
