using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

// Put this file in:  Assets/Editor/MuseumDoors.cs
// Requires Assets/Scripts/DoorInteract.cs first.
//
//   Tools > Add Doors
//
// Puts a pair of hinged double doors in the ENTRY opening (Room 1's south wall)
// and the EXIT opening (Room 5's north wall), with a stone lintel above each.
// Walk up, look at a door, press E.

public static class MuseumDoors
{
    const float GAP    = 3.0f;    // doorway width, matches MuseumBuilder
    const float LEAF_H = 2.55f;   // door height
    const float WALL_T = 0.3f;

    static bool urp;

    [MenuItem("Tools/Add Doors")]
    static void Build()
    {
        var doorType = System.Type.GetType("DoorInteract, Assembly-CSharp");
        if (doorType == null)
        {
            Debug.LogError("DoorInteract.cs must be in Assets/Scripts/ and compiled first.");
            return;
        }

        urp = GraphicsSettings.currentRenderPipeline != null;

        var old = GameObject.Find("MuseumDoors");
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject("MuseumDoors").transform;

        var woodMat  = Mat("M_DoorWood",  new Color(0.24f, 0.14f, 0.09f), 0f,   0.30f);
        var brassMat = Mat("M_DoorBrass", new Color(0.72f, 0.57f, 0.24f), 0.8f, 0.60f);
        var stoneMat = Mat("M_Lintel",    new Color(0.30f, 0.29f, 0.27f), 0f,   0.15f);

        // ENTRY: Room 1 south wall, centred on x = -13, at z = -10
        Doorway(root, doorType, "Entry Door", new Vector3(-13f, 0f, -10f), 0f,
                woodMat, brassMat, stoneMat);

        // EXIT: Room 5 north wall, centred on x = 0, at z = +10
        Doorway(root, doorType, "Exit Door", new Vector3(0f, 0f, 10f), 0f,
                woodMat, brassMat, stoneMat);

        Debug.Log("Doors added at the entry (-13, -10) and the exit (0, +10). Look at one and press E.");
    }

    // centre = middle of the doorway at floor level; yaw = 0 for a wall running along X
    static void Doorway(Transform root, System.Type doorType, string label,
                        Vector3 centre, float yaw,
                        Material wood, Material brass, Material stone)
    {
        var group = new GameObject(label).transform;
        group.SetParent(root);
        group.position    = centre;
        group.eulerAngles = new Vector3(0f, yaw, 0f);

        // stone lintel filling the wall above the doors
        var lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lintel.name = "Lintel";
        lintel.transform.SetParent(group);
        lintel.transform.localPosition = new Vector3(0f, LEAF_H + (4f - LEAF_H) * 0.5f, 0f);
        lintel.transform.localScale    = new Vector3(GAP + 0.06f, 4f - LEAF_H, WALL_T + 0.02f);
        lintel.transform.localRotation = Quaternion.identity;
        lintel.GetComponent<Renderer>().sharedMaterial = stone;

        // two leaves, hinged at the outer edges of the opening
        Leaf(group, doorType, label, wood, brass, -1);
        Leaf(group, doorType, label, wood, brass, +1);
    }

    // side = -1 for the left leaf, +1 for the right
    static void Leaf(Transform group, System.Type doorType, string label,
                     Material wood, Material brass, int side)
    {
        float half = GAP * 0.5f;

        var hinge = new GameObject((side < 0 ? "HingeL" : "HingeR"));
        hinge.transform.SetParent(group);
        hinge.transform.localPosition = new Vector3(side * half, 0f, 0f);
        hinge.transform.localRotation = Quaternion.identity;

        var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leaf.name = "Leaf";
        leaf.transform.SetParent(hinge.transform);
        leaf.transform.localPosition = new Vector3(-side * half * 0.5f, LEAF_H * 0.5f, 0f);
        leaf.transform.localScale    = new Vector3(half - 0.02f, LEAF_H, 0.08f);
        leaf.transform.localRotation = Quaternion.identity;
        leaf.GetComponent<Renderer>().sharedMaterial = wood;

        // handle near the free edge
        var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        handle.name = "Handle";
        handle.transform.SetParent(hinge.transform);
        handle.transform.localPosition = new Vector3(-side * 0.22f, 1.05f, 0.07f);
        handle.transform.localScale    = new Vector3(0.05f, 0.22f, 0.05f);
        handle.transform.localRotation = Quaternion.identity;
        handle.GetComponent<Renderer>().sharedMaterial = brass;
        Object.DestroyImmediate(handle.GetComponent<Collider>());

        var comp = hinge.AddComponent(doorType);
        var so = new SerializedObject(comp);
        so.FindProperty("doorName").stringValue  = label.ToLower();
        so.FindProperty("openAngle").floatValue  = side < 0 ? -95f : 95f;
        so.FindProperty("speed").floatValue      = 260f;
        so.ApplyModifiedPropertiesWithoutUndo();
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
