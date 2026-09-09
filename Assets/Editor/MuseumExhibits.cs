using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

// Put this file in:  Assets/Editor/MuseumExhibits.cs   (replaces the old one)
// Requires Assets/Scripts/Inspectable.cs and InspectController.cs.
//
//   Tools > Add Exhibits
//
// Room 1 -> Viking sword
// Room 2 -> Egyptian canopic jar, whose LID lifts off as its own inspectable

public static class MuseumExhibits
{
    // ============ WHICH ROOM EACH EXHIBIT GOES IN ============
    // Room 1 is the ENTRY room (you walk straight into it from the entry door).
    // Room 5 is the EXIT room (its far wall has the exit door).
    // Change these numbers and re-run Tools > Add Exhibits to move things.
    const int SWORD_ROOM = 1;
    const int JAR_ROOM   = 2;
    // =========================================================

    static readonly Vector2[] Rooms =
    {
        new Vector2(-13f, -6f),   // Room 1  - ENTRY room
        new Vector2(  0f, -6f),   // Room 2
        new Vector2( 13f, -6f),   // Room 3
        new Vector2( 13f,  6f),   // Room 4
        new Vector2(  0f,  6f),   // Room 5  - EXIT room
    };

    // Which side of each room you walk in from, so placards face the visitor.
    static readonly Vector3[] Approach =
    {
        new Vector3( 0f, 0f, -1f),   // Room 1: entered from the south
        new Vector3(-1f, 0f,  0f),   // Room 2: entered from the west
        new Vector3(-1f, 0f,  0f),   // Room 3: entered from the west
        new Vector3( 0f, 0f, -1f),   // Room 4: entered from the south
        new Vector3( 1f, 0f,  0f),   // Room 5: entered from the east
    };

    const float PLINTH_H = 1.0f;

    // If placard text comes out mirrored on your Unity version, flip this.
    const bool FLIP_PLACARD = true;

    static bool urp;
    static System.Type inspectableType;

    [MenuItem("Tools/Add Exhibits")]
    static void Build()
    {
        inspectableType = System.Type.GetType("Inspectable, Assembly-CSharp");
        var controllerType = System.Type.GetType("InspectController, Assembly-CSharp");
        if (inspectableType == null || controllerType == null)
        {
            Debug.LogError("Inspectable.cs and InspectController.cs must be in Assets/Scripts/ "
                         + "and compiled before running this.");
            return;
        }

        urp = GraphicsSettings.currentRenderPipeline != null;

        var old = GameObject.Find("MuseumExhibits");
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject("MuseumExhibits").transform;

        // NOTE: metallic values are deliberately LOW. A fully metallic material has
        // almost no diffuse colour and relies on reflections, and with no reflection
        // probe inside a sealed building that renders as solid black.
        var plinthMat = Mat("M_Plinth",    new Color(0.34f, 0.34f, 0.36f), 0f,    0.20f);
        var steelMat  = Mat("M_Steel",     new Color(0.74f, 0.75f, 0.78f), 0.25f, 0.45f);
        var hiltMat   = Mat("M_Hilt",      new Color(0.55f, 0.42f, 0.22f), 0.25f, 0.35f);
        var stoneMat  = Mat("M_Limestone", new Color(0.86f, 0.83f, 0.74f), 0f,    0.15f);
        var faceMat   = Mat("M_JarFace",   new Color(0.89f, 0.81f, 0.67f), 0f,    0.18f);
        var paintMat  = Mat("M_JarPaint",  new Color(0.18f, 0.20f, 0.30f), 0f,    0.30f);
        var goldMat   = Mat("M_JarGold",   new Color(0.80f, 0.64f, 0.26f), 0.25f, 0.45f);
        var plateMat  = Mat("M_Placard",   new Color(0.16f, 0.16f, 0.17f), 0f,    0.25f);

        // plinths in every room
        for (int i = 0; i < Rooms.Length; i++)
        {
            var c = Rooms[i];
            Box(root, "Plinth" + (i + 1),
                new Vector3(c.x, PLINTH_H * 0.5f, c.y),
                new Vector3(0.75f, PLINTH_H, 0.75f), plinthMat);
        }

        // ---------------- Viking sword ----------------
        {
            int i = Mathf.Clamp(SWORD_ROOM - 1, 0, Rooms.Length - 1);
            var c = Rooms[i];
            var sword = BuildVikingSword(root, steelMat, hiltMat);
            sword.transform.position = new Vector3(c.x, PLINTH_H + 0.14f, c.y);
            // turn the exhibit to face the visitor, same direction as its placard
            sword.transform.rotation = Quaternion.LookRotation(Approach[i].normalized, Vector3.up);

            MakeInspectable(sword, "Viking Sword",
                "Iron blade with a lobed pommel and decorated crossguard, c. 9th-10th century. "
              + "Blades of this type were pattern-welded, and many carry an inlaid maker's mark along the fuller.",
                0.85f, new Vector3(0f, 0f, 75f));

            Placard(root, "VIKING SWORD", c, Approach[i], plateMat);
        }

        // ---------------- canopic jar ----------------
        {
            int i = Mathf.Clamp(JAR_ROOM - 1, 0, Rooms.Length - 1);
            var c = Rooms[i];
            var jar = BuildCanopicJar(root, stoneMat, faceMat, paintMat, goldMat);
            jar.transform.position = new Vector3(c.x, PLINTH_H, c.y);
            // the carved face looks straight at the visitor, same direction as its placard
            jar.transform.rotation = Quaternion.LookRotation(Approach[i].normalized, Vector3.up);

            Placard(root, "CANOPIC JAR", c, Approach[i], plateMat);
        }

        // attach the controller to the player camera
        var camGO = GameObject.Find("PlayerCamera");
        if (camGO == null)
            Debug.LogWarning("No PlayerCamera found - run Tools > Add First Person Player first, "
                           + "then run Tools > Add Exhibits again.");
        else if (camGO.GetComponent(controllerType) == null)
            camGO.AddComponent(controllerType);

        Debug.Log("Exhibits added. Room 1: sword. Room 2: canopic jar - look at the LID and press E to lift it off.");
    }

    // =====================================================================
    //  VIKING SWORD
    // =====================================================================

    static GameObject BuildVikingSword(Transform parent, Material steel, Material hilt)
    {
        var sword = new GameObject("VikingSword");
        sword.transform.SetParent(parent);

        Cube(sword, "Blade",      new Vector3(0f,  0.420f, 0f), new Vector3(0.055f, 0.720f, 0.011f), steel);
        Cube(sword, "Fuller",     new Vector3(0f,  0.440f, 0f), new Vector3(0.016f, 0.620f, 0.014f), hilt);
        Cube(sword, "Tip",        new Vector3(0f,  0.800f, 0f), new Vector3(0.030f, 0.090f, 0.011f), steel);
        Cube(sword, "Guard",      new Vector3(0f,  0.050f, 0f), new Vector3(0.200f, 0.028f, 0.032f), hilt);
        Cyl (sword, "Grip",       new Vector3(0f, -0.020f, 0f), new Vector3(0.019f, 0.045f, 0.019f), steel);
        Cube(sword, "UpperGuard", new Vector3(0f, -0.075f, 0f), new Vector3(0.105f, 0.022f, 0.030f), hilt);
        Cube(sword, "Pommel",     new Vector3(0f, -0.108f, 0f), new Vector3(0.088f, 0.046f, 0.034f), hilt);

        var col = sword.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.32f, 0f);
        col.size   = new Vector3(0.22f, 1.02f, 0.08f);
        return sword;
    }

    // =====================================================================
    //  EGYPTIAN CANOPIC JAR  (lid is a separate inspectable)
    // =====================================================================

    static GameObject BuildCanopicJar(Transform parent, Material stone, Material face,
                                      Material paint, Material gold)
    {
        var jar = new GameObject("CanopicJar");
        jar.transform.SetParent(parent);

        // --- vessel: stacked cylinders, gently tapering ---
        Cyl(jar, "Foot",       new Vector3(0f, 0.020f, 0f), new Vector3(0.175f, 0.020f, 0.175f), stone);
        Cyl(jar, "BodyLower",  new Vector3(0f, 0.100f, 0f), new Vector3(0.205f, 0.070f, 0.205f), stone);
        Cyl(jar, "BodyUpper",  new Vector3(0f, 0.235f, 0f), new Vector3(0.180f, 0.070f, 0.180f), stone);
        Cyl(jar, "Rim",        new Vector3(0f, 0.312f, 0f), new Vector3(0.160f, 0.012f, 0.160f), stone);
        // painted band of hieroglyphs around the belly
        Cyl(jar, "TextBand",   new Vector3(0f, 0.150f, 0f), new Vector3(0.207f, 0.028f, 0.207f), paint);

        var jarCol = jar.AddComponent<BoxCollider>();
        jarCol.center = new Vector3(0f, 0.165f, 0f);
        jarCol.size   = new Vector3(0.22f, 0.33f, 0.22f);

        MakeInspectable(jar, "Canopic Jar",
            "Limestone jar used to hold an organ removed during mummification, c. 1000 BCE. "
          + "The human-headed lid is Imsety, guardian of the liver. The painted band names the deceased.",
            0.60f, Vector3.zero);

        // --- lid: its own object, its own collider, its own Inspectable ---
        var lid = new GameObject("CanopicLid");
        lid.transform.SetParent(jar.transform);
        lid.transform.localPosition = new Vector3(0f, 0.324f, 0f);
        lid.transform.localRotation = Quaternion.identity;

        Cyl (lid, "Collar",    new Vector3( 0f,     0.012f,  0f    ), new Vector3(0.158f, 0.012f, 0.158f), stone);
        Sph (lid, "Headdress", new Vector3( 0f,     0.078f, -0.006f), new Vector3(0.148f, 0.165f, 0.152f), stone);
        Sph (lid, "Face",      new Vector3( 0f,     0.068f,  0.030f), new Vector3(0.100f, 0.120f, 0.092f), face);
        Cube(lid, "LappetL",   new Vector3(-0.068f, 0.048f,  0.016f), new Vector3(0.022f, 0.092f, 0.078f), paint);
        Cube(lid, "LappetR",   new Vector3( 0.068f, 0.048f,  0.016f), new Vector3(0.022f, 0.092f, 0.078f), paint);
        Cube(lid, "BrowBand",  new Vector3( 0f,     0.118f,  0.026f), new Vector3(0.102f, 0.013f, 0.090f), paint);
        Cube(lid, "EyeL",      new Vector3(-0.024f, 0.082f,  0.070f), new Vector3(0.026f, 0.008f, 0.010f), paint);
        Cube(lid, "EyeR",      new Vector3( 0.024f, 0.082f,  0.070f), new Vector3(0.026f, 0.008f, 0.010f), paint);
        Cube(lid, "Uraeus",    new Vector3( 0f,     0.135f,  0.048f), new Vector3(0.020f, 0.026f, 0.020f), gold);

        var lidCol = lid.AddComponent<BoxCollider>();
        lidCol.center = new Vector3(0f, 0.076f, 0.008f);
        lidCol.size   = new Vector3(0.17f, 0.165f, 0.17f);

        MakeInspectable(lid, "Canopic Jar Lid",
            "Carved head of Imsety, one of the four Sons of Horus. Traces of the original blue and "
          + "black pigment survive on the nemes headdress and the rearing cobra at the brow.",
            0.40f, new Vector3(10f, 0f, 0f));

        return jar;
    }

    // =====================================================================
    //  helpers
    // =====================================================================

    static void MakeInspectable(GameObject go, string name, string desc,
                                float holdDistance, Vector3 holdRotation)
    {
        var comp = go.AddComponent(inspectableType);
        var so = new SerializedObject(comp);
        so.FindProperty("displayName").stringValue   = name;
        so.FindProperty("description").stringValue   = desc;
        so.FindProperty("holdDistance").floatValue   = holdDistance;
        so.FindProperty("holdRotation").vector3Value = holdRotation;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Cube(GameObject p, string n, Vector3 pos, Vector3 s, Material m)
    { Fit(GameObject.CreatePrimitive(PrimitiveType.Cube),     p, n, pos, s, m); }

    static void Cyl(GameObject p, string n, Vector3 pos, Vector3 s, Material m)
    { Fit(GameObject.CreatePrimitive(PrimitiveType.Cylinder), p, n, pos, s, m); }

    static void Sph(GameObject p, string n, Vector3 pos, Vector3 s, Material m)
    { Fit(GameObject.CreatePrimitive(PrimitiveType.Sphere),   p, n, pos, s, m); }

    static void Fit(GameObject g, GameObject parent, string name, Vector3 pos, Vector3 scale, Material m)
    {
        g.name = name;
        g.transform.SetParent(parent.transform);
        g.transform.localPosition = pos;
        g.transform.localScale    = scale;
        g.transform.localRotation = Quaternion.identity;
        g.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(g.GetComponent<Collider>());   // the root object owns the collider
    }

    static GameObject Box(Transform root, string name, Vector3 pos, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(root);
        g.transform.position   = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    // roomCentre = the plinth position; approach = the direction the visitor comes from
    static void Placard(Transform root, string text, Vector2 roomCentre, Vector3 approach, Material plateMat)
    {
        Vector3 a = approach.normalized;

        // pivot sits on the front edge of the plinth top, tilted back like a real label
        const float CHAR = 0.0055f;                          // world size per character
        float plateW = text.Length * CHAR * 5f + 0.05f;      // plate grows to fit the text

        var pivot = new GameObject("Placard").transform;
        pivot.SetParent(root);
        pivot.position = new Vector3(roomCentre.x, PLINTH_H + 0.025f, roomCentre.y) + a * 0.24f;

        Vector3 look = FLIP_PLACARD ? -a : a;
        pivot.rotation    = Quaternion.LookRotation(look, Vector3.up);
        pivot.eulerAngles = new Vector3(45f, pivot.eulerAngles.y, 0f);

        // backing plate
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = "Plate";
        plate.transform.SetParent(pivot);
        plate.transform.localPosition = Vector3.zero;
        plate.transform.localRotation = Quaternion.identity;
        plate.transform.localScale    = new Vector3(plateW, 0.055f, 0.010f);
        plate.GetComponent<Renderer>().sharedMaterial = plateMat;
        Object.DestroyImmediate(plate.GetComponent<Collider>());

        // text, sitting just proud of the readable face of the plate
        var g = new GameObject("Text");
        g.transform.SetParent(pivot);
        g.transform.localPosition = new Vector3(0f, 0f, -0.008f);
        g.transform.localRotation = Quaternion.identity;

        var tm = g.AddComponent<TextMesh>();
        tm.text          = text;
        tm.characterSize = CHAR;
        tm.fontSize      = 90;
        tm.anchor        = TextAnchor.MiddleCenter;
        tm.alignment     = TextAlignment.Center;
        tm.color         = new Color(0.94f, 0.92f, 0.88f);

        try
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f != null) { tm.font = f; g.GetComponent<Renderer>().sharedMaterial = f.material; }
        }
        catch { /* font name differs between Unity versions; the default still renders */ }
    }

    static Material Mat(string name, Color col, float metallic, float smoothness)
    {
        string path = "Assets/Materials/" + name + ".mat";
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        Shader sh = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
        if (sh == null) sh = Shader.Find(urp ? "Standard" : "Universal Render Pipeline/Lit");
        bool isUrp = sh != null && sh.name.StartsWith("Universal");

        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = sh;
        mat.SetColor(isUrp ? "_BaseColor" : "_Color", col);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat(isUrp ? "_Smoothness" : "_Glossiness", smoothness);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
