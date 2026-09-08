// =============================================================================
//  Haku - VR Museum  |  Meta Quest 3  |  Unity 6000.5.5f1
//  Assets/Editor/HakuMuseumGenerator.cs
//
//  Builds the entire grey-box gallery from Unity primitives (no external art),
//  saves it to Assets/Scenes/Museum_Greybox.unity, makes it build index 0,
//  and exits 0 (1 on failure) when run headless.
//
//  Headless invocation:
//    "C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe" ^
//      -batchmode -nographics -projectPath C:\Dev\haku-museum\unity ^
//      -executeMethod HakuMuseumGenerator.Generate -logFile -
//
//  Do NOT add -quit: this method calls EditorApplication.Exit itself so the
//  process exit code actually means something to a build script.
//  Re-running is safe: the generated root is destroyed and rebuilt in place,
//  and any hand-placed objects elsewhere in the scene are left alone.
//
// -----------------------------------------------------------------------------
//  THE SPACE  (plan view, +X right, +Z up the page)
//
//   +--------------------------------------------------------+
//   |    7 <----------- 6 <----------- 5                      |
//   |    |    +---------------------------+       ^          |   outer shell:
//   |    v    |                           |       |          |   30 x 22 m interior
//   |    8    |   sealed core block       |       4          |   4.8 m ceiling
//   |    |    |  (the dividing walls)     |       ^          |
//   |    v    +---------------------------+       |          |   4.2 m ring corridor
//   |    1 -----------> 2 -----------> 3 ---------+          |
//   +--------------------------------------------------------+
//
//  WHY THIS SHAPE:
//  A solid core in the middle of a rectangular shell leaves exactly one walkable
//  ring. There is one way to walk and it returns you to where you started, so a
//  visitor in a headset can never be lost and "next" is always "keep going".
//  It is a guided loop, not a sandbox, without a single locked door or barrier.
//
//  SIGHTLINES:
//   - Stations 1-2-3 and 5-6-7 sit in a straight run along the long walls, ~7.2 m
//     apart. Step back off any plinth and the next one is already in peripheral
//     vision down the same corridor.
//   - Stations 4 and 8 sit at the midpoint of the short walls, so the moment you
//     reach a corner the next plinth is dead ahead.
//   - A continuous emissive floor strip on the ring centreline states the route
//     even where the next plinth is around a corner, and it points at the corner
//     from halfway down each straight.
//
//  Everything below the TUNING BLOCK is derived. Change the constants, re-run,
//  get a different gallery.
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class HakuMuseumGenerator
{
    // =========================================================================
    // ==============            T U N I N G   B L O C K            ============
    // =========================================================================
    // Every value here is metres / degrees and every one of them has a reason.
    // These are the only numbers you should need to touch.

    // ---- Shell -------------------------------------------------------------
    /// Interior clear span, X. 30 m gives three well-spaced stations per long
    /// wall (7.2 m apart) plus 4.2 m corners to turn in without cutting a plinth.
    const float RoomWidth = 30.0f;

    /// Interior clear span, Z. 22 m keeps the core block a sane 21.6 x 13.6 m;
    /// much shorter and the short walls stop being a real leg of the route.
    const float RoomLength = 22.0f;

    /// Ceiling height. Real galleries are tall - anything under ~4 m reads as an
    /// office and kills the "museum" feeling instantly in a headset, where you
    /// judge scale with your actual body. 4.5-5.0 is the sweet spot; 4.8 also
    /// leaves room to hang the plinth spots at 4.5 m, well clear of a raised hand.
    const float RoomHeight = 4.80f;

    /// Structural thickness for shell + dividing walls. Purely visual in a
    /// grey-box, but 0.30 m stops walls reading as paper when you stand next to one.
    const float WallThickness = 0.30f;

    /// Ring corridor width, wall face to core face. The brief's floor is 3.5 m.
    /// 4.2 m is chosen deliberately: the plinth eats ~1.0 m and the visitor stands
    /// ~1.6 m off the wall, which still leaves ~1.7 m of clear floor behind them
    /// so a second person can walk past without a headset-wearer flinching.
    const float CorridorWidth = 4.20f;

    // ---- Stations ----------------------------------------------------------
    // Station count is 2 * (long + short) = 8. Set 4/2 for twelve, 2/1 for six.
    const int StationsPerLongWall = 3;
    const int StationsPerShortWall = 1;

    /// Top surface height. Brief says 1.05-1.15. 1.10 puts an artefact's centre
    /// at ~1.35 m: a standing adult (eye height ~1.60 m) looks slightly DOWN at
    /// it, which is how you actually inspect an object, and a seated or shorter
    /// visitor still clears the rim.
    const float PlinthHeight = 1.10f;
    const float PlinthWidth = 0.85f;   // square footprint, reads as a museum plinth
    const float PlinthDepth = 0.85f;
    /// Gap between wall face and the back of the plinth. Small enough to read as
    /// "against the wall", big enough that the wall panel is not occluded.
    const float PlinthWallGap = 0.55f;

    /// Nominal artefact envelope sitting on the plinth. The marker is placed at
    /// the CENTRE of this volume, so a model dropped in with a centred pivot lands
    /// correctly, and the spot light aims here.
    const float DisplayVolumeHeight = 0.50f;

    // ---- Wall panel (label / caption board) --------------------------------
    const float PanelWidth = 1.30f;
    const float PanelHeight = 0.85f;
    const float PanelThickness = 0.06f;
    /// Panel centre height. Museum standard is a 1.45-1.65 m centre line;
    /// 1.62 puts it at eye level for a standing adult reading over the plinth.
    const float PanelCentreHeight = 1.62f;

    // ---- Teleport anchor ---------------------------------------------------
    /// Distance from plinth CENTRE to where the visitor's feet land. 1.65 m puts
    /// the artefact roughly an arm-and-a-half away: close enough to lean in and
    /// inspect, far enough that a 0.85 m plinth is not clipping your knees on arrival.
    const float TeleportStandoff = 1.65f;
    const float TeleportPadDiameter = 0.55f;

    // ---- Wayfinding --------------------------------------------------------
    /// Emissive floor strip on the ring centreline. 0.35 m reads clearly from
    /// standing height without becoming a runway.
    const float RouteStripWidth = 0.35f;

    // ---- Lighting ----------------------------------------------------------
    /// Per-plinth spot. Tuned for a dark room: the plinths should be the brightest
    /// thing in the gallery by a wide margin. If the project is on URP with
    /// physical light units this will need re-tuning (URP non-physical intensity
    /// is a plain multiplier, which is what this assumes).
    const float SpotIntensity = 6.0f;
    const float SpotAngle = 34.0f;
    /// Spot is pushed this far out from the wall so light rakes ACROSS the artefact
    /// rather than dropping straight down on it (straight down = flat and ugly).
    const float SpotForwardOffset = 0.85f;
    const float SpotCeilingDrop = 0.30f;
    /// Key directional. Deliberately weak - it exists to keep walls and floor from
    /// going pure black and to give the room a direction, not to light the show.
    const float KeyLightIntensity = 0.55f;

    // =========================================================================
    // ==============        end of tuning block - derived below     ===========
    // =========================================================================

    const string RootName = "HAKU_MUSEUM_GENERATED";
    const string ScenePath = "Assets/Scenes/Museum_Greybox.unity";
    const string SceneFolder = "Assets/Scenes";
    const string MaterialFolder = "Assets/Materials/Haku_Greybox";
    const string SettingsFolder = "Assets/Settings";
    const string LightingSettingsPath = SettingsFolder + "/Haku_Museum_Lighting.lighting";
    const string FirstExhibitId = "bronze-mirror-01";

    static int StationCount => 2 * (StationsPerLongWall + StationsPerShortWall);
    /// Half extents of the core block (= inner edge of the ring corridor).
    static float CoreHalfX => RoomWidth * 0.5f - CorridorWidth;
    static float CoreHalfZ => RoomLength * 0.5f - CorridorWidth;
    /// Ring corridor centreline, used for the route strip and the spawn point.
    static float RingHalfX => RoomWidth * 0.5f - CorridorWidth * 0.5f;
    static float RingHalfZ => RoomLength * 0.5f - CorridorWidth * 0.5f;
    /// Distance from wall face to plinth centre.
    static float PlinthOffsetFromWall => PlinthWallGap + PlinthDepth * 0.5f;

    enum Side { South, East, North, West }

    // Materials, created once per run and reused by every builder below.
    static Material s_MatFloor, s_MatWall, s_MatCore, s_MatCeiling,
                    s_MatPlinth, s_MatPanel, s_MatRoute, s_MatPad;

    // =========================================================================
    //  ENTRY POINT
    // =========================================================================

    [MenuItem("Haku/Generate Grey-box Museum")]
    public static void Generate()
    {
        int exitCode = 0;
        try
        {
            Build();
        }
        catch (Exception e)
        {
            Debug.LogError("[Haku] Generation FAILED: " + e);
            exitCode = 1;
        }
        finally
        {
            if (Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }
    }

    static void Build()
    {
        ValidateTuning();

        EnsureFolder(SceneFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(SettingsFolder);
        CreateMaterials();

        // Open the existing scene if there is one so hand-added objects survive a
        // regeneration; otherwise start from an EMPTY scene (no stray default
        // camera / directional light to fight with).
        Scene scene;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Log("Opened existing scene " + ScenePath + " - regenerating in place.");
        }
        else
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Log("Created a new empty scene.");
        }

        // Idempotency: nuke anything we generated last time. Nothing else is touched.
        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == RootName)
                UnityEngine.Object.DestroyImmediate(go);

        // Root is at identity so every child transform below is also world space.
        var root = new GameObject(RootName);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        BuildShell(root.transform);
        BuildCore(root.transform);
        BuildRouteStrip(root.transform);

        Type exhibitPointType = FindExhibitPointType();
        Type teleportAnchorType = FindTeleportAnchorType();
        List<StationSpec> specs = LayOutRoute();
        var stationsRoot = new GameObject("Stations");
        stationsRoot.transform.SetParent(root.transform, false);
        for (int i = 0; i < specs.Count; i++)
            BuildStation(stationsRoot.transform, i, specs[i], exhibitPointType, teleportAnchorType);

        BuildLighting(root.transform, scene);
        BuildRig(root.transform, exhibitPointType != null || teleportAnchorType != null);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new Exception("EditorSceneManager.SaveScene returned false for " + ScenePath);
        AssetDatabase.SaveAssets();
        SetAsFirstBuildScene();

        ReportBudget(scene, root);
        Log("DONE. Scene saved to " + ScenePath + " and set as build index 0.");
    }

    static void ValidateTuning()
    {
        if (CoreHalfX <= 2f || CoreHalfZ <= 2f)
            throw new Exception(string.Format(
                "Tuning is impossible: RoomWidth/Length ({0} x {1}) minus 2x CorridorWidth ({2}) " +
                "leaves no core block. Widen the room or narrow the corridor.",
                RoomWidth, RoomLength, CorridorWidth));
        if (CorridorWidth < 3.5f)
            Debug.LogWarning("[Haku] CorridorWidth is " + CorridorWidth +
                             " m. Below 3.5 m a headset wearer feels pinched.");
        if (RoomHeight < 4.0f)
            Debug.LogWarning("[Haku] RoomHeight is " + RoomHeight + " m. Galleries read as offices below ~4 m.");
    }

    // =========================================================================
    //  ROUTE LAYOUT
    //  Stations are emitted in WALKING ORDER: south wall west->east, up the east
    //  wall, north wall east->west, down the west wall, closing the loop at 1.
    //  'along' is the station's world coordinate on its wall's own axis.
    // =========================================================================

    struct StationSpec
    {
        public Side Side;
        public float Along;
    }

    static List<StationSpec> LayOutRoute()
    {
        // Stations only occupy the span that faces the core; the corridor-width
        // corners at each end stay clear so turning never means squeezing past a plinth.
        float longUsable = RoomWidth - 2f * CorridorWidth;
        float shortUsable = RoomLength - 2f * CorridorWidth;

        var list = new List<StationSpec>(StationCount);
        for (int i = 0; i < StationsPerLongWall; i++)   // walk +X
            list.Add(new StationSpec { Side = Side.South, Along = Spread(i, StationsPerLongWall, longUsable) });
        for (int i = 0; i < StationsPerShortWall; i++)  // walk +Z
            list.Add(new StationSpec { Side = Side.East, Along = Spread(i, StationsPerShortWall, shortUsable) });
        for (int i = 0; i < StationsPerLongWall; i++)   // walk -X, so mirror the spread
            list.Add(new StationSpec { Side = Side.North, Along = -Spread(i, StationsPerLongWall, longUsable) });
        for (int i = 0; i < StationsPerShortWall; i++)  // walk -Z
            list.Add(new StationSpec { Side = Side.West, Along = -Spread(i, StationsPerShortWall, shortUsable) });
        return list;
    }

    /// Evenly distributes n items across a span of length L, centred on 0.
    static float Spread(int i, int n, float length)
    {
        return length * ((i + 0.5f) / n) - length * 0.5f;
    }

    /// Point on the inner face of the wall this station belongs to, at floor level.
    static Vector3 WallFacePoint(Side side, float along)
    {
        switch (side)
        {
            case Side.South: return new Vector3(along, 0f, -RoomLength * 0.5f);
            case Side.North: return new Vector3(along, 0f, RoomLength * 0.5f);
            case Side.East: return new Vector3(RoomWidth * 0.5f, 0f, along);
            default: return new Vector3(-RoomWidth * 0.5f, 0f, along);
        }
    }

    /// Unit vector pointing from the wall INTO the room.
    static Vector3 Inward(Side side)
    {
        switch (side)
        {
            case Side.South: return Vector3.forward;
            case Side.North: return Vector3.back;
            case Side.East: return Vector3.left;
            default: return Vector3.right;
        }
    }

    // =========================================================================
    //  GEOMETRY
    // =========================================================================

    static void BuildShell(Transform parent)
    {
        var shell = new GameObject("Shell");
        shell.transform.SetParent(parent, false);

        float outerW = RoomWidth + 2f * WallThickness;
        float outerL = RoomLength + 2f * WallThickness;
        float halfW = RoomWidth * 0.5f + WallThickness * 0.5f;
        float halfL = RoomLength * 0.5f + WallThickness * 0.5f;

        // Floor top sits exactly at y = 0 so every other height in this file is
        // an honest "height above the floor".
        Box(shell.transform, "Floor",
            new Vector3(0f, -WallThickness * 0.5f, 0f),
            new Vector3(outerW, WallThickness, outerL), s_MatFloor);

        Box(shell.transform, "Ceiling",
            new Vector3(0f, RoomHeight + WallThickness * 0.5f, 0f),
            new Vector3(outerW, WallThickness, outerL), s_MatCeiling);

        Box(shell.transform, "Wall_South",
            new Vector3(0f, RoomHeight * 0.5f, -halfL),
            new Vector3(outerW, RoomHeight, WallThickness), s_MatWall);
        Box(shell.transform, "Wall_North",
            new Vector3(0f, RoomHeight * 0.5f, halfL),
            new Vector3(outerW, RoomHeight, WallThickness), s_MatWall);
        Box(shell.transform, "Wall_East",
            new Vector3(halfW, RoomHeight * 0.5f, 0f),
            new Vector3(WallThickness, RoomHeight, RoomLength), s_MatWall);
        Box(shell.transform, "Wall_West",
            new Vector3(-halfW, RoomHeight * 0.5f, 0f),
            new Vector3(WallThickness, RoomHeight, RoomLength), s_MatWall);
    }

    static void BuildCore(Transform parent)
    {
        // Four dividing walls, not one solid block: the cavity inside is sealed
        // for now, but leaving it hollow means a later iteration can knock a
        // doorway through and turn it into a media / orientation room without
        // re-cutting geometry. It costs the same 48 triangles either way.
        var core = new GameObject("Core_DividingWalls");
        core.transform.SetParent(parent, false);

        float y = RoomHeight * 0.5f;
        float innerZ = 2f * CoreHalfZ - 2f * WallThickness;

        Box(core.transform, "Divider_South",
            new Vector3(0f, y, -CoreHalfZ + WallThickness * 0.5f),
            new Vector3(2f * CoreHalfX, RoomHeight, WallThickness), s_MatCore);
        Box(core.transform, "Divider_North",
            new Vector3(0f, y, CoreHalfZ - WallThickness * 0.5f),
            new Vector3(2f * CoreHalfX, RoomHeight, WallThickness), s_MatCore);
        Box(core.transform, "Divider_East",
            new Vector3(CoreHalfX - WallThickness * 0.5f, y, 0f),
            new Vector3(WallThickness, RoomHeight, innerZ), s_MatCore);
        Box(core.transform, "Divider_West",
            new Vector3(-CoreHalfX + WallThickness * 0.5f, y, 0f),
            new Vector3(WallThickness, RoomHeight, innerZ), s_MatCore);
    }

    static void BuildRouteStrip(Transform parent)
    {
        // A closed rectangle of emissive floor tape on the ring centreline.
        // This is the single cheapest wayfinding device available: it is visible
        // from anywhere on the loop, it survives a dark room, and it removes any
        // ambiguity at the corners where the next plinth is not yet in view.
        var strip = new GameObject("RouteStrip");
        strip.transform.SetParent(parent, false);

        const float h = 0.012f;   // 12 mm proud of the floor; no trip hazard, no z-fight
        float y = h * 0.5f;
        float lengthX = 2f * RingHalfX + RouteStripWidth;   // long runs own the corners
        float lengthZ = 2f * RingHalfZ - RouteStripWidth;   // short runs stop short of them

        Box(strip.transform, "Route_South", new Vector3(0f, y, -RingHalfZ),
            new Vector3(lengthX, h, RouteStripWidth), s_MatRoute);
        Box(strip.transform, "Route_North", new Vector3(0f, y, RingHalfZ),
            new Vector3(lengthX, h, RouteStripWidth), s_MatRoute);
        Box(strip.transform, "Route_East", new Vector3(RingHalfX, y, 0f),
            new Vector3(RouteStripWidth, h, lengthZ), s_MatRoute);
        Box(strip.transform, "Route_West", new Vector3(-RingHalfX, y, 0f),
            new Vector3(RouteStripWidth, h, lengthZ), s_MatRoute);
    }

    /// One station = plinth + display volume marker + wall panel + teleport anchor
    /// + its own spot light. The station root sits at the plinth footprint centre
    /// and FACES INTO THE ROOM, so every child below is placed in comfortable local
    /// space: -Z is "towards the wall", +Z is "towards the visitor".
    static void BuildStation(Transform parent, int index, StationSpec spec,
                             Type exhibitPointType, Type teleportAnchorType)
    {
        int number = index + 1;
        Vector3 inward = Inward(spec.Side);
        Vector3 wallPoint = WallFacePoint(spec.Side, spec.Along);
        Vector3 stationPos = wallPoint + inward * PlinthOffsetFromWall;

        var station = new GameObject(string.Format("Station_{0:00}_{1}", number, spec.Side));
        station.transform.SetParent(parent, false);
        station.transform.SetPositionAndRotation(stationPos, Quaternion.LookRotation(inward, Vector3.up));

        // --- plinth ---------------------------------------------------------
        GameObject plinth = Box(station.transform, string.Format("Plinth_{0:00}", number),
            new Vector3(0f, PlinthHeight * 0.5f, 0f),
            new Vector3(PlinthWidth, PlinthHeight, PlinthDepth), s_MatPlinth);

        // --- display volume marker -------------------------------------------
        // Empty on purpose. Drop the real artefact prefab in as a child of this
        // and it lands centred at inspection height with the spot already on it.
        var slot = new GameObject(string.Format("ART_SLOT_{0:00}_DropModelHere", number));
        slot.transform.SetParent(station.transform, false);
        slot.transform.localPosition = new Vector3(0f, PlinthHeight + DisplayVolumeHeight * 0.5f, 0f);
        slot.transform.localRotation = Quaternion.identity;

        // --- wall panel -------------------------------------------------------
        Box(station.transform, string.Format("WallPanel_{0:00}", number),
            new Vector3(0f, PanelCentreHeight, -(PlinthOffsetFromWall - PanelThickness * 0.5f - 0.005f)),
            new Vector3(PanelWidth, PanelHeight, PanelThickness), s_MatPanel);

        // --- teleport anchor ---------------------------------------------------
        // The empty carries the ARRIVAL POSE: yaw 180 in station space means the
        // visitor's forward vector points back at the plinth, i.e. they always
        // materialise already looking at the exhibit, never at a blank wall.
        var anchor = new GameObject(string.Format("TeleportAnchor_{0:00}", number));
        anchor.transform.SetParent(station.transform, false);
        anchor.transform.localPosition = new Vector3(0f, 0f, TeleportStandoff);
        anchor.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        GameObject pad = Cylinder(anchor.transform, string.Format("TeleportPad_{0:00}", number),
            new Vector3(0f, 0.011f, 0f),
            new Vector3(TeleportPadDiameter, 0.011f, TeleportPadDiameter), s_MatPad);

        if (teleportAnchorType != null)
        {
            try
            {
                Component ta = pad.AddComponent(teleportAnchorType);
                SetMember(ta, new[] { "teleportAnchorTransform" }, anchor.transform);
                EditorUtility.SetDirty(ta);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Haku] Could not configure TeleportationAnchor on station " +
                                 number + ": " + e.Message);
            }
        }

        // --- spot -------------------------------------------------------------
        var spotGo = new GameObject(string.Format("Spot_{0:00}", number));
        spotGo.transform.SetParent(station.transform, false);
        spotGo.transform.localPosition = new Vector3(0f, RoomHeight - SpotCeilingDrop, SpotForwardOffset);
        Light spot = spotGo.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color(1.00f, 0.96f, 0.90f);      // warm gallery halogen
        spot.intensity = SpotIntensity;
        spot.spotAngle = SpotAngle;
        spot.range = Vector3.Distance(spotGo.transform.position, slot.transform.position) + 2.0f;
        spot.shadows = LightShadows.Soft;
        spot.bounceIntensity = 1.0f;
        // BAKED: see the lighting notes in BuildLighting(). Do not flip these to
        // Realtime and then wonder why the Quest 3 frame time tripled.
        spot.lightmapBakeType = LightmapBakeType.Baked;
        spotGo.transform.LookAt(slot.transform.position, Vector3.up);

        // --- ExhibitPoint ------------------------------------------------------
        // Attached to the plinth because that is the object with the collider the
        // XRI ray / poke will actually hit.
        if (exhibitPointType != null)
        {
            Component ep = plinth.AddComponent(exhibitPointType);
            string id = (number == 1) ? FirstExhibitId : string.Empty;
            bool wrote = SetStringPropertyLikeExhibitId(ep, id);
            EditorUtility.SetDirty(ep);
            if (number == 1)
                Log(wrote
                    ? "Station 01 ExhibitPoint id set to \"" + FirstExhibitId + "\"."
                    : "ExhibitPoint attached but no exhibit-id string field was found - set station 01 by hand.");
        }
    }

    // =========================================================================
    //  LIGHTING
    // =========================================================================

    static void BuildLighting(Transform parent, Scene scene)
    {
        // ---------------------------------------------------------------------
        //  MOOD: near-black ambient, one weak directional for shape, and eight
        //  hard spots that make the plinths the only bright things in the room.
        //  Dark room + lit object = the artefact is the subject. That is the
        //  whole trick, and it is free.
        // ---------------------------------------------------------------------
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.055f, 0.060f, 0.075f); // cold, very low
        RenderSettings.ambientIntensity = 1.0f;
        RenderSettings.fog = false;                                       // fog is a fill-rate tax on mobile
        RenderSettings.skybox = null;                                     // no skybox: nothing outside is visible anyway
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom; // and no bright skybox reflections

        var lights = new GameObject("Lighting");
        lights.transform.SetParent(parent, false);

        var keyGo = new GameObject("KeyLight_Directional");
        keyGo.transform.SetParent(lights.transform, false);
        keyGo.transform.rotation = Quaternion.Euler(52f, -34f, 0f);
        Light key = keyGo.AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(0.88f, 0.92f, 1.00f);   // cool, to contrast the warm spots
        key.intensity = KeyLightIntensity;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.85f;
        // MIXED, not Realtime: static geometry takes its shadows from the lightmap,
        // and anything dynamic Mazin drops in later still gets a real-time shadow
        // from this single light. One shadow-casting realtime light is the budget.
        key.lightmapBakeType = LightmapBakeType.Mixed;

        // ---------------------------------------------------------------------
        //  *** WHAT MUST BE BAKED BEFORE THIS SHIPS ***
        //
        //  All 8 plinth spots are marked BAKED and contribute NOTHING until you
        //  bake. Straight after generation the room will look flat and dim - that
        //  is correct, not a bug.
        //
        //  Bake steps:
        //    1. Window > Rendering > Lighting. The scene already points at
        //       Assets/Settings/Haku_Museum_Lighting.lighting (Auto Generate OFF,
        //       Progressive GPU).
        //    2. Confirm every mesh under HAKU_MUSEUM_GENERATED is Contribute GI
        //       (this script sets that flag) and press Generate Lighting.
        //    3. Mixed lighting mode: Subtractive or Baked Indirect. Shadowmask
        //       costs an extra texture fetch per pixel on Quest and buys us
        //       nothing here - there is one mixed light in the whole scene.
        //
        //  WHY: on Quest 3 the URP forward path re-renders affected geometry per
        //  additional realtime light. Eight realtime shadow-casting spots over a
        //  room with ~40 renderers is how a 700-1000 draw call budget turns into
        //  several thousand and 72 fps turns into 45. Baked spots are free at
        //  runtime: they are pixels in a lightmap.
        //
        //  CAVEAT on this grey-box specifically: Unity's built-in primitives have
        //  cramped/overlapping lightmap UVs. Expect blotchy plinth bakes. When the
        //  real geometry arrives (or if you ProBuilder the boxes), tick Generate
        //  Lightmap UVs on the meshes and the bake cleans up.
        // ---------------------------------------------------------------------
        ApplyLightingSettings(scene);
    }

    static void ApplyLightingSettings(Scene scene)
    {
        try
        {
            LightingSettings ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "Haku_Museum_Lighting" };
                AssetDatabase.CreateAsset(ls, LightingSettingsPath);
            }
            ls.autoGenerate = false;                                  // never auto-bake in batch mode
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = 12f;                              // texels/unit: plenty for a grey-box
            ls.lightmapMaxSize = 1024;                                // keep the atlas Quest-friendly
            ls.ao = true;
            EditorUtility.SetDirty(ls);
            Lightmapping.SetLightingSettingsForScene(scene, ls);
            Log("Lighting settings asset assigned: " + LightingSettingsPath + " (Auto Generate OFF).");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Haku] Could not create/assign LightingSettings: " + e.Message +
                             " - set the Lighting window up by hand before baking.");
        }
    }

    // =========================================================================
    //  RIG  (XR Origin if XRI/core-utils has imported, plain camera if not)
    // =========================================================================

    static void BuildRig(Transform parent, bool xriPresent)
    {
        // Spawn in the south-west corner of the ring facing +X, looking straight
        // down the first straight: station 1 is dead ahead, 2 and 3 behind it.
        Vector3 spawn = new Vector3(-RingHalfX, 0f, -RingHalfZ);
        Quaternion spawnRot = Quaternion.LookRotation(Vector3.right, Vector3.up);

        Type xrOriginType = FindType("Unity.XR.CoreUtils.XROrigin");

        if (xrOriginType == null)
        {
            // ---- FALLBACK PATH -------------------------------------------------
            Log("PATH: XR Origin type NOT found (Unity.XR.CoreUtils.XROrigin). " +
                "XRI has not finished importing, or core-utils is missing. " +
                "Generated a plain Main Camera at 1.6 m instead - re-run this generator " +
                "once XRI has compiled and you will get a real rig.");
            var camGo = new GameObject("Main Camera");
            camGo.transform.SetParent(parent, false);
            camGo.transform.SetPositionAndRotation(spawn + Vector3.up * 1.6f, spawnRot);
            ConfigureCamera(camGo);
            return;
        }

        // ---- XR PATH ---------------------------------------------------------
        Log("PATH: XR Origin type found - building a real XR rig (" + xrOriginType.FullName + ").");

        var originGo = new GameObject("XR Origin (Haku)");
        originGo.transform.SetParent(parent, false);
        originGo.transform.SetPositionAndRotation(spawn, spawnRot);

        var offsetGo = new GameObject("Camera Offset");
        offsetGo.transform.SetParent(originGo.transform, false);
        offsetGo.transform.localPosition = Vector3.zero;   // Floor tracking origin: headset supplies the height

        var camObj = new GameObject("Main Camera");
        camObj.transform.SetParent(offsetGo.transform, false);
        camObj.transform.localPosition = Vector3.zero;
        Camera cam = ConfigureCamera(camObj);

        Component origin = originGo.AddComponent(xrOriginType);
        SetMember(origin, new[] { "Camera" }, cam);
        SetMember(origin, new[] { "CameraFloorOffsetObject" }, offsetGo);
        try
        {
            PropertyInfo modeProp = xrOriginType.GetProperty("RequestedTrackingOriginMode",
                BindingFlags.Public | BindingFlags.Instance);
            if (modeProp != null && modeProp.CanWrite)
                modeProp.SetValue(origin, Enum.Parse(modeProp.PropertyType, "Floor"));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Haku] Could not set RequestedTrackingOriginMode=Floor: " + e.Message);
        }
        EditorUtility.SetDirty(origin);

        TryAddInteractionManager(parent, xriPresent);
        TryWireHeadTracking(camObj);

        Log("TODO for Mazin: this rig is head-tracked only. Add controllers/hands and a " +
            "TeleportationProvider (XRI locomotion) before the walkthrough - the 8 " +
            "TeleportAnchor_XX transforms are already posed and waiting.");
    }

    static Camera ConfigureCamera(GameObject go)
    {
        go.tag = "MainCamera";
        Camera cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;      // dark gallery; also cheapest clear
        cam.nearClipPlane = 0.05f;              // Quest-appropriate; 0.3 clips your own hands
        cam.farClipPlane = 120f;                // room is 30 m across, no need for 1000
        if (UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 0)
            go.AddComponent<AudioListener>();
        return cam;
    }

    static void TryAddInteractionManager(Transform parent, bool xriPresent)
    {
        if (!xriPresent) return;
        Type imType = FindType("UnityEngine.XR.Interaction.Toolkit.XRInteractionManager");
        if (imType == null) return;
        if (UnityEngine.Object.FindFirstObjectByType(imType) != null) return;
        var go = new GameObject("XR Interaction Manager");
        go.transform.SetParent(parent, false);
        go.AddComponent(imType);
        Log("Added XRInteractionManager.");
    }

    /// Without a TrackedPoseDriver the headset does not move the camera on device.
    /// Everything here is reflection so this file compiles with or without the
    /// Input System, and any failure is a warning with a manual fix, not a crash.
    static void TryWireHeadTracking(GameObject camGo)
    {
        Type tpdType = FindType("UnityEngine.InputSystem.XR.TrackedPoseDriver");
        if (tpdType == null)
        {
            Debug.LogWarning("[Haku] TrackedPoseDriver not found - the camera will NOT follow the " +
                             "headset. Add one to Main Camera and bind centerEyePosition/Rotation.");
            return;
        }
        try
        {
            Component tpd = camGo.AddComponent(tpdType);
            Type actionType = FindType("UnityEngine.InputSystem.InputAction");
            Type actionEnum = FindType("UnityEngine.InputSystem.InputActionType");
            Type propType = FindType("UnityEngine.InputSystem.InputActionProperty");
            if (actionType == null || actionEnum == null || propType == null)
                throw new Exception("Input System types incomplete.");

            object valueKind = Enum.Parse(actionEnum, "Value");
            object posAction = Activator.CreateInstance(actionType,
                new object[] { "HMD Position", valueKind, "<XRHMD>/centerEyePosition", null, null, "Vector3" });
            object rotAction = Activator.CreateInstance(actionType,
                new object[] { "HMD Rotation", valueKind, "<XRHMD>/centerEyeRotation", null, null, "Quaternion" });

            object posProp = Activator.CreateInstance(propType, new object[] { posAction });
            object rotProp = Activator.CreateInstance(propType, new object[] { rotAction });

            bool ok = SetMember(tpd, new[] { "positionInput", "positionAction" }, posProp)
                    & SetMember(tpd, new[] { "rotationInput", "rotationAction" }, rotProp);
            EditorUtility.SetDirty(tpd);
            Log(ok ? "TrackedPoseDriver wired to <XRHMD>/centerEye*."
                   : "TrackedPoseDriver added but its input properties could not be set - bind them in the Inspector.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Haku] TrackedPoseDriver auto-wiring failed (" + e.Message +
                             "). Bind position/rotation to <XRHMD>/centerEyePosition and " +
                             "<XRHMD>/centerEyeRotation in the Inspector.");
        }
    }

    // =========================================================================
    //  PRIMITIVE HELPERS
    // =========================================================================

    static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);   // 1 m cube, so scale == size
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        MarkStatic(go);
        return go;
    }

    static GameObject Cylinder(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); // radius .5, HALF-height 1
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        // The primitive ships with a CapsuleCollider, which on a 22 mm disc is a
        // fat invisible dome you would bump into. Swap it for a flat box.
        Collider capsule = go.GetComponent<Collider>();
        if (capsule != null) UnityEngine.Object.DestroyImmediate(capsule);
        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.size = new Vector3(1f, 1f, 1f);
        MarkStatic(go);
        return go;
    }

    static void MarkStatic(GameObject go)
    {
        // Static Batching is ON for this project, so everything here must be
        // flagged BatchingStatic or the batcher cannot merge it. ContributeGI is
        // what makes the baked spots actually land on the geometry.
        GameObjectUtility.SetStaticEditorFlags(go,
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.ReflectionProbeStatic);
    }

    // =========================================================================
    //  MATERIALS
    // =========================================================================

    static void CreateMaterials()
    {
        Shader shader = PickShader();
        Log("Materials use shader: " + (shader != null ? shader.name : "<none found!>"));

        // Values are deliberately mid-to-dark: a grey-box that is too bright hides
        // the lighting design, and every one of these is a placeholder anyway.
        s_MatFloor = MakeMaterial(shader, "Haku_Floor", new Color(0.155f, 0.150f, 0.145f), 0.25f, Color.black);
        s_MatWall = MakeMaterial(shader, "Haku_Wall", new Color(0.600f, 0.595f, 0.575f), 0.05f, Color.black);
        s_MatCore = MakeMaterial(shader, "Haku_Core", new Color(0.470f, 0.470f, 0.480f), 0.05f, Color.black);
        s_MatCeiling = MakeMaterial(shader, "Haku_Ceiling", new Color(0.110f, 0.110f, 0.115f), 0.02f, Color.black);
        s_MatPlinth = MakeMaterial(shader, "Haku_Plinth", new Color(0.780f, 0.775f, 0.760f), 0.10f, Color.black);
        s_MatPanel = MakeMaterial(shader, "Haku_Panel", new Color(0.880f, 0.875f, 0.850f), 0.08f, Color.black);
        s_MatRoute = MakeMaterial(shader, "Haku_RouteStrip", new Color(0.720f, 0.430f, 0.140f), 0.30f,
                                   new Color(0.90f, 0.48f, 0.12f) * 1.6f);
        s_MatPad = MakeMaterial(shader, "Haku_TeleportPad", new Color(0.300f, 0.620f, 0.720f), 0.30f,
                                   new Color(0.28f, 0.62f, 0.75f) * 1.2f);
    }

    static Shader PickShader()
    {
        // The project template is Built-in RP with the URP package added on top.
        // Whether URP is actually the active pipeline depends on whether a URP
        // asset got assigned in Graphics settings, so ask instead of assuming.
        if (GraphicsSettings.currentRenderPipeline != null)
        {
            Shader urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp != null) return urp;
            Debug.LogWarning("[Haku] An SRP is active but 'Universal Render Pipeline/Lit' was not found. " +
                             "Falling back to Standard - expect magenta.");
        }
        Shader std = Shader.Find("Standard");
        if (std != null) return std;
        return Shader.Find("Universal Render Pipeline/Lit");
    }

    static Material MakeMaterial(Shader shader, string assetName, Color albedo, float smoothness, Color emission)
    {
        string path = MaterialFolder + "/" + assetName + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != shader && shader != null)
        {
            m.shader = shader;
        }

        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", albedo);   // URP
        if (m.HasProperty("_Color")) m.SetColor("_Color", albedo);           // Built-in
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);

        if (emission.maxColorComponent > 0.001f)
        {
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission);
            // BakedEmissive so the route strip also contributes to the lightmap -
            // free bounce light along the corridor for zero runtime cost.
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        else
        {
            m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        EditorUtility.SetDirty(m);
        return m;
    }

    // =========================================================================
    //  REFLECTION HELPERS - so this file compiles before the runtime scripts and
    //  the XR packages exist, and still does the right thing once they do.
    // =========================================================================

    static Type FindExhibitPointType()
    {
        Type t = TypeCache.GetTypesDerivedFrom<MonoBehaviour>()
                          .FirstOrDefault(x => x.Name == "ExhibitPoint");
        Log(t != null
            ? "ExhibitPoint found (" + t.FullName + ") - attaching to all " + StationCount + " plinths."
            : "ExhibitPoint NOT found - plinths generated without it. Copy the runtime scripts into " +
              "Assets/Scripts/Haku/ and re-run to wire them up.");
        return t;
    }

    static Type FindTeleportAnchorType()
    {
        // XRI 3.x moved this; try the new namespace first, then the 2.x one.
        Type t = FindType("UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor")
              ?? FindType("UnityEngine.XR.Interaction.Toolkit.TeleportationAnchor");
        Log(t != null
            ? "TeleportationAnchor found (" + t.FullName + ") - pads are teleport targets."
            : "TeleportationAnchor NOT found - TeleportAnchor_XX transforms are plain markers for now.");
        return t;
    }

    static Type FindType(string fullName)
    {
        Type t = Type.GetType(fullName);
        if (t != null) return t;
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                t = asm.GetType(fullName);
                if (t != null) return t;
            }
            catch { /* dynamic or unloadable assembly - ignore */ }
        }
        return null;
    }

    static bool SetMember(object target, string[] candidateNames, object value)
    {
        if (target == null || value == null) return false;
        Type t = target.GetType();
        foreach (string name in candidateNames)
        {
            PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite && p.PropertyType.IsInstanceOfType(value))
            {
                p.SetValue(target, value);
                return true;
            }
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null && f.FieldType.IsInstanceOfType(value))
            {
                f.SetValue(target, value);
                return true;
            }
        }
        return false;
    }

    /// ExhibitPoint's field name is not known to this generator (it is written in
    /// a file that may not exist yet), so find the string field that looks like an
    /// exhibit id via SerializedObject. Works for public fields and [SerializeField]
    /// privates alike.
    static bool SetStringPropertyLikeExhibitId(Component component, string id)
    {
        var so = new SerializedObject(component);

        // Pass 1: a string field mentioning both "exhibit" and "id".
        SerializedProperty it = so.GetIterator();
        while (it.NextVisible(true))
        {
            if (it.propertyType != SerializedPropertyType.String) continue;
            string n = it.name.ToLowerInvariant();
            if (n.Contains("exhibit") && n.Contains("id"))
            {
                it.stringValue = id;
                so.ApplyModifiedPropertiesWithoutUndo();
                return true;
            }
        }

        // Pass 2: a string field simply called "id".
        it = so.GetIterator();
        while (it.NextVisible(true))
        {
            if (it.propertyType != SerializedPropertyType.String) continue;
            string n = it.name.ToLowerInvariant().Replace("m_", "");
            if (n == "id")
            {
                it.stringValue = id;
                so.ApplyModifiedPropertiesWithoutUndo();
                return true;
            }
        }
        return false;
    }

    // =========================================================================
    //  PROJECT PLUMBING
    // =========================================================================

    static void EnsureFolder(string projectRelativePath)
    {
        projectRelativePath = projectRelativePath.Replace('\\', '/').TrimEnd('/');
        if (projectRelativePath == "Assets") return;
        if (AssetDatabase.IsValidFolder(projectRelativePath)) return;

        string parent = Path.GetDirectoryName(projectRelativePath).Replace('\\', '/');
        string leaf = Path.GetFileName(projectRelativePath);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    static void SetAsFirstBuildScene()
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes
            .Where(s => s.path != ScenePath)
            .ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // =========================================================================
    //  BUDGET REPORT
    // =========================================================================

    static void ReportBudget(Scene scene, GameObject root)
    {
        int gameObjects = 0;
        foreach (GameObject r in scene.GetRootGameObjects())
            gameObjects += CountObjects(r.transform);

        MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        Light[] lights = root.GetComponentsInChildren<Light>(true);

        long triangles = 0;
        foreach (MeshFilter mf in filters)
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
                triangles += mesh.GetIndexCount(sm) / 3;
        }

        var materials = new HashSet<Material>();
        foreach (MeshRenderer r in renderers)
            foreach (Material m in r.sharedMaterials)
                if (m != null) materials.Add(m);

        const int DrawCallBudgetLow = 700, DrawCallBudgetHigh = 1000;
        const long TriBudgetLow = 1300000, TriBudgetHigh = 1800000;

        Log("---------------- SCENE BUDGET ----------------");
        Log(string.Format("GameObjects (whole scene) : {0}", gameObjects));
        Log(string.Format("MeshRenderers             : {0}  (unique materials: {1})", renderers.Length, materials.Count));
        Log(string.Format("Lights                    : {0}  (1 Mixed directional + {1} Baked spots)",
            lights.Length, StationCount));
        Log(string.Format("Triangles                 : {0:N0}", triangles));
        Log(string.Format("Stations                  : {0}", StationCount));
        Log("----------------------------------------------");
        Log(string.Format(
            "DRAW CALLS: {0} raw renderer submissions against a {1}-{2} budget ({3:P1} used). " +
            "Static Batching is ON and all geometry is flagged BatchingStatic, so the real " +
            "number collapses towards ~{4} material batches. All 8 spots are BAKED - if they " +
            "were realtime this figure would multiply by the number of lights touching each mesh.",
            renderers.Length, DrawCallBudgetLow, DrawCallBudgetHigh,
            renderers.Length / (float)DrawCallBudgetHigh, materials.Count));
        Log(string.Format(
            "TRIANGLES: {0:N0} against a {1:N0}-{2:N0} budget ({3:P3} used). " +
            "That leaves ~{4:N0} triangles for real art - about {5:N0} per exhibit across " +
            "{6} stations before the low end of the budget is touched.",
            triangles, TriBudgetLow, TriBudgetHigh, triangles / (float)TriBudgetHigh,
            TriBudgetLow - triangles, (TriBudgetLow - triangles) / Math.Max(1, StationCount), StationCount));

        if (renderers.Length > DrawCallBudgetHigh)
            Debug.LogWarning("[Haku] OVER the draw call budget before any art has been added.");
        if (triangles > TriBudgetHigh)
            Debug.LogWarning("[Haku] OVER the triangle budget before any art has been added.");
        Log("Reminder: this is the grey-box only. Re-run this report mentally after each " +
            "artefact import - the whole-scene ceiling is 1.8 M triangles at 72 fps, render scale floor 0.85.");
    }

    static int CountObjects(Transform t)
    {
        int n = 1;
        for (int i = 0; i < t.childCount; i++)
            n += CountObjects(t.GetChild(i));
        return n;
    }

    static void Log(string message)
    {
        Debug.Log("[HakuMuseumGenerator] " + message);
    }
}
