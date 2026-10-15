# Haku VR Museum

## Team Name

Haku

## Project

AI-Powered VR Museum Experience

## Team Members

| Member | Role                              |
| ------ | --------------------------------- |
| Clemen | Team Leader / Project Manager     |
| Sneha  | VR Developer                      |
| Ameen  | AI Developer                      |
| Femin  | Interaction & UI Developer        |
| Mazin  | 3D Environment Designer           |
| Mahir  | Research, Testing & Documentation |

## Description

An immersive virtual reality museum where users can explore exhibits and interact with an
AI-powered museum guide to learn about artefacts through natural conversation.

You walk up to an object (or pick it up with the trigger), hold **A** and ask a question out loud.
The guide answers in two or three short spoken sentences — based on that object's catalogue description, and
told to say so when it is not sure rather than make things up.

Everything runs on the team's own Windows PC (`127.0.0.1`). **No cloud service, no API key, no account,
no per-request cost.** That is a design constraint, not an accident: the interesting engineering
problem here is a guide that declines.

> **Status.** The guide works in the Unity Editor over Quest Link (voice also needs the whisper.cpp
> server running). A standalone headset build cannot reach it yet. See
> [Current status](#current-status).

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

**Working today** (Unity Editor on Windows + Quest 3 over Quest Link)

- Five-gallery brick museum, generated from code
- Two exhibits: a Viking sword, and an Egyptian canopic jar whose lid lifts off as its own
  inspectable object
- Hinged double doors at the entry and exit
- VR controls: stick walking and snap turn, controller rays, pick up and turn exhibits, open doors
- **Haku, the AI guide** — a character that stays in front of you (without passing through walls,
  doors or plinths) and answers questions about the exhibit you hold or stand near, using a
  local Ollama model (`qwen3:8b`)
- **Voice** — hold **A** and ask out loud (local whisper.cpp speech-to-text); Haku reads its
  answers aloud with the built-in Windows voice. Fully offline.
- Desktop fallback (WASD, mouse, E to interact) when no headset is connected

**Not working yet**

- The AI guide and voice work only when the game runs on the Windows PC that runs Ollama and
  whisper.cpp (Unity Editor with Quest Link), because they call `127.0.0.1`. In a standalone Quest
  APK Haku still appears but cannot answer or listen, and its spoken voice is Windows-only.
- Lighting is not baked until someone runs Generate Lighting (see the Unity quickstart)

---

## Repository layout

| Path                                             | What it is                                                                                                                                           |
| ------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Assets/`, `Packages/`, `ProjectSettings/` | The Unity 6 project (Quest 3, URP, OpenXR)                                                                                                           |
| `Assets/Scripts/`                              | Runtime C#: VR controls (`VRInteractor`), AI guide (`MuseumGuide`, Ollama), voice (`GuideVoice`, whisper.cpp + Windows voice), exhibits, doors |
| `Assets/Resources/`                            | Shaders the scripts load at runtime (depth-tested text, guide colours)                                                                               |
| `Assets/Editor/`                               | Editor tooling: the museum generator (`BrickMuseumVR.cs`, menu **Brick Museum → Generate VR Museum**)                                       |
| `Assets/Scenes/`                               | Generated scenes — regenerate, do not hand-merge                                                                                                    |
| `ai-service/`                                  | Legacy standalone AI service (FastAPI + faster-whisper + Piper) —**not used by the current Unity build**                                      |
| `ai-service/voices/`                           | Piper voice models —**downloaded, never committed**                                                                                           |
| `docs/`                                        | Setup guide, sprint plan, museum design, measured performance                                                                                        |

## Documentation

| Document                                      | Read it for                                                                                                                                      |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ |
| [docs/SETUP.md](docs/SETUP.md)                 | Getting your machine building to a Quest 3 from zero (written for the legacy`ai-service` layout; for the AI guide follow the quickstart below) |
| [docs/SPRINT-1.md](docs/SPRINT-1.md)           | Who is doing what in Weeks 4–6                                                                                                                  |
| [docs/DESIGN-museum.md](docs/DESIGN-museum.md) | Spatial design, poly budgets, comfort decisions                                                                                                  |
| [docs/MEASURED.md](docs/MEASURED.md)           | Real benchmark numbers, not estimates                                                                                                            |
| [docs/STATUS.md](docs/STATUS.md)               | Environment and build state as of Week 4 (describes the legacy`ai-service` setup)                                                              |

---

## New PC: from zero to running

Git brings the project. Everything below has to be installed or set up on each PC once, in this
order. Needs a VR-ready Windows 10/11 PC with an NVIDIA GPU (about 8 GB of GPU memory or more for
`qwen3:8b`) and a Meta Quest 3 with a Link cable (or Air Link).

1. **Install** [Git for Windows](https://git-scm.com/download/win) (includes Git LFS),
   [Unity Hub](https://unity.com/download), [Ollama](https://ollama.com/download) and the
   [Meta Horizon Link PC app](https://www.meta.com/quest/setup/) (formerly the Oculus / Meta Quest Link app).
2. **Clone** (LFS first, see the rules above):

   ```powershell
   git lfs install
   git clone https://github.com/clemen4202/Haku-VR-Museum.git
   ```
3. **Unity**: in Unity Hub install editor **6000.5.5f1** exactly (Android Build Support only if you
   will build an APK), then add and open the cloned folder. The first import takes a while.
4. **Quest Link**: open the Meta Horizon Link app, sign in, connect the headset (USB cable or Air
   Link) and enable Link in the headset. Then in the app: **Settings → General → OpenXR Runtime**
   and make Meta Horizon Link the active runtime. Without this Unity cannot see the headset and the
   game starts the desktop (keyboard and mouse) player instead.
5. **AI guide and voice**: follow the quickstart below — `ollama pull qwen3:8b`, the whisper.cpp
   server and its model, microphone permission.
6. **Play**: start Ollama and `whisper-server`, put the headset on with Link active, open
   `Assets/Scenes/BrickMuseum_VR.unity` and press **Play**. The Console should show
   `[RigSelector] Headset detected` and, after the first load, `[MuseumGuide] Ollama model 'qwen3:8b' is loaded and ready.`

If something does not work: the Console shows a `[RigSelector]`, `[MuseumGuide]` or `[GuideVoice]`
line saying why, and Haku says so on his panel.

## Quickstart — the AI guide and voice (current build)

Everything runs on the same Windows PC as Unity. Nothing is sent to the internet.

**1. Ollama (the guide's brain)** — install from [https://ollama.com/download](https://ollama.com/download), then:

```powershell
ollama pull qwen3:8b
ollama list
```

`qwen3:8b` needs about 6 GB of GPU memory on top of VR. On a smaller GPU use `qwen3:4b` and change
`model` in `Assets/Scripts/MuseumGuide.cs`. Haku loads the model in the background as soon as you
press Play with the headset on (~30-40 s from cold) and asks Ollama to free it when you stop Play. Before a
demo, wait for the Console line `[MuseumGuide] Ollama model 'qwen3:8b' is loaded and ready.`

**2. whisper.cpp (speech-to-text for "hold A to talk")**

1. From [https://github.com/ggml-org/whisper.cpp/releases](https://github.com/ggml-org/whisper.cpp/releases) download `whisper-bin-x64.zip` from the
   newest release that has it, and unzip it (e.g. to `C:\whisper`). It unzips into a `Release`
   folder that contains `whisper-server.exe`.
2. Download the model `ggml-base.en.bin` from
   [https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin](https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin) into that same
   `Release` folder, next to `whisper-server.exe`.
3. Start the server from the `Release` folder and leave the window open while you play:

   ```powershell
   cd C:\whisper\Release
   .\whisper-server.exe -m ggml-base.en.bin --host 127.0.0.1 --port 8080
   ```
4. Check it is listening: `Test-NetConnection 127.0.0.1 -Port 8080` should say `TcpTestSucceeded : True`.
5. If `whisper-server.exe` will not start and mentions a missing `VCRUNTIME140.dll` / `MSVCP140.dll`,
   install the [Microsoft Visual C++ Redistributable (x64)](https://aka.ms/vs/17/release/vc_redist.x64.exe).

Haku's spoken answers use the Windows built-in voice, so the PC needs an English voice installed
(normally there already: Settings → Time & language → Speech → Manage voices).

**3. Microphone** — Windows Settings → Privacy & security → Microphone → allow desktop apps. With
Quest Link the headset mic ("Headset Microphone (Oculus Virtual Audio Device)") is picked
automatically.

**4. Play** — open the museum in Unity, connect the headset with Quest Link, press **Play**.

The addresses live in code because these components are added at runtime, not in the scene:
`MuseumGuide.ollamaUrl` / `MuseumGuide.model` and `GuideVoice.whisperUrl`. If something is not
running, Haku says so on its panel and the Console shows a `[MuseumGuide]` or `[GuideVoice]` line.

### Controls in the headset

| Control                | Action                                                                                                                                                    |
| ---------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Left stick             | Walk                                                                                                                                                      |
| Right stick            | Snap turn                                                                                                                                                 |
| **T1** (trigger) | Point and press: pick up an exhibit, open/close a door, press a panel button, talk to Haku (aim at his head when he stands in front of a door or exhibit) |
| **G1** (grip)    | Put the held exhibit back                                                                                                                                 |
| **A** (right)    | Hold and ask Haku out loud; let go to send                                                                                                                |
| **X** (left)     | Show / hide the debug readout (fps, controller state)                                                                                                     |

## Legacy — the standalone AI service (not used by the current Unity build)

The Unity build talks to Ollama and whisper.cpp directly. The service below is kept for reference.

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
3. Open `Assets/Scenes/BrickMuseum_VR.unity` — it is already generated and committed, so to just
   run the museum you can skip to step 4. Only regenerate (menu **Brick Museum → Generate VR
   Museum**) if the scene is missing or you changed the generator.

   The generator builds the whole scene from code — floor, five galleries, corridors, doorways, brick and tile
   materials with size-derived tiling, ceiling, lighting, plinths, exhibits and hinged doors — and
   saves it to `Assets/Scenes/BrickMuseum_VR.unity`. Re-running is safe: it rebuilds in place.
4. **Window → Rendering → Lighting → Generate Lighting**

   The lights are authored as **Baked** and contribute nothing until this runs. The museum looks
   flat and dim before the bake. That is expected, not a fault.
5. Start Ollama and the whisper server (see the AI guide quickstart above), connect Quest Link and
   press **Play**.

## Quickstart — building to the Quest 3

| Setting                          | Value                                                               |
| -------------------------------- | ------------------------------------------------------------------- |
| Platform                         | Android (**File → Build Profiles → Switch Platform**)       |
| Scripting Backend                | IL2CPP                                                              |
| Target Architectures             | ARM64 only                                                          |
| Minimum API Level                | 32                                                                  |
| Graphics APIs                    | Vulkan only (untick Auto)                                           |
| Color Space                      | Linear                                                              |
| Texture Compression              | ASTC                                                                |
| XR Plug-in Management → Android | OpenXR                                                              |
| OpenXR → Android                | Oculus Touch Controller Profile +**Meta Quest** feature group |

Quest in developer mode, connected by USB, headset unlocked, USB debugging accepted, then
**Build and Run**.

> **Networking on campus** (legacy `ai-service` only). The Quest reaches the service only over Wi-Fi, and campus networks
> often isolate clients from each other. If `/health` times out on device, use a phone hotspot.
> Android also blocks plain HTTP until cleartext traffic is permitted for the service host.

---

## Service API (legacy `ai-service`)

| Endpoint           | Purpose                                                                                               |
| ------------------ | ----------------------------------------------------------------------------------------------------- |
| `GET /health`    | Readiness of Ollama, speech recognition and the voice                                                 |
| `GET /exhibits`  | All loaded exhibit records                                                                            |
| `POST /ask`      | Text question in, text answer out — lets Unity be tested with no audio at all                        |
| `POST /converse` | Audio +`exhibit_id` + `held` + `distance_m` in; spoken WAV out with `X-Haku-*` timing headers |
| `POST /say`      | Speech synthesis only, for pre-rendering exhibit introductions                                        |

Try it without a headset:

```bash
curl -X POST http://127.0.0.1:8000/ask \
  -H "Content-Type: application/json" \
  -d '{"exhibit_id":"bronze-mirror-01","question":"What is this made of?"}'
```

### Exhibit records (legacy `ai-service`)

**In the current Unity build** Haku answers from each exhibit's `Inspectable` name and description
(the one you hold, or the nearest within 6 m). To add an exhibit, build it in
`BrickMuseumVR.BuildExhibits()` with `Inspect(...)` and re-run **Brick Museum → Generate VR Museum**.
The JSON records below belong to the legacy service.

One JSON file per artefact. Each holds title, date, culture, materials, dimensions, provenance,
facts, an explicit **`unknown`** field, and sources.

- **The `unknown` field is load-bearing.** It gives the model something concrete to decline with
  instead of leaving silence for it to fill.
- **Sources are stripped before the record reaches the model.** Citations exist for the report and
  are never read aloud.

To add an exhibit: drop a new JSON record in, restart the service, and set the matching
`exhibit_id` on that artefact's `ExhibitPoint` component in Unity (that component belonged to the legacy setup and is no longer in the project).

---

## Technology

| Layer          | Choice                                                                                                                                 | Licence                                    |
| -------------- | -------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------ |
| Engine         | Unity 6000.5.5f1, URP 17.5                                                                                                             | Unity Personal (free)                      |
| XR             | OpenXR 1.17.1, XR Interaction Toolkit 3.6                                                                                              | Unity Companion                            |
| Speech to text | whisper.cpp server (`ggml-base.en`) — legacy `ai-service`: faster-whisper                                                         | MIT                                        |
| Language model | Ollama +`qwen3:8b`                                                                                                                   | MIT (Ollama) / Apache 2.0 (Qwen3)          |
| Text to speech | Windows built-in voice (System.Speech) — legacy`ai-service`: Piper ([OHF-Voice/piper1-gpl](https://github.com/OHF-Voice/piper1-gpl)) | Part of Windows /**GPL-3.0** (Piper) |
| 3D content     | Blender                                                                                                                                | GPL                                        |

Every component is free and self-hosted. Total running cost: **$0**.

## Measured performance

Benchmarked on the team's dev laptop (RTX 3070 Ti Laptop, i9-12900H) using the legacy `ai-service`
pipeline. Full detail in [docs/MEASURED.md](docs/MEASURED.md).

| Stage                            | Warm                   |
| -------------------------------- | ---------------------- |
| Speech to text                   | ~580 ms                |
| Language model                   | ~780 ms                |
| Text to speech                   | ~330 ms                |
| **Full spoken round trip** | **1.6 – 1.9 s** |

The target is under 1.5 s. Streaming the model output into sentence-level speech synthesis, plus
moving speech recognition onto the GPU, is expected to reach roughly 1.0 s. That is Milestone 3.

The model's **thinking mode is deliberately disabled**: it costs about 4.8 s and never starts the
answer sooner. For a two-sentence museum answer that trade is not worth it.

---

## Known issues

| Issue                                             | Detail                                                                                                                                                                                  | When                 |
| ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------- |
| Grounding drift under register change             | Asked to explain "like I'm nine", the guide added detail not in the record — colourful grapes, big strong lions. Small, plausible, and exactly the drift a museum guide cannot afford. | Weeks 10–11         |
| Response time above target (legacy`ai-service`) | 1.6–1.9 s against a 1.5 s target; speech recognition is the largest single cost. The current Unity pipeline has not been timed; its Windows voice takes about 1 s to start speaking    | Week 10              |
| Content behind (legacy`ai-service`)             | 1 of 8 exhibit records written; sources still placeholders                                                                                                                              | Weeks 9–10          |
| VRAM headroom                                     | The language model takes about 5.2 GB of an 8 GB GPU alongside VR rendering (speech recognition now runs on the CPU)                                                                    | To verify under load |

---

RMIT Mixed Reality · Milestone 2, September 2026