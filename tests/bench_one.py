import sys, time, subprocess, wave

def vram_mb():
    try:
        out = subprocess.run(
            ["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
            capture_output=True, text=True, timeout=30)
        return int(out.stdout.strip().split("\n")[0])
    except Exception:
        return -1

def main():
    model_name = sys.argv[1]
    compute_type = sys.argv[2]
    paths = sys.argv[3:]
    base = vram_mb()
    t0 = time.time()
    from faster_whisper import WhisperModel
    model = WhisperModel(model_name, device="cuda", compute_type=compute_type)
    print(f"MODEL_LOAD {time.time()-t0:.2f}s | VRAM {base} -> {vram_mb()} MiB (delta {vram_mb()-base})", flush=True)
    for path in paths:
        t1 = time.time()
        segments, info = model.transcribe(path, language="sl", beam_size=5,
                                          temperature=0.0, condition_on_previous_text=False)
        text = "".join(s.text for s in segments).strip()
        dt = time.time() - t1
        with wave.open(path, "rb") as w:
            dur = w.getnframes() / w.getframerate()
        print(f"RESULT {round(dt,2)}s x{round(dur/dt,1)} | VRAM {vram_mb()} MiB | {text}", flush=True)

main()