"""Haku AI service configuration. Values here were MEASURED on the team's dev laptop."""
import os

# --- Ollama ---
OLLAMA_HOST = os.getenv("HAKU_OLLAMA", "http://127.0.0.1:11434")
LLM_MODEL = os.getenv("HAKU_MODEL", "qwen3:8b")
# qwen3 is a REASONING model. Measured: think=True -> 4.8s and 148 wasted tokens.
#                                      think=False -> 0.57s to first token. Never flip this on.
LLM_THINK = False
# Measured: a cold model load costs 50 SECONDS. -1 pins it in VRAM forever.
LLM_KEEP_ALIVE = -1
LLM_TEMPERATURE = 0.4
LLM_MAX_TOKENS = 140          # ~3 spoken sentences; keeps answers short and fast

# --- Speech to text (faster-whisper) ---
# Measured on this laptop: base.en/cpu/int8 = 0.51s for 5.8s of audio (11x realtime), accurate.
#                          small.en/cpu/int8 = 2.62s -> too slow for conversation.
#                          cuda/float16 = FAILS without cublas64_12.dll (see docs/SETUP.md).
# CPU is deliberate: it keeps all 8GB of VRAM for the LLM.
STT_MODEL = os.getenv("HAKU_STT", "base.en")
STT_DEVICE = os.getenv("HAKU_STT_DEVICE", "cpu")
STT_COMPUTE = os.getenv("HAKU_STT_COMPUTE", "int8")

# --- Text to speech (Piper) ---
# Measured: 0.10s for one sentence, ~30x realtime. Voice model is NOT in git (63MB).
TTS_VOICE = os.getenv("HAKU_VOICE", "voices/en_US-lessac-medium.onnx")

# --- Server ---
HOST = "0.0.0.0"   # 0.0.0.0 so the Quest can reach it over Wi-Fi
PORT = int(os.getenv("HAKU_PORT", "8000"))
EXHIBITS_DIR = os.getenv("HAKU_EXHIBITS", "exhibits")
