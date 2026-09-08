# Haku VR Museum

## Team Name
Haku

## Project
AI-Powered VR Museum Experience

## Team Members
- Clemen – Team Leader / Project Manager
- Sneha – VR Developer
- Ameen – AI Developer
- Femin – Interaction & UI Developer
- Mazin – 3D Environment Designer
- Mahir – Research, Testing & Documentation

## Description
An immersive virtual reality museum where users can explore exhibits and interact with an
AI-powered museum guide to learn about artefacts through natural conversation.

You walk up to an object, hold the controller trigger, and ask a question out loud. The guide
answers in a spoken sentence or two — grounded on a curated catalogue record for that specific
object, and required to say *"the record doesn't say"* when the record does not say.

Everything runs on the team's own laptop over the LAN. **No cloud service, no API key, no account,
no per-request cost.** That is a design constraint, not an accident: the interesting engineering
problem here is a guide that declines.

---

## Before you clone: two rules that protect the whole team

1. **Run `git lfs install` once per machine before cloning.** Skip it and every texture and mesh
   arrives as a ~130-byte pointer file, Unity imports garbage, and you will think the repo is
   broken.
2. **Clone once. Never re-clone.** A fresh clone re-downloads every LFS object. Six people at
   1.5 GB each is the team's entire monthly LFS bandwidth in one lab session. When a merge goes
   wrong, fix it with `git reset --hard origin/main` — never by deleting the folder.

Never commit model weights. `.gitignore` explains exactly why.

---

## Repository layout

| Path | What it is |
|---|---|
| `Assets/`, `Packages/`, `ProjectSettings/` | The Unity 6 project (Quest 3, URP, OpenXR) |
| `Assets/Scripts/Haku/` | Runtime C#: mic capture, WAV encoding, guide client, exhibit points |
| `Assets/Editor/` | Editor tooling: project setup, museum generator, build script |
| `ai-service/` | The local AI service (FastAPI + Ollama + faster-whisper + Piper) |
| `docs/` | Setup guide, sprint plan, museum design, measured performance |

## Documentation

| Document | Read it for |
|---|---|
| [docs/SETUP.md](docs/SETUP.md) | Getting your machine building to a Quest 3 from zero |
| [docs/SPRINT-1.md](docs/SPRINT-1.md) | Who is doing what in Weeks 4–6 |
| [docs/DESIGN-museum.md](docs/DESIGN-museum.md) | Spatial design, poly budgets, comfort decisions |
| [docs/MEASURED.md](docs/MEASURED.md) | Real benchmark numbers, not estimates |
| [docs/STATUS.md](docs/STATUS.md) | Current environment and build state |

## Quickstart — the AI guide

```powershell
cd ai-service
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
# download a Piper voice into ai-service/voices/  (see docs/SETUP.md)
.\.venv\Scripts\python.exe main.py
```

Then check it is alive:

```powershell
curl.exe -s http://127.0.0.1:8000/health
```

## Technology

| Layer | Choice | Licence |
|---|---|---|
| Engine | Unity 6000.5.5f1, URP 17.5 | Unity Personal (free) |
| XR | OpenXR 1.18, XR Interaction Toolkit 3.6 | Unity Companion |
| Speech to text | faster-whisper (`base.en`) | MIT |
| Language model | Ollama + `qwen3:8b` | Apache 2.0 |
| Text to speech | Piper ([OHF-Voice/piper1-gpl](https://github.com/OHF-Voice/piper1-gpl)) | **GPL-3.0** |
| 3D content | Blender | GPL |

Every component is free and self-hosted. Total running cost: **$0**.

## Measured performance

Benchmarked on the team's dev laptop (RTX 3070 Ti Laptop, i9-12900H). Full detail in
[docs/MEASURED.md](docs/MEASURED.md).

| Stage | Warm |
|---|---|
| Speech to text | ~580 ms |
| Language model | ~780 ms |
| Text to speech | ~330 ms |
| **Full spoken round trip** | **1.6 – 1.9 s** |

The target is under 1.5 s. Streaming the model output into sentence-level speech synthesis, plus
moving speech recognition onto the GPU, is expected to reach roughly 1.0 s. That is Milestone 3.
