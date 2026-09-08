"""
Haku AI museum guide - local service.

One HTTP service on the dev laptop fronting all three models:
    speech  -> text    (faster-whisper)
    question -> answer (Ollama, grounded on a curated exhibit record)
    answer  -> speech  (Piper)

Nothing here calls the cloud. Run:  uvicorn main:app --host 0.0.0.0 --port 8000
"""
import io
import json
import time
import wave
import glob
import os
from contextlib import asynccontextmanager
from typing import List, Optional

import requests
from fastapi import FastAPI, UploadFile, File, Form, HTTPException
from fastapi.responses import Response
from pydantic import BaseModel

import config

_stt = None          # faster_whisper.WhisperModel
_tts = None          # piper.PiperVoice
_exhibits = {}       # id -> record dict


# --------------------------------------------------------------------------
# The system prompt. This is the single most important piece of the project:
# it is what stops the guide inventing provenance.
# --------------------------------------------------------------------------
SYSTEM_PROMPT = """You are the museum guide for an exhibit in a virtual museum.

You will be given ONE exhibit record. It is your ONLY source of fact.

Rules, in priority order:
1. Answer ONLY using the exhibit record below. Never add outside knowledge, never guess,
   never infer beyond what the record states.
2. If the record does not answer the question, say so plainly and briefly, for example
   "The record doesn't say" or "We don't actually know that one." Then offer something
   the record DOES cover.
3. Reply in 2 to 3 short spoken sentences. Your words are read aloud by a speech
   synthesiser, so write plain speech only: no markdown, no bullet points, no headings,
   no emoji, no special characters.
4. Speak warmly and directly to the visitor, as a knowledgeable curator standing beside
   them. Never mention "the record", "the data", or that you are an AI, except when you
   are declining because the information is missing.

EXHIBIT RECORD:
"""


def load_exhibits() -> dict:
    out = {}
    for path in glob.glob(os.path.join(config.EXHIBITS_DIR, "*.json")):
        with open(path, encoding="utf-8") as f:
            rec = json.load(f)
        out[rec["id"]] = rec
    return out


def build_prompt(record: dict) -> str:
    """Serialise the record for the model, dropping fields it must not read aloud."""
    visible = {k: v for k, v in record.items() if k != "sources"}
    return SYSTEM_PROMPT + json.dumps(visible, ensure_ascii=False, indent=2)


def ask_llm(record: dict, question: str, history: Optional[List[dict]] = None) -> dict:
    messages = [{"role": "system", "content": build_prompt(record)}]
    for turn in (history or [])[-6:]:          # cap context: latency and drift
        messages.append(turn)
    messages.append({"role": "user", "content": question})

    body = {
        "model": config.LLM_MODEL,
        "messages": messages,
        "stream": False,
        "think": config.LLM_THINK,
        "keep_alive": config.LLM_KEEP_ALIVE,
        "options": {
            "temperature": config.LLM_TEMPERATURE,
            "num_predict": config.LLM_MAX_TOKENS,
        },
    }
    t0 = time.perf_counter()
    r = requests.post(config.OLLAMA_HOST + "/api/chat", json=body, timeout=120)
    r.raise_for_status()
    data = r.json()
    return {
        "answer": data["message"]["content"].strip(),
        "ms": int((time.perf_counter() - t0) * 1000),
    }


def synth_wav(text: str) -> bytes:
    buf = io.BytesIO()
    with wave.open(buf, "wb") as wf:
        _tts.synthesize_wav(text, wf)
    return buf.getvalue()


@asynccontextmanager
async def lifespan(app: FastAPI):
    global _stt, _tts, _exhibits
    print("[haku] loading exhibits...", flush=True)
    _exhibits = load_exhibits()
    print("[haku] " + str(len(_exhibits)) + " exhibit(s): " + str(list(_exhibits)), flush=True)

    print("[haku] loading STT " + config.STT_MODEL, flush=True)
    from faster_whisper import WhisperModel
    _stt = WhisperModel(config.STT_MODEL, device=config.STT_DEVICE,
                        compute_type=config.STT_COMPUTE)

    if os.path.exists(config.TTS_VOICE):
        print("[haku] loading TTS " + config.TTS_VOICE, flush=True)
        from piper import PiperVoice
        _tts = PiperVoice.load(config.TTS_VOICE)
    else:
        print("[haku] WARNING: no voice at " + config.TTS_VOICE + " - /converse will 503", flush=True)

    # Pin the LLM in VRAM now, so the first visitor doesn't wait 50 seconds.
    try:
        requests.post(config.OLLAMA_HOST + "/api/chat", json={
            "model": config.LLM_MODEL,
            "messages": [{"role": "user", "content": "hi"}],
            "stream": False, "think": False,
            "keep_alive": config.LLM_KEEP_ALIVE,
            "options": {"num_predict": 1},
        }, timeout=180)
        print("[haku] LLM warm and pinned", flush=True)
    except Exception as e:
        print("[haku] WARNING: could not warm LLM: " + str(e), flush=True)
    print("[haku] ready", flush=True)
    yield


app = FastAPI(title="Haku AI Guide", version="0.1.0", lifespan=lifespan)


@app.get("/health")
def health():
    ollama_ok, models = False, []
    try:
        r = requests.get(config.OLLAMA_HOST + "/api/tags", timeout=5)
        ollama_ok = r.ok
        models = [m["name"] for m in r.json().get("models", [])]
    except Exception:
        pass
    return {
        "ok": ollama_ok and _stt is not None and _tts is not None,
        "ollama": ollama_ok,
        "llm_model": config.LLM_MODEL,
        "llm_available": config.LLM_MODEL in models,
        "models_pulled": models,
        "stt_loaded": _stt is not None,
        "tts_loaded": _tts is not None,
        "exhibits": list(_exhibits),
    }


@app.get("/exhibits")
def exhibits():
    return [{"id": r["id"], "title": r["title"], "intro": r.get("intro", "")}
            for r in _exhibits.values()]


class AskRequest(BaseModel):
    exhibit_id: str
    question: str
    history: Optional[List[dict]] = None


@app.post("/ask")
def ask(req: AskRequest):
    """Text in, text out. Lets Unity be tested with zero audio plumbing."""
    rec = _exhibits.get(req.exhibit_id)
    if not rec:
        raise HTTPException(404, "unknown exhibit " + req.exhibit_id)
    out = ask_llm(rec, req.question, req.history)
    return {"exhibit_id": req.exhibit_id, "question": req.question, **out}


@app.post("/converse")
async def converse(
    audio: UploadFile = File(...),
    exhibit_id: str = Form(...),
    held: bool = Form(False),
    distance_m: float = Form(0.0),
):
    """Audio in, audio out. The full loop Unity calls on device."""
    rec = _exhibits.get(exhibit_id)
    if not rec:
        raise HTTPException(404, "unknown exhibit " + exhibit_id)
    if _tts is None:
        raise HTTPException(503, "no TTS voice loaded")

    raw = await audio.read()
    t0 = time.perf_counter()
    tmp = "_incoming.wav"
    with open(tmp, "wb") as f:
        f.write(raw)
    segs, _ = _stt.transcribe(tmp, beam_size=1)
    question = " ".join(s.text for s in segs).strip()
    stt_ms = int((time.perf_counter() - t0) * 1000)

    if not question:
        raise HTTPException(422, "no speech detected")

    # Spatial context becomes part of the question. This is what makes the project XR
    # rather than a chatbot: "this" resolves because we told the model what "this" is.
    where = "holding" if held else "standing near"
    ctx = "(The visitor is " + where + " the " + rec["title"] + ".) "
    llm = ask_llm(rec, ctx + question)

    t1 = time.perf_counter()
    wav = synth_wav(llm["answer"])
    tts_ms = int((time.perf_counter() - t1) * 1000)

    return Response(
        content=wav,
        media_type="audio/wav",
        headers={
            "X-Haku-Question": question[:400],
            "X-Haku-Answer": llm["answer"][:900],
            "X-Haku-Ms-Stt": str(stt_ms),
            "X-Haku-Ms-Llm": str(llm["ms"]),
            "X-Haku-Ms-Tts": str(tts_ms),
            "X-Haku-Ms-Total": str(stt_ms + llm["ms"] + tts_ms),
            "Access-Control-Expose-Headers": "*",
        },
    )


@app.post("/say")
def say(text: str = Form(...)):
    """TTS only - handy for pre-rendering the exhibit intro lines."""
    if _tts is None:
        raise HTTPException(503, "no TTS voice loaded")
    return Response(content=synth_wav(text), media_type="audio/wav")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host=config.HOST, port=config.PORT)
