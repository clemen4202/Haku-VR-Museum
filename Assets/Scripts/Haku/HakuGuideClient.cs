// ---------------------------------------------------------------------------------------------
// HakuGuideClient.cs - the ONLY class in the project that touches UnityWebRequest.
//
// !! ANDROID CLEARTEXT HTTP - CONFIGURE ALL OF THIS OR EVERY REQUEST BELOW FAILS !!
//    The failure is opaque: it works perfectly in the Editor and over Link, then on device you get
//    a bare "Unknown Error" / "Cannot connect to destination host" with no mention of cleartext.
//    These gates are INDEPENDENT - fixing one and not the other still fails.
//
//    1. Player Settings > Other Settings > Configuration >
//         "Allow downloads over HTTP" = Always Allowed        (default is Not Allowed)
//       This is checked inside UnityWebRequest before a socket is ever opened.
//
//    2. Player Settings > Publishing Settings > tick "Custom Main Manifest".
//       That GENERATES Assets/Plugins/Android/AndroidManifest.xml - do not hand-create it.
//       In the generated file:
//         - add  android:usesCleartextTraffic="true"  to the <application> element
//         - add as children of <manifest>:
//               <uses-permission android:name="android.permission.INTERNET" />
//               <uses-permission android:name="android.permission.RECORD_AUDIO" />
//         - leave the generated <activity> block EXACTLY as Unity wrote it. Unity 6 uses
//           GameActivity; pasting an old UnityPlayerActivity/UnityThemeSelector manifest from a
//           tutorial installs fine and crashes on launch with a Theme.AppCompat error.
//
//    3. Player Settings > Other Settings > Android Application Configuration >
//         Internet Access = Require
//       and in the Meta Quest OpenXR feature settings:
//         "Force Remove Internet Permission" = OFF   (Meta's own checklist recommends ON - it would
//                                                     silently kill every call in this file)
//
//    Ownership warning: Meta > Tools > Android Manifest Tool rewrites that same file and will wipe
//    the cleartext attribute and RECORD_AUDIO. Put a "HAKU: do not regenerate" banner at the top of
//    it, give it one owner, and diff it in review.
//
//    Confirming it: run  adb logcat  while a request fires. Android prints
//    "Cleartext HTTP traffic to 192.168.0.90 not permitted" - the one unambiguous signal you get.
//
// Endpoints (as implemented in ai-service/main.py):
//   GET  /health    -> {ok, ollama, llm_model, llm_available, models_pulled, stt_loaded, tts_loaded, exhibits}
//   POST /ask       <- JSON {exhibit_id, question}   -> {exhibit_id, question, answer, ms}
//   POST /converse  <- multipart audio + exhibit_id + held + distance_m
//                   -> audio/wav body, plus X-Haku-Question / X-Haku-Answer / X-Haku-Ms-* headers
//
// Style: coroutines, not async/await - certain to behave under IL2CPP and easier to read.
// ---------------------------------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Haku
{
    // ---- wire types. Field names are snake_case ON PURPOSE: JsonUtility binds by exact name, and a
    // ---- mismatch leaves the field at its default with NO exception. That is the nastiest bug class
    // ---- in this design, so do not "tidy" these into camelCase.

    [Serializable]
    public class HealthReport
    {
        public bool ok;
        public bool ollama;
        public string llm_model;
        public bool llm_available;
        public string[] models_pulled;
        public bool stt_loaded;
        public bool tts_loaded;
        public string[] exhibits;
    }

    [Serializable]
    public class AskAnswer
    {
        public string exhibit_id;
        public string question;
        public string answer;
        public int ms;
    }

    [Serializable]
    internal class AskBody
    {
        public string exhibit_id;
        public string question;
    }

    /// <summary>What /converse gives back: the spoken answer plus the per-stage telemetry headers.</summary>
    public class GuideReply
    {
        public string question;     // X-Haku-Question  (what Whisper heard)
        public string answer;       // X-Haku-Answer    (what the guide said)
        public AudioClip clip;      // the WAV body, decoded
        public int sttMs;
        public int llmMs;
        public int ttsMs;
        public int totalMs;

        public string Timings
        {
            get
            {
                return "stt " + sttMs + " ms + llm " + llmMs + " ms + tts " + ttsMs +
                       " ms = " + totalMs + " ms";
            }
        }
    }

    public class HakuGuideClient : MonoBehaviour
    {
        [Tooltip("Leave empty to use Resources/HakuConfig.")]
        [SerializeField] private HakuConfig config;

        private static HakuGuideClient instance;

        /// <summary>
        /// Scene-wide client. Exhibit PREFABS cannot serialise a reference to a scene object, so they
        /// reach the client through here. Creates one if the scene has none.
        /// </summary>
        public static HakuGuideClient Instance
        {
            get
            {
                if (instance != null) return instance;

                instance = FindAnyObjectByType<HakuGuideClient>();
                if (instance == null)
                {
                    GameObject go = new GameObject("HakuGuideClient");
                    instance = go.AddComponent<HakuGuideClient>();
                    DontDestroyOnLoad(go);
                    Debug.Log("[Haku] Created a HakuGuideClient automatically.");
                }
                return instance;
            }
        }

        public HakuConfig Config
        {
            get { return config != null ? config : HakuConfig.Instance; }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }
            instance = this;
            Debug.Log("[Haku] Guide service endpoint: " + Config.BaseUrl);
        }

        // -----------------------------------------------------------------------------------------
        // GET /health - call this at boot. If it fails, nothing else in the app will work, and you
        // want to know that before a visitor is wearing the headset.
        // -----------------------------------------------------------------------------------------

        public Coroutine HealthCheck(Action<HealthReport> onOk, Action<string> onError)
        {
            return StartCoroutine(HealthRoutine(onOk, onError));
        }

        private IEnumerator HealthRoutine(Action<HealthReport> onOk, Action<string> onError)
        {
            string url = Config.Url("/health");

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.timeout = Config.HealthTimeoutSeconds;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Fail("GET /health", req, onError);
                    yield break;
                }

                HealthReport report = null;
                try
                {
                    report = JsonUtility.FromJson<HealthReport>(req.downloadHandler.text);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Haku] GET " + url + " returned 200 but the body did not parse: " +
                                   e.Message + "\nbody: " + Truncate(req.downloadHandler.text, 500));
                }

                if (report == null)
                {
                    if (onError != null) onError("health response did not parse");
                    yield break;
                }

                Debug.Log("[Haku] /health ok=" + report.ok + " ollama=" + report.ollama +
                          " llm=" + report.llm_model + " (available=" + report.llm_available + ")" +
                          " stt=" + report.stt_loaded + " tts=" + report.tts_loaded +
                          " exhibits=" + (report.exhibits == null ? 0 : report.exhibits.Length));

                if (!report.ok)
                    Debug.LogWarning("[Haku] The service is up but NOT ready. Check ollama is running " +
                                     "(ollama ps must show 100% GPU) and that the Piper voice file exists.");

                if (onOk != null) onOk(report);
            }
        }

        // -----------------------------------------------------------------------------------------
        // POST /ask - text in, text out. Build and test against THIS first: it needs no microphone,
        // no permission, no WAV encoding. If /ask works and /converse does not, the fault is audio,
        // not networking.
        // (The endpoint also accepts an optional "history" array of {role, content} turns. Not sent
        //  here - add it only when the team actually wants multi-turn memory.)
        // -----------------------------------------------------------------------------------------

        public Coroutine AskText(string exhibitId, string question,
                                 Action<AskAnswer> onOk, Action<string> onError)
        {
            return StartCoroutine(AskRoutine(exhibitId, question, onOk, onError));
        }

        private IEnumerator AskRoutine(string exhibitId, string question,
                                       Action<AskAnswer> onOk, Action<string> onError)
        {
            string url = Config.Url("/ask");

            AskBody body = new AskBody();
            body.exhibit_id = exhibitId;
            body.question = question;
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));

            using (UnityWebRequest req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(payload);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = Config.AskTimeoutSeconds;

                if (Config.VerboseLogging)
                    Debug.Log("[Haku] POST " + url + " exhibit_id=" + exhibitId + " question=\"" + question + "\"");

                float t0 = Time.realtimeSinceStartup;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Fail("POST /ask", req, onError);
                    yield break;
                }

                AskAnswer parsed = null;
                try
                {
                    parsed = JsonUtility.FromJson<AskAnswer>(req.downloadHandler.text);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Haku] POST " + url + " returned 200 but the body did not parse: " +
                                   e.Message + "\nbody: " + Truncate(req.downloadHandler.text, 500));
                }

                if (parsed == null || string.IsNullOrEmpty(parsed.answer))
                {
                    Debug.LogError("[Haku] POST " + url + " gave no answer field. body: " +
                                   Truncate(req.downloadHandler.text, 500));
                    if (onError != null) onError("empty answer");
                    yield break;
                }

                int roundTripMs = Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f);
                Debug.Log("[Haku] /ask answered in " + roundTripMs + " ms (server llm " + parsed.ms +
                          " ms): " + parsed.answer);

                if (onOk != null) onOk(parsed);
            }
        }

        // -----------------------------------------------------------------------------------------
        // POST /converse - the full loop. Multipart: audio file + exhibit_id + held + distance_m.
        // The reply body is the spoken answer as a WAV; the text and per-stage timings come back in
        // X-Haku-* response headers.
        // -----------------------------------------------------------------------------------------

        public Coroutine Converse(byte[] wavBytes, string exhibitId, bool held, float distanceMetres,
                                  Action<GuideReply> onOk, Action<string> onError)
        {
            return StartCoroutine(ConverseRoutine(wavBytes, exhibitId, held, distanceMetres, onOk, onError));
        }

        private IEnumerator ConverseRoutine(byte[] wavBytes, string exhibitId, bool held,
                                            float distanceMetres, Action<GuideReply> onOk,
                                            Action<string> onError)
        {
            string url = Config.Url("/converse");

            if (wavBytes == null || wavBytes.Length == 0)
            {
                Debug.LogError("[Haku] Converse called with no audio - nothing sent to " + url);
                if (onError != null) onError("no audio");
                yield break;
            }

            List<IMultipartFormSection> form = new List<IMultipartFormSection>();
            form.Add(new MultipartFormFileSection("audio", wavBytes, "speech.wav", "audio/wav"));
            form.Add(new MultipartFormDataSection("exhibit_id", exhibitId));
            form.Add(new MultipartFormDataSection("held", held ? "true" : "false"));
            form.Add(new MultipartFormDataSection("distance_m",
                     distanceMetres.ToString("0.##", CultureInfo.InvariantCulture)));

            using (UnityWebRequest req = UnityWebRequest.Post(url, form))
            {
                req.timeout = Config.ConverseTimeoutSeconds;

                if (Config.VerboseLogging)
                    Debug.Log("[Haku] POST " + url + " exhibit_id=" + exhibitId + " held=" + held +
                              " distance_m=" + distanceMetres.ToString("0.##", CultureInfo.InvariantCulture) +
                              " audio=" + wavBytes.Length + " bytes");

                float t0 = Time.realtimeSinceStartup;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    // 404 = exhibit_id does not match any record on the server.
                    // 422 = Whisper heard nothing (silence, mic muted, trigger tapped not held).
                    // 503 = the Piper voice file is missing on the laptop.
                    Fail("POST /converse", req, onError);
                    yield break;
                }

                byte[] wav = req.downloadHandler.data;
                if (wav == null || wav.Length == 0)
                {
                    Debug.LogError("[Haku] POST " + url + " returned 200 with an empty body.");
                    if (onError != null) onError("empty audio response");
                    yield break;
                }

                GuideReply reply = new GuideReply();
                reply.question = req.GetResponseHeader("X-Haku-Question");
                reply.answer = req.GetResponseHeader("X-Haku-Answer");
                reply.sttMs = ParseHeaderMs(req, "X-Haku-Ms-Stt");
                reply.llmMs = ParseHeaderMs(req, "X-Haku-Ms-Llm");
                reply.ttsMs = ParseHeaderMs(req, "X-Haku-Ms-Tts");
                reply.totalMs = ParseHeaderMs(req, "X-Haku-Ms-Total");
                reply.clip = WavUtility.ToAudioClip(wav, "HakuAnswer");

                if (reply.clip == null)
                {
                    Debug.LogError("[Haku] POST " + url + " returned " + wav.Length +
                                   " bytes that did not decode as a WAV.");
                    if (onError != null) onError("undecodable audio");
                    yield break;
                }

                int roundTripMs = Mathf.RoundToInt((Time.realtimeSinceStartup - t0) * 1000f);
                Debug.Log("[Haku] /converse round trip " + roundTripMs + " ms | server " + reply.Timings +
                          "\n  heard: \"" + reply.question + "\"" +
                          "\n  said : \"" + reply.answer + "\"");

                if (onOk != null) onOk(reply);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Failure reporting. Silent failure is the single fastest way for this team to lose a day,
        // so every failure prints the URL, the HTTP status, Unity's result enum, the error string
        // and whatever the server put in the body.
        // -----------------------------------------------------------------------------------------

        private void Fail(string what, UnityWebRequest req, Action<string> onError)
        {
            string body = "";
            try
            {
                if (req.downloadHandler != null && req.downloadHandler.data != null &&
                    req.downloadHandler.data.Length > 0 && req.downloadHandler.data.Length <= 8192)
                {
                    body = Truncate(req.downloadHandler.text, 500);
                }
            }
            catch (Exception)
            {
                body = "<unreadable body>";
            }

            string message = what + " FAILED\n" +
                             "  url    : " + req.url + "\n" +
                             "  status : " + req.responseCode + "\n" +
                             "  result : " + req.result + "\n" +
                             "  error  : " + req.error + "\n" +
                             "  body   : " + body;

            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                message += "\n  A ConnectionError with status 0 usually means one of:\n" +
                           "    - the service is not running (uvicorn main:app --host 0.0.0.0 --port 8000)\n" +
                           "    - Windows Firewall / the Wi-Fi profile is Public, not Private\n" +
                           "    - campus AP isolation: the headset and laptop cannot see each other at all\n" +
                           "    - Android cleartext HTTP is still blocked (see the header of this file)\n" +
                           "    - the endpoint is wrong: currently " + Config.BaseUrl;
            }

            Debug.LogError("[Haku] " + message);
            if (onError != null) onError(req.error);
        }

        private static int ParseHeaderMs(UnityWebRequest req, string header)
        {
            string raw = req.GetResponseHeader(header);
            int value;
            return int.TryParse(raw, out value) ? value : -1;
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }
    }
}
