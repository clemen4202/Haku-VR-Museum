"""Measure real time-to-first-token and generation speed for the Haku museum-guide prompt."""
import json, time, urllib.request

HOST = "http://127.0.0.1:11434"

SYSTEM = (
    "You are a museum guide. Answer ONLY from the exhibit record below. "
    "If the record does not contain the answer, say you don't know. "
    "Reply in 2-3 short spoken sentences. No lists, no markdown."
)
RECORD = {
    "id": "bronze-mirror-01",
    "title": "Tang Dynasty Bronze Mirror",
    "date": "8th century CE",
    "culture": "Tang Dynasty China",
    "materials": "High-tin bronze, cast",
    "dimensions": "21.5 cm diameter, 0.8 cm thick, 1.2 kg",
    "facts": [
        "The polished face served as the mirror; the decorated back faced away.",
        "The lion-and-grapevine motif blends Chinese and Central Asian design.",
        "High tin content made the surface bright but brittle.",
        "Mirrors were often buried with the dead as protective objects.",
    ],
    "unknown": "The specific workshop and the name of the caster are not recorded.",
}
QUESTION = "What was this actually used for?"


def bench(model, think=None, label=""):
    body = {
        "model": model,
        "messages": [
            {"role": "system", "content": SYSTEM + "\n\nEXHIBIT RECORD:\n" + json.dumps(RECORD)},
            {"role": "user", "content": QUESTION},
        ],
        "stream": True,
        "options": {"temperature": 0.4, "num_predict": 120},
    }
    if think is not None:
        body["think"] = think

    req = urllib.request.Request(
        HOST + "/api/chat",
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json"},
    )
    t0 = time.perf_counter()
    ttft = None
    tokens = 0
    text = []
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            for line in r:
                if not line.strip():
                    continue
                chunk = json.loads(line)
                piece = chunk.get("message", {}).get("content", "")
                if piece:
                    if ttft is None:
                        ttft = time.perf_counter() - t0
                    tokens += 1
                    text.append(piece)
                if chunk.get("done"):
                    break
    except Exception as e:
        print(f"  [{label}] FAILED: {e}")
        return
    total = time.perf_counter() - t0
    tps = tokens / total if total else 0
    print(f"  [{label}] time-to-first-token: {ttft:.2f}s | total: {total:.2f}s | ~{tps:.0f} tok/s")
    ans = "".join(text).strip().replace("\n", " ")
    print(f"  [{label}] answer: {ans[:260]}")
    print()


def main():
    with urllib.request.urlopen(HOST + "/api/tags", timeout=10) as r:
        models = [m["name"] for m in json.load(r)["models"]]
    print("Models available:", models)
    print()

    m = "qwen3:8b"
    if m in models:
        print(f"--- {m} ---")
        print("  (run 1 includes model load into VRAM)")
        bench(m, think=False, label="warm-up, think=False")
        bench(m, think=False, label="warmed, think=False")
        bench(m, think=True, label="warmed, think=True")


if __name__ == "__main__":
    main()
