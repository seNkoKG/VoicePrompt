"""Product-path tests for VAD warm-up and lossless cold-start fallback."""

from __future__ import annotations

import importlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest
import urllib.request
from unittest.mock import Mock, patch

import numpy as np


class _Engine:
    def __init__(self) -> None:
        self.transcribed: list[np.ndarray] = []
        self.closed = False
        self.prepare_calls = 0

    def is_available(self) -> bool:
        return True

    def transcribe(self, audio: np.ndarray, _sample_rate: int = 16000) -> str:
        self.transcribed.append(audio.copy())
        return "retained transcript"

    def close(self) -> None:
        self.closed = True

    def prepare(self) -> None:
        self.prepare_calls += 1


class _Hotkey:
    def __init__(self, *, events: list[str], **_kwargs: object) -> None:
        self.events = events

    def start(self) -> None:
        self.events.append("hotkey")

    def stop(self) -> None:
        self.events.append("hotkey-stop")


@unittest.skipUnless(os.environ.get("VOICEPROMPT_PATCHED_SITE"), "patched runtime not supplied")
class PatchedDaemonTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        module = Path(os.environ["VOICEPROMPT_PATCHED_SITE"])
        sys.path.insert(0, str(module.parent))
        cls.module_parent = str(module.parent)

    @classmethod
    def tearDownClass(cls) -> None:
        sys.path.remove(cls.module_parent)

    def setUp(self) -> None:
        for name in list(sys.modules):
            if name == "whisper_dictation" or name.startswith("whisper_dictation."):
                del sys.modules[name]
        self.daemon_module = importlib.import_module("whisper_dictation.daemon")
        config_module = importlib.import_module("whisper_dictation.config")
        self.config = config_module.Config(
            engine=config_module.EngineConfig(type="local", compute_type="float16", device="cpu"),
            hotkey=config_module.HotkeyConfig(binding="f1", mode="hold"),
        )
        self.engine = _Engine()
        self.events: list[str] = []
        self.typed: list[str] = []
        self.patches = [
            patch.object(self.daemon_module, "create_engine", return_value=self.engine),
            patch.object(
                self.daemon_module,
                "HotkeyListener",
                side_effect=lambda **kwargs: _Hotkey(events=self.events, **kwargs),
            ),
            patch.object(self.daemon_module, "notify"),
            patch.object(self.daemon_module, "publish_state"),
            patch.object(self.daemon_module, "publish_level"),
            patch.object(self.daemon_module, "type_text", side_effect=self.typed.append),
        ]
        for active_patch in self.patches:
            active_patch.start()
            self.addCleanup(active_patch.stop)

    def test_vad_is_ready_before_hotkey_accepts_recordings(self) -> None:
        instance = self.daemon_module.DictationDaemon(self.config, streaming=True)
        instance._vad = Mock()
        instance._vad.prepare.side_effect = lambda: self.events.append("vad")
        instance.start()
        self.assertEqual(self.events[:2], ["vad", "hotkey"])
        instance.stop()

    def test_vad_failure_switches_to_lossless_batch_mode(self) -> None:
        with patch.dict(os.environ, {"VOICEPROMPT_BUFFERED_STREAMING": "1"}):
            instance = self.daemon_module.DictationDaemon(self.config, streaming=True)
        self.assertTrue(instance._buffered_streaming)
        instance._vad = Mock()
        instance._vad.prepare.side_effect = RuntimeError("VAD unavailable")
        with self.assertLogs(self.daemon_module.log, level="ERROR") as logs:
            instance.start()
        self.assertTrue(any("using lossless batch mode" in line for line in logs.output))
        self.assertFalse(instance.streaming)
        self.assertFalse(instance._buffered_streaming)

        chunks = [np.arange(512, dtype=np.float32), np.arange(512, 1024, dtype=np.float32)]
        instance._recording = True
        instance._recording_start = 0.0
        for chunk in chunks:
            instance._on_audio_chunk(chunk)
        instance._on_deactivate()
        instance.stop()

        self.assertEqual(len(self.engine.transcribed), 1)
        np.testing.assert_array_equal(self.engine.transcribed[0], np.concatenate(chunks))
        self.assertEqual(self.typed, ["retained transcript"])
        instance._vad.process_chunk.assert_not_called()
        instance._vad.flush.assert_not_called()

    def test_keep_loaded_prepares_model_on_background_worker(self) -> None:
        with patch.dict(os.environ, {"VOICEPROMPT_MODEL_IDLE_SECONDS": "0"}):
            instance = self.daemon_module.DictationDaemon(self.config, streaming=True)
        instance._vad = Mock()
        instance.start()
        instance._preload_future.result(timeout=2)
        self.assertEqual(self.engine.prepare_calls, 1)
        instance.stop()

    def test_direct_vad_prepare_loads_once_per_detector(self) -> None:
        vad_module = importlib.import_module("whisper_dictation.vad")
        detector = vad_module.SpeechDetector()
        previous_model = vad_module._model
        self.addCleanup(setattr, vad_module, "_model", previous_model)
        fake_model = Mock(return_value=0.0)
        fake_model.reset_states = Mock()

        def load() -> None:
            vad_module._model = fake_model

        with patch.object(vad_module, "_load_model", side_effect=load) as load_model:
            detector.prepare()
            detector.prepare()
        load_model.assert_called_once_with()
        self.assertEqual(fake_model.call_count, 2)
        self.assertEqual(fake_model.reset_states.call_count, 2)

    def test_vad_download_has_a_total_deadline(self) -> None:
        vad_module = importlib.import_module("whisper_dictation.vad")
        previous_model = vad_module._model
        self.addCleanup(setattr, vad_module, "_model", previous_model)
        vad_module._model = None

        class SlowDripHandler(BaseHTTPRequestHandler):
            def do_GET(self) -> None:
                self.send_response(200)
                self.send_header("Content-Length", "1000")
                self.end_headers()
                try:
                    for _index in range(1000):
                        self.wfile.write(b"x")
                        self.wfile.flush()
                        time.sleep(0.02)
                except OSError:
                    pass

            def log_message(self, *_args: object) -> None:
                pass

        server = ThreadingHTTPServer(("127.0.0.1", 0), SlowDripHandler)
        server.daemon_threads = True
        server_thread = threading.Thread(target=server.serve_forever, daemon=True)
        server_thread.start()
        model_url = f"http://127.0.0.1:{server.server_port}/silero_vad.onnx"
        try:
            with tempfile.TemporaryDirectory() as cache_dir, \
                 patch("platformdirs.user_cache_dir", return_value=cache_dir), \
                 patch.object(vad_module, "_ONNX_MODEL_URL", model_url), \
                 patch.object(vad_module, "_VAD_DOWNLOAD_DEADLINE_SECONDS", 0.15), \
                 patch.object(vad_module, "_VAD_DOWNLOAD_SOCKET_TIMEOUT_SECONDS", 0.2):
                started = time.monotonic()
                with self.assertRaisesRegex(TimeoutError, "exceeded 0.15 seconds"):
                    vad_module._load_onnx_model()
                self.assertLess(time.monotonic() - started, 0.75)
                self.assertFalse((Path(cache_dir) / "silero_vad.tmp").exists())
        finally:
            server.shutdown()
            server.server_close()
            server_thread.join(timeout=1)

    def test_vad_deadline_also_bounds_delayed_connection_setup(self) -> None:
        vad_module = importlib.import_module("whisper_dictation.vad")
        previous_model = vad_module._model
        self.addCleanup(setattr, vad_module, "_model", previous_model)
        vad_module._model = None
        open_started = threading.Event()

        def delayed_open(*_args: object, **_kwargs: object) -> object:
            open_started.set()
            time.sleep(0.4)
            raise TimeoutError("simulated delayed resolver")

        with tempfile.TemporaryDirectory() as cache_dir, \
             patch("platformdirs.user_cache_dir", return_value=cache_dir), \
             patch.object(vad_module, "_VAD_DOWNLOAD_DEADLINE_SECONDS", 0.05), \
             patch.object(vad_module, "_VAD_DOWNLOAD_SOCKET_TIMEOUT_SECONDS", 0.02), \
             patch.object(urllib.request, "urlopen", side_effect=delayed_open):
            started = time.monotonic()
            with self.assertRaisesRegex(TimeoutError, "exceeded 0.05 seconds"):
                vad_module._load_onnx_model()
            self.assertTrue(open_started.wait(timeout=0.1))
            self.assertLess(time.monotonic() - started, 0.25)
            self.assertFalse((Path(cache_dir) / "silero_vad.tmp").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
