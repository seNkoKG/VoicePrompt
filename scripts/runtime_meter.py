"""Low-overhead recording state and audio level publisher for Windows."""

from __future__ import annotations

import math
import mmap
import os
import struct
import sys

import numpy as np

MAP_NAME = "VoicePrompt.AudioMeter.v2"
WAVE_SAMPLES = 48
MAP_SIZE = 16 + WAVE_SAMPLES
SILENCE = bytes([128]) * WAVE_SAMPLES

_mapping: mmap.mmap | None = None
_sequence = 0
_disabled = sys.platform != "win32"


def _open_mapping() -> mmap.mmap | None:
    global _disabled, _mapping
    if _disabled:
        return None
    if _mapping is not None:
        return _mapping
    try:
        _mapping = mmap.mmap(-1, MAP_SIZE, tagname=MAP_NAME)
    except (OSError, ValueError):
        _disabled = True
    return _mapping


def _write(state: int, level: float, waveform: bytes = SILENCE) -> None:
    global _disabled, _sequence
    mapping = _open_mapping()
    if mapping is None:
        return
    try:
        _sequence = (_sequence + 2) & 0x7FFFFFFE
        mapping.seek(0)
        mapping.write(struct.pack("<i", _sequence | 1))
        mapping.write(struct.pack("<ifi", state, level, os.getpid()) + waveform)
        mapping.seek(0)
        mapping.write(struct.pack("<i", _sequence))
    except (OSError, ValueError):
        _disabled = True


def publish_state(recording: bool) -> None:
    _write(1 if recording else 0, 0.0)


def publish_level(audio: np.ndarray) -> None:
    try:
        samples = np.asarray(audio, dtype=np.float32).reshape(-1)
        if samples.size == 0:
            _write(1, 0.0)
            return
        rms = float(np.sqrt(np.mean(np.square(samples))))
        decibels = 20.0 * math.log10(max(rms, 1e-6))
        level = min(1.0, max(0.0, (decibels + 50.0) / 50.0))
        indices = np.linspace(0, max(0, samples.size - 1), WAVE_SAMPLES, dtype=np.intp)
        shape = np.tanh(samples[indices] * 7.0)
        waveform = np.rint((shape + 1.0) * 127.5).astype(np.uint8).tobytes()
        _write(1, level, waveform)
    except (FloatingPointError, TypeError, ValueError):
        _write(1, 0.0)
