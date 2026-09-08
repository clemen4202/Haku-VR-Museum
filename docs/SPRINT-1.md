# Haku — Sprint 1 (Weeks 4–6)

**Covers milestones:** M1 Design lock & pipeline (W4–5), start of M2 Gallery vertical slice (W5–7), start of M3 Conversation loop (W6–8).
**Sprint window:** Week 4 Day 1 (tomorrow) → end of Week 6.
**Sprint ends with:** an internal playtest on device, Friday of Week 6.

---

## 1. Where we actually are

Two honest sentences before anyone opens Unity: **the AI half of this project already works and has been measured. The VR half cannot build to the headset yet.**

### What is proven working

The local AI service exists, runs, and has been timed on the team laptop (ASUS ROG Zephyrus M16, i9-12900H, 32 GB RAM, RTX 3070 Ti Laptop with 8 GB VRAM, LAN IP `192.168.0.90`). Nothing in it calls the cloud.

| Stage | Configuration | Measured (warm) |
|---|---|---|
| STT | faster-whisper `base.en`, CPU, int8 | 0.51 s for 5.76 s of audio (≈11× realtime), transcript accurate |
| LLM | Ollama `qwen3:8b` Q4_K_M, `think=false` | 0.57 s to first token, 1.21 s full short answer, ≈35 tok/s |
| TTS | Piper `en_US-lessac-medium` | 0.19–0.27 s for 5.7 s of audio (≈30× realtime); **first sentence only: 0.10 s** |
| **Full `/converse` round trip** | non-streaming, 1.0 s question clip | **STT 567–623 ms + LLM 744–996 ms + TTS 313–385 ms = 1.62–1.95 s** |

Behaviour is verified, not assumed. The guide answers grounded questions correctly, adapts its register for a nine-year-old, and **correctly refuses** when the record does not contain the answer — the observed refusal was *"The record doesn't say. We don't actually know the cost or which emperor ruled at the time. However, we do know this mirror was made in the Tang Dynasty…"*

Two numbers everyone needs to remember, because they are load-bearing constraints:
- **`think=true` costs 4.80 s and 148 reasoning tokens and never starts the answer.** It is off in `config.py`. Never turn it on.
- **A cold model load costs 50 seconds.** `keep_alive=-1` is mandatory, and the service pins the model at startup. Do not restart Ollama five minutes before a demo.

### The honest gap

The pitch deck promised **"under 1.5 s to first spoken word."** The naive implementation measures **1.62–1.95 s. That promise is not met yet.** We do not paper over this in any document, report, or presentation. It is reachable:
- streaming the LLM and firing Piper on the first sentence → **≈1.3–1.4 s**
- plus fixing GPU whisper → **≈1.0 s, comfortably met**

Both are Ameen's tickets this sprint (§4.3). Until they land and are re-measured, the number we quote out loud is the measured one.

### The single blocker

**Unity Android Build Support module is not installed.** Unity 6000.5.5f1 is in the Hub, but without that module *nobody can build an APK and nothing can go on the headset.* Git 2.46.2, Git LFS 3.5.1, Python 3.13.7, Node 22.19.0, Blender 4.3, Ollama and the Android SDK (with adb 35.x at `%LOCALAPPDATA%\Android\Sdk`) are all present.

Also missing, lower severity: `ffmpeg`, and `adb` is not on `PATH`.

**This blocker owns exactly one person for one morning (Clemen, CLE-1). It must not idle the other five.** Every ticket below is written so that the only work that genuinely needs a headset is Sneha's device-build verification, and even that has an Editor-side substitute (XR Device Simulator) until CLE-1 clears.

---

## 2. Sprint shape

| | Week 4 | Week 5 | Week 6 |
|---|---|---|---|
| **Clemen** | Unblock toolchain, repo + LFS, project settings lock | Milestone tracking, playtest logistics | Run the Week 6 playtest, collate results |
| **Sneha** | XR rig + locomotion in Editor sim | Grey-box room on device @ 72 fps (M1 done) | Grab-inspect + 3 exhibit stations live (M2 in flight) |
| **Ameen** | Repo fix, concurrency fix, GPU whisper | `/converse/stream` streaming loop | Re-measure, publish new latency table |
| **Femin** | `HakuClient.cs` against `/ask` on PC | Push-to-talk mic capture → `/converse` | Conversation state machine + subtitles in headset |
| **Mazin** | Budget-compliant grey-box room, 2 hero exhibits | 4 more heroes, plinth/vitrine kit, atlas pass | Bake pass, LOD chains, hand-off to Sneha |
| **Mahir** | Records 2–4, schema lock | Records 5–8, 20-question grounding set | Run grounding test, log embellishment defects |

---

## 3. The two budgets everyone works inside

### 3.1 Performance budget (Quest 3, 72 fps, URP mobile, single-pass instanced)

| | Working target | Hard fail |
|---|---|---|
| Draw calls | **800** | **1000** |
| Triangles (whole scene) | **1.5 M** | **1.8 M** |
| Texture memory | 700 MB | 900 MB |
| Realtime lights | 1 directional, everything else baked | 2 |
| Frame time | 13.8 ms (72 fps) | anything above is a rejected build |

If a build exceeds a hard-fail number it does not get merged. Sneha runs the numbers; Mazin models to them.

### 3.2 Art budget — **this is Mazin's spec, effective immediately**

Mazin cannot start modelling without numbers, so here they are. Whole scene = 8 exhibits + one room.

| Bucket | Draw calls | Triangles |
|---|---:|---:|
| Room shell (walls, floor, ceiling, trim, doorways) | 180 | 220,000 |
| Architectural dressing & props (benches, rails, vents, signage, rope stands) | 160 | 260,000 |
| Exhibit furniture × 8 (plinth + vitrine + label + spot) — 14 dc / 12 k each | 112 | 96,000 |
| Hero exhibit meshes × 8 (LOD0 + highlight shell) — 6 dc / 70 k each | 48 | 560,000 |
| Player rig, hands, UI panel, subtitle board | 70 | 40,000 |
| FX, emissives, blob shadows | 40 | 30,000 |
| **Unallocated headroom — do not spend** | **190** | **294,000** |
| **Total** | **800** | **1,500,000** |

**The number Mazin models against: 20 draw calls and 82,000 triangles per exhibit station** (70 k hero mesh + 12 k of furniture). Eight of those is 656 k triangles, which is the single largest line in the scene and the one most likely to blow the budget.

**Mandatory LOD chain on every hero mesh** (grab-inspect means the player brings it to 20 cm from their eyes, so LOD0 has to survive that):

| LOD | Distance | Triangles |
|---|---|---:|
| LOD0 | held, or < 0.6 m | 70,000 |
| LOD1 | 0.6 – 3 m | 25,000 |
| LOD2 | 3 – 8 m | 8,000 |
| Cull | > 12 m | — |

**Materials and textures:**
- Max **3 material slots** on a hero mesh. **1** on any furniture piece.
- All eight plinths/vitrines/labels share **one atlas** — that is what turns 112 furniture draw calls into ~4 batched ones in practice, and it is where the headroom above comes from. Do not give each plinth its own material.
- Hero maps: 2048 albedo + 2048 packed ORM + 1024 normal, ASTC 6×6. Room and furniture: 2 × 2048 shared atlases. **No 4 K textures anywhere in this project.**
- No stacked transparency. A vitrine is **one** glass layer, not glass + reflection plane + dust plane.

**Export contract (so Sneha never has to fix a mesh by hand):**
- FBX, metres, Y-up, transforms applied, no n-gons on anything the player can hold.
- Origin at the **base** for furniture; at the **centre of mass** for grabbables; +Z faces the visitor.
- **Real-world scale comes from the `dimensions` field of the exhibit record.** The bronze mirror is 21.5 cm across, 0.8 cm thick, 1.2 kg. It is not a dinner plate. Check the record before you scale.
- Naming: `haku_<exhibit-id>_<part>_LOD<n>` — e.g. `haku_bronze-mirror-01_hero_LOD0`.

---

## 4. Per-person plan

### 4.1 Clemen — Team Leader / Project Manager

**Goal for this sprint:** clear the one hard blocker within 24 hours, put a repo and a project-settings baseline under the whole team, and land the Week 6 playtest as a real booked event rather than an aspiration.

| # | Ticket | Definition of done |
|---|---|---|
| CLE-1 | **Install Unity Android Build Support** (module + OpenJDK + Android SDK/NDK) for 6000.5.5f1 via Unity Hub. Add `%LOCALAPPDATA%\Android\Sdk\platform-tools` to `PATH`. | An empty URP scene builds to an `.apk` **and launches on the Quest**. `adb devices` lists the headset from a fresh terminal. Screenshot of the running build posted to the team chat. **This is Day 1. Nothing else Clemen does matters more.** |
| CLE-2 | Stand up the Git repo with LFS: `.gitignore` for Unity (`Library/`, `Temp/`, `Logs/`, `Build/`), `.gitattributes` tracking `*.fbx *.png *.psd *.wav *.onnx` through LFS. Exclude the 63 MB Piper voice and any model weights. | All six people have cloned it, made one commit, and pushed. Repo is under 200 MB. `git lfs ls-files` shows the binaries are actually in LFS, not in the pack. |
| CLE-3 | **Lock the Unity project settings** and write them down: URP mobile renderer, IL2CPP, ARM64, Vulkan, 72 Hz display rate, single-pass instanced stereo, multi-view, static + GPU instancing on. | `docs/PROJECT-SETTINGS.md` exists, the settings match it, and a second person has verified the settings on a fresh clone. |
| CLE-4 | Write `docs/SETUP.md` — the file `config.py` already references and which does not exist. Cover: Ollama + model pull, Python venv, the Piper voice download, the cuBLAS/cuDNN pip fix, how to start the service, how to check `/health`. | A person who has never touched the project gets `/health` returning `ok: true` by following it alone, without asking Ameen. |
| CLE-5 | Book the Week 6 internal playtest: room, headset, laptop, 8 slots, consent form, observer script. Confirm the venue's network situation (see R4). | Calendar invite out by end of Week 4 with room, time, and the task script attached. |

**Blocked by:** nothing.
**Blocking:** Sneha's device builds (CLE-1), everyone's ability to share work (CLE-2), all performance measurement (CLE-3).

---

### 4.2 Sneha — VR Developer (Unity)

**Goal for this sprint:** M1 closed — grey-box room running on device at 72 fps — and M2 opened, with grab-inspect working on three real exhibit stations.

**Sneha does not wait for CLE-1.** XR Interaction Toolkit ships an **XR Device Simulator** that drives the rig with mouse and keyboard in the Editor. Everything except the on-device frame timing can be built and tested before the headset is reachable.

| # | Ticket | Definition of done |
|---|---|---|
| SNE-1 | XR rig + locomotion: continuous move, snap turn (30°), teleport as a comfort option, height calibration, and a hard blocker on walking through walls. | Driveable end-to-end in the XR Device Simulator. Comfort options are toggleable at runtime, not compile-time. |
| SNE-2 | Grey-box gallery room built to Mazin's layout (`MAZ-1`), assembled from prefabs so the art drop-in is a swap and not a rebuild. | Room loads, is walkable, and reports its own draw call / triangle count in an on-screen debug overlay. Under budget with placeholder geometry. |
| SNE-3 | **Deploy to device and hit 72 fps** with the grey-box room. Capture a frame with the Frame Debugger and OVR Metrics. | **M1 complete.** APK on the headset, sustained 72 fps for a 3-minute walk-around, numbers posted (fps, draw calls, tris, frame time). Blocked by CLE-1 only. |
| SNE-4 | Grab-inspect interaction: pick up a hero mesh, bring it close, rotate it, put it back on the plinth with a snap. Expose `held` (bool) and `distance_m` (float) as public state. | Works on 3 exhibits. **`held` and `distance_m` are readable by Femin's client** — this is exactly what `/converse` takes as form fields, and it is the thing that makes this XR rather than a chatbot. |
| SNE-5 | Exhibit station prefab: plinth + vitrine + label + spotlight + trigger volume + an `ExhibitId` string field matching Mahir's record `id`. | Dropping the prefab into the scene and typing `bronze-mirror-01` is the entire process of adding an exhibit. Three stations placed. |

**Blocked by:** CLE-1 for SNE-3 only (everything else runs in the Editor). MAZ-1 for the room layout — but grey-box first, so a whiteboard sketch unblocks it on Day 1.
**Blocking:** Femin needs SNE-4's `held` / `distance_m` and SNE-5's `ExhibitId`; agree those three names with Femin **on Day 1**, before either of you writes code.

---

### 4.3 Ameen — AI Developer

**Goal for this sprint:** get time-to-first-spoken-word from the measured 1.62–1.95 s down to ≈1.3–1.4 s by streaming, and to ≈1.0 s by fixing GPU whisper. Re-measure and publish. Do not touch grounding yet — that is M4 (W8–10) and it is Mahir's test set that will drive it.

**AME-1 and AME-2 are correctness fixes and should take under an hour between them. AME-3 and AME-4 are the sprint.**

| # | Ticket | Definition of done |
|---|---|---|
| AME-1 | **The repo copy of `ai-service/main.py` does not import.** It defines `async def lifespan(app: FastAPI)` but never constructs `app = FastAPI(lifespan=lifespan)`, and the lifespan body has no `yield`. The working copy on the laptop runs; the committed copy does not. Reconcile them. | `python -c "import main"` succeeds on a **fresh clone**, `uvicorn main:app` starts, `/health` returns `ok: true`. Nobody else can run the service until this is true. |
| AME-2 | `/converse` writes every upload to a fixed path `_incoming.wav` in the working directory. Two concurrent requests corrupt each other's audio, and two headsets in a playtest will do exactly that. Pass the bytes to `faster-whisper` as a `BytesIO` instead. | Two simultaneous `/converse` requests with different audio return two correct, different transcripts. No file is written to disk. |
| AME-3 | **Optimisation 1 — stream the LLM, fire Piper on the first sentence.** Set `"stream": true` on the Ollama `/api/chat` call, buffer tokens until the first sentence boundary (`.` `?` `!` followed by space), send that sentence to Piper immediately, and stream the WAV out chunked while the rest of the answer is still generating. **Add this as a new `POST /converse/stream`; leave `/converse` untouched** so Femin's client does not break mid-sprint. Note that a streamed response cannot carry `X-Haku-Ms-Total` as a header (headers precede the body) — keep `X-Haku-Question` and emit the timings as HTTP trailers or a final JSON frame. | **Expected gain ≈ 300–500 ms.** Rationale from the measured numbers: LLM first token is 0.57 s against 1.21 s for a full short answer, and Piper does a single sentence in 0.10 s against 0.19–0.27 s for a full one. Done when time-to-first-audio-byte is measured 20 times over the same 1.0 s question clip and the **median is ≤ 1.45 s**, with the raw numbers written into `docs/LATENCY.md`. |
| AME-4 | **Optimisation 2 — fix GPU whisper.** `pip install nvidia-cublas-cu12 "nvidia-cudnn-cu12==9.*"` into the service venv, then set `HAKU_STT_DEVICE=cuda`, `HAKU_STT_COMPUTE=float16`. This is the fix for the measured `cublas64_12.dll is not found or cannot be loaded` failure. Watch VRAM: `qwen3:8b` Q4_K_M is 5.2 GB of the 8 GB and is pinned — `base.en` at float16 is ~150 MB, so this should fit, but **verify with `nvidia-smi` under load** and fall back to CPU int8 if it does not. | **Expected gain ≈ 350–450 ms** (STT 0.51–0.62 s → ~0.15–0.25 s). Done when transcripts are still accurate on the same test clips (accuracy is not negotiable for speed), VRAM headroom is recorded, and `config.py` comments are updated with the new measured numbers alongside the old ones. |
| AME-5 | Re-measure the whole loop after AME-3 + AME-4 and publish `docs/LATENCY.md`: p50 and p95 for STT / LLM / TTS / time-to-first-word, before and after, on the same clips. | The table exists, and it states plainly whether the 1.5 s pitch-deck promise is now met. **If it is not met, the document says so.** No rounding down, no cherry-picked warm run. |

**Blocked by:** nothing. All of this runs on the laptop with no headset involved.
**Blocking:** Femin's `/converse/stream` integration (FEM-4) — so tell Femin the endpoint shape the day you design it, not the day you finish it. Also blocking the whole team's honest latency claim in the report.

---

### 4.4 Femin — Interaction & UI Developer

**Goal for this sprint:** a Unity client that talks to the AI service end-to-end, and a conversation UI that makes the 1.3–1.9 s wait feel intentional rather than broken.

**Femin needs no headset for the first three tickets** — the service is at `http://127.0.0.1:8000` on the laptop and `http://192.168.0.90:8000` from anywhere else on the LAN, and the `/ask` endpoint exists precisely so Unity can be tested with zero audio plumbing.

| # | Ticket | Definition of done |
|---|---|---|
| FEM-1 | `HakuClient.cs` in `unity-scripts/`: async wrappers for `GET /health`, `GET /exhibits`, `POST /ask`, `POST /converse`, `POST /say`. Configurable host, sane timeouts, and a clear error path for "service unreachable". | Play mode in the Editor: a debug panel lists the exhibits from `/exhibits`, sends a typed question to `/ask`, and prints the answer and `ms`. No headset touched. |
| FEM-2 | Push-to-talk mic capture: `Microphone.Start` → 16 kHz mono → PCM16 WAV in memory → multipart POST to `/converse` with `exhibit_id`, `held`, `distance_m`. Play the returned WAV. | Hold a key in the Editor, speak, hear the guide answer. Round-trip timings read from `X-Haku-Ms-Stt/-Llm/-Tts/-Total` and shown on screen. |
| FEM-3 | Conversation state machine: `Idle → Listening → Thinking → Speaking → Idle`, with a visible state for each and a cancel path. Subtitle panel renders `X-Haku-Answer`; question echo renders `X-Haku-Question`. | All five states reachable and visually distinct. Subtitles are legible at 1.5 m in the headset (or in the simulator at equivalent angular size). A user can interrupt `Speaking` and ask again. |
| FEM-4 | Swap FEM-2 onto Ameen's `POST /converse/stream` and start audio playback on the first chunk instead of waiting for the whole WAV. | Perceived latency measured from key-release to first audible word, 10 trials, median recorded. Falls back to `/converse` automatically if `/converse/stream` returns 404. |
| FEM-5 | Latency HUD + failure UX: an on-screen readout of the last four timing headers, and a spoken/visual "one moment" cue if nothing has come back in 900 ms. | HUD toggleable with a debug key. Service down = a clear, calm in-world message, not a hang and not an exception spew. |

**Blocked by:** Ameen for FEM-4 (design agreed in Week 4, code lands Week 5). Sneha for the real `held` / `distance_m` values — until SNE-4 lands, hard-code `held=false, distance_m=1.5` and move on.
**Blocking:** the Week 6 playtest cannot run without FEM-2 and FEM-3.

---

### 4.5 Mazin — 3D / Environment Designer

**Goal for this sprint:** a gallery room and six of the eight hero exhibits modelled *inside the budget in §3.2*, exported to the contract, and handed to Sneha as drop-in prefabs.

**Mazin is not blocked by anything and starts tomorrow morning.** The budget above is the spec. Model to it from the first mesh — retrofitting a LOD chain onto eight finished heroes in Week 10 is how projects miss frame rate.

| # | Ticket | Definition of done |
|---|---|---|
| MAZ-1 | **Room layout + grey-box blockout**, sized for 8 exhibit stations with comfortable sightlines and a walkable loop. Deliver as a Blender file plus a top-down plan with dimensions in metres. | Sneha can build SNE-2 from it without asking a question. Blockout is ≤ 220 k triangles. Walkable area is at least 6 × 8 m, no dead ends, every station visible from at least two others. |
| MAZ-2 | **Exhibit furniture kit**: one plinth, one vitrine, one label board, one spotlight housing — sharing a single 2048 atlas, ≤ 12 k triangles per station, ≤ 14 draw calls per station before batching. | Eight copies in a test scene batch down to ~4 draw calls. Blender statistics screenshot and Unity Statistics panel screenshot both attached to the ticket. |
| MAZ-3 | **Hero exhibits 1–2, full LOD chain** (70 k / 25 k / 8 k), starting with `bronze-mirror-01` at its **real** 21.5 cm diameter and 0.8 cm thickness from Mahir's record. 3 material slots max, ASTC 6×6 maps. | Imports into Unity with no scale fix, no rotation fix, no material reassignment. Held at 20 cm in the simulator, the silhouette and the lion-and-grapevine motif read clearly. |
| MAZ-4 | **Hero exhibits 3–6**, same contract, each one modelled from Mahir's record rather than from a web image — the geometry must not assert anything the record does not. | Four more heroes delivered. Running scene total stays under 1.5 M triangles / 800 draw calls with all six heroes plus the room loaded. |
| MAZ-5 | Lighting bake pass: lightmaps ≤ 4 × 2048, one directional light, blob shadows for everything dynamic, no realtime shadow casters. | Baked scene loads in under 8 s on device and holds the frame budget. Bake settings written into `docs/PROJECT-SETTINGS.md`. |

**Blocked by:** nothing. MAZ-3 wants Mahir's records for accuracy, but `bronze-mirror-01` is already written and worked — start there.
**Blocking:** Sneha's SNE-2 (layout) and SNE-5 (furniture prefab). Get MAZ-1 to Sneha inside the first two days even if it is rough.

---

### 4.6 Mahir — Research, Content & Testing

**Goal for this sprint:** eight complete exhibit records with real citations, and the 20-question grounding test set that M4 will be graded against.

**Mahir owns the exhibit records. They are the substance of this project** — the guide is only as good, and only as honest, as the JSON behind it.

**The worked example to copy is `ai-service/exhibits/bronze-mirror-01.json`.** Schema: `id`, `title`, `intro`, `date`, `culture`, `materials`, `dimensions`, `provenance`, `facts[]`, `unknown`, `sources[]`.

**`unknown` and `sources` are mandatory fields, not optional ones.**
- **`unknown`** is what makes the guide refuse instead of invent. It is the field that produced the verified refusal quoted in §1. A record without a substantial `unknown` will hallucinate, because we have given the model nothing to decline with. Write it as prose about what is genuinely not known — the workshop, the owner, a contested interpretation.
- **`sources`** is what makes this academic work rather than a chatbot demo. **Every fact in `facts[]` must be traceable to a published source.** The bronze mirror record currently says `"PLACEHOLDER - Mahir to replace with real citations before the final report."` — that placeholder is a debt with your name on it, and it is due this sprint, not in Week 13. Note that `sources` is deliberately stripped before the record reaches the model (`build_prompt()` drops it), so citations never get read aloud — they exist for us and for the report.

| # | Ticket | Definition of done |
|---|---|---|
| MAH-1 | Replace the `sources` placeholder in `bronze-mirror-01.json` with real citations, one traceable to each of the six `facts` entries. | Six facts, six citable sources, in a consistent citation style agreed with Clemen. No placeholder strings remain in the file. |
| MAH-2 | **Write records 2–4**, chosen so Mazin can model them (physical objects with known dimensions, not paintings or manuscripts). | Three files in `ai-service/exhibits/`. Service `/health` lists 4 exhibits. Each has ≥ 6 facts, a real `unknown`, real `sources`, and a `dimensions` string Mazin can model from. |
| MAH-3 | **Write records 5–8.** Target the full set of 8 that M5 commits to. | `/exhibits` returns 8. Every record passes MAH-5's validator. |
| MAH-4 | **Build the 20-question grounding test set** for M4: ~12 answerable from the record, ~8 deliberately unanswerable (price, owner's name, "was it magic", modern comparisons), each with the expected behaviour written down (answer / decline). Include a **child-register red-team block** aimed squarely at the known defect. | `docs/GROUNDING-TESTS.md` exists with 20 questions, expected behaviour, and a pass/fail column ready to fill. |
| MAH-5 | Run the test set against the current service and log every failure verbatim. **Specifically re-test the known defect:** under *"explain like I'm nine"* the guide embellished beyond the record — *"the grapes are colorful"*, *"lions are big and strong"* — neither of which the record states. | Results table filled in with the real score. **This is not expected to be 20/20 and it must not be reported as if it were.** Grounding is real work for M4 (W8–10), not a solved problem, and this sprint's job is to measure the gap precisely so Ameen can close it. |

**Blocked by:** nothing.
**Blocking:** Mazin's accuracy on heroes 3–6 (MAZ-4), and all of M4. Get records 2–4 to Mazin by end of Week 4.

---

## 5. Definition of done for this sprint

The sprint is done when **all** of these are true. Not most.

**Toolchain**
- [ ] Unity Android Build Support installed; an APK builds and runs on the Quest (CLE-1).
- [ ] All six people can clone, build, and push; LFS holds the binaries.
- [ ] `docs/SETUP.md` and `docs/PROJECT-SETTINGS.md` exist and have been followed by someone other than their author.

**VR**
- [ ] **M1 complete:** grey-box room on device, sustained 72 fps for 3 minutes, numbers recorded.
- [ ] Locomotion + snap turn + comfort options working on device.
- [ ] Grab-inspect working on 3 exhibits, exposing `held` and `distance_m`.
- [ ] Scene is under 800 draw calls and 1.5 M triangles with 6 heroes and the room loaded.

**AI**
- [ ] `ai-service/main.py` imports and runs from a **fresh clone** (AME-1).
- [ ] Concurrent `/converse` requests no longer collide (AME-2).
- [ ] `POST /converse/stream` exists; median time-to-first-audio-byte **≤ 1.45 s**, measured over 20 runs.
- [ ] GPU whisper either working and measured, or documented as attempted-and-failed with the reason.
- [ ] `docs/LATENCY.md` published with before/after p50 and p95, and a plain statement of whether the 1.5 s promise is met.

**Content**
- [ ] 8 exhibit records exist, each with ≥ 6 sourced facts, a real `unknown`, and real `sources`. Zero placeholders.
- [ ] `docs/GROUNDING-TESTS.md` has 20 questions with expected behaviour, run once, scored honestly.

**Integration**
- [ ] In the headset: walk to an exhibit, pick it up, hold a button, ask a spoken question, hear a spoken answer, read the subtitle.
- [ ] At least one observed refusal in the headset, spoken aloud, on a question the record cannot answer.

---

## 6. Week 6 internal playtest

**When:** Friday of Week 6, booked in Week 4 by Clemen (CLE-5).
**Who:** all six of us plus 2 people from outside the team. This is the rehearsal, not the M6 user study (M6 needs n ≥ 8 external participants in W12–14) — the point is to find what breaks before we spend real participants on it.
**Duration:** 10 minutes each, on device, observed.

**The task script each tester runs:**
1. Enter the gallery, walk to any exhibit.
2. Pick it up, look at it closely, put it back.
3. Ask one question the record **can** answer.
4. Ask one question the record **cannot** answer (Mahir supplies the list).
5. Ask a follow-up that depends on the first answer.
6. Say one sentence about whether it felt like a person or a machine.

**Pass criteria — the playtest passes if:**
- 72 fps held throughout every session, no dropped frames logged.
- 3 exhibits are fully conversational.
- Median time-to-first-spoken-word ≤ 1.5 s, measured from the headers, **reported as measured**.
- Every tester witnesses at least one honest refusal and none of them describes it as a failure.
- Zero crashes and zero service restarts across all 8 sessions.

**What we capture:** per-session fps trace, all four timing headers per utterance, every question asked verbatim (this becomes M4's real test corpus), every observed embellishment, and one sentence of verbal feedback per tester.

---

## 7. Risk register — top 5 right now

| # | Risk | Impact | Owner | Mitigation |
|---|---|---|---|---|
| **R1** | **Unity Android Build Support is not installed — nothing can reach the headset.** A large module download plus SDK/NDK/JDK setup, and version mismatches between Hub, Editor and SDK are common. | **Blocks M1 entirely.** M1 is due W4–5; a week lost here cascades into M2 and the Week 6 playtest. | **Clemen** | Day 1, before anything else (CLE-1). Verified by an empty-scene APK actually launching, not by the Hub showing a tick. Meanwhile **nobody else is waiting**: Sneha uses the XR Device Simulator, Femin tests against the service on PC, Ameen and Mahir never needed a headset, Mazin is in Blender. Escalate to the lab technician if it is not resolved by end of Day 2. |
| **R2** | **The pitch deck's "under 1.5 s to first spoken word" is not met.** Measured 1.62–1.95 s. Streaming is fiddly, and GPU whisper already failed once on `cublas64_12.dll`. | Credibility. We put the number in an assessed deck; being visibly wrong about our own measurements is worse than the latency itself. | **Ameen** | Two independent fixes, either of which helps on its own: sentence-level streaming (≈300–500 ms) and GPU whisper (≈350–450 ms). Publish real p50/p95 in `docs/LATENCY.md`. **If we land at 1.6 s we say 1.6 s** and explain what we tried — a measured, explained miss reads as engineering; a fudged number reads as dishonesty. Perceptual fallback: Femin's 900 ms acknowledgement cue (FEM-5) masks the remaining gap without lying about it. |
| **R3** | **Grounding embellishment.** Under *"explain like I'm nine"* the guide invented *"the grapes are colorful"* and *"lions are big and strong"* — small, plausible, and exactly the kind of drift that undermines a museum guide. Register adaptation and strict grounding are in tension. | Goes to the heart of the project's claim. M4 requires 20/20 answer-or-decline. | **Mahir** (measurement), **Ameen** (fix) | Measure first: MAH-4/MAH-5 build and run the 20-question set with a dedicated child-register red-team block, this sprint, so M4 starts with a real number rather than a vibe. Fix levers for M4: an explicit "add no adjective or detail that is not in the record" rule, lower temperature, and richer `unknown` fields giving the model something to decline with. **We do not claim this is solved until the test set says 20/20.** |
| **R4** | **One laptop, one LAN.** The Zephyrus at `192.168.0.90` is the whole AI backend on 8 GB of VRAM, and the Quest reaches it only over Wi-Fi. Campus and venue networks routinely isolate clients, which would make the headset unable to see the service at all. Cold start is 50 s. | Total demo failure, and it would happen in the room, in front of the marker. | **Clemen** (network), **Ameen** (service) | Test the actual demo room's network in Week 5, not on the day. Carry a phone hotspot **and** a small travel router as fallback; validate both. `keep_alive=-1` stays on and the service is started 10 minutes before any demo, never during. Pre-render exhibit intro audio via `/say` so the gallery has a voice even if the LLM is down, and keep `/ask` text mode as a demonstrable fallback. Back up the venv, `requirements.txt`, the Piper voice, and the exhibit JSON off the laptop. |
| **R5** | **Art and performance budget overrun.** 8 heroes at grab-inspect fidelity is 560 k triangles before the room exists, and per-exhibit materials would blow the draw-call ceiling. Discovered late, it is a re-modelling job, not a settings change. | Misses 72 fps, which is a hard requirement for M1 and M6 and a comfort issue for testers. | **Mazin** (budget), **Sneha** (measurement) | Budget published **before the first mesh** (§3.2): 20 draw calls and 82 k triangles per exhibit station, LOD chain mandatory at export, shared atlases for all furniture, 190 draw calls and 294 k triangles held back as unallocated headroom. Sneha reports scene draw calls and triangles from an on-screen overlay every build. **Weekly Friday budget audit** — if a hero is over, it is fixed that week, not in Week 10. |

---

## 8. Rules for this sprint

1. **Measured numbers beat estimated numbers, always.** If a document says something different from what the laptop said, the laptop wins. Update the document.
2. **Never turn `think` on** for `qwen3:8b`. 4.80 s, 148 wasted tokens, no answer.
3. **Never let the model go cold.** `keep_alive=-1`, warmed at service startup, 50 s otherwise.
4. **No placeholder ships.** `sources` fields, TODO strings, and grey-box art are all fine mid-sprint and all unacceptable at the Week 6 playtest.
5. **If you are blocked for more than two hours, say so in the team chat.** Every ticket above has a route around the blocker; there is no reason for anyone to lose a day waiting on the headset.
