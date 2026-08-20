import json
import unittest
from unittest.mock import Mock

import numpy as np
import requests

from whisper_dictation.config import ServerConfig
from whisper_dictation.engine.server import ServerEngine


class ServerEngineTests(unittest.TestCase):
    def test_openai_compatible_request_is_bounded_and_contains_wav(self):
        config = ServerConfig(
            url="https://speech.example.test/",
            model="Systran/faster-whisper-large-v3",
            language="sl",
            timeout=45,
            prompt="Imena: Žan",
            temperature=0.0,
            hotwords="Codex, Ljubljana",
        )
        engine = ServerEngine(config)
        response = Mock()
        response.headers = {}
        response.iter_content.return_value = [json.dumps({"text": "  Pozdravljen svet.  "}).encode()]
        response.raise_for_status.return_value = None
        session = Mock()
        session.post.return_value = response
        engine._session = session

        text = engine.transcribe(np.zeros(1600, dtype=np.float32), 16000)

        self.assertEqual("Pozdravljen svet.", text)
        args, kwargs = session.post.call_args
        self.assertEqual("https://speech.example.test/v1/audio/transcriptions", args[0])
        self.assertEqual(45, kwargs["timeout"])
        self.assertTrue(kwargs["stream"])
        self.assertEqual("Systran/faster-whisper-large-v3", kwargs["data"]["model"])
        self.assertEqual("sl", kwargs["data"]["language"])
        self.assertEqual("Imena: Žan", kwargs["data"]["prompt"])
        self.assertEqual("Codex, Ljubljana", kwargs["data"]["hotwords"])
        name, wav, media_type = kwargs["files"]["file"]
        self.assertEqual("audio.wav", name)
        self.assertEqual("audio/wav", media_type)
        self.assertTrue(wav.startswith(b"RIFF"))

    def test_timeout_fails_closed_without_pasting_error_text(self):
        engine = ServerEngine(ServerConfig(url="http://localhost:8000", timeout=5))
        session = Mock()
        session.post.side_effect = requests.Timeout("late")
        engine._session = session

        with self.assertRaisesRegex(RuntimeError, "timed out"):
            engine.transcribe(np.zeros(160, dtype=np.float32), 16000)

    def test_server_language_routes_slang_profile_without_leaking_internal_mode(self):
        engine = ServerEngine(ServerConfig(url="http://localhost:8000", language="sl-slang"))
        response = Mock()
        response.headers = {}
        response.iter_content.return_value = [json.dumps({"text": "Živjo."}).encode()]
        response.raise_for_status.return_value = None
        session = Mock()
        session.post.return_value = response
        engine._session = session

        self.assertEqual("Živjo.", engine.transcribe(np.zeros(160, dtype=np.float32), 16000))
        self.assertEqual("sl", session.post.call_args.kwargs["data"]["language"])

        auto_engine = ServerEngine(ServerConfig(url="http://localhost:8000", language=""))
        auto_session = Mock()
        auto_session.post.return_value = response
        auto_engine._session = auto_session
        self.assertEqual("Živjo.", auto_engine.transcribe(np.zeros(160, dtype=np.float32), 16000))
        self.assertNotIn("language", auto_session.post.call_args.kwargs["data"])

    def test_invalid_json_fails_closed_without_pasting_error_text(self):
        engine = ServerEngine(ServerConfig(url="http://localhost:8000", timeout=5))
        response = Mock()
        response.headers = {}
        response.raise_for_status.return_value = None
        response.iter_content.return_value = [b"invalid JSON"]
        session = Mock()
        session.post.return_value = response
        engine._session = session

        with self.assertRaisesRegex(RuntimeError, "Invalid transcription server response"):
            engine.transcribe(np.zeros(160, dtype=np.float32), 16000)

    def test_oversized_response_is_rejected_before_json_parsing(self):
        engine = ServerEngine(ServerConfig(url="http://localhost:8000", timeout=5))
        response = Mock()
        response.headers = {"Content-Length": str(1024 * 1024 + 1)}
        response.raise_for_status.return_value = None
        response.iter_content.return_value = []
        session = Mock()
        session.post.return_value = response
        engine._session = session

        with self.assertRaisesRegex(RuntimeError, "response is too large"):
            engine.transcribe(np.zeros(160, dtype=np.float32), 16000)
        response.close.assert_called_once()

    def test_non_object_json_response_is_rejected(self):
        engine = ServerEngine(ServerConfig(url="http://localhost:8000", timeout=5))
        response = Mock()
        response.headers = {}
        response.raise_for_status.return_value = None
        response.iter_content.return_value = [b"[]"]
        session = Mock()
        session.post.return_value = response
        engine._session = session

        with self.assertRaisesRegex(RuntimeError, "response root is not an object"):
            engine.transcribe(np.zeros(160, dtype=np.float32), 16000)


if __name__ == "__main__":
    unittest.main()
