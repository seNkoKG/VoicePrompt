"""Integration tests for VoicePrompt's optional OpenAI-compatible text cleanup."""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from unittest.mock import patch

from scripts.ai_rewriter import AiRewriter


class _Provider(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self):
        super().__init__(("127.0.0.1", 0), _Handler)
        self.requests: list[dict] = []
        self.authorizations: list[str] = []
        self.connections: set[tuple[str, int]] = set()
        self.reply = "I need you to fix my English and make this request clearer."
        self.delay = 0.0
        self.status = 200
        self.invalid_json = False


class _Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_POST(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        provider: _Provider = self.server  # type: ignore[assignment]
        length = int(self.headers.get("Content-Length", "0"))
        provider.requests.append(json.loads(self.rfile.read(length)))
        provider.authorizations.append(self.headers.get("Authorization", ""))
        provider.connections.add(self.client_address)
        if provider.delay:
            time.sleep(provider.delay)

        if provider.invalid_json:
            body = b"not-json"
        else:
            body = json.dumps({
                "choices": [{"message": {"content": provider.reply}}],
            }).encode("utf-8")
        try:
            self.send_response(provider.status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def log_message(self, _format: str, *args: object) -> None:
        pass


class AiRewriterTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="voiceprompt_ai_")
        self.provider = _Provider()
        self.thread = threading.Thread(target=self.provider.serve_forever, daemon=True)
        self.thread.start()

    def tearDown(self) -> None:
        self.provider.shutdown()
        self.provider.server_close()
        self.thread.join(timeout=2)
        self.temp.cleanup()

    def config(self, mode: str = "grammar", timeout_ms: int = 900) -> Path:
        path = Path(self.temp.name) / "ai.json"
        path.write_text(json.dumps({
            "mode": mode,
            "endpoint": f"http://127.0.0.1:{self.provider.server_port}/v1/chat/completions",
            "model": "test-model",
            "timeout_ms": timeout_ms,
            "api_key_protected": "",
        }), encoding="utf-8")
        return path

    def test_off_path_is_unchanged_and_does_not_call_provider(self) -> None:
        rewriter = AiRewriter(self.config(mode="off"))
        source = "  um keep this exactly  "
        started = time.perf_counter()
        for _ in range(10_000):
            self.assertIs(rewriter.rewrite(source), source)
        elapsed = time.perf_counter() - started
        self.assertLess(elapsed, 0.25)
        self.assertEqual(self.provider.requests, [])
        rewriter.close()

    def test_hand_edited_null_settings_normalize_safely(self) -> None:
        path = Path(self.temp.name) / "ai.json"
        path.write_text(json.dumps({
            "mode": None,
            "endpoint": None,
            "model": None,
            "timeout_ms": None,
        }), encoding="utf-8")
        rewriter = AiRewriter(path)
        self.assertFalse(rewriter.enabled)
        self.assertEqual(rewriter.settings["model"], "qwen2.5:3b")
        self.assertEqual(rewriter.settings["timeout_ms"], 900)
        rewriter.close()

    def test_grammar_request_preserves_outer_whitespace(self) -> None:
        rewriter = AiRewriter(self.config())
        result = rewriter.rewrite("  um fix my english  ")
        self.assertEqual(result, "  " + self.provider.reply + "  ")
        request = self.provider.requests[0]
        self.assertEqual(request["model"], "test-model")
        self.assertFalse(request["stream"])
        system_prompt = request["messages"][0]["content"]
        self.assertIn("Never translate", system_prompt)
        self.assertIn("Slovenian slang", system_prompt)
        self.assertIn("Do not paraphrase", system_prompt)
        self.assertEqual(request["messages"][1]["content"], "um fix my english")
        self.assertFalse(rewriter.used_fallback)
        rewriter.close()

    def test_prompt_mode_uses_prompt_restructuring_instruction(self) -> None:
        rewriter = AiRewriter(self.config(mode="prompt"))
        rewriter.rewrite("make this a useful prompt")
        system_prompt = self.provider.requests[0]["messages"][0]["content"]
        self.assertIn("well-structured AI prompt", system_prompt)
        self.assertIn("Never translate", system_prompt)
        rewriter.close()

    def test_environment_api_key_is_sent_without_disk_secret(self) -> None:
        rewriter = AiRewriter(self.config())
        with patch.dict(os.environ, {"VOICEPROMPT_AI_API_KEY": "test-token"}):
            rewriter.rewrite("hello")
        self.assertEqual(self.provider.authorizations, ["Bearer test-token"])
        rewriter.close()

    def test_http_and_json_failures_return_original_text(self) -> None:
        source = "never lose this"
        self.provider.status = 500
        rewriter = AiRewriter(self.config())
        self.assertEqual(rewriter.rewrite(source), source)
        self.assertTrue(rewriter.used_fallback)
        self.provider.status = 200
        self.provider.invalid_json = True
        self.assertEqual(rewriter.rewrite(source), source)
        self.assertTrue(rewriter.used_fallback)
        rewriter.close()

    def test_timeout_returns_original_within_bounded_wait(self) -> None:
        self.provider.delay = 0.7
        rewriter = AiRewriter(self.config(timeout_ms=400))
        source = "time-sensitive transcript"
        started = time.perf_counter()
        self.assertEqual(rewriter.rewrite(source), source)
        elapsed = time.perf_counter() - started
        self.assertTrue(rewriter.used_fallback)
        self.assertLess(elapsed, 1.0)
        rewriter.close()

    def test_suspiciously_long_output_returns_original(self) -> None:
        self.provider.reply = "x" * 500
        rewriter = AiRewriter(self.config())
        source = "short request"
        self.assertEqual(rewriter.rewrite(source), source)
        self.assertTrue(rewriter.used_fallback)
        rewriter.close()

    def test_session_reuses_local_http_connection(self) -> None:
        rewriter = AiRewriter(self.config())
        self.assertNotEqual(rewriter.rewrite("first"), "first")
        self.assertNotEqual(rewriter.rewrite("second"), "second")
        self.assertEqual(len(self.provider.connections), 1)
        rewriter.close()

    def test_connection_test_cli_returns_machine_readable_result(self) -> None:
        script = Path(__file__).parents[1] / "scripts" / "ai_rewriter.py"
        completed = subprocess.run(
            [sys.executable, str(script), "--test", "--config", str(self.config())],
            capture_output=True,
            text=True,
            timeout=8,
            check=False,
        )
        self.assertEqual(completed.returncode, 0, completed.stderr)
        result = json.loads(completed.stdout)
        self.assertTrue(result["ok"])
        self.assertEqual(result["text"], self.provider.reply)
        self.assertIsInstance(result["latency_ms"], int)


if __name__ == "__main__":
    unittest.main(verbosity=2)
