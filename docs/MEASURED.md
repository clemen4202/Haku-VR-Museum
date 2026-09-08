# Measured performance — Week 4 baseline

Every number here was measured on the team's dev laptop, not estimated. Re-run the scripts in
`ai-service/bench/` to reproduce. Quote these in the report rather than the pitch-deck estimates.

**Test machine (the designated AI server):** ASUS ROG Zephyrus M16 · Intel i9-12900H ·
32 GB RAM · NVIDIA RTX 3070 Ti Laptop, 8 GB VRAM · Windows 11 · LAN address `192.168.0.90`

---

## 1. Language model — Ollama + `qwen3:8b` (Q4_K_M, 5.2 GB)

| Condition | Time to first token | Total | Throughput |
|---|---|---|---|
| Warm, `think: false` | **0.57 s** | 1.21 s | ~35 tok/s |
| Warm, `think: true` | never emitted | 4.80 s | 148 reasoning tokens wasted |
| **Cold** (model not resident) | **50 s** | 64 s | — |

Two findings that became hard configuration rules:

**Reasoning mode must be disabled.** Qwen3 is a hybrid reasoning model. With `think: true` it spent
148 tokens on internal reasoning and the visible answer had still not begun at 4.8 s. Every request
sends `"think": false`.

**The model must be pinned in VRAM.** Ollama evicts after 5 minutes idle by default. The reload cost
is 50 seconds — long enough to destroy a live demo. Every request sends `"keep_alive": -1`, and the
service warms the model at startup.

---

## 2. Speech to text — faster-whisper

Test clip: 5.76 s of real speech. Chosen over `whisper.cpp` because it is pip-installable on
Windows with no C++ toolchain and no ffmpeg dependency.

| Model | Device | Time | Realtime factor | Verdict |
|---|---|---|---|---|
| `base.en` | CPU / int8 | **0.51 s** | 11× | **Chosen** |
| `small.en` | CPU / int8 | 2.62 s | 2.2× | Too slow |
| `base.en` | CUDA / fp16 | — | — | Fails, see below |

Transcription of the test clip was verbatim-accurate at `base.en`.

**GPU is currently broken and it is worth fixing.** CUDA fails with
`Library cublas64_12.dll is not found or cannot be loaded`. The fix is:

```
pip install nvidia-cublas-cu12 "nvidia-cudnn-cu12==9.*"
```

Running STT on CPU is a *deliberate* choice for now — it leaves all 8 GB of VRAM to the language
model. But STT is the single largest cost in the pipeline, so moving it to GPU is the highest-value
optimisation available (see §5).

---

## 3. Text to speech — Piper (`en_US-lessac-medium`)

| Operation | Time |
|---|---|
| Voice model load (once, at startup) | 1.73 s |
| Synthesise 5.7 s of speech | 0.19–0.27 s (~30× realtime) |
| **Synthesise one sentence** | **0.10 s** |

Piper is not the bottleneck and never will be. The 0.10 s single-sentence figure is what makes the
streaming optimisation in §5 viable.

> **Licensing note for the report:** the active Piper repository is
> [OHF-Voice/piper1-gpl](https://github.com/OHF-Voice/piper1-gpl) and is **GPL-3.0**. The older
> `rhasspy/piper` is archived and was MIT. `pip install piper-tts` delivers the GPL-3.0 version.
> This belongs in the third-party components table.

---

## 4. Full round trip — `POST /converse`

Audio in, audio out, measured warm with a 1.0 s spoken question. Non-streaming implementation.

| Run | STT | LLM | TTS | **Total** |
|---|---|---|---|---|
| 1 | 623 ms | 996 ms | 330 ms | 1949 ms |
| 2 | 567 ms | 744 ms | 313 ms | **1624 ms** |
| 3 | 580 ms | 774 ms | 385 ms | 1739 ms |

**Median ≈ 1.74 s.**

---

## 5. Against the target — the honest position

The pitch deck promised **under 1.5 seconds to the first spoken word**. We are not there yet.

| Configuration | Expected | Status |
|---|---|---|
| Current, non-streaming | 1.6 – 1.9 s | **Measured** |
| + stream LLM, fire Piper on first sentence | ~1.3 – 1.4 s | Not yet built (M3) |
| + GPU whisper via cuBLAS/cuDNN fix | ~1.0 s | Not yet built (M3) |

Both remaining optimisations are well understood and neither is speculative:

1. **Stream the LLM.** The model reaches its first token in 0.57 s but we currently wait for the
   entire answer (~0.8 s) before starting speech. Sending the first complete sentence to Piper as
   soon as it exists saves roughly 400 ms, since Piper only needs 0.10 s for that sentence.
2. **Move STT to the GPU.** At ~580 ms, speech-to-text is the largest single cost. GPU inference
   should cut it to well under 200 ms.

**Owner:** Ameen, during M3 (Weeks 6–8).

---

## 6. Grounding quality — a real defect found

The service answers from the curated exhibit record and refuses correctly when the record is silent:

> **Q:** "How much did this mirror cost when it was made, and who was the emperor at the time?"
> **A:** "The record doesn't say. We don't actually know the cost or which emperor ruled at the
> time. However, we do know this mirror was made in the Tang Dynasty, and such mirrors were often
> given as imperial gifts."

That is exactly the behaviour the pitch claims. **However**, grounding degrades under register
change. Asked to explain the decoration "like I am nine years old", the model added detail that is
not in the record — that the grapes are *colourful* and the lions *big and strong*. Neither claim
appears in the source data.

This is minor as a fact but important as a principle: the "cite or decline" guarantee is not yet
airtight. Tightening it is the substance of **M4 (Weeks 8–10, Ameen + Mahir)**, and the 20-question
grounding test named in the milestone plan is how we will prove it.

---

## Reproducing these numbers

```powershell
cd haku\ai-service
.\.venv\Scripts\Activate.ps1
python bench\bench_ollama.py    # section 1
python bench\bench_tts.py       # section 3
python bench\bench_stt.py       # section 2
```

For §4, start the service and run the `curl.exe` commands in [SETUP.md](SETUP.md).
