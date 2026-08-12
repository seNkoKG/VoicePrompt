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
        response.json.return_value = {"text": "  Pozdravljen svet.  "}
        response.raise_for_status.return_value = None
        session = Mock()
        session.post.return_value = response
        engine._session = session

        text = engine.transcribe(np.zeros(1600, dtype=np.float32), 16000)

        self.assertEqual("Pozdravljen svet.", text)
        args, kwargs = session.post.call_args
        self.assertEqual("https://speech.example.test/v1/audio/transcriptions", args[0])
        self.assertEqual(45, kwargs["timeout"])
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

        self.assertEqual("", engine.transcribe(np.zeros(160, dtype=np.float32), 16000))

    def test_server_language_routes_slang_profile_without_leaking_internal_mode(self):
        engine = ServerEngine(ServerConfig(url="http://localhost:8000", language="sl-slang"))
        response = Mock()
        response.json.return_value = {"text": "Živjo."}
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
        response.raise_for_status.return_value = None
        response.json.side_effect = ValueError("invalid JSON")
        session = Mock()
        session.post.return_value = response
        engine._session = session

        self.assertEqual("", engine.transcribe(np.zeros(160, dtype=np.float32), 16000))


if __name__ == "__main__":
    unittest.main()
