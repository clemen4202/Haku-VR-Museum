using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

// Put this in:  Assets/Scripts/MuseumGuide.cs
//
// "Haku", the museum's AI guide. VRInteractor creates one when a headset is running.
//
//   * A small floating character follows the visitor round the museum, keeping a few
//     metres away. If it loses sight of you (you went through a door) it re-appears at
//     your side instead of drifting through walls.
//   * Point at it and press T1 to open a panel. The panel shows the guide's last answer and
//     a few preset question buttons (a headset has no keyboard, and Ollama cannot do speech
//     recognition). Point at a button and press T1.
//   * Each question is sent to a LOCAL Ollama server together with what the visitor is
//     looking at: the exhibit they hold, or the nearest one, plus the other exhibits and
//     which they have already inspected. The model writes a short answer.
//
// SETTINGS are the public fields below. To use a different model, change `model` (it must be
// one you have pulled: `ollama list`). `ollamaUrl` is localhost, which only works while the game
// runs on the same PC as Ollama (Editor + Quest Link). A standalone Quest build would need
// this PC's Wi-Fi address here and Ollama started with OLLAMA_HOST=0.0.0.0.

public class MuseumGuide : MonoBehaviour
{
    // ------------------------------------------------------------ settings
    public string ollamaUrl = "http://localhost:11434";
    public string model     = "qwen3:8b";
    public string keepAlive = "30m";        // keep the model in memory between questions
    public int    maxTokens = 140;          // ~55 words plus slack
    public float  temperature = 0.6f;

    public float followDistance = 1.7f;     // always hovers ahead of you...
    public float sideOffset     = 0.75f;    // ...a little to the right, so it does not block the view or your right-hand ray
    public float comfortAngle   = 25f;      // turn your head further than this and it glides back in front

    /// Set by VRInteractor: what the visitor is holding right now (null if nothing).
    public Func<Inspectable> heldProvider;

    // ------------------------------------------------------------ prompt
    const string SystemPrompt =
        "You are Haku, the friendly guide of a small brick-walled museum that the visitor is exploring in virtual reality. " +
        "The museum has five rooms joined by corridors on a one-way route, and some rooms have an exhibit on a plinth. " +
        "Reply in 2 or 3 short sentences, at most 55 words, in plain text: no markdown, lists, emojis or stage directions. " +
        "Use the CONTEXT for what the visitor is looking at. Do not invent exact dates, names or facts the context does not " +
        "support; if you are unsure, say so briefly. Speak directly and warmly to the visitor.";

    static readonly string[] Labels =
    {
        "What is this?", "How old is it?", "How was it used?", "A fun fact", "Where next?",
    };
    static readonly string[] Questions =
    {
        "Tell me about what I am looking at.",
        "How old is it?",
        "How was it made and how was it used?",
        "Tell me one fun fact about it.",
        "Which exhibit should I see next, and which way is it?",
    };

    // ------------------------------------------------------------ state
    Transform head, rig;
    CharacterController playerCC;

    Transform docent, panel;
    TextMesh  replyText;
    Shader    solid;

    bool  open, busy, ready;
    string warmProblem;
    bool  greeted;
    bool  catchingUp;
    Vector3 followVel;
    Vector3 spot;                 // where it wants to stand (feet, at floor level)
    bool  hasSpot, hereOk = true;
    float nextCheck;
    float appearT = 1f;           // 0..1 while popping back in after a jump

    // The guide's body for space checks: a capsule from 0.25 m to 1.75 m above the floor.
    const float BodyRadius = 0.3f, BodyBottom = 0.25f, BodyTop = 1.75f;
    readonly Collider[] overlap = new Collider[16];
    float bobPhase;

    readonly HashSet<Inspectable> inspected = new HashSet<Inspectable>();
    readonly List<Msg> history = new List<Msg>();
    readonly RaycastHit[] buf = new RaycastHit[16];

    // ------------------------------------------------------------ JSON shapes (JsonUtility)
    [Serializable] class Msg  { public string role; public string content; public Msg() { } public Msg(string r, string c) { role = r; content = c; } }
    [Serializable] class Opts { public int num_predict; public float temperature; }
    [Serializable] class ChatReq
    {
        public string model; public Msg[] messages; public bool stream; public bool think;
        public string keep_alive; public Opts options;
    }
    [Serializable] class ChatResp { public Msg message; public string error; }
    [Serializable] class WarmReq  { public string model; public string keep_alive; }

    // ============================================================ setup

    public void Init(Transform headTransform, CharacterController playerController, Transform rigRoot)
    {
        head = headTransform; playerCC = playerController; rig = rigRoot;

        solid = Resources.Load<Shader>("MuseumSolid");
        if (solid == null) solid = Shader.Find("Universal Render Pipeline/Unlit");

        BuildDocent();
        BuildPanel();
        panel.gameObject.SetActive(false);
        docent.position = FindSpot(out spot) ? spot : head.position + Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;

        StartCoroutine(WarmUp());
    }

    public void NoteInspected(Inspectable item) { if (item != null) inspected.Add(item); }

    void OnDestroy()
    {
        if (docent != null) Destroy(docent.gameObject);
        if (panel != null)  Destroy(panel.gameObject);
    }

    // ------------------------------------------------------------ character

    Material Solid(Color c, float shade = 1f, Color glow = default(Color))
    {
        var m = new Material(solid);
        if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Emit"))      m.SetColor("_Emit", glow);
        if (m.HasProperty("_Shade"))     m.SetFloat("_Shade", shade);
        return m;
    }

    GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name;
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        return g;
    }

    void BuildDocent()
    {
        docent = new GameObject("Museum Guide (Haku)").transform;

        var robe  = Solid(new Color(0.62f, 0.22f, 0.16f));
        var skin  = Solid(new Color(0.93f, 0.84f, 0.70f));
        var gold  = Solid(new Color(0.85f, 0.68f, 0.28f), 1f, new Color(0.10f, 0.07f, 0f));
        var dark  = Solid(new Color(0.06f, 0.06f, 0.08f), 0f);

        Part(docent, PrimitiveType.Capsule,  "Robe", new Vector3(0f, 0.75f, 0f),   new Vector3(0.55f, 0.55f, 0.55f), robe);
        Part(docent, PrimitiveType.Cylinder, "Sash", new Vector3(0f, 1.00f, 0f),   new Vector3(0.58f, 0.03f, 0.58f), gold);
        Part(docent, PrimitiveType.Sphere,   "Head", new Vector3(0f, 1.52f, 0f),   new Vector3(0.30f, 0.30f, 0.30f), skin);
        Part(docent, PrimitiveType.Sphere,   "EyeL", new Vector3(-0.06f, 1.55f, 0.13f), new Vector3(0.05f, 0.05f, 0.05f), dark);
        Part(docent, PrimitiveType.Sphere,   "EyeR", new Vector3( 0.06f, 1.55f, 0.13f), new Vector3(0.05f, 0.05f, 0.05f), dark);

        // name tag: the character faces the visitor (+Z), so the text faces the other way (TextMesh reads with +Z away)
        var tag = new GameObject("Tag").transform;
        tag.SetParent(docent, false);
        tag.localPosition = new Vector3(0f, 1.88f, 0f);
        tag.localRotation = Quaternion.Euler(0f, 180f, 0f);
        Text(tag, "Haku - guide", 0.05f, TextAnchor.LowerCenter, TextAlignment.Center, Vector3.zero, new Color(1f, 0.9f, 0.6f));

        // the whole character is one "talk to me" target
        var col = docent.gameObject.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.95f, 0f); col.radius = 0.3f; col.height = 1.9f;
        var b = docent.gameObject.AddComponent<DocentButton>();
        b.kind = DocentButton.Kind.Talk; b.guide = this;
        b.prompt = "[T1]  talk to Haku, the guide";
        IgnorePlayer(col);
    }

    void IgnorePlayer(Collider c)
    {
        if (playerCC != null && c != null) Physics.IgnoreCollision(c, playerCC);
    }

    // ------------------------------------------------------------ panel

    void BuildPanel()
    {
        const float W = 1.3f, H = 1.3f;

        panel = new GameObject("Museum Guide Panel").transform;

        // background (local +Z points away from the viewer, so things nearer the viewer have smaller z)
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Background";
        Destroy(bg.GetComponent<Collider>());
        bg.transform.SetParent(panel, false);
        bg.transform.localScale = new Vector3(W, H, 1f);
        bg.GetComponent<Renderer>().sharedMaterial = Solid(new Color(0.07f, 0.07f, 0.09f), 0f);

        Text(panel, "Haku - museum guide", 0.05f, TextAnchor.UpperLeft, TextAlignment.Left,
             new Vector3(-W * 0.5f + 0.06f, H * 0.5f - 0.05f, -0.01f), new Color(1f, 0.85f, 0.45f));

        replyText = Text(panel, "", 0.04f, TextAnchor.UpperLeft, TextAlignment.Left,
                         new Vector3(-W * 0.5f + 0.06f, H * 0.5f - 0.15f, -0.01f), Color.white);

        // 3 rows x 2 columns: five questions and Close
        for (int i = 0; i < 6; i++)
        {
            float x = (i % 2 == 0) ? -0.32f : 0.32f;
            float y = -0.14f - (i / 2) * 0.17f;
            bool close = i == 5;
            MakeButton(panel, close ? "Close" : Labels[i], close ? null : Questions[i],
                       close ? DocentButton.Kind.Close : DocentButton.Kind.Question, new Vector3(x, y, -0.005f));
        }
    }

    void MakeButton(Transform parent, string label, string question, DocentButton.Kind kind, Vector3 pos)
    {
        var idle = kind == DocentButton.Kind.Close ? new Color(0.30f, 0.14f, 0.14f) : new Color(0.16f, 0.20f, 0.30f);

        var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
        plate.name = "Button: " + label;
        Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(parent, false);
        plate.transform.localPosition = pos;
        plate.transform.localScale = new Vector3(0.61f, 0.14f, 1f);
        var r = plate.GetComponent<Renderer>();
        r.sharedMaterial = Solid(idle, 0f);

        var box = plate.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 1f, 0.25f);          // thick enough in Z for the ray to hit reliably
        IgnorePlayer(box);

        var b = plate.AddComponent<DocentButton>();
        b.kind = kind; b.guide = this; b.label = label; b.question = question;
        b.prompt = kind == DocentButton.Kind.Close ? "[T1]  close" : "[T1]  ask: " + label;
        b.plate = r; b.idle = idle;

        // text is a child of the panel, not of the (non-uniformly scaled) plate
        Text(parent, label, 0.034f, TextAnchor.MiddleCenter, TextAlignment.Center,
             pos + new Vector3(0f, 0f, -0.008f), Color.white);
    }

    TextMesh Text(Transform parent, string txt, float meters, TextAnchor anchor, TextAlignment align, Vector3 localPos, Color col)
    {
        var g = new GameObject("Text");
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        var tm = g.AddComponent<TextMesh>();
        var f = MuseumText.BuiltinFont();
        if (f != null) tm.font = f;
        tm.fontSize      = 64;
        tm.characterSize = meters / 6.4f;      // world height of a line ~= characterSize * fontSize * 0.1
        tm.anchor        = anchor;
        tm.alignment     = align;
        tm.color         = col;
        tm.text          = txt;
        MuseumText.Apply(tm);
        return tm;
    }

    // ============================================================ behaviour

    void Update()
    {
        if (head == null || docent == null) return;

        Vector3 hp = head.position;
        Vector3 dp = docent.position;
        float floorY = FloorY();
        Vector3 feet = new Vector3(dp.x, floorY, dp.z);

        // Re-plan ten times a second: where should it stand, and is where it stands still OK?
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.1f;
            hasSpot = FindSpot(out spot);
            hereOk  = SpotFree(feet) && LineClear(hp, feet + Vector3.up * 1.2f);
        }

        if (hasSpot)
        {
            Vector3 toGuide = new Vector3(dp.x - hp.x, 0f, dp.z - hp.z);
            Vector3 toSpot  = new Vector3(spot.x - hp.x, 0f, spot.z - hp.z);
            float dist = toGuide.magnitude;

            if (!hereOk)
            {
                // Standing somewhere it must not be (a door swung into it, a wall is now between
                // you): re-appear at the free spot rather than staying in, or behind, the wall.
                Appear(spot);
            }
            else
            {
                // Lazy follow, like a headset menu: small head movements leave it where it is (so the
                // panel holds still while you aim at a button); once it drifts out of the comfortable
                // zone in front of you it moves back.
                float off = (dist > 0.01f && toSpot.sqrMagnitude > 0.01f) ? Vector3.Angle(toGuide, toSpot) : 0f;
                if (off > comfortAngle || dist < 0.8f || dist > followDistance + 1f) catchingUp = true;

                if (catchingUp)
                {
                    if (!PathClear(feet, spot))
                    {
                        Appear(spot);          // a wall, door or plinth is in the way: never glide through it
                    }
                    else
                    {
                        Vector3 cur = new Vector3(dp.x, 0f, dp.z), tgt = new Vector3(spot.x, 0f, spot.z);
                        Vector3 next = Vector3.SmoothDamp(cur, tgt, ref followVel, 0.25f, 8f);
                        docent.position = new Vector3(next.x, dp.y, next.z);
                        if ((next - tgt).sqrMagnitude < 0.0025f) catchingUp = false;     // within 5 cm: settle
                    }
                }
            }
        }
        // (no free spot anywhere in front - e.g. squeezed in a doorway - so it simply stays put)

        // pop back in after a jump
        if (appearT < 1f)
        {
            appearT = Mathf.Min(1f, appearT + Time.deltaTime / 0.2f);
            docent.localScale = Vector3.one * Mathf.SmoothStep(0.4f, 1f, appearT);
        }

        // hover a little and face the visitor
        bobPhase += Time.deltaTime * 1.6f;
        var p = docent.position;
        p.y = floorY + 0.12f + Mathf.Sin(bobPhase) * 0.04f;
        docent.position = p;

        Vector3 toHead = hp - p; toHead.y = 0f;
        if (toHead.sqrMagnitude > 0.01f)
        {
            var want = Quaternion.LookRotation(toHead.normalized, Vector3.up);
            docent.rotation = Quaternion.Slerp(docent.rotation, want, 5f * Time.deltaTime);
        }
    }

    void Appear(Vector3 at)
    {
        docent.position = new Vector3(at.x, docent.position.y, at.z);
        catchingUp = false;
        followVel  = Vector3.zero;
        hereOk     = true;
        appearT    = 0f;
    }

    void LateUpdate()
    {
        if (!open || panel == null || head == null) return;

        Vector3 toHead = head.position - docent.position; toHead.y = 0f;
        if (toHead.sqrMagnitude < 0.01f) return;
        Vector3 n = toHead.normalized;

        panel.position = docent.position + Vector3.up * 1.45f + n * 0.35f;
        panel.rotation = Quaternion.LookRotation(-n, Vector3.up);   // +Z away from the visitor
    }

    float FloorY() { return rig != null ? rig.position.y : 0f; }

    // ------------------------------------------------------------ where it can stand

    // Places to try, as (metres ahead, metres to the right) of the visitor, best first.
    // All are in front, so it never ends up behind you; the last ones are wider to the side
    // for tight corridors and doorways.
    static readonly Vector2[] Spots =
    {
        new Vector2(0f, 0f),                                   // filled in from followDistance / sideOffset
        new Vector2(0f, 0f),
        new Vector2(1.4f,  0.5f), new Vector2(1.4f, -0.5f),
        new Vector2(1.1f,  0.6f), new Vector2(1.1f, -0.6f),
        new Vector2(2.2f,  0.4f), new Vector2(2.2f, -0.4f),
        new Vector2(0.9f,  0.3f), new Vector2(0.9f, -0.3f),
        new Vector2(0.9f,  0.8f), new Vector2(0.9f, -0.8f),
        new Vector2(0.5f,  1.0f), new Vector2(0.5f, -1.0f),
    };

    /// Finds a spot in front of the visitor where the guide's whole body fits, there is floor
    /// under it, and the visitor can see it (no wall in between).
    bool FindSpot(out Vector3 result)
    {
        Vector3 hp = head.position;
        var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
        Vector3 fwd = yaw * Vector3.forward, right = yaw * Vector3.right;
        Vector3 basePos = new Vector3(hp.x, FloorY(), hp.z);

        Spots[0] = new Vector2(followDistance,  sideOffset);
        Spots[1] = new Vector2(followDistance, -sideOffset);

        foreach (var s in Spots)
        {
            Vector3 c = basePos + fwd * s.x + right * s.y;
            if (LineClear(hp, c + Vector3.up * 1.2f) && SpotFree(c)) { result = c; return true; }
        }
        result = basePos;
        return false;
    }

    /// Room for the guide's body at these feet, and real floor under it (not a void outside the building).
    bool SpotFree(Vector3 feet)
    {
        float y = FloorY();
        Vector3 a = new Vector3(feet.x, y + BodyBottom + BodyRadius, feet.z);
        Vector3 b = new Vector3(feet.x, y + BodyTop - BodyRadius, feet.z);
        int n = Physics.OverlapCapsuleNonAlloc(a, b, BodyRadius + 0.05f, overlap, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (Blocks(overlap[i])) return false;

        RaycastHit hit;
        if (!Physics.Raycast(new Vector3(feet.x, y + 1f, feet.z), Vector3.down, out hit, 1.6f, ~0, QueryTriggerInteraction.Ignore))
            return false;
        return hit.point.y < y + 0.3f;
    }

    /// Can the guide's body travel in a straight line from one spot to another without
    /// touching a wall, door, plinth or exhibit?
    bool PathClear(Vector3 fromFeet, Vector3 toFeet)
    {
        Vector3 d = new Vector3(toFeet.x - fromFeet.x, 0f, toFeet.z - fromFeet.z);
        float len = d.magnitude;
        if (len < 0.01f) return true;

        float y = FloorY();
        Vector3 a = new Vector3(fromFeet.x, y + BodyBottom + BodyRadius, fromFeet.z);
        Vector3 b = new Vector3(fromFeet.x, y + BodyTop - BodyRadius, fromFeet.z);
        int n = Physics.CapsuleCastNonAlloc(a, b, BodyRadius, d / len, buf, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (Blocks(buf[i].collider)) return false;
        return true;
    }

    /// Solid things the guide must keep out of: everything except the visitor and the guide itself.
    bool Blocks(Collider c)
    {
        if (c == null || c.isTrigger) return false;
        if (c == playerCC || (rig != null && c.transform.IsChildOf(rig))) return false;
        if (c.GetComponentInParent<DocentButton>() != null) return false;     // its own body and panel
        return true;
    }

    bool LineClear(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return true;
        int n = Physics.RaycastNonAlloc(a, d / len, buf, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = buf[i].collider;
            if (c == playerCC || (rig != null && c.transform.IsChildOf(rig))) continue;
            if (c.GetComponentInParent<DocentButton>() != null) continue;     // the guide and its panel
            if (c.GetComponentInParent<Inspectable>() != null) continue;      // exhibits do not block sight
            return false;
        }
        return true;
    }

    // ============================================================ interaction

    public void Press(DocentButton b)
    {
        switch (b.kind)
        {
            case DocentButton.Kind.Talk:  SetOpen(!open); break;
            case DocentButton.Kind.Close: SetOpen(false); break;
            case DocentButton.Kind.Question:
                if (!busy) StartCoroutine(Ask(b.question));
                break;
        }
    }

    void SetOpen(bool value)
    {
        open = value;
        panel.gameObject.SetActive(value);
        if (!value) return;

        if (!greeted)
        {
            greeted = true;
            SetReply(warmProblem != null
                ? "Hello! I'm Haku. I can't reach my thinking cap right now: " + warmProblem
                : "Hello! I'm Haku, your guide. Pick a question below and I'll tell you what I know.");
        }
    }

    void SetReply(string text)
    {
        if (replyText != null) replyText.text = Wrap(text, 44, 9);
    }

    // ============================================================ Ollama

    string Url(string path) { return ollamaUrl.TrimEnd('/') + path; }

    /// Loads the model into memory now, so the first real question is not a 30+ second wait.
    IEnumerator WarmUp()
    {
        string json = JsonUtility.ToJson(new WarmReq { model = model, keep_alive = keepAlive });
        using (var www = Post("/api/generate", json, 180))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                ready = true;
                warmProblem = null;
                Debug.Log("[MuseumGuide] Ollama model '" + model + "' is loaded and ready.");
            }
            else
            {
                warmProblem = Explain(www);
                Debug.LogWarning("[MuseumGuide] Ollama warm-up failed: " + warmProblem);
            }
        }
    }

    IEnumerator Ask(string question)
    {
        busy = true;
        SetReply("Hmm, let me think...");

        var msgs = new List<Msg> { new Msg("system", SystemPrompt) };
        msgs.AddRange(history);
        msgs.Add(new Msg("user", "CONTEXT:\n" + BuildContext() + "\n\nVISITOR: " + question));

        var req = new ChatReq
        {
            model = model, messages = msgs.ToArray(), stream = false, think = false, keep_alive = keepAlive,
            options = new Opts { num_predict = maxTokens, temperature = temperature },
        };

        string answer = null, problem = null;
        using (var www = Post("/api/chat", JsonUtility.ToJson(req), 180))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success) problem = Explain(www);
            else
            {
                try
                {
                    var resp = JsonUtility.FromJson<ChatResp>(www.downloadHandler.text);
                    if (resp != null && resp.message != null) answer = Clean(resp.message.content);
                    else problem = "the reply was empty";
                }
                catch (Exception e) { problem = "I could not read the reply (" + e.Message + ")"; }
            }
        }

        if (answer != null && answer.Length > 0)
        {
            history.Add(new Msg("user", "VISITOR: " + question));
            history.Add(new Msg("assistant", answer));
            while (history.Count > 6) history.RemoveAt(0);     // keep the prompt short and quick
            ready = true;
            SetReply(answer);
        }
        else
        {
            Debug.LogWarning("[MuseumGuide] " + problem);
            SetReply("Sorry, I lost my train of thought: " + (problem ?? "no answer") + ".");
        }
        busy = false;
    }

    UnityWebRequest Post(string path, string json, int timeoutSeconds)
    {
        var www = new UnityWebRequest(Url(path), "POST");
        www.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.timeout = timeoutSeconds;
        return www;
    }

    string Explain(UnityWebRequest www)
    {
        string body = www.downloadHandler != null ? www.downloadHandler.text : "";
        if (body.Length > 0)
        {
            try
            {
                var r = JsonUtility.FromJson<ChatResp>(body);
                if (r != null && !string.IsNullOrEmpty(r.error))
                    return r.error.Contains("not found")
                        ? "the model '" + model + "' is not installed (run: ollama pull " + model + ")"
                        : r.error;
            }
            catch { }
        }
        return "Ollama is not answering at " + ollamaUrl + " (" + www.error + "). Is it running?";
    }

    static readonly Regex ThinkTags = new Regex("<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = ThinkTags.Replace(s, "");
        s = s.Replace("*", "").Replace("#", "").Replace("`", "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > 420 ? s.Substring(0, 420).TrimEnd() + "..." : s;
    }

    // ------------------------------------------------------------ what the visitor is looking at

    string BuildContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Distances are straight-line; there are walls and doors in between, and the exhibits are in different rooms.");

        Inspectable held = heldProvider != null ? heldProvider() : null;
        var all = FindObjectsByType<Inspectable>(FindObjectsInactive.Exclude);

        Inspectable nearest = null;
        float nd = 6f;
        foreach (var it in all)
        {
            if (it == held) continue;
            float d = FlatDistance(it.transform.position);
            if (d < nd) { nd = d; nearest = it; }
        }

        if (held != null)
            sb.AppendLine("The visitor is holding: " + held.displayName + " - " + held.description);
        else if (nearest != null)
            sb.AppendLine("The nearest exhibit is: " + nearest.displayName + " - " + nearest.description + " (" + Where(nearest.transform.position) + ").");
        else
            sb.AppendLine("The visitor is not near any exhibit at the moment.");

        sb.AppendLine("All exhibits:");
        var listed = new HashSet<string>();
        foreach (var it in all)
        {
            if (!listed.Add(it.displayName)) continue;
            sb.AppendLine("- " + it.displayName + " (" + Where(it.transform.position) + ")" +
                          (inspected.Contains(it) ? " [already inspected up close]" : ""));
        }
        return sb.ToString();
    }

    float FlatDistance(Vector3 p)
    {
        Vector3 d = p - head.position; d.y = 0f;
        return d.magnitude;
    }

    string Where(Vector3 p)
    {
        Vector3 to = p - head.position; to.y = 0f;
        Vector3 f = head.forward; f.y = 0f;
        float ang = f.sqrMagnitude > 0.001f ? Vector3.SignedAngle(f.normalized, to, Vector3.up) : 0f;
        string dir = Mathf.Abs(ang) < 45f ? "ahead" : Mathf.Abs(ang) > 135f ? "behind you" : ang > 0f ? "to your right" : "to your left";
        return to.magnitude.ToString("0") + " m " + dir;
    }

    // ------------------------------------------------------------ text layout

    /// Word-wrap to `width` characters and keep at most `maxLines` lines.
    static string Wrap(string s, int width, int maxLines)
    {
        var sb = new StringBuilder();
        int col = 0, lines = 1;
        foreach (var w in s.Split(' '))
        {
            if (col + w.Length > width && col > 0)
            {
                if (++lines > maxLines) { sb.Append("..."); break; }
                sb.Append('\n'); col = 0;
            }
            else if (col > 0) { sb.Append(' '); col++; }
            sb.Append(w);
            col += w.Length;
        }
        return sb.ToString();
    }
}
