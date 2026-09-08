# Haku VR Museum — Setup

**What this gets you:** every one of the six of us able to build the project onto a Quest 3, and one laptop serving the AI guide over the LAN. That is M1 (W4–5) plus the plumbing M3 needs.

**Shell convention.** Every command is tagged `[PowerShell]` or `[Git Bash]`. PowerShell is the default shell on our machines. Several of these fail *silently* in the wrong shell, so read the tag.

**Everything below has been run on the ROG Zephyrus M16 already.** Numbers in this document are measured, not estimated. Where something is not yet working, it says so.

---

## ⏱ Do these two things in the next 15 minutes

Both have lead time you cannot compress by working harder. Start them, then come back and read the rest.

### 1. Start the Unity Android module download (~2.5 GB) — everyone

**This is the hard blocker for M1.** No one can put anything on a headset until it is installed. It is currently missing on the AI-host laptop and probably on yours.

Unity Hub → **Installs** → select **6000.5.5f1** → **Manage** → **Add modules** → expand **Platforms** → tick **Android Build Support**, then expand its arrow and tick **BOTH** children:

- **OpenJDK**
- **Android SDK & NDK Tools**

Ticking the parent does **not** auto-tick the children.

Or headless `[PowerShell]`:

```powershell
& "C:\Program Files\Unity Hub\Unity Hub.exe" -- --headless install-modules --version 6000.5.5f1 -m android --childModules
```

Pass **only** `-m android --childModules`. Never enumerate child IDs by hand — `android-open-jdk` is not a valid ID (it is `android-open-jdk-17.0.18+8`), and on a miss the Hub prints a suggestion and **carries on with the rest**, leaving you with no JDK and a Gradle failure that looks like a compiler bug.

Budget 20–45 min on campus Wi-Fi.

### 2. Start Meta developer-organisation verification — Clemen

Multi-**day** lead time. Nothing about Developer Mode works until it clears.

1. Go to **developers.meta.com** and sign in with **the exact Meta account that is signed in on the Quest 3**. Account mismatch is the number one cause of the "create or join a developer team" loop.
2. Create an organisation.
3. Complete security verification with **two-factor authentication**. Meta will also accept a payment method — **do not use it.** Nobody puts card details into a coursework setup.
4. Invite all six of us as members.

> **STOP — verify before continuing**
> **(1)** `[PowerShell]` `Test-Path "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Data\PlaybackEngines\AndroidPlayer"` prints **True**. Then in Unity: Edit → Preferences → External Tools shows a green tick and **"Installed with Unity (recommended)"** on the **JDK**, **Android SDK** and **Android NDK** rows.
> **(2)** developers.meta.com shows the organisation as **verified** and all six names in the members list.

**Do not** untick those External Tools boxes and point Unity at the pre-existing `%LOCALAPPDATA%\Android\Sdk`. That SDK came from another tool, lacks NDK r27c, and produces IL2CPP link failures.

---

# PART A — One person does this once

**Integrator: Sneha.** If two people do Part A in parallel we end up with two incompatible projects.

## A1. Get the repo out of OneDrive

The project folder currently sits under `OneDrive\Documents\Rmit\Mixed Reality\assin1`. A Unity project cannot live there:

- Unity's `Library/ArtifactDB` is a live database file; OneDrive opens it to upload and Unity needs exclusive access → "Cannot access database - Locked".
- Files On-Demand replaces synced files with 0-byte stubs. A dehydrated `.fbx` imports as an empty mesh; a dehydrated file inside `.git` corrupts the object store.
- OneDrive does not merge — it writes `Museum_Base-DESKTOP-8KQ2A1.unity`, and now two assets claim the **same GUID**. Silent until someone opens the scene.
- The path is 62 characters before the project even starts, and contains a space.

`[PowerShell]`:

```powershell
New-Item -ItemType Directory -Force C:\Dev\haku-museum
Set-Location C:\Dev\haku-museum
git init
New-Item -ItemType Directory -Force docs, unity, ai-service, scripts
Move-Item "C:\Users\mazin\OneDrive\Documents\Rmit\Mixed Reality\assin1\haku\ai-service\*" .\ai-service\
Move-Item "C:\Users\mazin\OneDrive\Documents\Rmit\Mixed Reality\assin1\haku\docs\*" .\docs\
```

**`C:\Dev\haku-museum` on all six machines.** Short, no spaces, and specifically **not** under `Documents`, which Windows 11 backs up to OneDrive by default.

The `.docx` report and `.pptx` pitch deck stay in OneDrive. They are inert documents and sync suits them. Only the live repo moves.

**Do not create the Unity project yet.** The ignore files must be commit #1.

> **STOP — verify before continuing**
> `Get-Location` prints `C:\Dev\haku-museum`; `ai-service\main.py` and `ai-service\config.py` exist there; `git status` says "On branch main / No commits yet".

## A2. Git hygiene — this must be commit #1

`git lfs track` is **not retroactive.** An `.fbx` committed before `.gitattributes` exists is a raw blob in history forever and still counts against our 10 GiB quota after you "fix" it. Fixing it means `git lfs migrate import --everything` and a force-push that rewrites history for all six of us.

**`C:\Dev\haku-museum\.gitignore`:**

```gitignore
# OS / editor
.DS_Store
Thumbs.db
desktop.ini
.idea/
.vs/
.vscode/*

# Secrets
.env
.env.*
!.env.example
*.keystore
*.jks

# Merge-tool leftovers
*.orig
*.rej
*.BACKUP.*
*.BASE.*
*.LOCAL.*
*.REMOTE.*

# Python AI service
__pycache__/
*.py[cod]
.venv/
venv/

# MODEL WEIGHTS: NEVER COMMIT. One of these blows the whole team's LFS quota.
*.gguf
ggml-*.bin
*.safetensors
*.pt
/ai-service/voices/
*.onnx.json

# AI service runtime scratch
/ai-service/_incoming.wav
/ai-service/logs/
/ai-service/**/*.wav

# Build output
/build/
*.log
```

**`C:\Dev\haku-museum\unity\.gitignore`** — this goes in `unity\`, **not** at the root. The canonical file uses `/[Ll]ibrary/`, which anchors to the folder holding the `.gitignore`. At the root it will not match `unity/Library/` and someone pushes a multi-gigabyte Library.

`[PowerShell]`:

```powershell
Invoke-WebRequest -Uri https://raw.githubusercontent.com/github/gitignore/main/Unity.gitignore -OutFile C:\Dev\haku-museum\unity\.gitignore
```

Append to the bottom of that file:

```gitignore
# --- Haku additions ---
*.keystore
*.jks
*.orig
*.rej
/[Aa]ssets/[Ss]treamingAssets/models/
*.gguf
ggml-*.bin
# DO NOT ignore Assets/XR/ — that is the XR loader config and MUST be committed.
# DO commit Packages/manifest.json, Packages/packages-lock.json, and all of ProjectSettings/.
```

**`C:\Dev\haku-museum\.gitattributes`** — Unity YAML stays **text** so it can diff and merge; only real binaries go to LFS.

```gitattributes
* text=auto

# Unity YAML: text, LF, SmartMerge. NEVER LFS.
*.unity              text eol=lf merge=unityyamlmerge lockable
*.prefab             text eol=lf merge=unityyamlmerge
*.asset              text eol=lf merge=unityyamlmerge
*.mat                text eol=lf merge=unityyamlmerge
*.anim               text eol=lf merge=unityyamlmerge
*.controller         text eol=lf merge=unityyamlmerge
*.physicMaterial     text eol=lf merge=unityyamlmerge
*.mask               text eol=lf merge=unityyamlmerge
*.preset             text eol=lf merge=unityyamlmerge
*.meta               text eol=lf merge=unityyamlmerge

# Source and config: text, no LFS
*.cs      text eol=lf diff=csharp
*.shader  text eol=lf
*.hlsl    text eol=lf
*.asmdef  text eol=lf
*.json    text eol=lf
*.md      text eol=lf
*.py      text eol=lf
*.yml     text eol=lf
*.ps1     text eol=crlf
*.bat     text eol=crlf

# LFS: binaries only
*.fbx  filter=lfs diff=lfs merge=lfs -text
*.blend filter=lfs diff=lfs merge=lfs -text
*.obj  filter=lfs diff=lfs merge=lfs -text
*.glb  filter=lfs diff=lfs merge=lfs -text
*.png  filter=lfs diff=lfs merge=lfs -text
*.jpg  filter=lfs diff=lfs merge=lfs -text
*.psd  filter=lfs diff=lfs merge=lfs -text
*.tga  filter=lfs diff=lfs merge=lfs -text
*.exr  filter=lfs diff=lfs merge=lfs -text
*.hdr  filter=lfs diff=lfs merge=lfs -text
*.wav  filter=lfs diff=lfs merge=lfs -text
*.mp3  filter=lfs diff=lfs merge=lfs -text
*.ogg  filter=lfs diff=lfs merge=lfs -text
*.mp4  filter=lfs diff=lfs merge=lfs -text
*.ttf  filter=lfs diff=lfs merge=lfs -text
*.otf  filter=lfs diff=lfs merge=lfs -text
*.onnx filter=lfs diff=lfs merge=lfs -text
*.dll  filter=lfs diff=lfs merge=lfs -text
*.aar  filter=lfs diff=lfs merge=lfs -text
*.zip  filter=lfs diff=lfs merge=lfs -text

# Bare filename, NO slash. A slashed path anchors to the repo root and never matches.
packages-lock.json  -diff
```

`*.unity` is `lockable` but deliberately **not** `filter=lfs` — scenes must stay text. `lockable` works without LFS tracking.

`[PowerShell]`:

```powershell
git add .gitignore .gitattributes unity\.gitignore
git commit -m "chore: git hygiene - ignore rules, LFS tracking, SmartMerge attributes"
```

> **STOP — verify before continuing**
> `git log --stat` shows one commit, three files. `git check-attr filter -- unity/Assets/test.fbx` prints `filter: lfs`. `git check-ignore -v unity/Library/ArtifactDB` prints a matching rule (silence = the unity `.gitignore` is in the wrong place).

## A3. Push to GitHub

Create an **empty** repo (no README, no .gitignore — we have them), then `[PowerShell]`:

```powershell
git remote add origin https://github.com/<owner>/haku-museum.git
git branch -M main
git push -u origin main
```

**Pick the owner deliberately.** All six people's clones and pulls bill LFS bandwidth to the repo owner. Nominate someone who can see Settings → Billing — **Clemen**.

**The quota is 10 GiB storage AND 10 GiB bandwidth per month, on Free and on Pro.** The Student Developer Pack does not raise it. The two overages fail completely differently:

- **Storage exceeded** (history counts) → clones still *succeed*, but hand out ~130-byte pointer files instead of assets, and pushes are refused. Unity imports garbage, materials go magenta, meshes vanish, and everyone concludes the repo is broken and re-clones. **This is the silent killer.**
- **Bandwidth exceeded** → `git lfs pull` fails loudly. Easier.

Four rules that keep us inside it:

1. **Never commit model weights.** qwen3:8b is 5.2 GB — half the annual storage in one file. Already ignored.
2. **Never commit `.apk`s.** Hand builds to the tutor via a GitHub **Release** (releases do not consume LFS bandwidth).
3. **Keep source art out.** Raw `.blend` sculpts and 4K PSDs go to a shared OneDrive folder. The repo gets the exported, decimated `.fbx` and 1K/2K textures that actually ship. This is a 10x lever — Mazin owns it.
4. **Clone once. Never re-clone.** Six people × 1.5 GB = 9 GiB, the entire month, in one lab session. When a merge goes wrong: `git reset --hard origin/main`, never delete the folder.

> **STOP — verify before continuing**
> The repo shows three files and one commit on github.com, and Settings → Billing → Git LFS shows 0 / 0.

## A4. Create the Unity project

Unity Hub → **New project**. Editor **6000.5.5f1**. Template **VR** (not "Mixed Reality" — that pulls AR Foundation and passthrough plumbing a walled museum room never uses, and it costs frame time). Location `C:\Dev\haku-museum`, project name `unity`, so it lands at `C:\Dev\haku-museum\unity`.

**Edit → Project Settings → Editor:**

- **Asset Serialization → Mode = Force Text.** Default in Unity 6, but verify. Binary scenes are unmergeable and every conflict becomes total. Without this the rest of this document is worthless.
- **Version Control → Mode = Visible Meta Files.** Without committed `.meta` files everyone's Unity assigns different GUIDs and every cross-asset reference breaks.
- **Line Endings For New Scripts = Unix.**

Verify on disk rather than trusting the UI `[PowerShell]`:

```powershell
Select-String -Path unity\ProjectSettings\EditorSettings.asset -Pattern "m_SerializationMode"
Get-Content unity\ProjectSettings\VersionControlSettings.asset
```

Expect `m_SerializationMode: 2` and `m_Mode: Visible Meta Files`.

```powershell
git add unity\ProjectSettings unity\Packages\manifest.json unity\Packages\packages-lock.json
git commit -m "chore(unity): Force Text serialization, Visible Meta Files, pinned packages"
```

`packages-lock.json` pins the resolved XR package versions so all six of us get an identical graph. `ProjectVersion.txt` is committed too — **everyone runs 6000.5.5f1 exactly.** Someone on 6000.5.6f1 silently rewrites it and triggers a full reimport for the rest of us. **Turn off Unity Hub auto-update.**

> **STOP — verify before continuing**
> Unity opens with no console errors; both values confirmed on disk; commit pushed.

## A5. Quest build profile and XR packages

**Meta Quest is not a platform in the Platforms list.** It is a build profile you create: **File → Build Profiles → "Add Build Profile" → Meta Quest**, then activate it. Creating the profile is what auto-installs OpenXR — accept that prompt.

The first switch triggers a full reimport to ASTC. **5–20 minutes. Do not interrupt it.**

**Window → Package Manager → Unity Registry.** Install exactly two things:

- **OpenXR Plugin** (`com.unity.xr.openxr`)
- **XR Interaction Toolkit** (`com.unity.xr.interaction.toolkit`), plus from its **Samples** tab: **Starter Assets** and **XR Interaction Simulator** (**not** "XR Device Simulator (Legacy)").

**That is the entire stack M1 needs.** Do not install:

| Package | Why not |
|---|---|
| `com.unity.xr.oculus` (Oculus XR Plugin) | **Deprecated starting in Unity 6.5** — our exact editor. Every tutorial saying "XR Plug-in Management → tick Oculus" is dead on arrival. |
| `com.unity.xr.meta-openxr` | An AR Foundation provider for passthrough. Hard-depends on AR Foundation 6.0. A fully-virtual museum uses none of it. |
| Meta XR SDK / `com.meta.xr.sdk.voice` | Voice SDK is backed by Meta's cloud (Wit.ai) and breaks our self-hosted constraint outright. Our speech path is faster-whisper on the laptop. Rule it out now. |

**Edit → Project Settings → XR Plug-in Management → Android tab → tick OpenXR.** A warning triangle appears; click it and **Fix All**. Then under **OpenXR** (Android tab):

- Enabled Interaction Profiles → **+** → **Oculus Touch Controller Profile**
- OpenXR Feature Groups → tick **Meta Quest** (read the actual label in the tab; sources disagree on the exact string)
- **Render Mode = Multi-view.** Multi-Pass literally doubles draw calls. Biggest free GPU win available.
- Depth Submission Mode = **None**
- Foveated Rendering API = **SRP Foveation**
- Latency Optimization = **Prioritize Input Polling**
- Additional Graphics Queue = **OFF**
- Offscreen Rendering Only = **ON**

**⚙ beside Meta Quest Support:**

- Target Devices → **Quest 3**
- Symmetric Projection (Vulkan) = **ON**, Optimize Buffer Discards (Vulkan) = **ON**, Late Latching (Vulkan) = **ON** (Late Latching Debug Mode **OFF**)
- Multiview Render Regions Optimizations = **All Passes**
- **🚨 Force Remove Internet Permission = OFF.** Meta's own checklist recommends turning this on. It strips `android.permission.INTERNET` and **every call to our AI service dies with no useful error.**

**Then do the PC tab too** (Windows, Mac, Linux → tick OpenXR, add the Oculus Touch Controller Profile, enable the Meta Quest feature group). Play Mode uses the Standalone settings, not the Android ones. Skip this and pressing Play with the headset tethered gives you a black headset and a flat Game view.

> **STOP — verify before continuing**
> The Meta Quest build profile exists and is active. XR Plug-in Management → Android shows OpenXR ticked with **no warning triangle**. `unity/Packages/manifest.json` contains `com.unity.xr.openxr` and `com.unity.xr.interaction.toolkit`, and does **not** contain `com.unity.xr.oculus` or `com.unity.xr.meta-openxr`.

## A6. Player Settings, URP, Quality

**Player → Android tab → Other Settings.** These live in three different sections, not one.

*Rendering:*
- Color Space = **Linear** (project-wide, painful to change later)
- Auto Graphics API = **UNTICKED**; Graphics APIs = **Vulkan only** (remove OpenGLES3 — GLES disables FFR, buffer discards, symmetric projection and late latching)
- Multithreaded Rendering = **ON**
- Static Batching = **ON**
- Dynamic Batching = **OFF** (deprecated in 6.5; the SRP Batcher supersedes it)
- GPU Skinning = **"GPU (Batched)"** — it is a three-value dropdown now, not a checkbox

*Identification:*
- Package Name = **`com.rmit.haku.museum`** (you must change it off `com.DefaultCompany.*`)
- **Minimum API Level = Android 12L (API 32)**
- **Target API Level = Android 14 (API 34)**

*Android Application Configuration:*
- **Internet Access = Require**
- Write Permission = Internal, Install Location = Automatic
- Application Entry Point = **GameActivity** (Unity 6 default — leave it)
- Scripting Backend = **IL2CPP**, API Compatibility = **.NET Standard 2.1**
- Target Architectures = **ARM64 only** (untick ARMv7)

*Configuration:*
- **Allow downloads over HTTP = "Always Allowed."** Default is "Not Allowed". This is **gate 1 of 2** for our LAN calls — see B5.

**Resolution and Presentation:** Landscape Left; **untick "Optimized Frame Pacing"** (the XR compositor owns pacing and this fights it).
**Publishing Settings:** leave Custom Keystore off for M1 (Unity signs sideloads with a debug key). **Minify = None** — R8 hides the IL2CPP stack traces you will need. **Tick "Custom Main Manifest"** — needed in B5.

**URP Asset:** Depth Texture **OFF**, Opaque Texture **OFF**, **HDR OFF** (the Quest display is 8-bit; HDR doubles resolve cost for nothing), **MSAA 4x** (and set Anti-aliasing on the Camera component too or you get none), **Render Scale 1.0 — never below 0.85**, SRP Batcher **ON**, Post-processing **OFF** for M1, one shadow-casting directional light maximum, Additional Lights **Disabled**, Shadow Max Distance 15–25 m, Cascades **1**, Soft Shadows **Disabled or Low**.

**Universal Renderer:** Rendering Path = **Forward**, not Forward+. A baked museum has 0–1 dynamic lights; Forward+ only wins above ~30 lights. Intermediate Texture = **Auto**. No SSAO, Decal or Screen Space Shadows features.

**Quality:** VSync = **Don't Sync** (the compositor governs), Realtime Reflection Probes **OFF**.

### The budget — Mazin, this table is yours

Official Quest 3 ceiling for a *light* scene: **700–1000 draw calls, 1.3–1.8 M triangles**. (Not the 200–300 figure tutorials quote — that is the busy-simulation row and chasing it wastes days on pointless batching.)

**Our design target is deliberately a third of that: ≤200 draw calls, ≤600k visible triangles**, leaving CPU headroom for the AI networking and audio.

| Asset | Triangles | Materials |
|---|---|---|
| Hero artefact (grabbable, held to the face) | 15,000–30,000 | **ONE.** Normal-map the detail; do not model it. |
| Secondary exhibit / plinth | 2,000–5,000 | 1, shared atlas |
| Small prop | 300–1,500 | shared atlas |
| Modular architecture piece | 200–2,000 | shared atlas; reuse 6–10 modules for the whole museum |
| Grey-box room shell (M1) | under 20,000 | 1–2 |

**Materials cost more than triangles.** Switching shader is the most expensive thing a draw call can do; redrawing the same object is the cheapest. Fewer unique materials beats fewer triangles every time.

**Textures:** 2048² only for hero artefacts; 1024² for plinths and mid-ground; 512² for architecture. **ASTC, mipmaps always on.** Total app memory limit is 5.75 GiB — exceed it and Android kills the app, which reads as "it crashes randomly".

**72 fps = 13.9 ms per frame.** Build M1 at 72 Hz: it is the Quest 3 default so you write no code at all. 90 Hz cuts the budget 20% for zero marks.

> **STOP — verify before continuing**
> Graphics APIs lists Vulkan only. Package Name is `com.rmit.haku.museum`. ARM64 ticked, ARMv7 unticked. URP Asset: HDR off, MSAA 4x, post-processing off. `ProjectSettings/` committed and pushed.

## A7. Scenes and ownership

A `.unity` file is YAML keyed by fileIDs. Two people adding objects produces overlapping hunks that merge into *syntactically valid but semantically broken* YAML — and it stays silent until someone opens the scene. The structural fix matters far more than any merge tool: **one scene, one owner.**

| Scene | Contents | Owner |
|---|---|---|
| `00_Bootstrap.unity` | Camera + Bootstrapper only. Build index 0. Almost never edited. | Clemen |
| `10_Museum_Shell.unity` | Architecture, floor, walls, lighting, route path | Mazin |
| `20_Exhibits_WingA.unity` | Exhibit prefab instances | Mahir |
| `30_Player_XRRig.unity` | XR rig, locomotion, hands, interaction | Sneha |
| `40_UI_Diegetic.unity` | World-space UI, subtitles, guide avatar | Femin |
| `50_AI_Guide.unity` | Guide client, mic capture, TTS playback | Ameen |

Bootstrapper loads the rest additively: `SceneManager.LoadScene("10_Museum_Shell", LoadSceneMode.Additive);`

**🚨 Add ALL SIX scenes to File → Build Profiles → Scene List, with `00_Bootstrap` at index 0.** A scene missing from that list loads fine in the Editor whenever it happens to be open in the Hierarchy, and fails **only on the Quest** — so the failure surfaces exactly at M1 and looks like a device problem. (Add `21_Exhibits_WingB` at M5 when we go to eight exhibits.)

**Honest cost of the split:** lightmaps, light probes and occlusion data are per-scene build-time artifacts — bake with all scenes loaded together. Static batching does not span the split, so draw calls go up. Split along architectural seams, never through a single visible space.

**Prefab-first.** Nothing lives loose in a scene. Every exhibit, interactable and UI panel is a prefab; a scene holds only instances and transforms. Edit in **Prefab Mode** — overriding in the scene stores the override in the scene file and reintroduces the conflict you just avoided.

**Social rules (put these in `docs/OWNERS.md`):**

- You may only save a scene you own. Need a change elsewhere? Ask the owner or send a prefab.
- Branch per task, merged within 48 hours.
- **Pull before you open Unity. Close Unity before you pull.** Push before you leave the lab.
- Weekly integration session: one person, one machine, verify the Quest build still hits 72 fps.
- **If a scene conflicts anyway, do NOT hand-merge YAML.** Decide who did less work, take the other side whole, and redo the 20 minutes. Agree this now so nobody argues about it at 2am. During a merge, `--ours` is the branch you are **on**; `--theirs` is the branch being **merged in**. Taking one side of the `.unity` does not revert the `.meta`/`.prefab`/`.cs` files the other side added — `git status` and decide those separately.

> **STOP — verify before continuing**
> All six scenes exist, all six are in the Scene List with `00_Bootstrap` at index 0, and `docs/OWNERS.md` names one owner per scene. Pushed.

---

# PART B — Every member, on their own machine

**B1 (the Android module, top of this document) blocks everything else.**

## B2. Git config and clone

Run once per machine `[PowerShell]` — identical in Git Bash:

```powershell
git config --global user.name  "Your Real Name"
git config --global user.email "you@student.rmit.edu.au"
git config --global core.longpaths true
git config --global core.autocrlf true
git config --global pull.rebase true
git config --global init.defaultBranch main
git lfs install
```

**`git lfs install` is the #1 onboarding failure.** Skip it and the clone succeeds with no error, but every texture and mesh is a ~130-byte pointer file. Unity imports garbage and you conclude the repo is broken.

**`core.longpaths` affects Git only** — it does nothing for Unity, Gradle, the NDK or IL2CPP, none of which go through Git. What *those* need is the OS policy `HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled = 1`. **Check yours individually.** And even then many Java-based Android tools still enforce 260 characters — which is the real reason we use `C:\Dev\haku-museum`.

```powershell
Set-Location C:\Dev
git clone https://github.com/<owner>/haku-museum.git
```

> **STOP — verify before continuing**
> `git lfs env` prints a valid config. Once art exists: `Get-Content unity\Assets\...\<some>.fbx -TotalCount 1`. If it starts with `version https://git-lfs.github.com/spec/v1` it is a pointer — fix with `git lfs pull`, **never** by re-cloning.
> Two causes, distinguishable: missing `git lfs install` → `git lfs pull` succeeds. Exhausted LFS **storage** → `git lfs pull` fails. Check Settings → Billing before you go debugging Unity.

## B3. Unity SmartMerge

`%USERPROFILE%\.gitconfig`. `[PowerShell]` — **the closing `'@` must be at column 0:**

```powershell
$cfg = @'

[merge]
	tool = unityyamlmerge

[mergetool]
	keepBackup = false

[mergetool "unityyamlmerge"]
	trustExitCode = false
	cmd = 'C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"

[merge "unityyamlmerge"]
	name = Unity SmartMerge
	driver = 'C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -h -p --force --fallback none %O %B %A %A
	recursive = binary
'@
Add-Content -Path "$env:USERPROFILE\.gitconfig" -Value $cfg -Encoding utf8
```

Both blocks are needed and do different jobs: `[mergetool ...]` is what `git mergetool` invokes by hand; `[merge ...]` is the **driver** that `merge=unityyamlmerge` in `.gitattributes` resolves against, and it is what makes `git pull` run SmartMerge automatically. `keepBackup` has **no per-tool form** — inside the tool section it is silently ignored and `.orig` files pile up invisibly, which is why it sits in its own `[mergetool]` block.

**Smoke-test it in Week 4, not the night before submission** `[PowerShell]`:

```powershell
git checkout -b smoketest-a   # add Cube A in Unity, save, commit
git checkout main
git checkout -b smoketest-b   # add Cube B, save, commit
git checkout smoketest-a
git merge smoketest-b
```

| What you see | Verdict |
|---|---|
| Both cubes present, clean exit | Wired up |
| Non-zero exit, partial merge **without** conflict markers | Wired up; this particular merge is genuinely unresolvable |
| `<<<<<<<` / `=======` markers | Driver **not** invoked |

> **STOP — verify before continuing**
> `git check-attr merge -- unity/Assets/_Project/Scenes/10_Museum_Shell.unity` prints `merge: unityyamlmerge`, and the smoke test lands in row 1 or 2.

## B4. adb on PATH

Two adb versions will fight: the pre-existing `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe` (v35.x) and Unity's own v36.0.0 installed by the Android module. Mixing them gives `adb server version doesn't match this client` and **the device disappears from Unity's Run Device dropdown mid-session.**

**Use Unity's.** Permanent, user-level `[PowerShell]`:

```powershell
[Environment]::SetEnvironmentVariable('PATH',
  "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools;" + [Environment]::GetEnvironmentVariable('PATH','User'),
  'User')
```

Open a new terminal afterwards. Note `%LOCALAPPDATA%` does **not** expand in PowerShell — that is `$env:LOCALAPPDATA`. Pasting the `%VAR%` form gives a path error and people conclude adb is missing.

> **STOP — verify before continuing**
> `adb version` prints **36.0.0**. On a later mismatch: `adb kill-server` then `adb start-server` from the copy you are keeping.

## B5. Headset pairing, Developer Mode, and the two cleartext gates

**Prerequisite: the Meta organisation is verified.**

On your phone, **Meta Horizon app** → headset icon → your Quest 3 → **Headset Settings** → **Developer Mode** → ON. **Reboot the headset** — it frequently does not take effect until you do.

If the toggle is missing or it demands you "create or join a developer team": (1) the org is not verified yet, (2) the phone app is signed into a different Meta account than the headset, (3) headset firmware is stale.

**🚨 Use a USB-C cable that carries DATA.** The white cable in the Quest 3 box is **power-only** and fails silently. Use a known-good phone data cable, into a direct motherboard port, not a hub.

Plug in, **put the headset on**, Quick Settings → Settings → System → Developer → toggle **MTP Notification** on. A dialog appears **inside the headset** asking to allow USB debugging — tick "Always allow from this computer". Staring at the laptop waiting for a popup is how people conclude the cable is broken.

### The two cleartext gates — fix BOTH

This is the most confusing failure in the project: it works perfectly in the Editor and fails on the Quest with a generic "Unknown Error" that never mentions cleartext.

**Gate 1 — Unity's:** Player Settings → Other Settings → Configuration → **Allow downloads over HTTP = "Always Allowed"**. Already set in A6. Checked inside `UnityWebRequest` before a socket is ever opened.

**Gate 2 — Android's** (cleartext blocked by default since API 28):

1. Player Settings → **Publishing Settings** → tick **"Custom Main Manifest"**. This generates `Assets/Plugins/Android/AndroidManifest.xml`. **This is the prerequisite every tutorial skips** — the file does not exist in a fresh project.
2. In the generated file, add `android:usesCleartextTraffic="true"` to the existing `<application>` tag, and add as children of `<manifest>`:
   ```xml
   <uses-permission android:name="android.permission.RECORD_AUDIO" />
   <uses-permission android:name="android.permission.INTERNET" />
   ```
3. **Leave the generated `<activity>` block exactly as Unity wrote it.** Unity 6 uses **GameActivity**; pasting an old manifest with `UnityPlayerActivity` / `@style/UnityThemeSelector` installs, launches, and instantly crashes with a `Theme.AppCompat` error.

Confirm with `adb logcat` while a request fires. Android's rejection appears there verbatim as `Cleartext HTTP traffic to 192.168.0.90 not permitted` — the one unambiguous signal you get.

**Ownership warning:** Meta's *Meta → Tools → Android Manifest Tool* writes to the same path and will wipe the cleartext attribute and RECORD_AUDIO. Put a `HAKU: do not regenerate` banner at the top, give Femin ownership, and diff it in review.

> **STOP — verify before continuing**
> `[PowerShell]` `adb devices` prints your serial followed by **`device`**. `unauthorized` → redo the in-headset allow prompt. Nothing at all → `adb kill-server; adb start-server`, then a different port, then a different cable.

## B6. The AI service — Ameen, on the ROG Zephyrus M16 only

**This is already built and tested. Do not rewrite it.** `ai-service/main.py`, `config.py`, `requirements.txt` and `exhibits/bronze-mirror-01.json` are in the repo and verified working: the guide answers grounded questions correctly, refuses cleanly when the record doesn't cover something, and adapts its register for a nine-year-old.

The host is the **ROG Zephyrus M16 (i9-12900H, 32 GB, RTX 3070 Ti 8 GB), LAN IP 192.168.0.90.** That is the address the Quest connects to. Nobody else needs to run this.

### B6.1 Venv and dependencies

`[PowerShell]`, from `C:\Dev\haku-museum\ai-service`:

```powershell
Set-Location C:\Dev\haku-museum\ai-service
python -m venv .venv
.venv\Scripts\python -m pip install --upgrade pip
.venv\Scripts\pip install -r requirements.txt
```

That is fastapi, uvicorn, python-multipart, requests, faster-whisper and piper-tts. Verified on Python 3.13.7.

**ffmpeg genuinely does not matter.** faster-whisper goes through PyAV, which statically links the ffmpeg libraries inside its wheel. Do not install ffmpeg; do not resample anything in C#.

### B6.2 Download the Piper voice

```powershell
.venv\Scripts\python -m piper.download_voices en_US-lessac-medium --data-dir voices
```

Produces `voices/en_US-lessac-medium.onnx` (63 MB) and `voices/en_US-lessac-medium.onnx.json` (5 KB). **Both files are required** — the `.json` holds the phoneme map and sample rate. Both are gitignored; never commit them.

`config.py` looks for `voices/en_US-lessac-medium.onnx` **relative to the working directory**, so always launch uvicorn from `ai-service`.

**For the report's third-party components table:** Piper's active repo is **OHF-Voice/piper1-gpl** and it is **GPL-3.0**. (`rhasspy/piper` is archived and was MIT — nothing was relicensed, a separate successor was started under a different licence.) We invoke Piper as a separate service, so the GPL does not propagate to the Unity app — but **do not link or embed Piper code into Unity**, and list the licence correctly.

### B6.3 The two Ollama environment variables

Ollama is installed and running with `qwen3:8b` pulled (5.2 GB, Q4_K_M). Two user-level variables:

| Variable | Value | Why |
|---|---|---|
| `OLLAMA_HOST` | `0.0.0.0:11434` | Binds the server to the LAN instead of loopback. |
| `OLLAMA_KEEP_ALIVE` | `-1` | **Measured: a cold model load costs 50 SECONDS.** `-1` pins the model in VRAM permanently. Mandatory. |

**Set them through Windows Settings, not a shell.** A shell variable only affects the CLI client you launch from it; the *server* is an already-running background process started by the tray app and keeps its original bind. Teams "set it" and still get connection refused.

1. **Quit Ollama from the system tray.** Closing the window is not enough.
2. Windows Settings → search "environment variables" → **"Edit environment variables for your account"**.
3. Add both user variables.
4. **Relaunch Ollama from the Start menu.**

**🚨 This breaks the `ollama` CLI.** The Windows CLI reads `OLLAMA_HOST` as its *client target* too, and tries to connect to `0.0.0.0`, which is not routable. `ollama list` and `ollama pull` will hang. In any shell where you use the CLI, first run `[PowerShell]`:

```powershell
$env:OLLAMA_HOST='127.0.0.1:11434'
```

The server keeps its 0.0.0.0 binding from the machine-level variable. **Do all your pulls before setting it and you dodge this entirely.**

**And do not pull a second model while one is pinned.** With `keep_alive=-1` the pinned model never goes idle, so it is never eligible for eviction and the new request **queues, potentially forever, with no error text.** It looks like a hang, not a failure. Pick one model and stay on it; `ollama stop <model>` releases it deliberately.

Verify `[PowerShell]`:

```powershell
netstat -ano | Select-String 11434
```

Must show **`0.0.0.0:11434`**, not `127.0.0.1:11434`.

### B6.4 Firewall and network profile

**ADMIN PowerShell:**

```powershell
New-NetFirewallRule -DisplayName "Haku AI Service" -Direction Inbound -Protocol TCP -LocalPort 8000 -Action Allow -Profile Private
New-NetFirewallRule -DisplayName "Ollama LAN" -Direction Inbound -Protocol TCP -LocalPort 11434 -Action Allow -Profile Private
Get-NetConnectionProfile
Set-NetConnectionProfile -InterfaceAlias "Wi-Fi" -NetworkCategory Private
```

The Quest only ever talks to **port 8000** — Unity never speaks to Ollama, whisper or Piper directly. Port 11434 is opened purely so we can prove the LAN path from a phone browser before Unity exists, which is much cheaper than debugging a C# coroutine.

**The network profile matters as much as the rules.** Windows blocks inbound LAN traffic hard on Public profiles, and campus Wi-Fi almost always defaults to Public.

### B6.5 Run it

```powershell
Set-Location C:\Dev\haku-museum\ai-service
.venv\Scripts\python -m uvicorn main:app --host 0.0.0.0 --port 8000
```

Startup loads exhibits, then faster-whisper, then Piper, then fires a throwaway Ollama request to pin the model. Watch for these lines:

```
[haku] 1 exhibit(s): ['bronze-mirror-01']
[haku] loading STT base.en
[haku] loading TTS voices/en_US-lessac-medium.onnx
[haku] LLM warm and pinned
[haku] ready
```

**Never restart this between demo runs if you can avoid it** — the first request after a restart pays the 50 s cold load unless the warm-up completed.

### B6.6 The tests that are known to work

**🚨 `curl.exe`, not `curl`.** In PowerShell `curl` is an alias for `Invoke-WebRequest`, so `curl -s http://...` fails with "missing mandatory parameters: Uri" — at exactly the moment you are trying to work out whether the service is alive, so you blame the service. In Git Bash bare `curl` is fine.

`[PowerShell]`:

```powershell
curl.exe -s http://127.0.0.1:8000/health
curl.exe -s http://127.0.0.1:8000/exhibits
```

`/health` must show `"ok":true`, `"ollama":true`, `"llm_available":true`, `"stt_loaded":true`, `"tts_loaded":true`.

**Text round trip.** PowerShell mangles inline JSON, so use a file `[PowerShell]`:

```powershell
'{"exhibit_id":"bronze-mirror-01","question":"What is this made of?"}' | Set-Content ask.json -Encoding utf8
curl.exe -s -X POST http://127.0.0.1:8000/ask -H "Content-Type: application/json" -d "@ask.json"
```

Same thing `[Git Bash]`:

```bash
curl -s -X POST http://127.0.0.1:8000/ask \
  -H "Content-Type: application/json" \
  -d '{"exhibit_id":"bronze-mirror-01","question":"What is this made of?"}'
```

**TTS only** `[PowerShell]`:

```powershell
curl.exe -s -X POST http://127.0.0.1:8000/say -F "text=Welcome to the Haku Museum." -o hello.wav
```

**Full audio-in, audio-out loop.** Make a test question with `/say`, then feed it to `/converse` `[PowerShell]`:

```powershell
curl.exe -s -X POST http://127.0.0.1:8000/say -F "text=What is this mirror made of" -o question.wav
curl.exe -s -X POST http://127.0.0.1:8000/converse `
  -F "audio=@question.wav;type=audio/wav" `
  -F "exhibit_id=bronze-mirror-01" -F "held=true" -F "distance_m=0.4" `
  -D headers.txt -o reply.wav
Get-Content headers.txt
```

`headers.txt` carries the whole story: `X-Haku-Question`, `X-Haku-Answer`, and the per-stage timings `X-Haku-Ms-Stt`, `X-Haku-Ms-Llm`, `X-Haku-Ms-Tts`, `X-Haku-Ms-Total`. **Read those numbers every time** — they are how you tell which stage regressed, and they are free evidence for the report.

**Prove the LAN path before touching Unity.** From a **phone or another laptop on the same Wi-Fi**, open `http://192.168.0.90:8000/health` in a browser. You must get the JSON. Also try `http://192.168.0.90:11434/` — plain text "Ollama is running".

> **STOP — verify before continuing**
> `/health` returns `"ok":true` from another device on the Wi-Fi, and `/converse` returns a `reply.wav` you can actually play with `X-Haku-Ms-Total` under about 2000.
> **If the browser check fails there are two independent causes:** (a) the Windows network profile is Public — `Set-NetConnectionProfile`; (b) **campus AP/client isolation** blocks device-to-device traffic *at the access point* and cannot be fixed from either machine.

### B6.7 The highest-risk unknown: RMIT Wi-Fi — Mahir, test this in the actual demo room, this week

eduroam and other institutional networks commonly enable **AP/client isolation**. Headset and laptop can both have internet and still be unable to see each other, and no firewall or `OLLAMA_HOST` change fixes it, because the block is upstream of both.

Mitigations in order: **(1) a cheap travel router** with laptop + Quest on its own SSID; (2) Windows Mobile Hotspot on the laptop; (3) phone hotspot for both.

**Never hardcode `192.168.0.90` into a shipped APK.** DHCP changes it between sessions, and a travel router puts us on a different subnet entirely — a hardcoded IP breaks during the demo and needs a full rebuild to fix. Put the base URL in a JSON file read from `Application.persistentDataPath` that we can push with `adb push`. For USB dev, `adb reverse tcp:8000 tcp:8000` lets you use `127.0.0.1` — **but still set `usesCleartextTraffic`**, because localhost is not exempt from Android's cleartext block at Quest's API level.

---

# PART C — The prove-it-works checkpoint

This is M1's evidence. Everything above is preparation.

## C1. Install the profiling tool BEFORE the app you want to measure

**🚨 Ordering is critical.** Install **OVR Metrics Tool** from the Horizon Store inside the headset **before** launching the app you want to measure. Installing while the target app runs **force-closes it** and you lose the session.

Launch it from your Library, toggle **"Enable Persistent Overlay"**, **reboot the headset**. Open the **Stats** tab and pick the **"Basic"** preset — that adds stale frame count, CPU/GPU utilisation and **App GPU time**. It exports CSV straight into the report appendix.

## C2. Build and run

**File → Build Profiles → Meta Quest.**

1. Confirm all six scenes are in the Scene List with `00_Bootstrap` at index 0. **An empty scene list produces an APK that installs, launches, and shows a black void** — routinely misdiagnosed as an XR configuration failure.
2. Tick **Development Build** and **Autoconnect Profiler**.
3. Set **Run Device** to the Quest 3 (refresh if absent).
4. **Build And Run.** Output the `.apk` to `build/`, outside the Assets tree.

**The first IL2CPP build takes 10–25 minutes. It is not hung.** Incremental builds drop to 2–5. **Do a throwaway empty-scene build early this week so the first slow build is not happening during a demo.**

Faster redeploys afterwards: `adb install -r build\HakuMuseum.apk`.

## C3. The one diagnostic rule for the whole team

**Read App GPU time. Never read GPU%.**

When the app misses the 13.9 ms deadline, the compositor re-displays the previous frame and the GPU then sits **idle** waiting for vsync — which drags reported utilisation *down*. Meta's own worked example: GPU% reads 65% (looks fine) while App GPU time is 18.05 ms against a 13.9 ms budget, 30% over.

- **GPU-bound** if App GPU time > 13.9 ms.
- **CPU-bound** if App GPU time is comfortably under and you are still dropping frames — then read the Profiler's main-thread timeline.

**Watch the Stale counter in `adb logcat`.** `Stale = 36` on a 72 Hz display means every frame is shown twice — you are at exactly half rate. That is what "it runs at 30 fps" almost always is on Quest: 36, not 30.

## C4. The M1 gate — this is our definition of done

**Nothing merges to `main` unless, measured on the headset with the OVR Metrics HUD visible:**

| Metric | Gate |
|---|---|
| App GPU time | **≤ 11 ms** (20% margin under 13.9) |
| Stale frames | **0** over a 60-second walk of the full route |
| Draw calls | **≤ 200** |
| Visible triangles | **≤ 600k** |
| `ollama ps` PROCESSOR column | **100% GPU** |

**Photograph or screen-record the HUD at each milestone.** It is report evidence and it makes performance everyone's problem, not one person's.

**For M1 this gate is trivially passable, which is exactly the point** — build the habit while a grey-box room makes it easy, not in Week 10 with eight textured exhibits, an AI guide and Wi-Fi all fighting for the same 13.9 ms.

## C5. End-to-end checklist

| # | Check | Pass signal |
|---|---|---|
| 1 | `adb devices` | serial + `device` |
| 2 | `adb install -r build\HakuMuseum.apk` | `Success` |
| 3 | `adb logcat -s Unity` during launch | RECORD_AUDIO granted |
| 4 | On-screen debug text | `Microphone.devices.Length > 0`, prints `clip.frequency` |
| 5 | Hold trigger, speak, release | logcat shows a non-zero WAV byte count |
| 6 | logcat during the request | HTTP **200** from `/converse` plus the transcript |
| 7 | Guide audio | plays, and **pans correctly** as you walk around the exhibit |
| 8 | OVR Metrics HUD | flat **72**, stale ~0, App GPU time ≤ 11 ms while walking the route |

Dev shortcut for step 3: `adb shell pm grant com.rmit.haku.museum android.permission.RECORD_AUDIO`.

## C6. Meta Horizon Link — use it, and know the trap

Install from meta.com/quest/setup. In Settings → General enable **Unknown Sources** and **set Meta Horizon Link as the active OpenXR runtime** (there is an explicit "Set as active" button) — if SteamVR grabs the runtime, Play mode goes to SteamVR and you see nothing.

With A5's PC tab configured, pressing Play renders live into the headset. **This removes the 2–25 minute build loop and is the biggest time-saver for six people iterating on layout.**

> **🚨 The trap.** Over Link, the app is a **Windows player.** It uses the Windows audio stack, not Android `AudioRecord`. It has **no Android permission model and no Android cleartext policy**, and PC-class thermals. **Every failure mode in B5 and every performance number in A6 is invisible over Link.** Link also lets you reach the AI service over localhost, which hides the LAN problem until the week you can least afford it.
>
> Order of operations: (1) get a grey-box APK onto the Quest **this week** — M1 literally requires "built from source, on device"; (2) *then* use Link for fast layout iteration; (3) re-verify mic and HTTP **on device** before every milestone. **A scene at 200 fps over Link can run at 45 on the Quest 3.**

**For anyone without a headset:** drag in the **XR Interaction Simulator** prefab (the sample installed in A5) for WASD + mouse control of the rig in Play mode.

---

# Latency: where we actually stand

**The pitch deck promised "under 1.5 seconds to first spoken word." We are not there yet. Say so.** These are measured on the ROG Zephyrus M16, warm, with a 1.0 s question clip.

| Stage | Measured |
|---|---|
| STT — faster-whisper `base.en`, CPU int8 | **567–623 ms** |
| LLM — qwen3:8b, `think=false`, non-streaming | **744–996 ms** |
| TTS — Piper en_US-lessac-medium | **313–385 ms** |
| **Total `/converse` round trip** | **1.62 – 1.95 s** |

Component measurements behind those numbers:

- qwen3:8b `think=false`: **0.57 s to first token**, 1.21 s for a full short answer, ~35 tok/s.
- qwen3:8b `think=true`: **4.80 s, 148 reasoning tokens, and the answer never started. Never enable it.** `config.LLM_THINK = False` is not a preference.
- **Cold Ollama model load: 50 seconds.** This is why `keep_alive=-1` is mandatory and why the service warms the model at startup.
- faster-whisper `base.en` cpu/int8: **0.51 s for 5.76 s of audio** (11x realtime), transcript accurate.
- faster-whisper `small.en` cpu/int8: **2.62 s. Rejected** — it alone blows the budget.
- faster-whisper cuda/float16: **fails** — `Library cublas64_12.dll is not found or cannot be loaded`.
- Piper: voice load 1.73 s (startup, once), then 0.19–0.27 s for 5.7 s of audio (~30x realtime). **First sentence only: 0.10 s.**

## How we get to the promised number — this is M3/M4 work, not magic

1. **Stream the LLM and send the FIRST SENTENCE to Piper immediately** (`"stream": true`, buffer until `.`, `!` or `?`, synthesise, emit, repeat). Piper does a single sentence in 0.10 s instead of 0.31 s, and we stop waiting on tokens the visitor has not heard yet. Expected: **~1.3–1.4 s**.
2. **Fix GPU whisper**, which moves STT from ~590 ms to roughly 100–200 ms `[PowerShell]`:
   ```powershell
   .venv\Scripts\pip install nvidia-cublas-cu12 "nvidia-cudnn-cu12==9.*"
   ```
   Pin cuDNN to `9.*` — CTranslate2 loads `cudnn64_9.dll` specifically, and an unpinned install reproduces the exact error the moment cuDNN 10 wheels publish. **Then put those DLL folders on PATH** — `os.add_dll_directory()` was tested and does **not** work; only PATH did. In the launcher `.bat`:
   ```bat
   set PATH=%CD%\.venv\Lib\site-packages\nvidia\cublas\bin;%CD%\.venv\Lib\site-packages\nvidia\cudnn\bin;%PATH%
   ```
   With both changes, **~1.0 s is comfortably reachable.**

**Also be honest about what the number excludes.** It is measured from *audio file in hand* to *first audio byte out*. It does **not** include deciding the visitor stopped talking. That is why we use **push-to-talk on the controller trigger, not VAD**: zero tuning, no false triggers in a noisy assessment room, deterministic clip boundaries, and "hold the trigger to talk to the guide" is a legible VR affordance. It removes an entire class of 2am bug for about 20 lines of code.

## The bigger risk is not latency

**The model will confidently invent museum facts.** That is the actual product risk. The service already refuses correctly on the worked record — asked about cost and rulers it answers *"The record doesn't say. We don't actually know the cost or which emperor ruled at the time. However, we do know this mirror was made in the Tang Dynasty..."*

But there is a **known open defect**: under "explain like I'm nine" it embellishes beyond the record — *"the grapes are colorful"*, *"lions are big and strong"* — neither of which the record states. **Grounding is not solved. That is M4 (W8–10, Ameen + Mahir), and 20/20 grounded-or-declined is a real target, not a formality.** Log every request with its prompt and reply so the refusal rate is a measured number in the report, not a claim.

---

# Appendix: things tutorials will tell you that are wrong

| Tutorial says | Reality |
|---|---|
| "XR Plug-in Management → tick **Oculus**" | The Oculus XR Plugin is **deprecated starting in Unity 6.5** — our exact editor. Tick **OpenXR**, add the Oculus Touch Controller Profile, enable the Meta Quest feature group. **Any tutorial predating early 2025 is untrustworthy on this point.** |
| "Install `com.unity.xr.meta-openxr`, it's the Quest package" | It is an **AR Foundation provider** for passthrough and hard-depends on AR Foundation 6.0. A fully-virtual museum uses none of it. |
| "Switch platform to Android/Meta Quest in Build Settings" | "Build Settings" is the pre-Unity-6 name. It is **File → Build Profiles → Add Build Profile → Meta Quest**, and *creating that profile* is what installs OpenXR. |
| "Only the bootstrap scene goes in the build list" | A scene missing from the list loads fine in the Editor and fails **only on the Quest**. All six go in, `00_Bootstrap` at index 0. |
| "Just create `Assets/Plugins/Android/AndroidManifest.xml`" | That file does not exist in a fresh project. Tick **Publishing Settings → Custom Main Manifest** and let Unity generate it. |
| "Paste this AndroidManifest with `UnityPlayerActivity`" | Unity 6 defaults to **GameActivity**. The old activity/theme pair installs, launches, and instantly crashes with a `Theme.AppCompat` error. |
| "Drop `network_security_config.xml` in `Assets/Plugins/Android/res/`" | Unity **fails the build**: `OBSOLETE - Providing Android resources in Assets/Plugins/Android/res was removed`. Use `usesCleartextTraffic="true"`. |
| "Meta recommends Force Remove Internet Permission" | Only for guaranteed **offline** apps. Leave it **OFF** or the entire AI stack dies with no useful error. |
| "Turn on Dynamic Batching for mobile" | **Deprecated in 6.5** and superseded by the SRP Batcher under URP. Static Batching ON, Dynamic OFF. |
| "Set GPU Skinning to ON" | It is a three-value dropdown in 6.5. Pick **"GPU (Batched)"** — "ON" is not selectable. |
| "Quest 3 budget is 200–300 draw calls" | That is the **busy-simulation** row. A light scene gets **700–1000 draw calls / 1.3–1.8 M triangles**. Quoting the busy figure pushes you into pointless premature batching. (We *design* to ≤200 by choice, for CPU headroom — that is a target, not the ceiling.) |
| "Drop render scale to 0.7 for performance" | The floor is **0.85**. Below that you are shipping a blurry demo to save milliseconds you should find elsewhere. |
| "Judge performance from Quest Link" | Over Link the app is a **Windows player** with PC thermals, no Android permission model and no cleartext policy. 200 fps over Link can be 45 on device. **Never let Link be where a feature is declared done.** |
| "Read GPU% to see if you're GPU-bound" | GPU% drops when you *miss* frames, because the GPU idles waiting for vsync. Read **App GPU time** against 13.9 ms. |
| "The USB-C cable in the box works for debugging" | The white Quest 3 cable is **power-only** and fails silently. Use a known-good data cable into a direct motherboard port. |
| "`curl http://localhost:11434/...` to test Ollama" | In PowerShell `curl` is an alias for `Invoke-WebRequest` and dies on `-s`. Use **`curl.exe`** or `Invoke-RestMethod`. |
| "Set `$env:OLLAMA_HOST='0.0.0.0'` to expose Ollama" | A shell variable only affects the CLI *client*. The server keeps its 127.0.0.1 bind. Set a **user environment variable**, then **quit from the tray and relaunch**. And note that setting it **breaks the CLI**, which reads the same variable. |
| "faster-whisper needs ffmpeg installed" | It goes through PyAV, which statically links ffmpeg inside its wheel. Verified end-to-end with no ffmpeg on the machine. |
| "whisper needs exactly 16 kHz WAV — resample in C#" | Stale. Any sample rate is accepted and resampled internally. 16 kHz capture is a **bandwidth optimisation, not a correctness requirement.** |
| "Use `small.en` for better accuracy" | Measured on **our** CPU path: `small.en` is **2.62 s**, `base.en` is **0.51 s**. `small.en` alone blows the budget. Revisit only after CUDA whisper works. |
| "Piper is MIT-licensed" | `rhasspy/piper` is archived and was MIT. The **active** repo `OHF-Voice/piper1-gpl` — what `pip install piper-tts` now delivers — is **GPL-3.0**. Record it correctly in the report. |
| "GitHub Free gives 1 GB of LFS" | Stale. Free **and** Pro give **10 GiB storage + 10 GiB bandwidth/month**. Pro does not raise it, so the Student Developer Pack does not help. |
| "Just re-clone if the repo looks broken" | A fresh clone re-downloads **every** LFS object. Six people × 1.5 GB is the whole month's bandwidth in one lab session. Use `git lfs pull` or `git reset --hard origin/main`. |
| "`core.longpaths=true` fixes Unity's path length problems" | It affects **Git only**. Unity, Gradle, the NDK and IL2CPP need the OS policy `LongPathsEnabled=1` — and many Java Android tools still enforce 260 chars anyway. That is why the repo lives at `C:\Dev\haku-museum`. |
| "Set `keepBackup = false` inside `[mergetool \"unityyamlmerge\"]`" | No per-tool form exists. It is silently ignored there and `.orig` files pile up invisibly. Put it in its own `[mergetool]` section. |
| "Install the XR Device Simulator sample" | That is the **Legacy** one. Install **XR Interaction Simulator**. |
| "Grabbing feels wrong / the object snaps orientation" | `XRGrabInteractable` defaults. Set **Use Dynamic Attach = ON** and **Attach Ease In Time = 0.1–0.2 s**. Put the Attach Transform at the visual centre, not the imported pivot. **Movement Type = Kinematic.** |
