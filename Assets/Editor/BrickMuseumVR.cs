// =============================================================================
//  Brick Museum - VR  |  Meta Quest 3
//  Assets/Editor/BrickMuseumVR.cs
//
//  Builds the WHOLE brick museum from primitives in one click: floor, five
//  rooms with doorways, corridors, brick + tile materials with correct tiling,
//  ceiling, lighting, plinths, two exhibits, hinged doors and a VR rig. Saves
//  to Assets/Scenes/BrickMuseum_VR.unity and makes it build index 0.
//
//    Brick Museum > Generate VR Museum
//
//  Deliberately under its OWN top-level menu, not "Haku" and not "Tools", so it
//  can never be confused with the Haku grey-box generator again.
//
//  Re-running is safe: the generated root is destroyed and rebuilt in place.
//  Anything you hand-placed elsewhere in the scene is left alone.
//
//  OPTIONAL runtime scripts. If these are in Assets/Scripts/ they get wired up;
//  if not, the museum still builds and just logs that it skipped them:
//      Inspectable.cs, InspectController.cs, DoorInteract.cs
//
// -----------------------------------------------------------------------------
//  PLAN  (+X right, +Z up the page)
//
//        [ 5 ]--------[ 4 ]        z = +6      Room 5 has the EXIT door
//                        |
//    [ 1 ]---[ 2 ]---[ 3 ]         z = -6      Room 1 has the ENTRY door
//      |
//    ENTRY                          x = -13, 0, +13
//
//  One-way route. Every room is 8 x 8 with 3 m doorways; corridors are 3 m wide
//  and enclosed, so a visitor in a headset always has a wall to follow.
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

public static class BrickMuseumVR
{
    // =========================================================================
    // ==============            T U N I N G   B L O C K            ============
    // =========================================================================

    // ---- Shell -------------------------------------------------------------
    const float RoomSize   = 8.0f;    // rooms are square
    const float WallHeight = 4.0f;    // tall enough to read as a gallery in a headset
    const float WallThick  = 0.3f;    // thinner than this and walls look like paper
    const float DoorWidth  = 3.0f;    // doorways AND corridor width
    const float FloorWidth = 40.0f;
    const float FloorDepth = 30.0f;

    const bool  BuildCeiling = true;  // OFF only while editing from above

    // ---- Look ---------------------------------------------------------------
    /// World units covered by one repeat of the brick / tile texture.
    /// Lower = smaller bricks. This is what keeps a 2.5 m wall and an 8 m wall
    /// showing bricks of the same physical size.
    const float TextureSize = 2.0f;

    // ---- Lighting -----------------------------------------------------------
    const float LightIntensity   = 1.5f;   // the 2x2 grid in each room
    const float CorridorLight    = 2.2f;
    const float AmbientLevel     = 0.45f;  // 0 = black shadows, 0.7 = flat and even
    const float LightHeight      = 3.4f;

    /// Quest 3 cannot afford 26 realtime lights. Baked costs nothing at runtime.
    /// Set false only if you are testing on desktop and cannot wait for a bake.
    const bool  BakeLights = true;

    // ---- Exhibits -----------------------------------------------------------
    const float PlinthHeight = 1.0f;
    const float PlinthSize   = 0.75f;
    const int   SwordRoom = 1;   // 1 = entry room
    const int   JarRoom   = 2;
    /// Unity's TextMesh reads from the opposite face on some versions.
    const bool  FlipPlacardText = true;

    // =========================================================================
    // ==============       end of tuning block - derived below      ===========
    // =========================================================================

    const string RootName    = "BRICK_MUSEUM_VR";
    const string ScenePath   = "Assets/Scenes/BrickMuseum_VR.unity";
    const string SceneFolder = "Assets/Scenes";
    const string MatFolder   = "Assets/Materials/BrickMuseum";
    const string TexFolder   = "Assets/Textures";

    // Player spawns here, facing +Z into the entry porch.
    static readonly Vector3 Spawn = new Vector3(-13f, 0f, -12.5f);

    // Room centres, in walking order. Index 0 is the entry room.
    static readonly Vector2[] Rooms =
    {
        new Vector2(-13f, -6f),
        new Vector2(  0f, -6f),
        new Vector2( 13f, -6f),
        new Vector2( 13f,  6f),
        new Vector2(  0f,  6f),
    };

    // Which walls of each room have a doorway: North, East, South, West.
    static readonly bool[,] Doors =
    {
        { false, true,  true,  false },   // Room 1: in from the south, out east
        { false, true,  false, true  },   // Room 2
        { true,  false, false, true  },   // Room 3: out north
        { false, false, true,  true  },   // Room 4: out west
        { true,  true,  false, false },   // Room 5: out north = the exit
    };

    // Which side the visitor walks in from, so placards and exhibits face them.
    static readonly Vector3[] Approach =
    {
        new Vector3( 0f, 0f, -1f),
        new Vector3(-1f, 0f,  0f),
        new Vector3(-1f, 0f,  0f),
        new Vector3( 0f, 0f, -1f),
        new Vector3( 1f, 0f,  0f),
    };

    // Corridors along X:  x0, x1, centreZ
    static readonly Vector3[] CorridorsX =
    {
        new Vector3(-9f, -4f, -6f),   // 1 -> 2
        new Vector3( 4f,  9f, -6f),   // 2 -> 3
        new Vector3( 4f,  9f,  6f),   // 4 -> 5
    };

    // Corridors along Z:  z0, z1, centreX
    static readonly Vector3[] CorridorsZ =
    {
        new Vector3( -2f,   2f,  13f),   // 3 -> 4
        new Vector3(-13f, -10f, -13f),   // entry porch
        new Vector3( 10f,  13f,   0f),   // exit porch
    };

    // Doorways that get a real hinged door: centre of the opening at floor level.
    static readonly Vector3[] DoorFrames =
    {
        new Vector3(-13f, 0f, -10f),   // entry, Room 1 south wall
        new Vector3(  0f, 0f,  10f),   // exit,  Room 5 north wall
    };

    static Transform s_Root;
    static bool s_Urp;
    static Shader s_Shader;
    static string s_Albedo, s_Smooth;
    static Texture2D s_BrickTex, s_BrickNrm, s_FloorTex, s_FloorNrm;
    static Material s_Ceiling, s_Plinth, s_Placard, s_Steel, s_Hilt,
                    s_Stone, s_Face, s_Paint, s_Gold, s_Wood, s_Brass, s_Lintel;
    static readonly Dictionary<string, Material> s_TileCache = new Dictionary<string, Material>();

    // =========================================================================
    //  ENTRY POINT
    // =========================================================================

    [MenuItem("Brick Museum/Generate VR Museum", false, 1)]
    public static void Generate()
    {
        int exit = 0;
        try { Build(); }
        catch (Exception e) { Debug.LogError("[BrickMuseum] FAILED: " + e); exit = 1; }
        finally { if (Application.isBatchMode) EditorApplication.Exit(exit); }
    }

    static void Build()
    {
        EnsureFolder(SceneFolder);
        EnsureFolder(MatFolder);
        PickShader();
        LoadTextures();
        CreateSharedMaterials();

        Scene scene;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Log("Opened " + ScenePath + " - regenerating in place.");
        }
        else
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Log("Created a new empty scene.");
        }

        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == RootName || go.name == "Player")
                UnityEngine.Object.DestroyImmediate(go);

        var root = new GameObject(RootName);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        s_Root = root.transform;

        BuildFloor();
        BuildRooms();
        BuildCorridors();
        if (BuildCeiling) BuildCeilings();
        BuildDoors();
        BuildLighting();
        BuildExhibits();
        BuildRig();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new Exception("SaveScene returned false for " + ScenePath);
        AssetDatabase.SaveAssets();
        SetAsFirstBuildScene();

        Report(root);
        Log("DONE. Saved to " + ScenePath + " and set as build index 0.");
        if (BakeLights)
            Log("Lights are BAKED and contribute nothing until you run " +
                "Window > Rendering > Lighting > Generate Lighting. The room will look flat until then.");
    }

    // =========================================================================
    //  GEOMETRY
    // =========================================================================

    static void BuildFloor()
    {
        var g = Box(s_Root, "Floor", new Vector3(0f, -0.1f, 0f),
                    new Vector3(FloorWidth, 0.2f, FloorDepth), null);
        g.GetComponent<Renderer>().sharedMaterial =
            TiledMaterial(true, FloorWidth / TextureSize, FloorDepth / TextureSize);
        MarkStatic(g);
    }

    static void BuildRooms()
    {
        var parent = new GameObject("Rooms").transform;
        parent.SetParent(s_Root, false);

        float h = RoomSize * 0.5f;
        for (int i = 0; i < Rooms.Length; i++)
        {
            Vector2 c = Rooms[i];
            string p = "Room" + (i + 1) + "_";
            WallX(parent, p + "N", c.x, c.y + h, RoomSize, Doors[i, 0]);
            WallZ(parent, p + "E", c.x + h, c.y, RoomSize, Doors[i, 1]);
            WallX(parent, p + "S", c.x, c.y - h, RoomSize, Doors[i, 2]);
            WallZ(parent, p + "W", c.x - h, c.y, RoomSize, Doors[i, 3]);
        }
    }

    static void BuildCorridors()
    {
        var parent = new GameObject("Corridors").transform;
        parent.SetParent(s_Root, false);

        foreach (var c in CorridorsX)
        {
            float len = c.y - c.x, cx = (c.x + c.y) * 0.5f;
            Wall(parent, "Corridor", new Vector3(cx, WallHeight * 0.5f, c.z + DoorWidth * 0.5f),
                 new Vector3(len, WallHeight, WallThick), 0f);
            Wall(parent, "Corridor", new Vector3(cx, WallHeight * 0.5f, c.z - DoorWidth * 0.5f),
                 new Vector3(len, WallHeight, WallThick), 0f);
        }
        foreach (var c in CorridorsZ)
        {
            float len = c.y - c.x, cz = (c.x + c.y) * 0.5f;
            Wall(parent, "Corridor", new Vector3(c.z + DoorWidth * 0.5f, WallHeight * 0.5f, cz),
                 new Vector3(len, WallHeight, WallThick), 90f);
            Wall(parent, "Corridor", new Vector3(c.z - DoorWidth * 0.5f, WallHeight * 0.5f, cz),
                 new Vector3(len, WallHeight, WallThick), 90f);
        }
    }

    static void BuildCeilings()
    {
        var parent = new GameObject("Ceiling").transform;
        parent.SetParent(s_Root, false);

        const float slab = 0.2f, over = 0.3f;
        float y = WallHeight + slab * 0.5f;

        foreach (var c in Rooms)
            Slab(parent, new Vector3(c.x, y, c.y),
                 new Vector3(RoomSize + over, slab, RoomSize + over));

        foreach (var c in CorridorsX)
            Slab(parent, new Vector3((c.x + c.y) * 0.5f, y, c.z),
                 new Vector3(c.y - c.x + over, slab, DoorWidth + over));

        foreach (var c in CorridorsZ)
            Slab(parent, new Vector3(c.z, y, (c.x + c.y) * 0.5f),
                 new Vector3(DoorWidth + over, slab, c.y - c.x + over));
    }

    static void Slab(Transform parent, Vector3 pos, Vector3 scale)
    {
        var g = Box(parent, "CeilSlab", pos, scale, s_Ceiling);
        MarkStatic(g);
    }

    /// A wall running along X. If it has a doorway it becomes two segments with a
    /// DoorWidth gap in the middle - that is the only way to get an opening out of
    /// box primitives without boolean geometry.
    static void WallX(Transform parent, string name, float cx, float cz, float len, bool door)
    {
        if (!door)
        {
            Wall(parent, name, new Vector3(cx, WallHeight * 0.5f, cz),
                 new Vector3(len, WallHeight, WallThick), 0f);
            return;
        }
        float seg = (len - DoorWidth) * 0.5f;
        float off = DoorWidth * 0.5f + seg * 0.5f;
        Wall(parent, name + "_a", new Vector3(cx - off, WallHeight * 0.5f, cz),
             new Vector3(seg, WallHeight, WallThick), 0f);
        Wall(parent, name + "_b", new Vector3(cx + off, WallHeight * 0.5f, cz),
             new Vector3(seg, WallHeight, WallThick), 0f);
    }

    static void WallZ(Transform parent, string name, float cx, float cz, float len, bool door)
    {
        if (!door)
        {
            Wall(parent, name, new Vector3(cx, WallHeight * 0.5f, cz),
                 new Vector3(len, WallHeight, WallThick), 90f);
            return;
        }
        float seg = (len - DoorWidth) * 0.5f;
        float off = DoorWidth * 0.5f + seg * 0.5f;
        Wall(parent, name + "_a", new Vector3(cx, WallHeight * 0.5f, cz - off),
             new Vector3(seg, WallHeight, WallThick), 90f);
        Wall(parent, name + "_b", new Vector3(cx, WallHeight * 0.5f, cz + off),
             new Vector3(seg, WallHeight, WallThick), 90f);
    }

    /// Every wall gets a material whose tiling matches its own size, so bricks are
    /// the same physical size on a 2.5 m stub as on an 8 m run.
    static void Wall(Transform parent, string name, Vector3 pos, Vector3 scale, float yaw)
    {
        var g = Box(parent, name, pos, scale, null);
        g.transform.eulerAngles = new Vector3(0f, yaw, 0f);
        g.GetComponent<Renderer>().sharedMaterial =
            TiledMaterial(false, scale.x / TextureSize, scale.y / TextureSize);
        MarkStatic(g);
    }

    // =========================================================================
    //  DOORS
    // =========================================================================

    static void BuildDoors()
    {
        Type doorType = FindType("DoorInteract, Assembly-CSharp");
        if (doorType == null)
            Log("DoorInteract not found in Assets/Scripts/ - doors are built but will not open.");

        var parent = new GameObject("Doors").transform;
        parent.SetParent(s_Root, false);

        string[] labels = { "entry door", "exit door" };
        const float leafH = 2.55f;

        for (int i = 0; i < DoorFrames.Length; i++)
        {
            var group = new GameObject(labels[i]).transform;
            group.SetParent(parent, false);
            group.position = DoorFrames[i];

            // stone lintel filling the wall above the doors
            var lintel = Box(group, "Lintel",
                DoorFrames[i] + new Vector3(0f, leafH + (WallHeight - leafH) * 0.5f, 0f),
                new Vector3(DoorWidth + 0.06f, WallHeight - leafH, WallThick + 0.02f), s_Lintel);
            MarkStatic(lintel);

            for (int side = -1; side <= 1; side += 2)
            {
                float half = DoorWidth * 0.5f;

                var hinge = new GameObject(side < 0 ? "HingeL" : "HingeR");
                hinge.transform.SetParent(group, false);
                hinge.transform.localPosition = new Vector3(side * half, 0f, 0f);

                var leaf = Box(hinge.transform, "Leaf",
                    Vector3.zero, new Vector3(half - 0.02f, leafH, 0.08f), s_Wood);
                leaf.transform.localPosition = new Vector3(-side * half * 0.5f, leafH * 0.5f, 0f);

                var handle = Box(hinge.transform, "Handle",
                    Vector3.zero, new Vector3(0.05f, 0.22f, 0.05f), s_Brass);
                handle.transform.localPosition = new Vector3(-side * 0.22f, 1.05f, 0.07f);
                UnityEngine.Object.DestroyImmediate(handle.GetComponent<Collider>());

                if (doorType == null) continue;
                var comp = hinge.AddComponent(doorType);
                var so = new SerializedObject(comp);
                so.FindProperty("doorName").stringValue = labels[i];
                so.FindProperty("openAngle").floatValue = side < 0 ? -95f : 95f;
                so.FindProperty("speed").floatValue = 260f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    // =========================================================================
    //  LIGHTING
    // =========================================================================

    static void BuildLighting()
    {
        var parent = new GameObject("Lighting").transform;
        parent.SetParent(s_Root, false);

        // Four per room so there are no dark corners, one per corridor.
        foreach (var c in Rooms)
        {
            Point(parent, new Vector3(c.x - 2f, LightHeight, c.y - 2f), 10f, LightIntensity);
            Point(parent, new Vector3(c.x + 2f, LightHeight, c.y - 2f), 10f, LightIntensity);
            Point(parent, new Vector3(c.x - 2f, LightHeight, c.y + 2f), 10f, LightIntensity);
            Point(parent, new Vector3(c.x + 2f, LightHeight, c.y + 2f), 10f, LightIntensity);
        }
        foreach (var c in CorridorsX)
            Point(parent, new Vector3((c.x + c.y) * 0.5f, LightHeight, c.z), 9f, CorridorLight);
        foreach (var c in CorridorsZ)
            Point(parent, new Vector3(c.z, LightHeight, (c.x + c.y) * 0.5f), 9f, CorridorLight);

        // Ambient is what stops surfaces facing away from every lamp going pure black.
        RenderSettings.ambientMode      = AmbientMode.Flat;
        RenderSettings.ambientLight     = new Color(0.98f, 0.96f, 0.92f) * AmbientLevel;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog              = false;   // fill-rate tax on mobile

        if (QualitySettings.pixelLightCount < 8)
            QualitySettings.pixelLightCount = 8;
    }

    static void Point(Transform parent, Vector3 pos, float range, float intensity)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;

        var l = go.AddComponent<Light>();
        l.type      = LightType.Point;
        l.range     = range;
        l.intensity = intensity;
        l.color     = new Color(1.00f, 0.97f, 0.92f);
        l.shadows   = LightShadows.None;   // 26 shadowed lights is not a Quest budget
        if (BakeLights) l.lightmapBakeType = LightmapBakeType.Baked;
    }

    // =========================================================================
    //  EXHIBITS
    // =========================================================================

    static void BuildExhibits()
    {
        Type inspectable = FindType("Inspectable, Assembly-CSharp");
        if (inspectable == null)
            Log("Inspectable not found in Assets/Scripts/ - exhibits are built but not interactive.");

        var parent = new GameObject("Exhibits").transform;
        parent.SetParent(s_Root, false);

        for (int i = 0; i < Rooms.Length; i++)
        {
            var c = Rooms[i];
            var plinth = Box(parent, "Plinth" + (i + 1),
                new Vector3(c.x, PlinthHeight * 0.5f, c.y),
                new Vector3(PlinthSize, PlinthHeight, PlinthSize), s_Plinth);
            MarkStatic(plinth);
        }

        int si = Mathf.Clamp(SwordRoom - 1, 0, Rooms.Length - 1);
        var sword = BuildSword(parent);
        sword.transform.position = new Vector3(Rooms[si].x, PlinthHeight + 0.14f, Rooms[si].y);
        sword.transform.rotation = Quaternion.LookRotation(Approach[si], Vector3.up);
        Inspect(inspectable, sword, "Viking Sword",
            "Iron blade with a lobed pommel and decorated crossguard, c. 9th-10th century. " +
            "Blades of this type were pattern-welded and often carry an inlaid maker's mark.",
            0.85f, new Vector3(0f, 0f, 75f));
        Placard(parent, "VIKING SWORD", Rooms[si], Approach[si]);

        int ji = Mathf.Clamp(JarRoom - 1, 0, Rooms.Length - 1);
        var jar = BuildJar(parent, inspectable);
        jar.transform.position = new Vector3(Rooms[ji].x, PlinthHeight, Rooms[ji].y);
        jar.transform.rotation = Quaternion.LookRotation(Approach[ji], Vector3.up);
        Placard(parent, "CANOPIC JAR", Rooms[ji], Approach[ji]);
    }

    static GameObject BuildSword(Transform parent)
    {
        var sword = new GameObject("VikingSword");
        sword.transform.SetParent(parent, false);

        Part(sword, "Blade",      new Vector3(0f,  0.420f, 0f), new Vector3(0.055f, 0.720f, 0.011f), s_Steel, PrimitiveType.Cube);
        Part(sword, "Fuller",     new Vector3(0f,  0.440f, 0f), new Vector3(0.016f, 0.620f, 0.014f), s_Hilt,  PrimitiveType.Cube);
        Part(sword, "Tip",        new Vector3(0f,  0.800f, 0f), new Vector3(0.030f, 0.090f, 0.011f), s_Steel, PrimitiveType.Cube);
        Part(sword, "Guard",      new Vector3(0f,  0.050f, 0f), new Vector3(0.200f, 0.028f, 0.032f), s_Hilt,  PrimitiveType.Cube);
        Part(sword, "Grip",       new Vector3(0f, -0.020f, 0f), new Vector3(0.019f, 0.045f, 0.019f), s_Steel, PrimitiveType.Cylinder);
        Part(sword, "UpperGuard", new Vector3(0f, -0.075f, 0f), new Vector3(0.105f, 0.022f, 0.030f), s_Hilt,  PrimitiveType.Cube);
        Part(sword, "Pommel",     new Vector3(0f, -0.108f, 0f), new Vector3(0.088f, 0.046f, 0.034f), s_Hilt,  PrimitiveType.Cube);

        var col = sword.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.32f, 0f);
        col.size   = new Vector3(0.22f, 1.02f, 0.08f);
        return sword;
    }

    static GameObject BuildJar(Transform parent, Type inspectable)
    {
        var jar = new GameObject("CanopicJar");
        jar.transform.SetParent(parent, false);

        Part(jar, "Foot",      new Vector3(0f, 0.020f, 0f), new Vector3(0.175f, 0.020f, 0.175f), s_Stone, PrimitiveType.Cylinder);
        Part(jar, "BodyLower", new Vector3(0f, 0.100f, 0f), new Vector3(0.205f, 0.070f, 0.205f), s_Stone, PrimitiveType.Cylinder);
        Part(jar, "BodyUpper", new Vector3(0f, 0.235f, 0f), new Vector3(0.180f, 0.070f, 0.180f), s_Stone, PrimitiveType.Cylinder);
        Part(jar, "Rim",       new Vector3(0f, 0.312f, 0f), new Vector3(0.160f, 0.012f, 0.160f), s_Stone, PrimitiveType.Cylinder);
        Part(jar, "TextBand",  new Vector3(0f, 0.150f, 0f), new Vector3(0.207f, 0.028f, 0.207f), s_Paint, PrimitiveType.Cylinder);

        var jc = jar.AddComponent<BoxCollider>();
        jc.center = new Vector3(0f, 0.165f, 0f);
        jc.size   = new Vector3(0.22f, 0.33f, 0.22f);
        Inspect(inspectable, jar, "Canopic Jar",
            "Limestone jar holding an organ removed during mummification, c. 1000 BCE. " +
            "The human-headed lid is Imsety, guardian of the liver.",
            0.60f, Vector3.zero);

        // The lid is its OWN inspectable child, so aiming at the head lifts just
        // the lid while aiming at the body picks up the whole jar.
        var lid = new GameObject("CanopicLid");
        lid.transform.SetParent(jar.transform, false);
        lid.transform.localPosition = new Vector3(0f, 0.324f, 0f);

        Part(lid, "Collar",    new Vector3( 0f,     0.012f,  0f    ), new Vector3(0.158f, 0.012f, 0.158f), s_Stone, PrimitiveType.Cylinder);
        Part(lid, "Headdress", new Vector3( 0f,     0.078f, -0.006f), new Vector3(0.148f, 0.165f, 0.152f), s_Stone, PrimitiveType.Sphere);
        Part(lid, "Face",      new Vector3( 0f,     0.068f,  0.030f), new Vector3(0.100f, 0.120f, 0.092f), s_Face,  PrimitiveType.Sphere);
        Part(lid, "LappetL",   new Vector3(-0.068f, 0.048f,  0.016f), new Vector3(0.022f, 0.092f, 0.078f), s_Paint, PrimitiveType.Cube);
        Part(lid, "LappetR",   new Vector3( 0.068f, 0.048f,  0.016f), new Vector3(0.022f, 0.092f, 0.078f), s_Paint, PrimitiveType.Cube);
        Part(lid, "BrowBand",  new Vector3( 0f,     0.118f,  0.026f), new Vector3(0.102f, 0.013f, 0.090f), s_Paint, PrimitiveType.Cube);
        Part(lid, "EyeL",      new Vector3(-0.024f, 0.082f,  0.070f), new Vector3(0.026f, 0.008f, 0.010f), s_Paint, PrimitiveType.Cube);
        Part(lid, "EyeR",      new Vector3( 0.024f, 0.082f,  0.070f), new Vector3(0.026f, 0.008f, 0.010f), s_Paint, PrimitiveType.Cube);
        Part(lid, "Uraeus",    new Vector3( 0f,     0.135f,  0.048f), new Vector3(0.020f, 0.026f, 0.020f), s_Gold,  PrimitiveType.Cube);

        var lc = lid.AddComponent<BoxCollider>();
        lc.center = new Vector3(0f, 0.076f, 0.008f);
        lc.size   = new Vector3(0.17f, 0.165f, 0.17f);
        Inspect(inspectable, lid, "Canopic Jar Lid",
            "Carved head of Imsety, one of the four Sons of Horus. Traces of the original " +
            "blue and black pigment survive on the nemes headdress.",
            0.40f, new Vector3(10f, 0f, 0f));

        return jar;
    }

    static void Inspect(Type t, GameObject go, string name, string desc, float dist, Vector3 rot)
    {
        if (t == null) return;
        var so = new SerializedObject(go.AddComponent(t));
        so.FindProperty("displayName").stringValue   = name;
        so.FindProperty("description").stringValue   = desc;
        so.FindProperty("holdDistance").floatValue   = dist;
        so.FindProperty("holdRotation").vector3Value = rot;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Placard(Transform parent, string text, Vector2 room, Vector3 approach)
    {
        Vector3 a = approach.normalized;
        const float ch = 0.0055f;
        float w = text.Length * ch * 5f + 0.05f;

        var pivot = new GameObject("Placard").transform;
        pivot.SetParent(parent, false);
        pivot.position    = new Vector3(room.x, PlinthHeight + 0.025f, room.y) + a * 0.24f;
        pivot.rotation    = Quaternion.LookRotation(FlipPlacardText ? -a : a, Vector3.up);
        pivot.eulerAngles = new Vector3(45f, pivot.eulerAngles.y, 0f);

        var plate = Box(pivot, "Plate", Vector3.zero, new Vector3(w, 0.055f, 0.010f), s_Placard);
        plate.transform.localPosition = Vector3.zero;
        UnityEngine.Object.DestroyImmediate(plate.GetComponent<Collider>());

        var g = new GameObject("Text");
        g.transform.SetParent(pivot, false);
        g.transform.localPosition = new Vector3(0f, 0f, -0.008f);

        var tm = g.AddComponent<TextMesh>();
        tm.text = text;
        tm.characterSize = ch;
        tm.fontSize  = 90;
        tm.anchor    = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color     = new Color(0.94f, 0.92f, 0.88f);
        try
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                 ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f != null) { tm.font = f; g.GetComponent<Renderer>().sharedMaterial = f.material; }
        }
        catch { /* builtin font name varies between Unity versions */ }
    }

    // =========================================================================
    //  VR RIG
    // =========================================================================

    static void BuildRig()
    {
        // If the XRI Starter Assets rig is already in the scene it is far better
        // than anything built here - controllers, ray interactors and teleport are
        // already wired - so just move it to the entry and leave it alone.
        var starter = GameObject.Find("XR Origin (XR Rig)");
        if (starter != null)
        {
            starter.transform.SetPositionAndRotation(Spawn, Quaternion.identity);
            Log("Found the XRI Starter Assets rig and moved it to the entry porch.");
            return;
        }

        Type xrOrigin = FindType("Unity.XR.CoreUtils.XROrigin");
        var rig = new GameObject("XR Origin (Museum)");
        rig.transform.SetParent(s_Root, false);
        rig.transform.SetPositionAndRotation(Spawn, Quaternion.identity);

        if (xrOrigin == null)
        {
            Log("XROrigin type not found - built a plain camera at 1.6 m so the scene renders. " +
                "Import XR Interaction Toolkit > Samples > Starter Assets, drag in the " +
                "'XR Origin (XR Rig)' prefab, and run this again for a real rig.");
            var flatCam = new GameObject("PlayerCamera");
            flatCam.transform.SetParent(rig.transform, false);
            flatCam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            ConfigureCamera(flatCam);
            return;
        }

        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(rig.transform, false);

        var camGo = new GameObject("PlayerCamera");
        camGo.transform.SetParent(offset.transform, false);
        Camera cam = ConfigureCamera(camGo);

        Component origin = rig.AddComponent(xrOrigin);
        SetMember(origin, new[] { "Camera" }, cam);
        SetMember(origin, new[] { "CameraFloorOffsetObject" }, offset);
        try
        {
            PropertyInfo m = xrOrigin.GetProperty("RequestedTrackingOriginMode",
                BindingFlags.Public | BindingFlags.Instance);
            if (m != null && m.CanWrite) m.SetValue(origin, Enum.Parse(m.PropertyType, "Floor"));
        }
        catch (Exception e) { Debug.LogWarning("[BrickMuseum] tracking origin: " + e.Message); }
        EditorUtility.SetDirty(origin);

        Type im = FindType("UnityEngine.XR.Interaction.Toolkit.XRInteractionManager");
        if (im != null && UnityEngine.Object.FindObjectsByType(im, FindObjectsSortMode.None).Length == 0)
            new GameObject("XR Interaction Manager").AddComponent(im);

        WireHeadTracking(camGo);
        Log("XR rig built at the entry porch. HEAD-TRACKED ONLY - use the Starter Assets " +
            "prefab for controllers and teleport.");
    }

    static Camera ConfigureCamera(GameObject go)
    {
        go.tag = "MainCamera";
        var cam = go.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane   = 0.05f;   // 0.3 clips your own hands in a headset
        cam.farClipPlane    = 120f;
        if (UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 0)
            go.AddComponent<AudioListener>();
        foreach (var other in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (other != cam) other.gameObject.SetActive(false);
        return cam;
    }

    /// Without this the headset does not move the camera on device: everything
    /// builds and installs, and then the world simply does not turn with your head.
    static void WireHeadTracking(GameObject camGo)
    {
        Type tpd = FindType("UnityEngine.InputSystem.XR.TrackedPoseDriver");
        if (tpd == null)
        {
            Debug.LogWarning("[BrickMuseum] TrackedPoseDriver not found - the camera will NOT " +
                             "follow the headset. Add one and bind centerEyePosition/Rotation.");
            return;
        }
        try
        {
            Component c = camGo.AddComponent(tpd);
            Type at = FindType("UnityEngine.InputSystem.InputAction");
            Type ae = FindType("UnityEngine.InputSystem.InputActionType");
            Type pt = FindType("UnityEngine.InputSystem.InputActionProperty");
            if (at == null || ae == null || pt == null) throw new Exception("Input System incomplete.");

            object val = Enum.Parse(ae, "Value");
            object pos = Activator.CreateInstance(at, new object[]
                { "HMD Position", val, "<XRHMD>/centerEyePosition", null, null, "Vector3" });
            object rot = Activator.CreateInstance(at, new object[]
                { "HMD Rotation", val, "<XRHMD>/centerEyeRotation", null, null, "Quaternion" });

            SetMember(c, new[] { "positionInput", "positionAction" },
                      Activator.CreateInstance(pt, new object[] { pos }));
            SetMember(c, new[] { "rotationInput", "rotationAction" },
                      Activator.CreateInstance(pt, new object[] { rot }));
            EditorUtility.SetDirty(c);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[BrickMuseum] head tracking auto-wire failed (" + e.Message +
                             "). Bind it in the Inspector.");
        }
    }

    // =========================================================================
    //  MATERIALS
    // =========================================================================

    static void PickShader()
    {
        s_Urp = GraphicsSettings.currentRenderPipeline != null;
        s_Shader = Shader.Find(s_Urp ? "Universal Render Pipeline/Lit" : "Standard");
        if (s_Shader == null) s_Shader = Shader.Find(s_Urp ? "Standard" : "Universal Render Pipeline/Lit");
        if (s_Shader == null) throw new Exception("No usable lit shader found.");
        s_Urp    = s_Shader.name.StartsWith("Universal");
        s_Albedo = s_Urp ? "_BaseMap"    : "_MainTex";
        s_Smooth = s_Urp ? "_Smoothness" : "_Glossiness";
        Log("Shader: " + s_Shader.name);
    }

    static void LoadTextures()
    {
        s_BrickTex = LoadTex("brick_albedo.png", false);
        s_BrickNrm = LoadTex("brick_normal.png", true);
        s_FloorTex = LoadTex("floor_albedo.png", false);
        s_FloorNrm = LoadTex("floor_normal.png", true);
        if (s_BrickTex == null || s_FloorTex == null)
            Log("Textures not found in " + TexFolder + " - falling back to flat colours. " +
                "Drop brick_albedo.png / floor_albedo.png in there and re-run.");
    }

    static Texture2D LoadTex(string file, bool normal)
    {
        string path = TexFolder + "/" + file;
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (t == null) return null;
        if (normal)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti != null && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
        }
        return t;
    }

    /// One material per unique tiling, cached and saved as a real asset so you can
    /// tweak it by hand afterwards.
    static Material TiledMaterial(bool floor, float tx, float ty)
    {
        string key = (floor ? "Floor" : "Wall") + "_" + tx.ToString("0.##") + "x" + ty.ToString("0.##");
        Material m;
        if (s_TileCache.TryGetValue(key, out m)) return m;

        string path = MatFolder + "/M_" + key.Replace('.', '_') + ".mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(s_Shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = s_Shader;

        Texture2D albedo = floor ? s_FloorTex : s_BrickTex;
        Texture2D nrm    = floor ? s_FloorNrm : s_BrickNrm;
        var tiling = new Vector2(tx, ty);

        if (albedo != null)
        {
            m.SetTexture(s_Albedo, albedo);
            m.SetTextureScale(s_Albedo, tiling);
        }
        m.SetColor(s_Urp ? "_BaseColor" : "_Color",
                   albedo != null ? Color.white
                                  : (floor ? new Color(0.78f, 0.75f, 0.68f)
                                           : new Color(0.55f, 0.34f, 0.28f)));
        if (nrm != null)
        {
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_BumpMap", nrm);
            m.SetTextureScale("_BumpMap", tiling);
        }
        m.SetFloat("_Metallic", 0f);
        m.SetFloat(s_Smooth, floor ? 0.55f : 0.10f);
        EditorUtility.SetDirty(m);

        s_TileCache[key] = m;
        return m;
    }

    static void CreateSharedMaterials()
    {
        s_TileCache.Clear();
        // Metallic stays LOW everywhere: a fully metallic material has almost no
        // diffuse colour and needs reflections, and a sealed building with no
        // reflection probe renders those as solid black.
        s_Ceiling = Flat("M_Ceiling",  new Color(0.82f, 0.80f, 0.77f), 0f,    0.05f);
        s_Plinth  = Flat("M_Plinth",   new Color(0.34f, 0.34f, 0.36f), 0f,    0.20f);
        s_Placard = Flat("M_Placard",  new Color(0.16f, 0.16f, 0.17f), 0f,    0.25f);
        s_Steel   = Flat("M_Steel",    new Color(0.74f, 0.75f, 0.78f), 0.25f, 0.45f);
        s_Hilt    = Flat("M_Hilt",     new Color(0.55f, 0.42f, 0.22f), 0.25f, 0.35f);
        s_Stone   = Flat("M_Limestone",new Color(0.86f, 0.83f, 0.74f), 0f,    0.15f);
        s_Face    = Flat("M_JarFace",  new Color(0.89f, 0.81f, 0.67f), 0f,    0.18f);
        s_Paint   = Flat("M_JarPaint", new Color(0.18f, 0.20f, 0.30f), 0f,    0.30f);
        s_Gold    = Flat("M_JarGold",  new Color(0.80f, 0.64f, 0.26f), 0.25f, 0.45f);
        s_Wood    = Flat("M_DoorWood", new Color(0.24f, 0.14f, 0.09f), 0f,    0.30f);
        s_Brass   = Flat("M_DoorBrass",new Color(0.72f, 0.57f, 0.24f), 0.30f, 0.55f);
        s_Lintel  = Flat("M_Lintel",   new Color(0.30f, 0.29f, 0.27f), 0f,    0.15f);
    }

    static Material Flat(string name, Color col, float metallic, float smooth)
    {
        string path = MatFolder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(s_Shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = s_Shader;
        m.SetColor(s_Urp ? "_BaseColor" : "_Color", col);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat(s_Smooth, smooth);
        EditorUtility.SetDirty(m);
        return m;
    }

    // =========================================================================
    //  PRIMITIVES + PLUMBING
    // =========================================================================

    static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.position   = pos;
        g.transform.localScale = scale;
        if (m != null) g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    static void Part(GameObject parent, string name, Vector3 pos, Vector3 scale,
                     Material m, PrimitiveType type)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent.transform, false);
        g.transform.localPosition = pos;
        g.transform.localScale    = scale;
        g.transform.localRotation = Quaternion.identity;
        g.GetComponent<Renderer>().sharedMaterial = m;
        UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>()); // root owns the collider
    }

    /// Static batching plus ContributeGI, so the baked lights actually land and
    /// the batcher can merge these boxes. Never call this on doors or exhibits.
    static void MarkStatic(GameObject go)
    {
        GameObjectUtility.SetStaticEditorFlags(go,
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.ReflectionProbeStatic);
    }

    static void EnsureFolder(string path)
    {
        path = path.Replace('\\', '/').TrimEnd('/');
        if (path == "Assets" || AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void SetAsFirstBuildScene()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static void Report(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        var filters   = root.GetComponentsInChildren<MeshFilter>(true);
        var lights    = root.GetComponentsInChildren<Light>(true);

        long tris = 0;
        foreach (var mf in filters)
        {
            var mesh = mf.sharedMesh;
            if (mesh == null) continue;
            for (int i = 0; i < mesh.subMeshCount; i++) tris += mesh.GetIndexCount(i) / 3;
        }
        var mats = new HashSet<Material>();
        foreach (var r in renderers) foreach (var m in r.sharedMaterials) if (m != null) mats.Add(m);

        Log("--------- BUDGET ---------");
        Log("Renderers  : " + renderers.Length + "   unique materials: " + mats.Count);
        Log("Lights     : " + lights.Length + (BakeLights ? "  (baked)" : "  (REALTIME - too many for Quest)"));
        Log("Triangles  : " + tris.ToString("N0") + "   against a ~1.3 M mobile budget");
        Log("--------------------------");
    }

    // ---- reflection helpers -------------------------------------------------

    static Type FindType(string name)
    {
        Type t = Type.GetType(name);
        if (t != null) return t;

        string bare = name.Contains(",") ? name.Substring(0, name.IndexOf(',')).Trim() : name;
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            try { t = asm.GetType(bare); if (t != null) return t; } catch { }
        }
        string shortName = bare.Substring(bare.LastIndexOf('.') + 1);
        foreach (var asm in assemblies)
        {
            try { foreach (var c in asm.GetTypes()) if (c.Name == shortName) return c; } catch { }
        }
        return null;
    }

    static bool SetMember(object target, string[] names, object value)
    {
        if (target == null || value == null) return false;
        Type t = target.GetType();
        foreach (string n in names)
        {
            var p = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite && p.PropertyType.IsInstanceOfType(value))
            { p.SetValue(target, value); return true; }
            var f = t.GetField(n, BindingFlags.Public | BindingFlags.Instance);
            if (f != null && f.FieldType.IsInstanceOfType(value))
            { f.SetValue(target, value); return true; }
        }
        return false;
    }

    static void Log(string m) { Debug.Log("[BrickMuseum] " + m); }
}
