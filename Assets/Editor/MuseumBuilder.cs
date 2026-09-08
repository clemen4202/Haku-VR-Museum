using UnityEngine;
using UnityEditor;

// Put this file in:  Assets/Editor/MuseumBuilder.cs
// The folder MUST be named exactly "Editor".
// Then use the menu:  Tools > Build Museum

public static class MuseumBuilder
{
    const float H = 4f;      // wall height
    const float T = 0.3f;    // wall thickness
    const float D = 3f;      // doorway / corridor width

    static Transform root;
    static int roomNo;

    [MenuItem("Tools/Build Museum")]
    static void Build()
    {
        root = new GameObject("Museum").transform;
        roomNo = 0;

        Floor(40f, 30f);

        //    centreX centreZ size   North  East   South  West   <- which sides have a doorway
        Room(-13f,   -6f,    8f,    false, true,  true,  false); // Room 1  (entry)
        Room(  0f,   -6f,    8f,    false, true,  false, true );  // Room 2
        Room( 13f,   -6f,    8f,    true,  false, false, true );  // Room 3
        Room( 13f,    6f,    8f,    false, false, true,  true );  // Room 4
        Room(  0f,    6f,    8f,    true,  true,  false, false); // Room 5  (exit)

        CorridorX( -9f,  -4f,  -6f);   // Room 1 -> Room 2
        CorridorX(  4f,   9f,  -6f);   // Room 2 -> Room 3
        CorridorZ( -2f,   2f,  13f);   // Room 3 -> Room 4
        CorridorX(  4f,   9f,   6f);   // Room 4 -> Room 5
        CorridorZ(-13f, -10f, -13f);   // entry porch
        CorridorZ( 10f,  13f,   0f);   // exit porch

        Debug.Log("Museum built. Everything is under the 'Museum' object in the Hierarchy.");
    }

    // ---------- helpers ----------

    static GameObject Box(string name, Vector3 pos, Vector3 scale, float rotY)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(root);
        g.transform.position = pos;
        g.transform.eulerAngles = new Vector3(0f, rotY, 0f);
        g.transform.localScale = scale;
        return g;
    }

    static void Floor(float width, float depth)
    {
        Box("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(width, 0.2f, depth), 0f);
    }

    // Wall running along the X axis. If door is true it is split into two
    // segments with a D-wide gap in the middle.
    static void WallX(string name, float cx, float cz, float len, bool door)
    {
        if (!door)
        {
            Box(name, new Vector3(cx, H * 0.5f, cz), new Vector3(len, H, T), 0f);
            return;
        }
        float seg = (len - D) * 0.5f;
        float off = D * 0.5f + seg * 0.5f;
        Box(name + "_a", new Vector3(cx - off, H * 0.5f, cz), new Vector3(seg, H, T), 0f);
        Box(name + "_b", new Vector3(cx + off, H * 0.5f, cz), new Vector3(seg, H, T), 0f);
    }

    // Wall running along the Z axis (rotated 90 degrees).
    static void WallZ(string name, float cx, float cz, float len, bool door)
    {
        if (!door)
        {
            Box(name, new Vector3(cx, H * 0.5f, cz), new Vector3(len, H, T), 90f);
            return;
        }
        float seg = (len - D) * 0.5f;
        float off = D * 0.5f + seg * 0.5f;
        Box(name + "_a", new Vector3(cx, H * 0.5f, cz - off), new Vector3(seg, H, T), 90f);
        Box(name + "_b", new Vector3(cx, H * 0.5f, cz + off), new Vector3(seg, H, T), 90f);
    }

    static void Room(float cx, float cz, float size, bool n, bool e, bool s, bool w)
    {
        roomNo++;
        float h = size * 0.5f;
        string p = "Room" + roomNo + "_";
        WallX(p + "N", cx, cz + h, size, n);
        WallX(p + "S", cx, cz - h, size, s);
        WallZ(p + "E", cx + h, cz, size, e);
        WallZ(p + "W", cx - h, cz, size, w);
    }

    // Corridor running along X from x0 to x1, centred on cz.
    static void CorridorX(float x0, float x1, float cz)
    {
        float len = x1 - x0;
        float cx = (x0 + x1) * 0.5f;
        Box("Corridor", new Vector3(cx, H * 0.5f, cz + D * 0.5f), new Vector3(len, H, T), 0f);
        Box("Corridor", new Vector3(cx, H * 0.5f, cz - D * 0.5f), new Vector3(len, H, T), 0f);
    }

    // Corridor running along Z from z0 to z1, centred on cx.
    static void CorridorZ(float z0, float z1, float cx)
    {
        float len = z1 - z0;
        float cz = (z0 + z1) * 0.5f;
        Box("Corridor", new Vector3(cx + D * 0.5f, H * 0.5f, cz), new Vector3(len, H, T), 90f);
        Box("Corridor", new Vector3(cx - D * 0.5f, H * 0.5f, cz), new Vector3(len, H, T), 90f);
    }
}
