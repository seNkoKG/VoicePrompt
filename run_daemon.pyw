"""Silent launcher for the voice-typing daemon (run with pythonw.exe).

- Adds file logging to ~/.voice-typing/daemon.log
- Sets CUDA 12 DLL path (needed with newer NVIDIA drivers / CUDA 13+)
- Starts faster-whisper-dictation daemon (config lives in
  %LOCALAPPDATA%\faster-whisper-dictation\faster-whisper-dictation\config.toml)
"""
import logging
import os
import sys
from pathlib import Path

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

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
    handlers=[logging.FileHandler(BASE / "daemon.log", encoding="utf-8")],
)

from whisper_dictation.cli import main  # noqa: E402

sys.argv = [sys.argv[0], "start"]
sys.exit(main())