"""Run VoicePrompt's local recognition path with optional reference scoring."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time
import wave
from pathlib import Path
from types import SimpleNamespace

from accuracy_metrics import summarize


def configure_cuda_paths() -> None:
    """Match the installed daemon's pip-provided CUDA DLL discovery."""
    if sys.platform != "win32":
        return
    site_packages = Path(sys.executable).resolve().parent.parent / "Lib" / "site-packages"
    for relative in (
        "nvidia/cublas/bin",
        "nvidia/cudnn/bin",
        "nvidia/cuda_runtime/bin",
        "nvidia/cuda_nvrtc/bin",
    ):
        path = site_packages / relative
        if path.exists():
            os.environ["PATH"] = str(path) + os.pathsep + os.environ.get("PATH", "")


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
    try:
        with wave.open(str(path), "rb") as audio:
            return audio.getnframes() / audio.getframerate()
    except wave.Error:
        # FLEURS serves valid IEEE-float WAV files, which stdlib wave does not
        # decode. PyAV is already a faster-whisper runtime dependency.
        import av

        with av.open(str(path)) as container:
            stream = container.streams.audio[0]
            if stream.duration is not None:
                return float(stream.duration * stream.time_base)
            return sum(frame.samples / frame.sample_rate for frame in container.decode(stream))


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
    parser.add_argument(
        "--pipeline",
        choices=("voiceprompt", "raw"),
        default="voiceprompt",
        help="Exercise VoicePrompt routing by default; raw is a model-only diagnostic.",
    )
    args = parser.parse_args()
    cases = load_cases(args.manifest, args.paths)
    if not cases:
        parser.error("provide at least one WAV file or --manifest")

    configure_cuda_paths()
    baseline = vram_mb()
    started = time.perf_counter()
    if args.pipeline == "voiceprompt":
        from faster_whisper.audio import decode_audio
        from whisper_dictation.engine.local import LocalEngine

        engine = LocalEngine(
            SimpleNamespace(
                model=args.model,
                language="" if args.language.casefold() == "auto" else args.language,
                prompt="",
                temperature=0.0,
                hotwords="",
            ),
            SimpleNamespace(device="cuda", compute_type=args.compute_type),
            SimpleNamespace(
                threshold=0.6,
                silence_ms=200,
                min_speech_ms=250,
                max_speech_s=180.0,
            ),
        )
        engine.prepare()
        model = None
    else:
        from faster_whisper import WhisperModel

        model = WhisperModel(args.model, device="cuda", compute_type=args.compute_type)
        engine = None
    loaded_vram = vram_mb()
    print(json.dumps({
        "event": "model_loaded",
        "seconds": round(time.perf_counter() - started, 3),
        "vram_mb": loaded_vram,
        "vram_delta_mb": loaded_vram - baseline if baseline >= 0 else -1,
        "pipeline": args.pipeline,
    }), flush=True)

    records = []
    for case in cases:
        path = Path(case["audio"])
        started = time.perf_counter()
        if engine is not None:
            transcript = engine.transcribe(decode_audio(str(path)), sample_rate=16000)
            detected_language = engine.last_language
        else:
            segments, info = model.transcribe(
                str(path),
                language=None if args.language.casefold() == "auto" else args.language,
                beam_size=5,
                temperature=0.0,
                condition_on_previous_text=False,
            )
            transcript = " ".join(segment.text.strip() for segment in segments).strip()
            detected_language = info.language
        latency = time.perf_counter() - started
        record = {
            **case,
            "transcript": transcript,
            "detected_language": detected_language,
            "latency_seconds": latency,
            "audio_seconds": audio_duration(path),
            "vram_mb": vram_mb(),
        }
        records.append(record)
        print(json.dumps({"event": "result", **record}), flush=True)

    languages = sorted({record.get("expected_language", "") for record in records} - {""})
    by_language = {
        language: summarize([
            record for record in records if record.get("expected_language") == language
        ])
        for language in languages
    }
    print(json.dumps({
        "event": "summary",
        "pipeline": args.pipeline,
        **summarize(records),
        "by_language": by_language,
    }), flush=True)
    if engine is not None:
        engine.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
