using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

// Put this file in:  Assets/Editor/MuseumLights.cs   (replace the old one)
// Then use the menu:  Tools > Build Museum Lighting
//
// Even, bright interior lighting. No spotlights. Each room gets a 2x2 grid of
// point lights so there are no dark corners, and every corridor gets one.
// Running it again deletes the old lights and rebuilds.

public static class MuseumLights
{
    // ================= TUNE THESE THREE =================
    const float BRIGHTNESS = 1.0f;    // scales every light. 1.4 = noticeably brighter
    const float AMBIENT    = 0.45f;   // 0 = black shadows, 0.7 = very flat and even
    const bool  SHADOWS    = false;   // true = nicer looking, darker, slower
    // ====================================================

    const float CEIL = 3.4f;   // how high the lights hang (walls are 4 tall)

    // Must match the room and corridor centres in MuseumBuilder.
    static readonly Vector2[] Rooms =
    {
        new Vector2(-13f, -6f),   // Room 1
        new Vector2(  0f, -6f),   // Room 2
        new Vector2( 13f, -6f),   // Room 3
        new Vector2( 13f,  6f),   // Room 4
        new Vector2(  0f,  6f),   // Room 5
    };

    static readonly Vector2[] Corridors =
    {
        new Vector2( -6.5f,  -6f),     // 1 -> 2
        new Vector2(  6.5f,  -6f),     // 2 -> 3
        new Vector2( 13f,     0f),     // 3 -> 4
        new Vector2(  6.5f,   6f),     // 4 -> 5
        new Vector2(-13f,   -11.5f),   // entry porch
        new Vector2(  0f,    11.5f),   // exit porch
    };

    static Transform root;

    [MenuItem("Tools/Build Museum Lighting")]
    static void Build()
    {
        var old = GameObject.Find("MuseumLights");
        if (old != null) Object.DestroyImmediate(old);

        root = new GameObject("MuseumLights").transform;

        // Four lights per room, spread out so the whole 8x8 floor is covered evenly.
        foreach (var r in Rooms)
        {
            Point("RoomLight", new Vector3(r.x - 2f, CEIL, r.y - 2f), 10f, 1.5f);
            Point("RoomLight", new Vector3(r.x + 2f, CEIL, r.y - 2f), 10f, 1.5f);
            Point("RoomLight", new Vector3(r.x - 2f, CEIL, r.y + 2f), 10f, 1.5f);
            Point("RoomLight", new Vector3(r.x + 2f, CEIL, r.y + 2f), 10f, 1.5f);
        }

        foreach (var c in Corridors)
            Point("CorridorLight", new Vector3(c.x, CEIL, c.y), 9f, 2.2f);

        // Ambient light fills in every surface evenly, including ones facing away
        // from all the lamps. This is what stops walls going pure black.
        RenderSettings.ambientMode      = AmbientMode.Flat;
        RenderSettings.ambientLight     = new Color(0.98f, 0.96f, 0.92f) * AMBIENT;
        RenderSettings.ambientIntensity = 1f;

        // Interior scene, so the sun stays almost off.
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) l.intensity = 0.05f;

        // Built-in only lights each object with 4 lights at full quality by default.
        if (QualitySettings.pixelLightCount < 8)
        {
            QualitySettings.pixelLightCount = 8;
            Debug.Log("Raised Quality > Pixel Light Count to 8.");
        }

        Debug.Log("Museum lighting built: " + root.childCount + " point lights, ambient " + AMBIENT);
    }

    static void Point(string name, Vector3 pos, float range, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root);
        go.transform.position = pos;

        var l = go.AddComponent<Light>();
        l.type      = LightType.Point;
        l.range     = range;
        l.intensity = intensity * BRIGHTNESS;
        l.color     = new Color(1.00f, 0.97f, 0.92f);   // near-white, very slightly warm
        l.shadows   = SHADOWS ? LightShadows.Soft : LightShadows.None;
    }
}
