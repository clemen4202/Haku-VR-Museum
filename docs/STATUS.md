# Environment status — set up automatically

Everything below was executed and verified on the ROG Zephyrus M16 (`192.168.0.90`).
Nothing has been committed or pushed — `git status` will show the whole tree as untracked.

## Done

| Item | State |
|---|---|
| Repo location | `C:\Dev\haku-museum` — moved out of OneDrive, verified identical, original deleted |
| Git | `git init` done, branch `main`, **no commits** |
| Unity Android Build Support | **Installed** — OpenJDK, SDK and NDK all present |
| `adb` | Unity's copy (1.0.41) on user PATH; the duplicate standalone SDK path was removed |
| Python venv | `ai-service/.venv` created, all requirements installed |
| Piper voice | `ai-service/voices/en_US-lessac-medium.onnx` (63 MB, gitignored) |
| `OLLAMA_HOST` | `0.0.0.0:11434` (user env var, persists across reboot) |
| `OLLAMA_KEEP_ALIVE` | `-1` (model pinned; avoids the 50 s cold reload) |
| Ollama LAN binding | Listening on all interfaces, verified at `http://192.168.0.90:11434` |
| AI service | Running, `/health` returns `ok: true` |
| Service over LAN | `http://192.168.0.90:8000/health` returns **HTTP 200** — no firewall rule was needed |

Warm response time after setup: **0.86 s** for `/ask`.

## Still needs a human

1. **Meta developer-organisation verification — Clemen.** Requires signing in as you at
   developers.meta.com with the same Meta account as the headset, creating an org, and completing
   2FA. Multi-day lead time. Nothing about Developer Mode works until it clears.
2. **Enable Developer Mode on the Quest 3** via the Meta Horizon phone app, then accept the
   "Allow USB debugging" prompt *inside the headset*.
3. **Create the Unity project.** See [SETUP.md](SETUP.md) Part A. Create it at
   `C:\Dev\haku-museum\unity`. Do not create it anywhere under OneDrive.
4. **Commit and push** — deliberately not done. Make the `.gitignore` / `.gitattributes` the first
   commit; LFS tracking is not retroactive.

## Security note — read this

`OLLAMA_HOST=0.0.0.0:11434` exposes an **unauthenticated** language-model API to every device on
whatever network this laptop joins, including RMIT campus Wi-Fi. That is what makes the headset
able to reach it, and it is the documented approach — but do not leave it on a public network
longer than you need to. To disable:

```powershell
[Environment]::SetEnvironmentVariable("OLLAMA_HOST", $null, "User")
```

then fully quit Ollama from the system tray and relaunch it.

A Windows Firewall rule was **not** created — none turned out to be necessary, and changing
firewall configuration needs your explicit decision and admin rights.

## Running the service

```powershell
cd C:\Dev\haku-museum\ai-service
.\.venv\Scripts\python.exe main.py
```

It is currently **already running** in the background from this session. Stop it with:

```powershell
Get-Process python | Where-Object { $_.Path -like "*haku-museum*" } | Stop-Process
```

Smoke test:

```powershell
curl.exe -s http://127.0.0.1:8000/health
curl.exe -s -X POST http://127.0.0.1:8000/ask -H "Content-Type: application/json" -d "{\"exhibit_id\":\"bronze-mirror-01\",\"question\":\"What is it made of?\"}"
```

---

# Build milestone — M1 substantially reached

A **working Quest 3 APK now exists**, built headlessly from source.

`unity/Builds/Haku.apk` — 95 MB, `BuildResult: Succeeded`, 0 errors.

Verified by inspecting the APK itself, not by trusting the build log:

| Check | Result |
|---|---|
| Package | `com.teamhaku.haku` |
| Native ABI | **`arm64-v8a` only** (no ARMv7 bloat) |
| Target / compile SDK | 34 · min 32 |
| Libraries | `libunity`, `libil2cpp`, `libopenxr` all present |
| VR intent | `com.oculus.intent.category.VR` present |
| Supported devices | `quest, quest2, cambria, eureka, quest3s` — **eureka = Quest 3** |
| Cleartext HTTP | `usesCleartextTraffic=true` |
| Permissions | `INTERNET`, `RECORD_AUDIO` |

## Unity project state

- Unity 6000.5.5f1 · URP **17.5.0** · OpenXR **1.18.0** · XRI **3.6.0** · Input System 1.20.0
- Render pipeline `Assets/Settings/Haku_URP_Quest.asset`: 4x MSAA, HDR off, render scale 1.0,
  1 shadow cascade @ 25 m, SRP Batcher on
- OpenXR features enabled for Android: `com.unity.openxr.feature.metaquest` and
  `com.unity.openxr.feature.input.oculustouch`
- Scene `Assets/Scenes/Museum_Greybox.unity` at build index 0:
  81 GameObjects · 38 renderers · 8 materials · 8 baked spots + 1 mixed directional ·
  **1,000 triangles against a 1.3-1.8 M budget**

### Do not "fix" HakuProjectSetup step 4

Step 4 fails by design and that failure is **correct**. It looks for a "Meta Quest" OpenXR
*feature set*, which does not exist in `com.unity.xr.openxr` 1.18.0 — Meta feature sets ship with
the Meta XR SDK, which we deliberately do not install. `HakuOpenXRFeatures.Enable` enables the two
individual features instead, which is the right OpenXR-only path.

## Install on a headset

Once Developer Mode is on and the headset is plugged in with a **data** cable:

```powershell
adb devices
adb install -r C:\Dev\haku-museum\unity\Builds\Haku.apk
```

No headset was connected when this was built, so the APK is **verified valid but never run on
hardware**. Frame rate is unmeasured — M1 is not formally closed until OVR Metrics Tool shows a
stable 72 fps on device.

## Next code tasks

1. **XR rig is head-tracked only.** Controllers and a `TeleportationProvider` still need wiring;
   the 8 `TeleportAnchor_XX` transforms are posed and waiting. *(Femin)*
2. **Museum footprint unresolved** — see the warning at the top of `DESIGN-museum.md`. Decide
   before modelling. *(Mazin + Femin)*
3. **Wire `HakuGuideClient` into the scene**, point `HakuConfig` at `http://192.168.0.90:8000`.
   Test `/ask` first (no audio), then `/converse`. *(Ameen)*
4. **Bake lighting.** All 8 spots are baked but no lightmaps generated yet. *(Mazin)*
