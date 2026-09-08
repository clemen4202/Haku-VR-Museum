using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using System.Collections.Generic;

// Put this file in:  Assets/Editor/MuseumMaterials.cs
// Put the four PNGs in:  Assets/Textures/
// Then use the menu:  Tools > Apply Museum Materials
//
// This version detects whether the project is running Built-in or URP and picks
// the matching shader and property names, so it will not produce magenta.

public static class MuseumMaterials
{
    // How many world units one repeat of the texture covers.
    // Lower = smaller bricks/tiles. Try 1.5 or 3 for a different scale.
    const float TEX_SIZE = 2f;

    const string MatFolder   = "Assets/Materials";
    const string WallAlbedo  = "Assets/Textures/brick_albedo.png";
    const string WallNormal  = "Assets/Textures/brick_normal.png";
    const string FloorAlbedo = "Assets/Textures/floor_albedo.png";
    const string FloorNormal = "Assets/Textures/floor_normal.png";

    [MenuItem("Tools/Apply Museum Materials")]
    static void Apply()
    {
        var museum = GameObject.Find("Museum");
        if (museum == null)
        {
            Debug.LogError("No GameObject named 'Museum' found. Run Tools > Build Museum first.");
            return;
        }

        // ---- work out which render pipeline is actually active ----
        bool urp = GraphicsSettings.currentRenderPipeline != null;

        Shader sh = urp ? Shader.Find("Universal Render Pipeline/Lit")
                        : Shader.Find("Standard");

        // fall back to whichever one does exist
        if (sh == null) sh = Shader.Find(urp ? "Standard" : "Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("HDRP/Lit");
        if (sh == null)
        {
            Debug.LogError("Could not find a usable lit shader. Tell me what your Project Settings > Quality shows.");
            return;
        }
        urp = sh.name.StartsWith("Universal");

        string ALBEDO    = urp ? "_BaseMap"    : "_MainTex";
        string SMOOTHNESS = urp ? "_Smoothness" : "_Glossiness";

        Debug.Log("Museum materials: using shader '" + sh.name + "'.");

        MarkAsNormalMap(WallNormal);
        MarkAsNormalMap(FloorNormal);

        if (!AssetDatabase.IsValidFolder(MatFolder))
            AssetDatabase.CreateFolder("Assets", "Materials");

        var wallTex  = AssetDatabase.LoadAssetAtPath<Texture2D>(WallAlbedo);
        var wallNrm  = AssetDatabase.LoadAssetAtPath<Texture2D>(WallNormal);
        var floorTex = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorAlbedo);
        var floorNrm = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorNormal);

        if (wallTex == null || floorTex == null)
        {
            Debug.LogError("Textures not found. They must be at " + WallAlbedo + " and " + FloorAlbedo);
            return;
        }

        var cache = new Dictionary<string, Material>();

        foreach (var rend in museum.GetComponentsInChildren<Renderer>())
        {
            Vector3 s = rend.transform.localScale;
            bool isFloor = rend.gameObject.name == "Floor";

            // Cube UVs run along local X and Y on the side faces, and local X and Z
            // on the top face, so the floor and the walls measure differently.
            Vector2 tiling = isFloor
                ? new Vector2(s.x / TEX_SIZE, s.z / TEX_SIZE)
                : new Vector2(s.x / TEX_SIZE, s.y / TEX_SIZE);

            string key = (isFloor ? "Floor" : "Wall") + "_"
                       + tiling.x.ToString("0.##") + "x" + tiling.y.ToString("0.##");

            Material mat;
            if (!cache.TryGetValue(key, out mat))
            {
                string path = MatFolder + "/M_" + key.Replace('.', '_') + ".mat";
                mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(sh);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = sh;   // repairs any material left over from a wrong pipeline

                Texture2D albedo = isFloor ? floorTex : wallTex;
                mat.SetTexture(ALBEDO, albedo);
                mat.SetTextureScale(ALBEDO, tiling);
                mat.SetColor(urp ? "_BaseColor" : "_Color", Color.white);

                Texture2D nrm = isFloor ? floorNrm : wallNrm;
                if (nrm != null)
                {
                    mat.EnableKeyword("_NORMALMAP");
                    mat.SetTexture("_BumpMap", nrm);
                    mat.SetTextureScale("_BumpMap", tiling);
                    mat.SetFloat("_BumpScale", 1f);
                }

                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat(SMOOTHNESS, isFloor ? 0.55f : 0.10f); // polished floor, matte brick

                EditorUtility.SetDirty(mat);
                cache[key] = mat;
            }

            rend.sharedMaterial = mat;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Applied museum materials. Created/reused " + cache.Count + " materials in " + MatFolder);
    }

    static void MarkAsNormalMap(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        if (ti.textureType != TextureImporterType.NormalMap)
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }
    }
}
