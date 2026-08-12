r"""Silent launcher for the voice-typing daemon (run with pythonw.exe).

- Adds file logging to ~/.voice-typing/daemon.log
- Sets CUDA 12 DLL path (needed with newer NVIDIA drivers / CUDA 13+)
- Starts faster-whisper-dictation daemon (config lives in
  %LOCALAPPDATA%\faster-whisper-dictation\faster-whisper-dictation\config.toml)
"""
import logging
import os
import sys
from pathlib import Path

try:
    import tomllib
except ModuleNotFoundError:  # Python 3.10
    import tomli as tomllib

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
    handlers=[logging.FileHandler(BASE / "daemon.log", encoding="utf-8")],
)


def buffered_transcription_enabled() -> bool:
    """Enable lossless pre-transcription only for the local engine."""
    try:
        from platformdirs import user_config_dir

        config_path = Path(user_config_dir("faster-whisper-dictation")) / "config.toml"
        with config_path.open("rb") as handle:
            config = tomllib.load(handle)
        enabled = config.get("voiceprompt", {}).get("buffered_transcription", True)
        engine = config.get("engine", {}).get("type", "server")
        return enabled is True and engine == "local"
    except Exception:
        logging.getLogger(__name__).warning(
            "Could not read buffered transcription setting; using accurate batch mode",
            exc_info=True,
        )
        return False


def transcript_output_mode() -> str:
    """Read the opt-in delivery route once; runtime restart applies changes."""
    try:
        from platformdirs import user_config_dir

        config_path = Path(user_config_dir("faster-whisper-dictation")) / "config.toml"
        with config_path.open("rb") as handle:
            config = tomllib.load(handle)
        value = config.get("voiceprompt", {}).get("output_mode", "paste")
        return "clipboard" if isinstance(value, str) and value.strip().lower() == "clipboard" else "paste"
    except Exception:
        logging.getLogger(__name__).warning(
            "Could not read transcript output mode; using automatic paste",
            exc_info=True,
        )
        return "paste"

from whisper_dictation.cli import main  # noqa: E402

sys.argv = [sys.argv[0], "start"]
os.environ["VOICEPROMPT_OUTPUT_MODE"] = transcript_output_mode()
if os.environ["VOICEPROMPT_OUTPUT_MODE"] == "clipboard":
    logging.getLogger(__name__).info("Copy-only transcript output enabled")
if buffered_transcription_enabled():
    os.environ["VOICEPROMPT_BUFFERED_STREAMING"] = "1"
    sys.argv.append("--streaming")
    logging.getLogger(__name__).info(
        "Lossless buffered transcription enabled for long recordings"
    )
sys.exit(main())
