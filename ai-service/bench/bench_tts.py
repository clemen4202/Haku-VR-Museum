import time, wave, sys
t0=time.perf_counter()
from piper import PiperVoice
imp=time.perf_counter()-t0
print(f"import piper: {imp:.2f}s", flush=True)
t0=time.perf_counter()
v=PiperVoice.load("voices/en_US-lessac-medium.onnx")
print(f"voice load: {time.perf_counter()-t0:.2f}s", flush=True)
SENT="This bronze mirror was used for personal grooming, and was often buried with the dead as a protective object."
for i in range(3):
    t0=time.perf_counter()
    with wave.open("_bench_tts_out.wav","wb") as wf:
        v.synthesize_wav(SENT, wf)
    el=time.perf_counter()-t0
    w=wave.open("_bench_tts_out.wav"); dur=w.getnframes()/w.getframerate(); sr=w.getframerate(); w.close()
    print(f"  run{i+1}: synth {el:.2f}s -> {dur:.2f}s audio @ {sr}Hz | {dur/el:.1f}x realtime", flush=True)
# first-sentence-only latency (what actually matters for streaming TTS)
t0=time.perf_counter()
with wave.open("_bench_tts_first.wav","wb") as wf:
    v.synthesize_wav("This bronze mirror was used for personal grooming.", wf)
print(f"  FIRST SENTENCE ONLY: {time.perf_counter()-t0:.2f}s", flush=True)
