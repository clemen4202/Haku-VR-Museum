import time, wave, sys
from faster_whisper import WhisperModel
w=wave.open("_bench_tts_out.wav"); dur=w.getnframes()/w.getframerate(); w.close()
print(f"test clip: {dur:.2f}s of real speech (Piper-generated)", flush=True)

def run(dev, ct, size):
    tag=f"{size}/{dev}/{ct}"
    try:
        t0=time.perf_counter()
        m=WhisperModel(size, device=dev, compute_type=ct)
        load=time.perf_counter()-t0
        segs,_=m.transcribe("_bench_tts_out.wav", beam_size=1); list(segs)   # warm
        t0=time.perf_counter()
        segs,_=m.transcribe("_bench_tts_out.wav", beam_size=1)
        txt=" ".join(s.text for s in segs).strip()
        el=time.perf_counter()-t0
        print(f"  {tag:26s} load {load:5.2f}s | transcribe {el:5.2f}s ({dur/el:4.1f}x realtime)", flush=True)
        print(f"  {'':26s} -> {txt[:110]!r}", flush=True)
    except Exception as e:
        print(f"  {tag:26s} FAILED: {type(e).__name__}: {str(e)[:160]}", flush=True)

run("cpu","int8","base.en")
run("cpu","int8","small.en")
run("cuda","float16","base.en")
