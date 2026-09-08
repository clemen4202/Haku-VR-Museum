// Creates a Universal Render Pipeline asset tuned for Quest 3 and makes it the active pipeline.
//
// WHY: the project was created from the default 3D (Built-in RP) template. Adding the URP *package*
// does not switch the pipeline - GraphicsSettings.m_CustomRenderPipeline stayed {fileID: 0}, so
// materials were still being authored against the Built-in "Standard" shader. On Quest we want URP:
// Built-in is not the supported XR path on Unity 6 and costs performance we cannot spare.
//
// Settings below are chosen for a 72 fps standalone Quest 3 target:
//   MSAA 4x        - the single biggest visual win in VR; edge shimmer is very visible in a headset
//   HDR off        - costs bandwidth on a tile-based mobile GPU for little gain in a grey-box gallery
//   Render scale 1 - the floor is 0.85; start at 1.0 and only drop if profiling says so
//   1 shadow cascade, 25 m distance - a museum interior never needs long-range shadows
//   SRP Batcher on - this is what replaces the deprecated Dynamic Batching
//
// Run:
//   Unity.exe -batchmode -nographics -projectPath <proj> -executeMethod HakuURPSetup.Configure

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class HakuURPSetup
{
    const string kFolder = "Assets/Settings";
    const string kRendererPath = "Assets/Settings/Haku_UniversalRenderer.asset";
    const string kPipelinePath = "Assets/Settings/Haku_URP_Quest.asset";

    public static void Configure()
    {
        try
        {
            if (!AssetDatabase.IsValidFolder(kFolder))
                AssetDatabase.CreateFolder("Assets", "Settings");

            // --- renderer -------------------------------------------------------
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(kRendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, kRendererPath);
                Debug.Log("[HakuURP] created renderer: " + kRendererPath);
            }

            // --- pipeline asset -------------------------------------------------
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(kPipelinePath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, kPipelinePath);
                Debug.Log("[HakuURP] created pipeline asset: " + kPipelinePath);
            }

            // --- Quest 3 tuning -------------------------------------------------
            var so = new SerializedObject(urp);
            void Set(string prop, Action<SerializedProperty> apply)
            {
                var p = so.FindProperty(prop);
                if (p != null) { apply(p); Debug.Log("[HakuURP]   " + prop + " set"); }
                else Debug.LogWarning("[HakuURP]   property not found (skipped): " + prop);
            }

            Set("m_MSAA", p => p.intValue = 4);                       // 4x MSAA
            Set("m_SupportsHDR", p => p.boolValue = false);
            Set("m_RenderScale", p => p.floatValue = 1.0f);
            Set("m_ShadowDistance", p => p.floatValue = 25f);
            Set("m_ShadowCascadeCount", p => p.intValue = 1);
            Set("m_UseSRPBatcher", p => p.boolValue = true);
            Set("m_SupportsCameraDepthTexture", p => p.boolValue = false);
            Set("m_SupportsCameraOpaqueTexture", p => p.boolValue = false);
            Set("m_MainLightShadowmapResolution", p => p.intValue = 1024);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);

            // --- make it the active pipeline ------------------------------------
            GraphicsSettings.defaultRenderPipeline = urp;
            Debug.Log("[HakuURP] GraphicsSettings.defaultRenderPipeline assigned.");

            int levels = QualitySettings.names.Length;
            int original = QualitySettings.GetQualityLevel();
            for (int i = 0; i < levels; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(original, false);
            Debug.Log("[HakuURP] assigned to all " + levels + " quality level(s).");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // --- prove it -------------------------------------------------------
            var active = GraphicsSettings.currentRenderPipeline;
            if (active == null)
            {
                Debug.LogError("[HakuURP] FAILED: currentRenderPipeline is still null after assignment.");
                Exit(1); return;
            }
            Debug.Log("[HakuURP] active pipeline is now: " + active.GetType().Name);
            Debug.Log("[HakuURP] DONE. Re-run HakuMuseumGenerator.Generate so materials author against URP/Lit.");
            Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("[HakuURP] FAILED: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            Exit(1);
        }
    }

    static void Exit(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }
}
