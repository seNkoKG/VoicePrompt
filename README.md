<p align="center">
  <img src="assets/logo.png" width="160" alt="Voice Typing logo" />
</p>

<h1 align="center">🎤 Voice Typing</h1>

<p align="center">
  <strong>Local, private, GPU-accelerated voice-to-text dictation for Windows</strong><br>
  Speak Slovenian or English — text lands in whatever app you're typing in.<br>
  No cloud. No subscriptions. No audio leaves your machine.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/engine-faster--whisper--large--v3-8b5cf6" alt="engine" />
  <img src="https://img.shields.io/badge/acceleration-CUDA%20float16-22c55e" alt="cuda" />
  <img src="https://img.shields.io/badge/languages-sl%20%2F%20en-6366f1" alt="langs" />
  <img src="https://img.shields.io/badge/platform-Windows%2011-0ea5e9" alt="platform" />
</p>

---

## ✅ What it does

Hold **`F1`**, talk, release. ~0.5–1 second later the transcription is typed into the focused window — Notepad, Discord, browser chat, IDE, game chat. Works with **Slovenian and English**, and it auto-detects the language per utterance.

Measured on an RTX 5080:

| Model | VRAM while idle | Load time | Per 8s utterance | Slovenian quality |
|---|---|---|---|---|
| `large-v3-turbo` (fast) | ~2.2 GB | ~3 s | ~0.35 s | okay |
| **`large-v3` (accuracy)** | ~4 GB | ~5 s | ~0.6 s | **best** |

> Both run entirely locally on your GPU through **faster-whisper** (CTranslate2). The driving app is the open-source
> [`faster-whisper-dictation`](https://github.com/bhargavchippada/faster-whisper-dictation) daemon.

## 🏗️ Architecture

```
        ┌────────────┐       ┌────────────────┐       ┌──────────────────┐
 F1    │  hotkey    │       │  Windows       │       │  faster-whisper  │
 hold─▶ │  listener  │─────▶ │  audio capture │─────▶ │  large-v3 (CUDA) │
        │  (pynput)  │       │  (16 kHz mono) │       │  float16         │
        └────────────┘       └────────────────┘       └────────┬─────────┘
                                                                │ text
        ┌────────────┐       ┌────────────────┐       ┌────────▼─────────┐
 chat / │  focused   │◀───── │  clipboard     │◀────── │  language auto-  │
 IDE /  │  window    │ Ctrl+V│  paste         │        │  detect + prompt │
 Notepad└────────────┘       └────────────────┘       └──────────────────┘
```

- **Hold mode**: recording only while you hold the key → no accidental captures.
- **VAD filtering**: silence/speech detection chops dead air before decoding.
- **Prompt injection**: a Slovenian + English programming vocabulary biases decoding toward code terms (`pull request`, `null`, `async`, …).
- **Daemonized**: runs headless via `pythonw`, survives reboot via the Startup shortcut.

## 🚀 Getting started

```powershell
# 1. Create the venv and install the engine (requires Python 3.10+ and an NVIDIA GPU)
py -m venv "$env:USERPROFILE\.voice-typing\venv"
& "$env:USERPROFILE\.voice-typing\venv\Scripts\pip" install faster-whisper-dictation[local-gpu]

# 2. CUDA 12 DLLs (needed on driver 600+ / CUDA 13 UMD systems without a CUDA toolkit)
& "$env:USERPROFILE\.voice-typing\venv\Scripts\pip" install nvidia-cublas-cu12 nvidia-cudnn-cu12 nvidia-cuda-runtime-cu12 nvidia-cuda-nvrtc-cu12

# 3. Apply the Windows fixes (see "Patches")
powershell -ExecutionPolicy Bypass -File scripts\apply_patches.ps1

# 4. Build the tray UI (requires .NET SDK 8+; only once)
powershell -ExecutionPolicy Bypass -File scripts\build_ui.ps1

# 5. Start Voice Typing — it runs in the system tray and auto-starts the daemon
.\ui\publish\VoicePromptTray.exe --tray
```

First start downloads the model (~1.6 GB turbo / ~3.1 GB full) into `~/.cache/huggingface/hub` — one-time.

## 🖥️ Tray UI (`ui/VoicePromptTray`)

A dark-themed Windows tray app (C# / .NET 10 WinForms) that manages the whole setup:

- **System tray** — runs minimized next to the clock; double-click the icon (or the desktop **Voice Typing Settings** shortcut) to open settings. The tray menu starts/stops/restarts the daemon and quits the app.
- **Hotkey recorder** — click the box, press **one key (F1, Space, 7…)** or a **combo (Ctrl+Shift+F1, Alt+Space…)**, Enter confirms, Esc cancels. Supports `hold` (press & hold to talk) or `toggle` modes.
- **All settings** — language (auto / sl / en), decoding prompt, VAD threshold & timing, microphone (enumerated live) and sample rate, model (`large-v3` / `large-v3-turbo`), compute type, GPU/CPU, temperature, hotwords.
- **Save & Restart** writes the live config, keeps your comments, and restarts the daemon in one click.
- **Start with Windows** checkbox manages its own startup shortcut; the daemon auto-starts with the UI.

Rebuild after changes: `scripts\build_ui.ps1` (outputs `ui\publish\VoicePromptTray.exe`).

## ⚙️ Configuration

The tray UI edits the live config — `%LOCALAPPDATA%\faster-whisper-dictation\faster-whisper-dictation\config.toml`
(snapshot in this repo: [`config.toml`](config.toml)). Settings load at daemon start — the UI restarts it for you on Save.

| Key | Meaning | Example |
|---|---|---|
| `[hotkey] binding` | Global hotkey (single key or combo) | `"f1"`, `"ctrl+shift+f1"`, `"alt+space"` |
| `[hotkey] mode` | `hold` = press & hold; `toggle` = press once | `"hold"` |
| `[server] model` | Whisper model | `"Systran/faster-whisper-large-v3"` |
| `[server] language` | `""` = auto-detect per utterance | `"sl"`, `"en"` to pin |
| `[server] prompt` | Decoding context / vocabulary bias | mixed SI/EN code terms |
| `[vad] threshold` | Speech sensitivity (0–1) | `0.6` |
| `[engine] compute_type` | `float16` GPU / `int8` CPU | `"float16"` |
| `[audio] device` | `""` = Windows default input (HyperX Quadcast) | `""` |

## 🔧 Patches (required on Windows)

Five small fixes ship in this repo — apply them **after every reinstall/upgrade**:

1. **`cli.py`** — `_pid_alive()` used `os.kill(pid, 0)`, which raises `OSError` (WinError 87) on Windows and broke `status` / `stop`. Now uses `OpenProcess` via ctypes.
2. **`typer.py`** — clipboard calls had no `argtypes`/`restype`, so 64-bit HANDLEs were truncated to 32 bits → access violations when pasting. All Win32 calls now declare their signatures.
3. **`engine/local.py`** — `language = ""` / `"auto"` crashed with `ValueError: 'auto' is not a valid language code`; now maps to `None` = proper per-utterance auto-detect.
4. **`engine/local.py`** — logs detected language + confidence per utterance (auto-detect diagnostics).
5. **`config.py`** — hotkey validation accepts single keys (letters, digits, `f1`–`f24`, `space`, `enter`, …) and combos, so the UI's recorder can save them.

Run: `powershell -ExecutionPolicy Bypass -File scripts\apply_patches.ps1`

## 🧪 Testing

The E2E harness simulates what a human does (no spoken voice needed):

- `tests/e2e_test.ps1` — opens a live text target window, presses the hotkey via `keybd_event`, plays an audio file through the speakers/microphone path, and proves the transcribed text lands there (timestamps the release→paste latency).
- `tests/bench_one.py` — model load time, VRAM delta, decode time per utterance.
- `tests/probe_devices.py` — enumerates PortAudio input devices.
- `ui/ConfigManager.Tests` — verifies the UI's comment-preserving config.toml editor (run: `dotnet run --project ui\ConfigManager.Tests`).

Verified end-to-end results (simulated): **hotkey → record → GPU transcribe → paste ≈ 0.8 s** after key release, with text matching the spoken source.

## 🩹 Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `Library cublas64_12.dll is not found` | CUDA 12 DLLs not on PATH → install the `nvidia-*cu12` pip packages (Step 2); `run_daemon.pyw` prepends their `bin` dirs automatically |
| Nothing typed but recording starts | Transcription crashed → read `%USERPROFILE%\.voice-typing\daemon.log`; check `language = ""` (not `"auto"`) and patches applied |
| Hotkey does nothing in a game | Another app grabbed the binding → change it in the tray UI (Hotkey card) or `config.toml` |
| Bad Slovenian accuracy | Switch to `large-v3` full model; prefer full sentences over 2–3 words; optionally pin `language = "sl"` |
| Mic not captured | Windows Settings → Privacy → Microphone → allow desktop apps (and make sure the Quadcast is the default input) |

## 🖥️ Lifecycle

- **Auto-start on login**: `shell:startup` shortcut → `VoicePromptTray.exe --tray` (tray UI; starts the daemon itself)
- Desktop shortcuts: **Voice Typing Settings** (tray UI), **Start Voice Typing** / **Stop Voice Typing** (daemon only)
- Logs: `%USERPROFILE%\.voice-typing\daemon.log`
- State: daemon PID/status via `faster-whisper-dictation.exe status`
- UI prefs: `%APPDATA%\VoicePrompt\prefs.json`

## 📜 Credits

- [faster-whisper-dictation](https://github.com/bhargavchippada/faster-whisper-dictation) — the dictation daemon (MIT)
- [faster-whisper](https://github.com/SYSTRAN/faster-whisper) — CTranslate2 whisper runtime (MIT)
- [OpenAI Whisper large-v3](https://github.com/openai/whisper) — the model (MIT)
- Logo/icon: generated with this repo's `scripts/make_icon.ps1`