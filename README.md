# Haku VR Museum

## Team Name
Haku

## Project
AI-Powered VR Museum Experience

## Team Members

| Member | Role |
|---|---|
| Clemen | Team Leader / Project Manager | 
| Sneha | VR Developer | 
| Ameen | AI Developer | 
| Femin | Interaction & UI Developer | 
| Mazin | 3D Environment Designer | 
| Mahir | Research, Testing & Documentation | 

## Description
An immersive virtual reality museum where users can explore exhibits and interact with an
AI-powered museum guide to learn about artefacts through natural conversation.

You walk up to an object, hold the controller trigger, and ask a question out loud. The guide
answers in a spoken sentence or two — grounded on a curated catalogue record for that specific
object, and required to say *"the record doesn't say"* when the record does not say.

Everything runs on the team's own laptop over the LAN. **No cloud service, no API key, no account,
no per-request cost.** That is a design constraint, not an accident: the interesting engineering
problem here is a guide that declines.

> **Milestone 2 (half-way).** The paragraph above describes the finished experience. See
> [Current status](#current-status) for what actually runs today — both halves work, the join
> between them does not yet.

---

## Before you clone: two rules that protect the whole team

1. **Run `git lfs install` once per machine before cloning.** Skip it and every texture and mesh
   arrives as a ~130-byte pointer file, Unity imports garbage, and you will think the repo is
   broken.
2. **Clone once. Never re-clone.** A fresh clone re-downloads every LFS object. Six people at
   1.5 GB each is the team's entire monthly LFS bandwidth in one lab session. When a merge goes
   wrong, fix it with `git reset --hard origin/main` — never by deleting the folder.

Never commit model weights. `.gitignore` explains exactly why.

### And one rule about scenes

Unity scene files are large machine-generated YAML and merge badly — one merge in this project
briefly removed the main scene. So:

- Geometry is **generated from code**, not hand-placed. Regenerate rather than merge.
- **One owner per scene.** Do not open someone else's scene and save it.
- **Pull before opening Unity.** Never hand-merge a `.unity` file — take one side whole.

---

## Current status

**Working today**

- Five-gallery brick museum, generated from code, walkable with desktop controls
- Two exhibits: a Viking sword, and an Egyptian canopic jar whose lid lifts off as its own
  inspectable object
- Hinged double doors at the entry and exit
- Complete speech-to-speech AI loop, with per-stage timings returned as HTTP headers
- Correct refusal behaviour on questions the record cannot answer
- Quest 3 APK builds headlessly (95 MB, arm64-v8a, target SDK 34)
- OpenXR rig renders the museum on device with head tracking

**Not working yet**

- VR locomotion and teleport in the shipping scene
- Controller interaction with exhibits
- Triggering the guide from inside the headset
- 7 of 8 exhibit records

---

## Repository layout

| Path | What it is |
|---|---|
| `Assets/`, `Packages/`, `ProjectSettings/` | The Unity 6 project (Quest 3, URP, OpenXR) |
| `Assets/Scripts/Haku/` | Runtime C#: mic capture, WAV encoding, guide client, exhibit points |
| `Assets/Editor/` | Editor tooling: project setup, museum generator, build script |
| `Assets/Scenes/` | Generated scenes — regenerate, do not hand-merge |
| `ai-service/` | The local AI service (FastAPI + Ollama + faster-whisper + Piper) |
| `ai-service/voices/` | Piper voice models — **downloaded, never committed** |
| `docs/` | Setup guide, sprint plan, museum design, measured performance |

## Documentation

| Document | Read it for |
|---|---|
| [docs/SETUP.md](docs/SETUP.md) | Getting your machine building to a Quest 3 from zero |
| [docs/SPRINT-1.md](docs/SPRINT-1.md) | Who is doing what in Weeks 4–6 |
| [docs/DESIGN-museum.md](docs/DESIGN-museum.md) | Spatial design, poly budgets, comfort decisions |
| [docs/MEASURED.md](docs/MEASURED.md) | Real benchmark numbers, not estimates |
| [docs/STATUS.md](docs/STATUS.md) | Current environment and build state |

---

## Quickstart — the AI guide

```powershell
cd ai-service
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
# download a Piper voice into ai-service/voices/  (see docs/SETUP.md)
.\.venv\Scripts\python.exe main.py
```

macOS / Linux:

```bash
cd ai-service
python3 -m venv .venv
.venv/bin/pip install -r requirements.txt
.venv/bin/python main.py
```

Ollama must be installed and running, with the model pulled once:

```bash
ollama pull qwen3:8b
```

Then check it is alive:

```powershell
curl.exe -s http://127.0.0.1:8000/health
```

Ollama, speech recognition and the voice must all report ready.

> **Two things that will catch you.**
> A **cold model load costs about 50 seconds** — start the service well before any demo, and it
> stays pinned in memory afterwards. And the service must be reachable from the **headset**, not
> just from the laptop: bind to `0.0.0.0`, not `127.0.0.1`, before testing on device.

## Quickstart — the museum in Unity

1. Open the **repository root folder** in Unity Hub with **Unity 6000.5.5f1**.
2. Let the package resolve finish completely before touching anything.
3. Menu: **Brick Museum → Generate VR Museum**

   Builds the whole scene from code — floor, five galleries, corridors, doorways, brick and tile
   materials with size-derived tiling, ceiling, lighting, plinths, exhibits and hinged doors — and
   saves it to `Assets/Scenes/BrickMuseum_VR.unity`. Re-running is safe: it rebuilds in place.
4. **Window → Rendering → Lighting → Generate Lighting**

   The lights are authored as **Baked** and contribute nothing until this runs. The museum looks
   flat and dim before the bake. That is expected, not a fault.
5. Set the service address on the guide client component to the laptop's LAN IP, e.g.
   `http://192.168.1.42:8000`.

## Quickstart — building to the Quest 3

| Setting | Value |
|---|---|
| Platform | Android (**File → Build Profiles → Switch Platform**) |
| Scripting Backend | IL2CPP |
| Target Architectures | ARM64 only |
| Minimum API Level | 32 |
| Graphics APIs | Vulkan only (untick Auto) |
| Color Space | Linear |
| Texture Compression | ASTC |
| XR Plug-in Management → Android | OpenXR |
| OpenXR → Android | Oculus Touch Controller Profile + **Meta Quest** feature group |

Quest in developer mode, connected by USB, headset unlocked, USB debugging accepted, then
**Build and Run**.

> **Networking on campus.** The Quest reaches the service only over Wi-Fi, and campus networks
> often isolate clients from each other. If `/health` times out on device, use a phone hotspot.
> Android also blocks plain HTTP until cleartext traffic is permitted for the service host.

---

## Service API

| Endpoint | Purpose |
|---|---|
| `GET /health` | Readiness of Ollama, speech recognition and the voice |
| `GET /exhibits` | All loaded exhibit records |
| `POST /ask` | Text question in, text answer out — lets Unity be tested with no audio at all |
| `POST /converse` | Audio + `exhibit_id` + `held` + `distance_m` in; spoken WAV out with `X-Haku-*` timing headers |
| `POST /say` | Speech synthesis only, for pre-rendering exhibit introductions |

Try it without a headset:

```bash
curl -X POST http://127.0.0.1:8000/ask \
  -H "Content-Type: application/json" \
  -d '{"exhibit_id":"bronze-mirror-01","question":"What is this made of?"}'
```

### Exhibit records

One JSON file per artefact. Each holds title, date, culture, materials, dimensions, provenance,
facts, an explicit **`unknown`** field, and sources.

- **The `unknown` field is load-bearing.** It gives the model something concrete to decline with
  instead of leaving silence for it to fill.
- **Sources are stripped before the record reaches the model.** Citations exist for the report and
  are never read aloud.

To add an exhibit: drop a new JSON record in, restart the service, and set the matching
`exhibit_id` on that artefact's `ExhibitPoint` component in Unity.

---

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

The model's **thinking mode is deliberately disabled**: it costs about 4.8 s and never starts the
answer sooner. For a two-sentence museum answer that trade is not worth it.

---

## Known issues

| Issue | Detail | When |
|---|---|---|
| Duplicate museum in the VR scene | The scene contains two overlapping copies of the building | Week 9 |
| Grounding drift under register change | Asked to explain "like I'm nine", the guide added detail not in the record — colourful grapes, big strong lions. Small, plausible, and exactly the drift a museum guide cannot afford. | Weeks 10–11 |
| Response time above target | 1.6–1.9 s against a 1.5 s target; speech recognition is the largest single cost | Week 10 |
| Content behind | 1 of 8 exhibit records written; sources still placeholders | Weeks 9–10 |
| VRAM headroom | The language model pins 5.2 GB of 8 GB, constraining GPU speech recognition | To verify under load |

---

RMIT Mixed Reality · Milestone 2, September 2026
