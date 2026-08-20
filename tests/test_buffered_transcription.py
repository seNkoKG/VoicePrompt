"""Tests for lossless buffered long-recording transcription orchestration."""

import unittest

import numpy as np

from scripts.buffered_transcription import BufferedSession


class BufferedSessionTests(unittest.TestCase):
    def test_short_recording_stays_on_original_batch_path(self) -> None:
        session = BufferedSession(10, minimum_batch_seconds=2.0)
        self.assertIsNone(session.add_utterance(np.ones(10, dtype=np.float32)))
        self.assertFalse(session.has_prefetch)

    def test_complete_utterances_are_grouped_without_reordering(self) -> None:
        session = BufferedSession(10, minimum_batch_seconds=2.0)
        first = np.full(12, 1, dtype=np.float32)
        second = np.full(9, 2, dtype=np.float32)
        self.assertIsNone(session.add_utterance(first))
        batch = session.add_utterance(second)
        self.assertIsNotNone(batch)
        np.testing.assert_array_equal(batch, np.concatenate([first, second]))
        self.assertEqual(session.scheduled_batches, 1)

    def test_release_flushes_a_partial_final_batch(self) -> None:
        session = BufferedSession(10, minimum_batch_seconds=2.0)
        final = np.arange(7, dtype=np.float32)
        batch = session.add_utterance(final, force=True)
        np.testing.assert_array_equal(batch, final)
        session.record_result("final sentence", 0.2)
        self.assertFalse(session.needs_fallback)
        self.assertEqual(session.text, "final sentence")

    def test_results_join_once_in_executor_completion_order(self) -> None:
        session = BufferedSession(10, minimum_batch_seconds=1.0)
        session.add_utterance(np.ones(10, dtype=np.float32))
        session.add_utterance(np.ones(10, dtype=np.float32))
        session.record_result("first sentence.", 0.3)
        session.record_result("second sentence.", 0.4)
        self.assertEqual(session.text, "first sentence. second sentence.")
        self.assertEqual(session.compute_seconds, 0.7)
        self.assertFalse(session.needs_fallback)

    def test_code_switched_languages_keep_order_without_destructive_fallback(self) -> None:
        session = BufferedSession(10, minimum_batch_seconds=1.0)
        session.add_utterance(np.ones(10, dtype=np.float32))
        session.add_utterance(np.ones(10, dtype=np.float32))
        session.record_result("Odpri datoteko.", 0.2, "sl")
        session.record_result("Then run the tests.", 0.2, "en")
        self.assertTrue(session.language_conflict)
        self.assertFalse(session.needs_fallback)
        self.assertEqual(session.text, "Odpri datoteko. Then run the tests.")

    def test_consistent_auto_language_keeps_fast_buffered_result(self) -> None:
        session = BufferedSession(10, minimum_batch_seconds=1.0)
        session.add_utterance(np.ones(10, dtype=np.float32))
        session.add_utterance(np.ones(10, dtype=np.float32))
        session.record_result("first block", 0.2, "en")
        session.record_result("second block", 0.2, "en")
        self.assertFalse(session.language_conflict)
        self.assertFalse(session.needs_fallback)

    def test_empty_or_failed_chunk_requires_full_audio_fallback(self) -> None:
        empty = BufferedSession(10, minimum_batch_seconds=1.0)
        empty.add_utterance(np.ones(10, dtype=np.float32))
        empty.record_result("", 0.1)
        self.assertTrue(empty.needs_fallback)

        failed = BufferedSession(10, minimum_batch_seconds=1.0)
        failed.add_utterance(np.ones(10, dtype=np.float32))
        failed.record_failure(0.2)
        self.assertTrue(failed.needs_fallback)

        orchestration = BufferedSession(10, minimum_batch_seconds=1.0)
        orchestration.add_utterance(np.ones(10, dtype=np.float32))
        orchestration.record_result("valid partial", 0.1)
        orchestration.mark_failed()
        self.assertTrue(orchestration.needs_fallback)

    def test_incomplete_executor_work_requires_fallback(self) -> None:
        session = BufferedSession(10, minimum_batch_seconds=1.0)
        session.add_utterance(np.ones(10, dtype=np.float32))
        self.assertTrue(session.needs_fallback)

    def test_release_snapshot_is_stable(self) -> None:
        session = BufferedSession(10, minimum_batch_seconds=1.0)
        session.add_utterance(np.ones(10, dtype=np.float32))
        session.mark_released()
        session.add_utterance(np.ones(10, dtype=np.float32))
        self.assertEqual(session.scheduled_before_release, 1)
        self.assertEqual(session.scheduled_batches, 2)


if __name__ == "__main__":
    unittest.main()
