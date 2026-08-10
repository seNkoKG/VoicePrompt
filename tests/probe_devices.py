import sounddevice as sd

candidates = [None, "Stereo Mix", "Microphone (HyperX Quadcast)", "Microphone (Realtek(R) Audio)", "Microphone (Arctis Nova Pro Wireless)"]
for dev in candidates:
    try:
        with sd.InputStream(samplerate=16000, channels=1, dtype="float32", device=dev, blocksize=512) as s:
            print(f"OK   device={dev!r}")
    except Exception as e:
        print(f"FAIL device={dev!r}: {type(e).__name__}: {e}")
for dev in ["Stereo Mix", "Microphone (HyperX Quadcast)"]:
    try:
        with sd.InputStream(samplerate=44100, channels=1, dtype="float32", device=dev, blocksize=1480) as s:
            print(f"OK(44.1k) device={dev!r}")
    except Exception as e:
        print(f"FAIL device={dev!r} at 44.1k: {type(e).__name__}: {e}")