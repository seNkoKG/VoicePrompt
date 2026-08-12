"""Run a local model benchmark with optional reference transcript scoring."""

from __future__ import annotations

import argparse
import json
import subprocess
import time
import wave
from pathlib import Path

from accuracy_metrics import summarize


def vram_mb() -> int:
    try:
        result = subprocess.run(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )
        return int(result.stdout.strip().splitlines()[0])
    except (IndexError, OSError, subprocess.SubprocessError, ValueError):
        return -1


def audio_duration(path: Path) -> float:
    with wave.open(str(path), "rb") as audio:
        return audio.getnframes() / audio.getframerate()


def load_cases(manifest: Path | None, paths: list[str]) -> list[dict[str, str]]:
    if manifest is None:
        return [{"audio": str(Path(path).resolve())} for path in paths]
    payload = json.loads(manifest.read_text(encoding="utf-8"))
    if not isinstance(payload, list) or not payload:
        raise ValueError("Accuracy manifest must contain a non-empty JSON array.")
    cases = []
    for value in payload:
        if not isinstance(value, dict) or not isinstance(value.get("audio"), str):
            raise ValueError("Each accuracy case requires an audio path.")
        case = {key: str(value.get(key, "")) for key in ("audio", "reference", "expected_language")}
        case["audio"] = str((manifest.parent / case["audio"]).resolve())
        cases.append(case)
    return cases


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("model")
    parser.add_argument("compute_type")
    parser.add_argument("paths", nargs="*")
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--language", default="sl", help="Whisper language code or 'auto'")
    args = parser.parse_args()
    cases = load_cases(args.manifest, args.paths)
    if not cases:
        parser.error("provide at least one WAV file or --manifest")

    baseline = vram_mb()
    started = time.perf_counter()
    from faster_whisper import WhisperModel

    model = WhisperModel(args.model, device="cuda", compute_type=args.compute_type)
    loaded_vram = vram_mb()
    print(json.dumps({
        "event": "model_loaded",
        "seconds": round(time.perf_counter() - started, 3),
        "vram_mb": loaded_vram,
        "vram_delta_mb": loaded_vram - baseline if baseline >= 0 else -1,
    }), flush=True)

    records = []
    for case in cases:
        path = Path(case["audio"])
        started = time.perf_counter()
        segments, info = model.transcribe(
            str(path),
            language=None if args.language.casefold() == "auto" else args.language,
            beam_size=5,
            temperature=0.0,
            condition_on_previous_text=False,
        )
        transcript = " ".join(segment.text.strip() for segment in segments).strip()
        latency = time.perf_counter() - started
        record = {
            **case,
            "transcript": transcript,
            "detected_language": info.language,
            "latency_seconds": latency,
            "audio_seconds": audio_duration(path),
            "vram_mb": vram_mb(),
        }
        records.append(record)
        print(json.dumps({"event": "result", **record}, ensure_ascii=False), flush=True)

    print(json.dumps({"event": "summary", **summarize(records)}, ensure_ascii=False), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
