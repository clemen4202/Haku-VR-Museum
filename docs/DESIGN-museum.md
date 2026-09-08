# Haku — Museum Spatial & Experience Design

**Target:** Meta Quest 3, standalone. Unity 6000.5.5f1, URP, OpenXR (`com.unity.xr.openxr` only).
**Audience:** the whole team. **Mazin** owns the environment mesh, plinths, hero artefacts and lightmap UVs. **Femin** owns the XR rig, teleport anchors, hand menu, push-to-talk and the guide state machine.
**Status:** grey-box is authoritative. Art replaces surfaces, not dimensions.

This document gives numbers, not ranges. Where a number is arguable, the reasoning is written down so you can argue with the reasoning instead of the number.

---

> ## ⚠ UNRESOLVED: this document and the generator disagree on dimensions
>
> This design doc and `unity/Assets/Editor/HakuMuseumGenerator.cs` were written independently and
> specify **different footprints**. The generator is what actually built the scene you can open, so
> it is currently the source of truth — but the numbers below are the ones with written reasoning.
>
> | | This document | The generator (what actually ran) |
> |---|---|---|
> | Footprint | 16.4 × 12.4 m outer | **30 × 22 m interior** |
> | Ceiling | 4.6 m atrium | **4.80 m** |
> | Core | 9.2 × 5.2 m | sealed core, derived from 4.2 m corridor |
> | Station spacing | 6.0 m (48 m perimeter) | ~7.2 m along the long walls |
> | Plinth height | — | **1.10 m** |
>
> **Someone must decide before Mazin models anything.** The generator's constants are all in a
> single commented block at the top of the file, so changing the footprint is a one-line edit
> followed by re-running `HakuMuseumGenerator.Generate`. The smaller 16.4 × 12.4 m footprint gives
> the 6–9 minute visit this document argues for; 30 × 22 m is a noticeably longer walk.
>
> **Owner: Mazin + Femin, before the Week 6 playtest.**


## 1. The route: a guided loop of eight stations

### 1.1 Why a loop, not an open hall

An open gallery is the obvious choice and it is the wrong one for us, for four reasons:

1. **The visit has to be 6–9 minutes and legible.** This is a marked project demoed to people who have never worn a headset. In an open room a first-timer spends the first two minutes turning on the spot deciding where to go. A loop removes that decision entirely — there is one way forward, and the way forward is always the brighter thing.
2. **The AI guide needs an unambiguous "current exhibit".** `POST /ask` and `POST /converse` are far more useful when the service is told `exhibit_id`. In an open room the visitor is frequently between exhibits and near three at once; context becomes a guess. In a loop with alcoves, near/held state is unambiguous almost all the time.
3. **Draw calls are a sightline problem, not a modelling problem.** A loop with a solid opaque core in the middle means the camera frustum never contains more than about a quarter of the museum. That single decision is worth more than any mesh optimisation we will do later.
4. **A loop ends where it starts.** No backtracking, no "have I seen everything?", and the exit is the entrance so the demo resets cleanly for the next person in the queue.

### 1.2 Plan

Outer footprint **16.4 m × 12.4 m**. A single continuous corridor runs around a **solid, full-height 9.2 m × 5.2 m core**. Eight exhibit alcoves are recessed into the **outer** wall. The core is never penetrated — no doors, no windows, no arches.

```
        N
  +---------------------------------------------+
  |   [S6]              [S5]                    |
  |                                             |
  | [S7]   +-----------------------------+      |
  |        |                             |      |
  |        |         SOLID CORE          | [S4] |
  | [S8]   |        9.2 x 5.2 m          |      |
  |        |     (no openings, ever)     |      |
  |        +-----------------------------+ [S3] |
  | ATRIUM                                      |
  | 5x5 m     [S1]              [S2]            |
  +----o----------------------------------------+
       ^ entry / exit                     S
```

Walk direction is **clockwise** from the atrium. The centre-line of the corridor is a 14 m × 10 m rectangle, perimeter 48 m, so the eight stations sit at exactly **6.0 m intervals** along that centre-line.

| # | Station | Location on loop | Exhibit | Notes |
|---|---------|------------------|---------|-------|
| — | Atrium | s = 0 m | — | Arrival, guide introduces itself, tallest volume |
| S1 | Bronze mirror | s = 3 m | `bronze-mirror-01` — *Tang Dynasty Bronze Mirror* | **The only exhibit the service currently has.** Build this one properly first. |
| S2 | — | s = 9 m | TBD | |
| S3 | — | s = 15 m | TBD | |
| S4 | — | s = 21 m | TBD | |
| S5 | — | s = 27 m | TBD | |
| S6 | — | s = 33 m | TBD | |
| S7 | — | s = 39 m | TBD | |
| S8 | — | s = 45 m | TBD | Last station, faces the atrium — closes the loop |

### 1.3 What the visitor sees, moment by moment

**Arrival (atrium).** You spawn facing north-east into a 4.6 m volume, the brightest space in the museum but still only mid-grey. In front of you, set into the floor, is a **0.15 m wide warm emissive line** that runs off to your right and disappears round the corner. Eight small notches are cut into it — one per station. The guide's voice speaks once, from the atrium's own AudioSource, and says roughly *"Follow the line. There are eight things here. Ask me about any of them."* Nothing else in the atrium is interactive, deliberately: the first thing you learn is how to move.

**Approach.** The corridor ceiling drops from 4.6 m to 3.2 m. It is dim — the corridor floor sits at roughly one tenth the luminance of a lit plinth. Ahead and to your left, an alcove mouth spills a pool of light onto the corridor floor. That pool is the wayfinding. You do not need a sign to know where to go; the museum is dark and the exhibit is not.

**At a station.** Teleporting onto the station anchor lands you **1.2 m from the plinth centre, already facing the artefact** (anchor `MatchOrientation = TargetUpAndForward` — this is not optional, see §3). The alcove is 1.8 m wide, 1.1 m deep, with a 2.6 m opening: it reads as a small private room. The artefact sits on a plinth whose top is at 1.05 m. A short label plate is angled on the plinth face. The artefact is grabbable; the guide's voice, when it speaks, comes out of the artefact.

**Leaving.** The moment you release the artefact and step back off the anchor, the current alcove's *attention light* fades down over 1.2 s and the **next** station's fades up over 1.2 s. From any station anchor you can see at most **three** stations' light: the one you're at, the next, and a glimpse of the one after. That is enough to feel a route and not enough to feel a maze.

**Return.** S8's alcove faces back toward the atrium. Coming off it you see the atrium's tall volume and the start of the line, and the guide closes the visit.

### 1.4 Three redundant "next is that way" cues

Never rely on one. In a headset, one cue is zero cues.

1. **The floor line.** Continuous, unbroken, emissive, 0.15 m wide, baked into GI. Zero light cost. It works even if you look at your feet, which first-timers do constantly.
2. **Light.** The next station is always the brightest thing in your peripheral field. Peripheral vision is luminance-driven; this works even when you are not looking for it.
3. **A "3 / 8" counter on the non-dominant hand menu** (Femin). Non-diegetic, small, only visible when you rotate your palm up. This is the fallback for the visitor who has genuinely lost the plot, not the primary system.

The guide voice is a *fourth* cue but must never be load-bearing — the visitor may have muted, may be in a loud lab, or may simply not be listening.

---

## 2. Scale, with reasons

Every number below assumes a **standing eye height of 1.62 m**, and must still work for 1.45 m and 1.90 m. "Works" means: can see the artefact without craning, can reach it without crouching, never has geometry inside their head.

| Element | Number | Why this number |
|---|---|---|
| Atrium ceiling | **4.6 m** | The only tall volume. Arrival needs one moment of "this is a building". Above ~5 m the extra volume costs lightmap area and reads identically at eye height. |
| Corridor ceiling | **3.2 m** | Below 2.6 m a headset user feels the ceiling on their head and starts ducking. Above 3.5 m the corridor stops feeling like a corridor and the alcoves stop feeling like a reveal. 3.2 m is institutional without being a warehouse. |
| Alcove ceiling | **2.6 m** | Deliberately lower than the corridor. Compression → release → compression is the whole spatial rhythm. It also hides the light fixture above the visitor's natural gaze cone. |
| Corridor width | **2.4 m** | A Quest 3 guardian is typically ~2 × 2 m. At 2.4 m the walls sit 1.2 m either side of the centre-line, so a visitor physically walking inside their guardian never gets their head into a wall. It is also above the ~0.6 m threshold where nearby surfaces become uncomfortable to converge on and visibly blurry. |
| Alcove | **1.8 m wide × 1.1 m deep** | Wide enough for two people to be in shot for a demo video, narrow enough that the light pool is contained and the alcove occludes everything behind it. |
| Plinth top | **1.05 m** | With eye height 1.62 m and a viewing distance of 1.2 m, an artefact centred ~1.25 m puts the gaze angle about 17° below horizontal — the natural resting gaze. It is also squarely inside the comfortable grab band (0.90–1.35 m above floor). At 1.20 m the artefact occludes itself against the far wall; at 0.85 m short visitors are fine and tall visitors are bending. |
| Plinth footprint | **0.6 m × 0.6 m** | Reads as furniture, not architecture. Small enough that the visitor can lean over it. |
| Wall-mounted work centre | **1.55 m** | Slightly below the 1.60 m gallery convention, because our cohort skews shorter and a headset already tips the head forward. |
| Viewing anchor → plinth centre | **1.2 m** | The Quest 3's optics have a fixed focal plane around 1.3–2 m. Anything held or examined closer than ~0.5 m produces vergence–accommodation conflict and eye strain within a minute. 1.2 m is comfortable and still intimate. The visitor can lean in to 0.6 m by choice; we never put them there. |
| Station-to-station | **6.0 m** | Two comfortable teleport hops of 3.0 m. Also the acoustic decision: with a guide AudioSource max distance of 4.0 m, two guides can never be audible at once. Also the culling decision: three alcoves per frustum, maximum. |
| Teleport hop | **3.0 m**, max range 5.0 m | 3 m per press means the loop is 16 presses. Fewer, longer hops disorient; more, shorter hops are tedious and make the visit feel like admin. |
| Doorway / alcove opening | **1.6 m wide** | Wide enough that a visitor never brushes a jamb, which in VR reads as a collision you can't feel and breaks presence. |
| Elevation change across the entire museum | **0.0 m** | See §3. |
| Camera near / far clip | **0.05 m / 60 m** | Near clip below ~0.03 m causes z-fighting on held objects; far clip beyond the 20 m building diagonal is wasted depth precision. |

**Nothing interactive exists below 0.95 m or above 1.90 m.** No exhibit requires floor-level reach, kneeling, or a hand above the head. This is both an accessibility rule and a seated-play rule.

---

## 3. Comfort

The comfort model is simple: **we do not move the visitor's view. Ever, except by teleport.**

### 3.1 What actually causes sickness here

Sickness in a walkable museum is not mysterious. It comes from five specific things, in rough order of how often they bite:

1. **Vection.** Optical flow that says "you are moving" while the inner ear says "you are not". This is what smooth locomotion is. It is worst in **yaw** — a continuous turn stick at 30–90 °/s makes more people ill, faster, than forward motion does.
2. **Frame rate below 72 fps.** Judder decouples head motion from image motion. This is not a polish issue; a scene that dips to 60 fps in a busy sightline is a broken scene. 72 fps is a hard floor, not a target.
3. **Render scale below 0.85 / aliasing shimmer.** High-contrast edges (a bright plinth rim against a dark wall — which is our entire art direction) crawl and sparkle at low sample counts. That is eye strain, which people report as "VR made me feel weird".
4. **Script-driven camera motion.** Any code that translates or rotates the XR Origin, animates the camera, or head-locks UI closer than 0.6 m. Includes "helpful" auto-turn-to-face and camera shake.
5. **Physical/virtual mismatch.** Walking into a virtual wall your body doesn't feel; a guardian breach flashing passthrough mid-sentence; an artefact you're holding intersecting your face.

### 3.2 Decisions

- **Teleport locomotion, anchor-based.** `TeleportationArea` on the corridor floor for free movement along the loop, `TeleportationAnchor` at each station's viewing mark. A projectile arc, not a straight ray — the arc tells you where you land without you having to read a reticle.
- **Fade on teleport: 0.12 s out, 0.12 s in.** Long enough to suppress the jump-cut and the "where did I go" beat; short enough that 16 hops don't feel sluggish. Do not use 0.25 s "cinematic" fades; over a loop they cost 8 seconds of black.
- **Anchors match orientation** (`TargetUpAndForward`). You land facing the artefact, every time. This is the single largest contributor to "I always know where I am" and it costs nothing.
- **Snap turn, 45°, instantaneous, no fade.** 45° gives eight steps to a full circle. 30° needs twelve presses and becomes thumb admin; 90° is disorienting because you lose your visual anchor completely. **Continuous turn is disabled and is not a settings option** — if we ship it, someone will use it, and that someone will be the marker.
- **Zero elevation change.** One floor, no steps, no ramps, no lifts. Height change under teleport is a common nausea trigger and there is no design reason for it here.
- **No flicker.** No brightness change faster than 3 Hz anywhere in the museum. This is a photosensitivity rule as well as a comfort rule. The attention-light crossfade at 1.2 s is well inside it.
- **Held objects are clamped.** An artefact can never be brought closer than **0.25 m** to the camera. Use the XRI attach transform plus a distance clamp; do not let physics decide.
- **No head-locked UI.** The hand menu is hand-locked. Any world-space panel sits at ≥ 1.0 m and is billboarded, not parented to the head.
- **Accessibility switches** (Femin, hand menu): teleport hand (L/R), snap-turn hand, snap-turn size 30/45, and a **height offset** so a seated visitor can raise the rig to standing eye height. These are the only comfort options; a long options screen is a smell.

---

## 4. Lighting

### 4.1 The mood

Dark museum. Chiaroscuro. The corridor is a place you pass through; the alcove is a place that is *lit*. Concretely: **the plinth top reads roughly 8–10× the luminance of the corridor floor.** That contrast ratio is the whole aesthetic. If the corridor is bright enough to read comfortably, the museum has failed.

Colour temperature: artefact key lights **4200 K** (neutral, honest to the object's material — a bronze mirror lit at 2700 K just looks orange). Corridor fill **3000 K** and very dim. Atrium **3600 K**, in between, because it's a transition.

### 4.2 The constraint

Realtime lights are the most expensive thing we can casually add on Quest 3. In URP Forward, every additional per-pixel realtime light is more work per fragment, and realtime shadows mean an extra render pass plus a shadow map. A dramatic museum wants *many* lights, which is exactly the thing we cannot have. So:

> **Everything that does not change is baked. One realtime light exists in the entire museum.**

### 4.3 What gets baked

- Every wall, floor, ceiling, plinth, alcove interior, bench and rail: **Static + Contribute GI**. Renderers marked static, meshes with a clean second UV channel (Mazin — generate lightmap UVs on import, and check for overlaps on the alcove interiors first; that's where bake artefacts will show).
- **Eight baked Spot lights**, one per alcove: 3.4 m above the floor, aimed at the plinth top, **35° cone**, 4200 K. These are `Baked` mode, not Mixed. They produce the pools.
- **Corridor fill:** four baked Area lights hidden in a ceiling cove, 3000 K, very low. No visible fixtures at grey-box.
- **Atrium:** one baked Spot from 4.4 m plus a baked bounce off the floor.
- **Ambient:** Environment Lighting = **Color**, `#12141A`, intensity **0.15**. There is no sky. Do not use a skybox for ambient in an interior — it flattens the contrast we just paid for.
- **Emissive materials are the cheap win.** The floor route line and the plinth rim glow are emissive surfaces contributing to the bake. They cost zero light budget and do a lot of the mood work.

**Bake settings:** Progressive GPU, **Non-Directional** (directional lightmaps double the memory and we have no normal-mapped hero surfaces in the shell), lightmap resolution **12 texels/unit** for shell geometry and **30 texels/unit** for alcove interiors and plinths, max lightmap size **1024**. Compressed. **Hard cap: 6 atlases at 1024.** If the bake wants more, the problem is UV waste, not the cap.

### 4.4 What stays realtime

**Exactly one light: the Attention Light.** A Spot, `Realtime`, **shadows off**, parented to the active station, whose intensity is crossfaded when the visitor advances. It exists purely because a baked light cannot animate, and the "next station brightens" cue is the primary wayfinding system. One light, one purpose.

URP asset settings that enforce this:

- Main Light: **Disabled** (there is no sun indoors). If a dim directional fill is needed for the held artefact, set it to **Baked** and it stops counting.
- Additional Lights: **Per Pixel, max 2.** Two is the Attention Light plus one spare for a held-artefact rim if we decide we need it. Not four. Not "just in case".
- Additional light shadows: **off**. Soft shadows: **off**. Cast shadows: **off** globally.
- SSAO: **off**. Bake ambient occlusion into the lightmaps and into the artefact textures.

### 4.5 Light probes — the artefacts you pick up

Dynamic objects get no lightmap. The moment a visitor lifts the bronze mirror off the plinth, its lighting comes entirely from **light probes**. Get this wrong and the mirror goes black in your hand, which is the single most obvious failure mode in the build.

Probe strategy:

- **Per station:** a 3 × 3 × 2 grid — 0.75 m horizontal spacing over a 2.2 m × 2.2 m box centred on the plinth, at heights **0.9 m and 1.7 m**. That is 18 probes, and it covers exactly the volume the visitor's hands actually occupy. Heights are chosen to bracket the grab band, not to fill the room.
- **Corridors:** a probe pair (0.9 m and 1.7 m) every **2.0 m** along the centre-line.
- **Atrium:** a 3 × 3 × 2 grid at 1.5 m spacing.
- **Total ≈ 200 probes. Hard cap 300.** Probes are cheap per-renderer (spherical harmonics), but a bad one is very visible.

Probe rules, in order of how often they get broken:

1. **Never place a probe inside geometry or below the floor.** A probe in a wall bakes black and the artefact pops black when the visitor's hand crosses the tetrahedron.
2. **Bracket every lighting discontinuity.** Put a probe pair **0.3 m either side of the alcove mouth**. Without this, carrying the mirror from the light pool into the dark corridor makes it snap between two brightnesses instead of fading.
3. **Do not stretch probes across the core.** The tetrahedralisation will happily interpolate through a solid wall. Keep the corridor probe chain continuous around the loop.
4. Add a **Light Probe Proxy Volume** only for the atrium's tall volume if we ever put a large dynamic object there. Not needed for hand-sized artefacts.

**Reflection probes:** one **baked** box probe per corridor run (four total) at 64 px, plus one **128 px baked probe in S1's alcove** — the Tang mirror is polished metal and a flat cubemap is the difference between "bronze" and "brown plastic". No realtime reflection probes, ever.

**Held-object contact shadow:** there are no realtime shadows, so a held artefact would float. Fake it — an unlit multiply-blended quad projected under the object, fading with height. One draw call, no shadow map, and it solves the "where is this thing relative to the plinth" depth problem that people actually notice.

---

## 5. Performance budget as art direction

**Whole scene: 700–1000 draw calls, 1.3–1.8 M triangles.** We design to **~880 draw calls and ~1.5 M triangles** so there is real headroom, because the last 10% always arrives as "just one more prop".

### 5.1 Allocation

| Bucket | Draw calls | Triangles | Owner |
|---|---:|---:|---|
| Architectural shell (floor, walls, ceiling, core, alcove shells, atrium) | 120 | 150 k | Mazin |
| 8 stations × (plinth + case + label + hero artefact + local dressing) | 200 (25 ea.) | 880 k (110 k ea.) | Mazin |
| Corridor dressing (benches, rails, signage, cove fixtures) | 120 | 180 k | Mazin |
| UI, teleport ray, reticle, hand menu, guide visualisation | 40 | 20 k | Femin |
| **Committed subtotal** | **480** | **1.23 M** | |
| **Reserve — do not spend without a measurement** | 220–520 | 0.07–0.57 M | |

### 5.2 Per-station allowance, stated plainly

**Each station gets 25 draw calls and 110 000 triangles.** Inside that:

- **Hero artefact (the grabbable one): ≤ 40 k triangles, ≤ 3 draw calls** (body, one detail/trim material, optional glass). It earns the budget because the visitor holds it 30 cm from their face and it is the only object in the museum that gets silhouette scrutiny. Bevel the silhouette; don't bevel what you can't see against a background.
- **Plinth: ≤ 2 k triangles, 1 draw call.** A plinth is a box with a chamfer. If your plinth is 10 k triangles, delete it and start again.
- **Alcove interior: ≤ 3 k triangles, part of the shell's static batch.**
- **Label plate, case, local props: the remaining ~60 k and ~19 draw calls.**
- Everything that is not a hero artefact: **≤ 5 k triangles and no LODs.** At our viewing distances LOD switching costs more in complexity and popping than it saves.

Rule of thumb for the shell: **a flat 4 m wall panel is 2 triangles.** Not 200. Geometry only exists where a silhouette will be seen from under 1.5 m.

### 5.3 Materials and textures — the actual rules

1. **No unique material per object.** Ever. Target: **≤ 30 unique materials in the entire museum.** Variation comes from vertex colour and from UV placement within an atlas, not from new material assets. A new material is a new draw call and a broken static batch.
2. **Atlases:**
   - `T_Architecture_Albedo` — 2048², all walls/floors/ceilings/trim.
   - `T_Props_Albedo` — 2048², plinths, benches, rails, labels, fixtures.
   - `T_Artefact_XX` — 1024² per hero artefact, eight of them. These are the only per-object textures allowed.
3. **Compression: ASTC 6×6** for albedo and mask maps. **Normal maps only where they earn it** — the hero artefacts and nothing else. A normal map on a flat baked-lit wall is invisible and costs a sampler.
4. **Texture memory budget: ≤ 180 MB total.** Check it in the build report, not by guessing.
5. **Shaders:** `URP/Simple Lit` for all architecture and dressing. `URP/Lit` **only** for the eight hero artefacts, which need proper specular and the reflection probe. Custom shaders need a reason written in the PR.
6. **Transparency is overdraw and overdraw is the Quest killer.** Glass cases on **at most 3 of the 8 stations**, and never two transparent surfaces in the same sightline. No transparent floor decals, no soft-particle dust motes, no god rays.
7. **Batching:** **Static Batching ON**, Dynamic Batching **OFF** (deprecated on 6.5; the SRP Batcher supersedes it). Mark every non-moving renderer static. Repeated props (benches, fixtures) use **GPU instancing**.
8. **Occlusion culling: baked, and it is why the core is solid.** Bake it after any change to the shell. Verify in the Occlusion view that standing at S1's anchor culls S5–S7 entirely.
9. **No post-processing volume in v1.** HDR **off**, **MSAA 4×** (this is what buys us clean edges at 0.85 render scale — it is not optional given our high-contrast art direction). If the light pools look flat without bloom, first try painting the falloff into the emissive material. Only add Bloom after measuring the cost on device.
10. **Render scale 0.85 is a floor, not a target.** Start at 1.0 and only drop toward 0.85 if the profiler makes you. Foveated rendering: enable it if the Meta Quest feature group in your Editor exposes it — verify the toggle exists rather than assuming, and re-measure after enabling.

### 5.4 How we know

Measure on device, never in the Editor. Frame timing from the Unity Profiler over USB, plus the on-headset metrics overlay. **The busiest sightline is the acceptance test**: stand at the S1 anchor holding the mirror while the attention light crossfades to S2, and read the numbers there. If that frame is under 13.8 ms, we're fine everywhere.

---

## 6. Where the AI guide lives, spatially

The guide is not a HUD and not a floating orb. **The guide's voice comes out of the artefact.**

### 6.1 The sound

Each station prefab contains a child `GuideVoice` with an AudioSource, positioned at **1.35 m height, 0.15 m in front of the artefact centre** toward the visitor — near enough to read as the object speaking, offset enough that it isn't buried inside a mesh where spatialisation gets strange.

| AudioSource setting | Value | Why |
|---|---|---|
| Spatial Blend | **1.0** (fully 3D) | If it's 2D it comes from inside your skull and the illusion dies |
| Rolloff | Logarithmic | Natural falloff |
| Min Distance | **0.8 m** | Full volume across the whole alcove |
| Max Distance | **4.0 m** | Stations are 6 m apart, so **two guides can never be audible at once**. The visitor's ears do the disambiguation for free. |
| Doppler Level | **0** | Doppler on a stationary narrator is only ever a bug |
| Priority | 0 (highest) | Voice never gets stolen by ambience |

One **Audio Reverb Zone** per corridor run, low-mid "stone room" character, so the voice sits in the space instead of on top of it. Cheap and disproportionately convincing.

Spatialiser: Unity's built-in panning is acceptable for grey-box. If we later add a dedicated spatialiser plugin, that is an independent decision — **it does not mean adding `com.unity.xr.oculus`, which is deprecated on 6000.5 and must never enter this project.**

### 6.2 The context the service receives

`ExhibitPoint.cs` is the single source of truth for "what is the visitor engaged with". Per station it tracks four states, and the client sends the **highest-priority** one:

| Priority | State | Trigger |
|---|---|---|
| 1 (highest) | **Held** | XRI `SelectEntered` on the artefact |
| 2 | **Focused** | Camera forward within 20° of the artefact for ≥ 0.4 s, from within 2.5 m |
| 3 | **Near** | Visitor inside the anchor's 1.5 m trigger volume |
| 4 | **Far** | none of the above |

`HakuGuideClient` sends `exhibit_id` for the highest-priority station at request time — so `bronze-mirror-01` while the mirror is in your hand, and `null` if the visitor is mid-corridor, in which case the guide answers generally rather than pretending to know what you mean. Held beats Focused beats Near; if two stations somehow tie, the closer one wins.

This is also why the loop matters: with alcoves 6 m apart, ties essentially never happen.

### 6.3 Asking

- **Push-to-talk**, right-hand primary button, held. Not open mic. Two reasons: the demo happens in a room with five other students talking, and a held button is an unambiguous "the guide is listening" affordance that needs no tutorial.
- `MicrophoneCapture` → `WavUtility` → `POST /converse` (multipart) with the current `exhibit_id`. Text-only fallback path is `POST /ask`.
- **The response plays through that station's `GuideVoice`** — so the answer comes out of the object you are holding, not from your head. This is the whole spatial-audio idea in one sentence and it is worth protecting in code review.
- **Visual states,** all on the plinth rim so the visitor's gaze never has to leave the artefact:
  - *listening* — rim ring at steady low glow while the button is held
  - *thinking* — slow rim pulse (well under 3 Hz)
  - *speaking* — rim brightness follows the audio envelope
  One unlit animated material, 1 draw call. Not a UI canvas.
- **Latency cover:** if the round trip exceeds **1.2 s**, play a short (0.4 s) low hum from the same AudioSource. Silence for two seconds reads as "it's broken" and the visitor presses the button again, which makes it worse.

### 6.4 Network reality

The service is at `http://192.168.0.90:8000` — plain HTTP on the LAN. That requires **Publishing Settings → Custom Main Manifest** enabled, with `android:usesCleartextTraffic="true"` on `<application>`. Internet Access = **Require**, "Allow downloads over HTTP" = **Always Allowed**, Force Remove Internet Permission **OFF**. The IP is in `HakuConfig` — it changes every time the laptop joins a different network, so it must stay a field someone can edit without a rebuild if at all possible.

---

## 7. Grey-box first

### 7.1 What the current grey-box scene proves

The grey-box is not a placeholder for the design — it **is** the design. It exists to prove five things, and it either proves them or we change the design before anyone models anything:

1. **The loop reads as a loop.** A person who has never worn a headset can walk it end to end with no instruction beyond "follow the line", and ends up back at the atrium.
2. **Every anchor is reachable and lands you facing the exhibit.** All 8 stations, all 16 corridor hops, no dead ends, no anchor you can teleport past.
3. **The frame budget holds.** 72 fps sustained at the S1 acceptance sightline (§5.4), with the geometry allowances in place.
4. **The AI round trip works from the headset** — push-to-talk at S1 returns audio from the bronze mirror's own AudioSource, over cleartext HTTP, on the lab network.
5. **Sightlines behave.** From any anchor: at most three stations visible, the core never see-through, occlusion culling actually culling.

Until all five are true, no one models a single finished asset.

### 7.2 What replacing grey boxes must preserve

These are contracts. Breaking one silently is worse than missing a deadline.

**Scale is frozen.** Ceiling 4.6 / 3.2 / 2.6. Corridor 2.4 m clear. Plinth top 1.05 m. Anchor 1.2 m from plinth centre. Station spacing 6.0 m. Art may add surface, moulding and detail — **growing inward from the current wall plane is not allowed if it reduces the 2.4 m clear width.** If a skirting board is 40 mm, the wall moves out 40 mm; it does not eat the corridor.

**Sightlines are frozen.** The core stays solid and full-height. No windows, no arches, no "it would look nice if you could see through here". The draw-call budget and the occlusion bake both depend on it. Alcove mouths stay 1.6 m wide and 2.6 m high.

**Light positions are frozen.** Every baked Spot's transform is a contract with the wayfinding system and with the bake. **You may change the bulb; you may not move the fixture.** Colour temperature, intensity and cone angle are art decisions. Position and rotation are design decisions. If a beautiful pendant lamp doesn't sit where the key light is, move the lamp mesh — not the light.

**Teleport anchors are frozen and are not art's to touch.** No geometry may occupy the **0.8 m radius standing cylinder** above any anchor. A bench that looks perfect and clips the visitor's shins is a bug, not a detail.

**The route line is frozen.** Continuous, unbroken, 0.15 m wide, on the floor, atrium to atrium, emissive. It may change material. It may not become dotted, decorative, or interrupted by a rug.

### 7.3 The hierarchy contract

Art swaps meshes **inside** the existing prefabs. Nobody re-parents, renames, or deletes a station prefab — Femin's interaction wiring, the guide state machine and the anchor references all resolve through this structure.

```
Station_01
├── Plinth            (static, batched, replaceable mesh)
├── Case              (optional, transparent — 3 stations max)
├── Label             (static)
├── Artefact          (ExhibitPoint, XRGrabInteractable, exhibitId="bronze-mirror-01")
├── GuideVoice        (AudioSource — do not move, see §6.1)
├── Anchor            (TeleportationAnchor — design-owned, do not move)
└── Light_Key         (baked Spot — design-owned, do not move)
```

### 7.4 Definition of done, per station

- [ ] Meshes swapped in place; prefab structure unchanged
- [ ] ≤ 25 draw calls, ≤ 110 k triangles measured in the Frame Debugger
- [ ] Uses only the shared atlases (+ its own 1024² artefact texture)
- [ ] Lightmap UVs clean; alcove interior bakes with no seams or leaks
- [ ] 18 probes present, none inside geometry, pair bracketing the alcove mouth
- [ ] Artefact does not go black when carried into the corridor
- [ ] Anchor lands the visitor facing the artefact, 1.2 m out, nothing in the standing cylinder
- [ ] Guide audio plays from `GuideVoice`, inaudible from the neighbouring stations
- [ ] `exhibitId` set and confirmed against `GET /exhibits`
- [ ] 72 fps sustained standing at the anchor holding the artefact, on device

---

## 8. Assumptions and open questions

Stated so they can be argued with rather than discovered later:

1. **Session length is 6–9 minutes** and the visitor is a first-timer. Every decision above — loop, teleport, redundant wayfinding — follows from that. If the brief is actually "a space to explore for 30 minutes", the loop is the wrong shape and we should talk now.
2. **Only one exhibit record exists** (`bronze-mirror-01`). S1 gets built to final quality first and becomes the template; S2–S8 stay grey-box with placeholder content until the service has records for them. We should not model eight artefacts for content that does not exist.
3. **Seven of eight exhibit subjects are undecided.** Content shapes lighting (a scroll needs different key angle than a mirror). Assign them before S1's lighting bake is treated as the template.
4. **Passthrough / mixed reality is out of scope** — `com.unity.xr.meta-openxr` is deliberately absent and this is a fully virtual interior. If someone wants a passthrough moment, that is a new package, a new render path and a new design conversation.
5. **Multi-user is out of scope.** One headset, one visitor.
6. **The service IP is a LAN address** and will break on any other network. Someone needs to own that for demo day.
