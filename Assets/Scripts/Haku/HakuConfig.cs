// ---------------------------------------------------------------------------------------------
// HakuConfig.cs - where the Haku AI service lives on the network.
//
// One ScriptableObject asset, one field, edited in the Inspector so every teammate can point at
// their own laptop without touching code:
//
//     Assets > Create > Haku > Service Config   ->  save it as  Assets/Resources/HakuConfig.asset
//
// It MUST live in a folder called "Resources" (any depth) and be named HakuConfig, because
// HakuConfig.Instance finds it with Resources.Load. Drag it onto HakuGuideClient as well if you
// prefer an explicit reference.
//
// Runtime override, so a DHCP lease change does not mean a 20-minute IL2CPP rebuild:
// if a file named haku-endpoint.json exists in Application.persistentDataPath it wins over the
// asset. Push it with adb:
//
//     echo {"base_url":"http://192.168.0.90:8000"} > haku-endpoint.json
//     adb push haku-endpoint.json /sdcard/Android/data/com.rmit.haku.museum/files/haku-endpoint.json
//
// (USB-tethered dev alternative: adb reverse tcp:8000 tcp:8000, then base_url http://127.0.0.1:8000.
//  Note that localhost is NOT exempt from Android's cleartext block at this API level - you still
//  need the manifest settings listed at the top of HakuGuideClient.cs.)
// ---------------------------------------------------------------------------------------------
using System;
using System.IO;
using UnityEngine;

namespace Haku
{
    [CreateAssetMenu(fileName = "HakuConfig", menuName = "Haku/Service Config", order = 0)]
    public class HakuConfig : ScriptableObject
    {
        /// <summary>Resources path this config is loaded from: Assets/Resources/HakuConfig.asset</summary>
        public const string ResourcesPath = "HakuConfig";

        /// <summary>The team's designated AI laptop (ASUS ROG Zephyrus M16) on the demo LAN.</summary>
        public const string DefaultBaseUrl = "http://192.168.0.90:8000";

        /// <summary>Optional file in persistentDataPath that overrides the URL without a rebuild.</summary>
        public const string OverrideFileName = "haku-endpoint.json";

        [Header("Service")]
        [Tooltip("Base URL of the FastAPI service. No trailing slash needed. " +
                 "Change this to your own laptop's LAN IP when you host the service yourself.")]
        [SerializeField] private string baseUrl = DefaultBaseUrl;

        [Header("Timeouts (seconds)")]
        [Tooltip("/health - a liveness ping, should answer instantly.")]
        [SerializeField] private int healthTimeoutSeconds = 5;

        [Tooltip("/ask - measured LLM time is 0.7-1.0 s warm, but a cold Ollama load costs 50 s.")]
        [SerializeField] private int askTimeoutSeconds = 25;

        [Tooltip("/converse - measured full round trip is 1.6-1.9 s warm. 35 s leaves room for a cold start.")]
        [SerializeField] private int converseTimeoutSeconds = 35;

        [Header("Logging")]
        [Tooltip("Log every request URL and every reply's timings. Leave ON until the demo works.")]
        [SerializeField] private bool verboseLogging = true;

        private string runtimeBaseUrl;      // set from haku-endpoint.json, if that file exists
        private bool overrideChecked;

        public int HealthTimeoutSeconds { get { return Mathf.Max(1, healthTimeoutSeconds); } }
        public int AskTimeoutSeconds { get { return Mathf.Max(1, askTimeoutSeconds); } }
        public int ConverseTimeoutSeconds { get { return Mathf.Max(1, converseTimeoutSeconds); } }
        public bool VerboseLogging { get { return verboseLogging; } }

        /// <summary>Base URL with any trailing slash removed. The override file wins if present.</summary>
        public string BaseUrl
        {
            get
            {
                string chosen = string.IsNullOrEmpty(runtimeBaseUrl) ? baseUrl : runtimeBaseUrl;
                if (string.IsNullOrEmpty(chosen)) chosen = DefaultBaseUrl;
                return chosen.TrimEnd('/');
            }
        }

        /// <summary>Full URL for an endpoint, e.g. Url("/converse").</summary>
        public string Url(string path)
        {
            if (string.IsNullOrEmpty(path)) return BaseUrl;
            return path.StartsWith("/") ? BaseUrl + path : BaseUrl + "/" + path;
        }

        private static HakuConfig cached;

        /// <summary>
        /// The config asset from Resources. Falls back to an in-memory default (never null) so a
        /// missing asset produces one loud warning rather than a NullReferenceException per request.
        /// </summary>
        public static HakuConfig Instance
        {
            get
            {
                if (cached != null) return cached;

                cached = Resources.Load<HakuConfig>(ResourcesPath);
                if (cached == null)
                {
                    Debug.LogWarning("[Haku] No HakuConfig asset at Resources/" + ResourcesPath +
                                     " - falling back to " + DefaultBaseUrl + ". Create one with " +
                                     "Assets > Create > Haku > Service Config and save it in Assets/Resources.");
                    cached = CreateInstance<HakuConfig>();
                }

                cached.ApplyOverrideFile();
                return cached;
            }
        }

        /// <summary>Read haku-endpoint.json from persistentDataPath, once per run.</summary>
        public void ApplyOverrideFile()
        {
            if (overrideChecked) return;
            overrideChecked = true;

            string path = Path.Combine(Application.persistentDataPath, OverrideFileName);
            try
            {
                if (!File.Exists(path))
                {
                    if (verboseLogging)
                        Debug.Log("[Haku] No endpoint override at " + path + " - using " + BaseUrl);
                    return;
                }

                EndpointOverride parsed = JsonUtility.FromJson<EndpointOverride>(File.ReadAllText(path));
                if (parsed == null || string.IsNullOrEmpty(parsed.base_url))
                {
                    Debug.LogWarning("[Haku] " + path + " exists but has no base_url field. Ignoring it.");
                    return;
                }

                runtimeBaseUrl = parsed.base_url.Trim().TrimEnd('/');
                Debug.Log("[Haku] Endpoint overridden by " + path + " -> " + runtimeBaseUrl);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Haku] Could not read " + path + ": " + e.Message +
                                 " - using " + BaseUrl);
            }
        }

        [Serializable]
        private class EndpointOverride
        {
            public string base_url;
        }
    }
}
