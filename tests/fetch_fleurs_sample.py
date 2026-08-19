"""Fetch a small, reproducible public FLEURS ASR sample without a dataset SDK."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import requests

_ROWS_API = "https://datasets-server.huggingface.co/rows"
_LANGUAGE_CODES = {"sl_si": "sl", "en_us": "en"}


def fetch_rows(config: str, split: str, count: int, session: requests.Session) -> list[dict]:
    response = session.get(
        _ROWS_API,
        params={
            "dataset": "google/fleurs",
            "config": config,
            "split": split,
            "offset": 0,
            "length": count,
        },
        timeout=(5, 30),
    )
    response.raise_for_status()
    payload = response.json()
    rows = payload.get("rows", []) if isinstance(payload, dict) else []
    if not isinstance(rows, list) or len(rows) < count:
        raise RuntimeError(f"FLEURS returned only {len(rows)} {config}/{split} rows")
    return rows[:count]


def audio_url(row: dict) -> str:
    value = row.get("row", {}).get("audio")
    if isinstance(value, list) and value and isinstance(value[0], dict):
        value = value[0].get("src")
    elif isinstance(value, dict):
        value = value.get("src")
    if not isinstance(value, str) or not value.startswith("https://datasets-server.huggingface.co/"):
        raise RuntimeError("FLEURS row did not expose an approved audio URL")
    return value


def build_sample(output: Path, languages: list[str], split: str, count: int) -> Path:
    output.mkdir(parents=True, exist_ok=True)
    manifest: list[dict[str, str]] = []
    with requests.Session() as session:
        session.headers["User-Agent"] = "VoicePrompt accuracy benchmark"
        for config in languages:
            if config not in _LANGUAGE_CODES:
                raise ValueError(f"unsupported FLEURS language: {config}")
            for index, value in enumerate(fetch_rows(config, split, count, session)):
                row = value.get("row", {})
                reference = row.get("transcription")
                if not isinstance(reference, str) or not reference.strip():
                    raise RuntimeError("FLEURS row has no reference transcript")
                relative = Path("audio") / f"{config}-{split}-{index:03}.wav"
                destination = output / relative
                if not destination.exists():
                    response = session.get(audio_url(value), timeout=(5, 60))
                    response.raise_for_status()
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    destination.write_bytes(response.content)
                manifest.append({
                    "audio": relative.as_posix(),
                    "reference": reference,
                    "expected_language": _LANGUAGE_CODES[config],
                })
    path = output / "manifest.json"
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    (output / "SOURCE.txt").write_text(
        "Google FLEURS via Hugging Face dataset server\n"
        "Dataset: https://huggingface.co/datasets/google/fleurs\n"
        "License: CC-BY-4.0\n",
        encoding="utf-8",
    )
    return path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--languages", nargs="+", default=["sl_si", "en_us"])
    parser.add_argument("--split", default="validation", choices=["train", "validation", "test"])
    parser.add_argument("--samples-per-language", type=int, default=10)
    args = parser.parse_args()
    if not 1 <= args.samples_per_language <= 100:
        parser.error("--samples-per-language must be 1-100")
    manifest = build_sample(args.output.resolve(), args.languages, args.split, args.samples_per_language)
    print(json.dumps({"ok": True, "manifest": str(manifest), "samples": len(args.languages) * args.samples_per_language}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
