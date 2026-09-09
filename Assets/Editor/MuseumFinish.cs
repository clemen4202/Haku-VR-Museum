using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

// Put this file in:  Assets/Editor/MuseumFinish.cs
// Requires Assets/Scripts/SimpleFPS.cs to exist first.
//
// Two menu items:
//   Tools > Build Ceiling               - roofs every room and corridor
//   Tools > Add First Person Player     - drops a walkable player at the entry

public static class MuseumFinish
{
    const float WALL_TOP  = 4f;     // walls are 4 tall
    const float SLAB      = 0.2f;   // ceiling thickness
    const float DOOR      = 3f;     // corridor width, matches MuseumBuilder
    const float OVERLAP   = 0.3f;   // fudge so slabs meet with no seams

    // ---- must match MuseumBuilder ----
    static readonly Vector2[] Rooms =
    {
        new Vector2(-13f, -6f), new Vector2(0f, -6f), new Vector2(13f, -6f),
        new Vector2( 13f,  6f), new Vector2(0f,  6f),
    };

    // corridors running along X: x0, x1, centreZ
    static readonly Vector3[] CorridorsX =
    {
        new Vector3(-9f, -4f, -6f),
        new Vector3( 4f,  9f, -6f),
        new Vector3( 4f,  9f,  6f),
    };

    // corridors running along Z: z0, z1, centreX
    static readonly Vector3[] CorridorsZ =
    {
        new Vector3( -2f,   2f,  13f),   // 3 -> 4
        new Vector3(-13f, -10f, -13f),   // entry porch
        new Vector3( 10f,  13f,   0f),   // exit porch
    };

    // =====================================================================
    //  CEILING
    // =====================================================================

    [MenuItem("Tools/Build Ceiling")]
    static void BuildCeiling()
    {
        var old = GameObject.Find("MuseumCeiling");
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject("MuseumCeiling").transform;
        var mat  = CeilingMaterial();

        float y = WALL_TOP + SLAB * 0.5f;

        foreach (var r in Rooms)
            Slab(root, mat, "CeilRoom", new Vector3(r.x, y, r.y),
                 new Vector3(8f + OVERLAP, SLAB, 8f + OVERLAP));

        foreach (var c in CorridorsX)
            Slab(root, mat, "CeilCorridor",
                 new Vector3((c.x + c.y) * 0.5f, y, c.z),
                 new Vector3(c.y - c.x + OVERLAP, SLAB, DOOR + OVERLAP));

        foreach (var c in CorridorsZ)
            Slab(root, mat, "CeilCorridor",
                 new Vector3(c.z, y, (c.x + c.y) * 0.5f),
                 new Vector3(DOOR + OVERLAP, SLAB, c.y - c.x + OVERLAP));

        Debug.Log("Ceiling built: " + root.childCount + " slabs. "
                + "Untick 'MuseumCeiling' in the Hierarchy to see into the rooms while editing.");
    }

    static void Slab(Transform root, Material mat, string name, Vector3 pos, Vector3 scale)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(root);
        g.transform.position = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static Material CeilingMaterial()
    {
        const string path = "Assets/Materials/M_Ceiling.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

        bool urp = GraphicsSettings.currentRenderPipeline != null;
        Shader sh = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
        if (sh == null) sh = Shader.Find(urp ? "Standard" : "Universal Render Pipeline/Lit");
        urp = sh != null && sh.name.StartsWith("Universal");

        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = sh;
        mat.SetColor(urp ? "_BaseColor" : "_Color", new Color(0.82f, 0.80f, 0.77f));
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat(urp ? "_Smoothness" : "_Glossiness", 0.05f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    // =====================================================================
    //  PLAYER
    // =====================================================================

    [MenuItem("Tools/Add First Person Player")]
    static void AddPlayer()
    {
        var scriptType = System.Type.GetType("SimpleFPS, Assembly-CSharp");
        if (scriptType == null)
        {
            Debug.LogError("SimpleFPS not found. Put SimpleFPS.cs in Assets/Scripts/ "
                         + "(NOT Assets/Editor/) and wait for Unity to finish compiling, then run this again.");
            return;
        }

        var old = GameObject.Find("Player");
        if (old != null) Object.DestroyImmediate(old);

        // Spawn just outside the entry porch, facing into the museum (+Z).
        var player = new GameObject("Player");
        player.transform.position    = new Vector3(-13f, 0.2f, -12.5f);
        player.transform.eulerAngles = Vector3.zero;

        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);

        var camGO = new GameObject("PlayerCamera");
        camGO.transform.SetParent(player.transform);
        camGO.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        camGO.tag = "MainCamera";

        var cam = camGO.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane  = 200f;
        camGO.AddComponent<AudioListener>();

        // Switch off the old scene camera so there is only one active view.
        foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (other != cam) other.gameObject.SetActive(false);

        player.AddComponent(scriptType);

        Selection.activeGameObject = player;
        Debug.Log("Player added at the entry. Press Play, then WASD to walk, mouse to look, "
                + "Shift to run, Esc to release the cursor.");
    }
}
