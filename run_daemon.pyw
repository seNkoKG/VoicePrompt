r"""Silent launcher for the voice-typing daemon (run with pythonw.exe).

- Adds file logging to ~/.voice-typing/daemon.log
- Sets CUDA 12 DLL path (needed with newer NVIDIA drivers / CUDA 13+)
- Starts faster-whisper-dictation daemon (config lives in
  %LOCALAPPDATA%\faster-whisper-dictation\faster-whisper-dictation\config.toml)
"""
import logging
import os
import sys
from logging.handlers import RotatingFileHandler
from pathlib import Path

import tomllib

BASE = Path.home() / ".voice-typing"
BASE.mkdir(parents=True, exist_ok=True)

try:
    sitepkg = Path(sys.executable).resolve().parent.parent / "Lib" / "site-packages"
    paths = [
        sitepkg / "nvidia" / "cublas" / "bin",
        sitepkg / "nvidia" / "cudnn" / "bin",
        sitepkg / "nvidia" / "cuda_runtime" / "bin",
        sitepkg / "nvidia" / "cuda_nvrtc" / "bin",
    ]
    for p in paths:
        if p.exists():
            os.environ["PATH"] = str(p) + os.pathsep + os.environ.get("PATH", "")
except Exception:
    pass

os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")
os.environ.setdefault("DICTATION_HOLD_DEBOUNCE_MS", "60")
# Keep the transcript on the clipboard long enough for a temporarily busy
# target window to consume Ctrl+V before the user's clipboard is restored.
# Responsive apps paste immediately; this wait happens after the key event.
os.environ.setdefault("DICTATION_PASTE_DELAY", "0.35")

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
    handlers=[
        RotatingFileHandler(
            BASE / "daemon.log",
            maxBytes=2 * 1024 * 1024,
            backupCount=3,
            encoding="utf-8",
        )
    ],
)


def load_runtime_config() -> dict:
    """Read the runtime configuration once per daemon start."""
    try:
        from platformdirs import user_config_dir

        config_path = Path(user_config_dir("faster-whisper-dictation")) / "config.toml"
        with config_path.open("rb") as handle:
            value = tomllib.load(handle)
        return value if isinstance(value, dict) else {}
    except (OSError, TypeError, ValueError, tomllib.TOMLDecodeError):
        logging.getLogger(__name__).warning(
            "Could not read runtime settings; using safe defaults",
            exc_info=True,
        )
        return {}


RUNTIME_CONFIG = load_runtime_config()


def config_section(name: str) -> dict:
    value = RUNTIME_CONFIG.get(name, {})
    return value if isinstance(value, dict) else {}


def buffered_transcription_enabled() -> bool:
    """Enable lossless pre-transcription only for the local engine."""
    enabled = config_section("voiceprompt").get("buffered_transcription", True)
    engine = config_section("engine").get("type", "server")
    return enabled is True and engine == "local"


def transcript_output_mode() -> str:
    """Read the opt-in delivery route once; runtime restart applies changes."""
    value = config_section("voiceprompt").get("output_mode", "paste")
    return "clipboard" if isinstance(value, str) and value.strip().lower() == "clipboard" else "paste"


def voice_commands_enabled() -> bool:
    """Read the opt-in exact-command flag once; runtime restart applies it."""
    return config_section("voiceprompt").get("voice_commands") is True

from whisper_dictation.cli import main  # noqa: E402

sys.argv = [sys.argv[0], "start"]
os.environ["VOICEPROMPT_OUTPUT_MODE"] = transcript_output_mode()
os.environ["VOICEPROMPT_VOICE_COMMANDS"] = "1" if voice_commands_enabled() else "0"
if os.environ["VOICEPROMPT_OUTPUT_MODE"] == "clipboard":
    logging.getLogger(__name__).info("Copy-only transcript output enabled")
if os.environ["VOICEPROMPT_VOICE_COMMANDS"] == "1":
    logging.getLogger(__name__).info("Exact voice commands enabled")
if buffered_transcription_enabled():
    os.environ["VOICEPROMPT_BUFFERED_STREAMING"] = "1"
    sys.argv.append("--streaming")
    logging.getLogger(__name__).info(
        "Lossless buffered transcription enabled for long recordings"
    )
try:
    exit_code = main()
except Exception:
    logging.getLogger(__name__).critical("Runtime terminated unexpectedly", exc_info=True)
    raise
sys.exit(exit_code)
