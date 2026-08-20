"""Product-path tests for the generated local engine and model lifecycle."""

from __future__ import annotations

import importlib
from importlib.machinery import ModuleSpec
import os
import sys
import time
import types
import unittest
from dataclasses import dataclass

import numpy as np


@dataclass
class _Segment:
    text: str
    avg_logprob: float
    tokens: list[int]


class _Info:
    def __init__(
        self,
        language: str,
        confidence: float,
        probabilities: list[tuple[str, float]] | None = None,
    ):
        self.language = language
        self.language_probability = confidence
        self.all_language_probs = probabilities


class _Weights:
    def __init__(self):
        self.model_is_loaded = True
        self.loads = 0
        self.unloads = 0

    def load_model(self) -> None:
        self.model_is_loaded = True
        self.loads += 1

    def unload_model(self) -> None:
        self.model_is_loaded = False
        self.unloads += 1


class _WhisperModel:
    instances: list["_WhisperModel"] = []

    def __init__(self, *_args: object, **_kwargs: object):
        self.model = _Weights()
        self.languages: list[str | None] = []
        self.instances.append(self)

    def transcribe(self, _audio: np.ndarray, *, language: str | None, **_kwargs: object):
        self.languages.append(language)
        if language is None:
            return (
                [_Segment("Treba, " * 75, -0.776, list(range(75)))],
                _Info("sl", 0.66, [("sl", 0.66), ("en", 0.16)]),
            )
        if language == "sl":
            return [_Segment("Ampak verzija ni?", -0.776, [1, 2, 3, 4])], _Info("sl", 1.0)
        return [_Segment("But the version is not?", -0.454, [1, 2, 3, 4, 5])], _Info("en", 1.0)


class PatchedLocalEngineTests(unittest.TestCase):
    @unittest.skipUnless(os.environ.get("VOICEPROMPT_PATCHED_SITE"), "patched runtime not supplied")
    def test_product_routing_and_native_idle_lifecycle(self) -> None:
        module = os.environ["VOICEPROMPT_PATCHED_SITE"]
        sys.path.insert(0, os.path.dirname(module))
        self.addCleanup(sys.path.remove, os.path.dirname(module))
        for name in list(sys.modules):
            if name == "whisper_dictation" or name.startswith("whisper_dictation."):
                del sys.modules[name]
        _WhisperModel.instances.clear()
        fake_module = types.ModuleType("faster_whisper")
        fake_module.__spec__ = ModuleSpec("faster_whisper", loader=None)
        fake_module.WhisperModel = _WhisperModel
        sys.modules["faster_whisper"] = fake_module
        self.addCleanup(sys.modules.pop, "faster_whisper", None)

        local = importlib.import_module("whisper_dictation.engine.local")
        server = types.SimpleNamespace(
            model="test-model",
            language="",
            prompt="",
            temperature=0.0,
            hotwords="",
        )
        engine_config = types.SimpleNamespace(compute_type="float16", device="cuda")
        vad = types.SimpleNamespace(
            threshold=0.6,
            silence_ms=250,
            min_speech_ms=250,
            max_speech_s=180.0,
        )
        engine = local.LocalEngine(server, engine_config, vad)

        self.assertTrue(engine.is_available())
        self.assertEqual(_WhisperModel.instances, [])
        engine.prepare()
        model = _WhisperModel.instances[0]
        engine.prepare()
        self.assertEqual(len(_WhisperModel.instances), 1)
        self.assertFalse(engine.release_if_idle(999.0))
        engine._last_used = time.monotonic() - 1000.0
        self.assertTrue(engine.release_if_idle(999.0))
        self.assertEqual(model.model.unloads, 1)

        engine._recent_language = "en"
        text = engine.transcribe(np.zeros(32000, dtype=np.float32), 16000)
        self.assertEqual(text, "But the version is not?")
        self.assertEqual(model.languages, [None, "sl", "en"])
        self.assertEqual(model.model.loads, 1)
        self.assertEqual(engine.last_language, "en")
        self.assertEqual(engine._recent_language, "en")

        engine.close()
        self.assertEqual(model.model.unloads, 2)
        engine.close()


if __name__ == "__main__":
    unittest.main(verbosity=2)
