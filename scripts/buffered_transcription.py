"""Lossless orchestration for background transcription of long recordings."""

from __future__ import annotations

import threading

import numpy as np


class BufferedSession:
    """Collect VAD utterances into useful decoding batches for one recording.

    Audio ownership stays with the daemon. This object only coordinates speech
    blocks and text results; the daemon retains the complete microphone stream
    until the final paste so any anomaly can fall back to one full batch pass.
    """

    def __init__(self, sample_rate: int, minimum_batch_seconds: float = 6.0):
        if sample_rate <= 0:
            raise ValueError("sample_rate must be positive")
        if minimum_batch_seconds <= 0:
            raise ValueError("minimum_batch_seconds must be positive")
        self.sample_rate = sample_rate
        self.minimum_batch_samples = max(1, int(sample_rate * minimum_batch_seconds))
        self._pending: list[np.ndarray] = []
        self._pending_samples = 0
        self._results: list[str] = []
        self._failed = False
        self._lock = threading.Lock()
        self.scheduled_batches = 0
        self.completed_batches = 0
        self.compute_seconds = 0.0
        self.scheduled_before_release = 0

    def add_utterance(
        self,
        audio: np.ndarray | None,
        *,
        force: bool = False,
    ) -> np.ndarray | None:
        """Add one speech block and return a decode batch when enough is ready."""
        with self._lock:
            if audio is not None and audio.size:
                self._pending.append(audio)
                self._pending_samples += int(audio.size)
            if not self._pending:
                return None
            if not force and self._pending_samples < self.minimum_batch_samples:
                return None
            batch = self._pending[0] if len(self._pending) == 1 else np.concatenate(self._pending)
            self._pending = []
            self._pending_samples = 0
            self.scheduled_batches += 1
            return batch

    def mark_released(self) -> None:
        with self._lock:
            self.scheduled_before_release = self.scheduled_batches

    def record_result(self, text: str, elapsed_seconds: float) -> None:
        clean = text.strip()
        with self._lock:
            self.completed_batches += 1
            self.compute_seconds += max(0.0, elapsed_seconds)
            if clean:
                self._results.append(clean)
            else:
                self._failed = True

    def record_failure(self, elapsed_seconds: float = 0.0) -> None:
        with self._lock:
            self.completed_batches += 1
            self.compute_seconds += max(0.0, elapsed_seconds)
            self._failed = True

    def mark_failed(self) -> None:
        """Require full-audio fallback for a non-decoder orchestration error."""
        with self._lock:
            self._failed = True

    @property
    def has_prefetch(self) -> bool:
        with self._lock:
            return self.scheduled_batches > 0

    @property
    def needs_fallback(self) -> bool:
        with self._lock:
            return (
                self._failed
                or self.completed_batches != self.scheduled_batches
                or not self._results
            )

    @property
    def text(self) -> str:
        with self._lock:
            return " ".join(self._results).strip()
