using UnityEngine;

// Put this in:  Assets/Scripts/MuseumText.cs
//
// Swaps every TextMesh in the museum from Unity's built-in font material (which ignores
// depth, so text shows through walls and doors) to Assets/Resources/MuseumDepthText.shader
// (which does not). Runs automatically after the scene loads - nothing to wire up - and
// VRInteractor calls Apply / ApplyOverlay for the text it creates at runtime.
//
// The material is built at runtime because a dynamic font's glyph atlas is only created
// while the game runs, so it cannot be saved into a material asset.

public static class MuseumText
{
    static Material depthMat;      // walls hide it
    static Material overlayMat;    // always on top (head-locked debug readout)
    static Font     matFont;
    static bool     warned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void FixSceneText()
    {
        foreach (var tm in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include))
            Apply(tm);
    }

    /// Depth-tested: hidden by walls and doors.
    public static void Apply(TextMesh tm)         { Set(tm, false); }

    /// Always drawn on top - for text stuck to the player's view.
    public static void ApplyOverlay(TextMesh tm)  { Set(tm, true); }

    static void Set(TextMesh tm, bool overlay)
    {
        if (tm == null) return;
        var r = tm.GetComponent<MeshRenderer>();
        if (r == null) return;

        Font f = tm.font != null ? tm.font : BuiltinFont();
        if (f == null) return;

        if (!Build(f)) return;                     // shader missing: leave the default material
        r.sharedMaterial = overlay ? overlayMat : depthMat;
    }

    static bool Build(Font f)
    {
        if (depthMat != null && overlayMat != null && matFont == f) return true;

        Shader s = Resources.Load<Shader>("MuseumDepthText");
        if (s == null)
        {
            if (!warned) Debug.LogWarning("[MuseumText] Assets/Resources/MuseumDepthText.shader not found - " +
                                          "text will show through walls.");
            warned = true;
            return false;
        }

        Texture atlas = f.material != null ? f.material.mainTexture : null;

        depthMat   = new Material(s) { name = "MuseumText (depth)" };
        overlayMat = new Material(s) { name = "MuseumText (overlay)" };
        overlayMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
        depthMat.mainTexture = overlayMat.mainTexture = atlas;
        matFont = f;

        // The atlas can be rebuilt when new characters are needed.
        Font.textureRebuilt -= OnRebuilt;
        Font.textureRebuilt += OnRebuilt;
        return true;
    }

    static void OnRebuilt(Font f)
    {
        if (f != matFont || f.material == null) return;
        if (depthMat != null)   depthMat.mainTexture   = f.material.mainTexture;
        if (overlayMat != null) overlayMat.mainTexture = f.material.mainTexture;
    }

    public static Font BuiltinFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        return f;
    }
}
