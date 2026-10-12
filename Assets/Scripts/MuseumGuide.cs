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
//   * Or hold A and just ask out loud (GuideVoice: local Whisper speech-to-text); Haku
//     reads its answers aloud with the Windows voice.
//   * Each question is sent to a LOCAL Ollama server together with what the visitor is
//     looking at: the exhibit they hold, or the nearest one, plus the other exhibits and
//     which they have already inspected. The model writes a short answer.
//
// SETTINGS are the public fields below. To use a different model, change `model` (it must be
// one you have pulled: `ollama list`). `ollamaUrl` is this PC (127.0.0.1), which only works while the game
// runs on the same PC as Ollama (Editor + Quest Link). A standalone Quest build would need
// this PC's Wi-Fi address here and Ollama started with OLLAMA_HOST=0.0.0.0.

public class MuseumGuide : MonoBehaviour
{
    // ------------------------------------------------------------ settings
    public string ollamaUrl = "http://127.0.0.1:11434";   // not "localhost": Unity tries IPv6 first, Ollama listens on IPv4
    public string model     = "qwen3:8b";
    public string keepAlive = "4h";         // keep the model loaded through a whole demo session (a cold load is ~30-40 s)
    public int    maxTokens = 140;          // ~55 words plus slack
    public float  temperature = 0.6f;

    public float followDistance = 1.7f;     // always hovers ahead of you...
    public float sideOffset     = 0.75f;    // ...a little to the right, so it does not block the view or your right-hand ray
    public float comfortAngle     = 45f;    // it only moves once it is this far from where you are looking...
    public float openComfortAngle = 55f;    // ...or this far while its panel is open (so you can look across the buttons)
    public float viewAngle        = 55f;    // roughly the edge of the Quest 3 view: it only pops across a plinth/doorway once it is past this

    public bool robotLook = true;           // added: small white robot (about half the old height); false = the original robed guide

    /// Added: centre of Haku's head above his feet, for the "aim at his head" check in VRInteractor.
    /// 1.52 m for the original robed guide; set lower when the robot is built.
    public static float HeadHeight = 1.52f;

    /// Set by VRInteractor: what the visitor is holding right now (null if nothing).
    public Func<Inspectable> heldProvider;

    // ------------------------------------------------------------ prompt
    const string SystemPrompt =
        "You are Haku, the friendly guide of a small brick-walled museum that the visitor is exploring in virtual reality. " +
        "The museum has five rooms joined by corridors on a one-way route, and some rooms have an exhibit on a plinth. " +
        "Reply in 2 or 3 short sentences, at most 55 words, in plain text: no markdown, lists, emojis or stage directions. " +
        "Use the CONTEXT for what the visitor is looking at. Do not invent exact dates, names or facts the context does not " +
        "support; if you are unsure, say so briefly. Speak directly and warmly to the visitor.";

    const string Greeting = "Hello! I'm Haku, your guide. Pick a question below, or hold A and ask me anything out loud.";
    const string Waking   = "Hello! I'm Haku. One moment - I'm just waking up...";

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

    bool  open, busy, ready, warming;
    bool  idle = true;            // the panel shows IdleText() (not a transient status like "Listening...")
    string lastAnswer;            // the last answer, shown again whenever the panel is idle
    float nextRetry;              // when to try Ollama again after a failed warm-up
    bool  probeOk;                // Ollama answered; the model load is in progress
    float loadStart;
    bool  wakingShown;
    bool  unreachableProblem;     // warmProblem means "can't connect" (vs "connected but the load failed")
    string statusText;            // the transient status on show (Listening..., thinking...)
    TextMesh statusLine;          // shows that status in front of you when the panel can't be seen
    string statusLineShown;
    GuideVoice voice;
    bool  talkHeld;
    string warmProblem;
    bool  catchingUp;
    Vector3 followVel;
    Vector3 spot;                 // where it wants to stand (feet, at floor level)
    bool  hasSpot, hereOk = true, panelBlocked;
    float nextCheck;
    float appearT = 1f;           // 0..1 while popping back in after a jump
    bool  hidden;                 // no room anywhere in front of you: out of the way until there is
    Renderer[] bodyRenderers;
    Collider   bodyCollider;

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
        Hide();                                        // placed by Update once head tracking is live

        statusLine = Text(head, "", 0.03f, TextAnchor.UpperCenter, TextAlignment.Center,
                          new Vector3(0f, -0.2f, 0.9f), new Color(1f, 0.88f, 0.5f));
        MuseumText.ApplyOverlay(statusLine);
        statusLine.gameObject.SetActive(false);

        // Voice: hold A to ask out loud, answers are spoken. A failure here must not stop the guide.
        try
        {
            voice = gameObject.AddComponent<GuideVoice>();
            voice.Init(docent);
            voice.OnStatus = s => { if (!open) SetOpen(true); ShowStatus(s); };
            voice.OnNothingHeard = ShowIdle;                     // A was only tapped: put the panel back
            voice.OnHeard  = q =>
            {
                if (busy) { ShowStatus("One moment - I'm still answering. Ask me again when I've finished."); return; }
                StartCoroutine(Ask(q, true));
            };
        }
        catch (Exception e) { Debug.LogException(e); voice = null; }

        StartCoroutine(WarmUp());
    }

    public void NoteInspected(Inspectable item) { if (item != null) inspected.Add(item); }

    /// True while Haku's panel is open and on screen (VRInteractor keeps its own labels off it).
    public bool PanelShowing { get { return open && panel != null && panel.gameObject.activeSelf; } }

    // The panel hangs between Haku and you: this high above his feet, this far towards you.
    const float PanelHeight = 1.45f + 0.12f, PanelOut = 0.35f;

    /// Called every frame by VRInteractor with whether A is held: press starts listening,
    /// release sends what was said.
    public void PushToTalk(bool held)
    {
        bool pressed = held && !talkHeld, released = !held && talkHeld;
        talkHeld = held;                       // first, so a failure below cannot repeat every frame
        if (voice == null || (!pressed && !released)) return;
        try
        {
            if (pressed)
            {
                if (!open) SetOpen(true);
                if (busy) ShowStatus("One moment - I'm still answering. Hold A again when I've finished.");
                else      voice.BeginListening();
            }
            else voice.EndListening();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            ShowStatus("Sorry, I can't use the microphone right now.");
        }
    }

    /// Stopping Play: tell Ollama to free the GPU memory now instead of holding the model for keepAlive.
    void OnApplicationQuit()
    {
        if (!ready && !warming) return;
        try
        {
            string url = Url("/api/generate").Replace("://localhost", "://127.0.0.1");
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = 1500;
            byte[] body = Encoding.UTF8.GetBytes("{\"model\":\"" + model + "\",\"keep_alive\":0}");
            using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            using (req.GetResponse()) { }
        }
        catch { /* Ollama already gone - nothing to free */ }
    }

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
        if (robotLook) { BuildRobotDocent(); return; }   // added: the robot look (the original body below is kept)
        HeadHeight = 1.52f;

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
        bodyCollider   = col;
        bodyRenderers  = docent.GetComponentsInChildren<Renderer>(true);
        var b = docent.gameObject.AddComponent<DocentButton>();
        b.kind = DocentButton.Kind.Talk; b.guide = this;
        b.prompt = "[T1]  talk to Haku, the guide";
        IgnorePlayer(col);
    }

    // ------------------------------------------------------------ added: robot body
    // A small, cute white robot about 0.95 m tall (half the robed guide): big glossy helmet
    // head, two large dark eyes with blue glows, dark ear pods, a little body with a glowing
    // chest light, stubby arms and chunky boots. Faces +Z (the visitor), feet at local y = 0.
    void BuildRobotDocent()
    {
        docent = new GameObject("Museum Guide (Haku)").transform;

        var white = Solid(new Color(0.95f, 0.95f, 0.97f));
        var grey  = Solid(new Color(0.55f, 0.57f, 0.60f));
        var dark  = Solid(new Color(0.10f, 0.10f, 0.12f), 0.6f);
        var eye   = Solid(new Color(0.06f, 0.06f, 0.08f), 0f);
        var glow  = Solid(new Color(0.35f, 0.75f, 1.00f), 0f, new Color(0.20f, 0.55f, 1.00f));

        // boots and legs
        foreach (float sx in new[] { -1f, 1f })
        {
            string s = sx < 0 ? "L" : "R";
            Part(docent, PrimitiveType.Cube,    "Sole" + s, new Vector3(sx * 0.075f, 0.010f, 0.020f), new Vector3(0.10f, 0.020f, 0.15f), dark);
            Part(docent, PrimitiveType.Sphere,  "Boot" + s, new Vector3(sx * 0.075f, 0.045f, 0.020f), new Vector3(0.11f, 0.08f, 0.15f), white);
            Part(docent, PrimitiveType.Capsule, "Leg"  + s, new Vector3(sx * 0.070f, 0.140f, 0f),     new Vector3(0.085f, 0.065f, 0.085f), white);
            Part(docent, PrimitiveType.Sphere,  "Knee" + s, new Vector3(sx * 0.070f, 0.125f, 0.035f), new Vector3(0.045f, 0.045f, 0.025f), dark);
        }

        // body
        Part(docent, PrimitiveType.Sphere,   "Hips",       new Vector3(0f, 0.225f, 0f),      new Vector3(0.17f, 0.07f, 0.13f), dark);
        Part(docent, PrimitiveType.Sphere,   "Torso",      new Vector3(0f, 0.310f, 0f),      new Vector3(0.22f, 0.22f, 0.17f), white);
        Part(docent, PrimitiveType.Sphere,   "ChestLight", new Vector3(-0.045f, 0.335f, 0.080f), new Vector3(0.030f, 0.030f, 0.015f), glow);
        Part(docent, PrimitiveType.Cylinder, "Neck",       new Vector3(0f, 0.420f, 0f),      new Vector3(0.12f, 0.015f, 0.10f), dark);

        // stubby arms
        foreach (float sx in new[] { -1f, 1f })
        {
            string s = sx < 0 ? "L" : "R";
            Part(docent, PrimitiveType.Sphere,  "Shoulder" + s, new Vector3(sx * 0.130f, 0.380f, 0f),    new Vector3(0.060f, 0.060f, 0.060f), dark);
            Part(docent, PrimitiveType.Capsule, "Arm"      + s, new Vector3(sx * 0.150f, 0.330f, 0f),    new Vector3(0.055f, 0.045f, 0.055f), white);
            Part(docent, PrimitiveType.Sphere,  "Hand"     + s, new Vector3(sx * 0.155f, 0.265f, 0.010f), new Vector3(0.050f, 0.050f, 0.040f), dark);
        }

        // big round head
        const float hy = 0.620f;
        Part(docent, PrimitiveType.Sphere, "Head", new Vector3(0f, hy, 0f), new Vector3(0.42f, 0.38f, 0.38f), white);
        foreach (float sx in new[] { -1f, 1f })
        {
            string s = sx < 0 ? "L" : "R";
            Part(docent, PrimitiveType.Sphere, "Eye" + s,     new Vector3(sx * 0.080f, hy - 0.030f, 0.165f), new Vector3(0.110f, 0.120f, 0.060f), eye);
            Part(docent, PrimitiveType.Cube,   "EyeGlow" + s, new Vector3(sx * 0.080f, hy - 0.005f, 0.193f), new Vector3(0.050f, 0.012f, 0.010f), glow);
            var ear = Part(docent, PrimitiveType.Cylinder, "Ear" + s, new Vector3(sx * 0.205f, hy, 0f), new Vector3(0.12f, 0.020f, 0.12f), dark);
            ear.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            var cap = Part(docent, PrimitiveType.Cylinder, "EarCap" + s, new Vector3(sx * 0.222f, hy, 0f), new Vector3(0.075f, 0.006f, 0.075f), grey);
            cap.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        HeadHeight = hy;

        // name tag just above the head (text faces the visitor, see BuildDocent)
        var tag = new GameObject("Tag").transform;
        tag.SetParent(docent, false);
        tag.localPosition = new Vector3(0f, 0.86f, 0f);
        tag.localRotation = Quaternion.Euler(0f, 180f, 0f);
        Text(tag, "Haku - guide", 0.04f, TextAnchor.LowerCenter, TextAlignment.Center, Vector3.zero, new Color(1f, 0.9f, 0.6f));

        // the whole robot is one "talk to me" target
        var col = docent.gameObject.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.42f, 0f); col.radius = 0.22f; col.height = 0.84f;
        bodyCollider   = col;
        bodyRenderers  = docent.GetComponentsInChildren<Renderer>(true);
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
        var bgCol = bg.AddComponent<BoxCollider>();
        bgCol.center = new Vector3(0f, 0f, 0.02f);      // just BEHIND the buttons (their faces are at z = -0.015)
        bgCol.size   = new Vector3(1.04f, 1.04f, 0.01f);
        IgnorePlayer(bgCol);
        var bgBtn = bg.AddComponent<DocentButton>();
        bgBtn.kind = DocentButton.Kind.Panel; bgBtn.guide = this;   // no prompt, Press ignores it

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

        Text(panel, "or hold [A] and ask me anything out loud", 0.03f, TextAnchor.MiddleCenter, TextAlignment.Center,
             new Vector3(0f, -H * 0.5f + 0.05f, -0.01f), new Color(1f, 0.85f, 0.45f));
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
        box.size = new Vector3(1f, 1f, 0.02f);          // thin: a deep box makes slanted rays pick the neighbouring button
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

        // Until head tracking is live the camera sits at the rig's floor: placing Haku then would put
        // him somewhere arbitrary (e.g. dead centre in front of the entry door). Wait, hidden.
        if (hp.y - floorY < 0.6f) { if (!hidden) Hide(); return; }

        // Re-plan ten times a second: where should it stand, and is where it stands still OK?
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.1f;
            hasSpot = FindSpot(out spot);
            hereOk  = SpotFree(feet) && LineClear(hp, feet + Vector3.up * 1.2f);
            panelBlocked = open && !hidden && hereOk && !PanelFree(feet, hp);
        }

        // A model load has been running for a second: switch an idle panel to "waking up".
        if (open && idle && !wakingShown && LoadingSlowly()) { wakingShown = true; ShowIdle(); }

        // Ollama wasn't reachable: keep trying quietly (a refused connection fails at once).
        if (warmProblem != null && !warming && !busy && Time.time >= nextRetry)
        {
            nextRetry = Time.time + 10f;
            StartCoroutine(WarmUp());
        }

        Vector3 toGuide = new Vector3(dp.x - hp.x, 0f, dp.z - hp.z);
        float dist = toGuide.magnitude;

        if (!hasSpot)
        {
            // No room anywhere in front of you (e.g. right up against a wall or a closed door):
            // rather than stand in your face, or behind the wall, step out of sight until there is.
            if (!hidden && (!hereOk || dist < 0.9f)) Hide();
        }
        else if (hidden || !hereOk)
        {
            // Back from hiding, or standing somewhere it must not be (a door swung into it, a wall
            // is now between you): re-appear at the free spot rather than in, or behind, the wall.
            Appear(spot);
        }
        else
        {
            // Lazy follow, like a headset menu: while it is still comfortably in view it stays
            // exactly where it is - so small head movements (or its ideal spot flipping sides next
            // to a plinth) never move it, and the open panel holds still while you aim at it.
            // It moves to the planned spot only when it drifts out of view or gets too close or
            // too far - and never chases a spot it is already standing on (a wide spot in a
            // tight corner may itself be further off-centre than the comfort angle).
            Vector3 look = head.forward; look.y = 0f;
            Vector3 toSpot = new Vector3(spot.x - hp.x, 0f, spot.z - hp.z);
            bool lookOk = look.sqrMagnitude > 0.001f;
            float off     = (lookOk && dist > 0.01f) ? Vector3.Angle(look, toGuide) : 0f;
            float spotOff = (lookOk && toSpot.sqrMagnitude > 0.01f) ? Vector3.Angle(look, toSpot) : 0f;
            float limit   = Mathf.Max(open ? openComfortAngle : comfortAngle, spotOff + 10f);
            float fromSpot = new Vector2(feet.x - spot.x, feet.z - spot.z).magnitude;
            // Open panel: keep it at a readable distance - but only move for that if there is a
            // further spot to go to (otherwise it would hop between two equally close ones).
            bool badDistance  = dist < 0.8f || dist > followDistance + 1f ||
                                (open && dist < 1.2f && toSpot.magnitude > dist + 0.2f);
            bool uncomfortable = off > limit || badDistance || panelBlocked;
            // A spot that is itself out of view (a tight corner) is not worth chasing for comfort.
            bool worthIt = spotOff <= viewAngle || badDistance || panelBlocked;
            if (uncomfortable && fromSpot > 0.15f && worthIt) catchingUp = true;

            if (catchingUp)
            {
                if (!PathClear(feet, spot) || PassesNear(feet, spot, hp, 0.6f))
                {
                    // A wall, door or plinth is in the way, or the way runs through you: never glide
                    // through it. Pop across - but only once it has really left your view (or is too
                    // close/far); while you can still see it, it just stays put. This is what stops
                    // it hopping over a plinth every time you glance around an exhibit.
                    if (off > viewAngle || badDistance || panelBlocked) Appear(spot);
                    else { catchingUp = false; followVel = Vector3.zero; }
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
        Vector3 face = head.position - docent.position; face.y = 0f;
        if (face.sqrMagnitude > 0.01f) docent.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
        catchingUp = false;
        followVel  = Vector3.zero;
        hereOk     = true;
        appearT    = 0f;
        if (hidden)
        {
            hidden = false;
            foreach (var r in bodyRenderers) if (r != null) r.enabled = true;
            if (bodyCollider != null) bodyCollider.enabled = true;
            if (open) panel.gameObject.SetActive(true);
        }
    }

    /// Out of sight and out of the way (renderers and collider off - the AudioSource keeps playing).
    void Hide()
    {
        hidden     = true;
        catchingUp = false;
        followVel  = Vector3.zero;
        foreach (var r in bodyRenderers) if (r != null) r.enabled = false;
        if (bodyCollider != null) bodyCollider.enabled = false;
        if (open) panel.gameObject.SetActive(false);
    }

    void SetStatusLine(string s)
    {
        if (statusLine == null || ReferenceEquals(s, statusLineShown)) return;
        statusLineShown = s;
        statusLine.gameObject.SetActive(s != null);
        if (s != null) statusLine.text = Wrap(s, 44, 3);
    }

    /// Does the straight line between two floor points pass within `radius` of the visitor's head?
    static bool PassesNear(Vector3 a, Vector3 b, Vector3 headPos, float radius)
    {
        Vector2 p = new Vector2(headPos.x, headPos.z), s = new Vector2(a.x, a.z), e = new Vector2(b.x, b.z);
        Vector2 d = e - s;
        float t = d.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - s, d) / d.sqrMagnitude) : 0f;
        return (s + d * t - p).sqrMagnitude < radius * radius;
    }

    void LateUpdate()
    {
        if (panel == null || head == null) return;
        if (!open) { SetStatusLine(null); return; }

        Vector3 toHead = head.position - docent.position; toHead.y = 0f;
        if (toHead.sqrMagnitude < 0.01f) return;
        Vector3 n = toHead.normalized;

        // The panel goes with Haku: hidden while he is hidden or out of view (e.g. you are right up
        // against a wall), so it never pops up beside your head. Then "Listening..." and the like
        // appear as a short line in front of you instead; answers are still spoken.
        Vector3 look = head.forward; look.y = 0f;
        float edge = panel.gameObject.activeSelf ? viewAngle + 5f : viewAngle - 5f;    // no blinking at the edge
        bool inView = look.sqrMagnitude < 0.001f || Vector3.Angle(look, -n) <= edge;
        bool show = !hidden && inView;
        if (panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
        bool inProgress = busy || (voice != null && (voice.IsRecording || voice.IsBusy));
        SetStatusLine(show || idle || !inProgress ? null : statusText);   // Listening / one moment / thinking only
        if (!show) return;

        panel.position = new Vector3(docent.position.x, FloorY() + PanelHeight, docent.position.z) + n * PanelOut;
        panel.rotation = Quaternion.LookRotation(-n, Vector3.up);   // +Z away from the visitor
    }

    float FloorY() { return rig != null ? rig.position.y : 0f; }

    // ------------------------------------------------------------ where it can stand

    // Places to try, as (metres ahead, metres to the side) of the visitor, best first. All are in
    // front, so it never ends up behind you. Comfortable spots (within ~30 deg of where you look)
    // come first; the wider ones are for plinths, tight corridors, doorways and corners.
    static readonly Vector2[] Comfy = { new Vector2(0f, 0f) /* followDistance, sideOffset */, new Vector2(1.4f, 0.5f),
                                        new Vector2(1.1f, 0.6f), new Vector2(2.2f, 0.4f), new Vector2(0.9f, 0.3f) };
    static readonly Vector2[] Near  = { new Vector2(0.9f, 0.8f) };     // ~42 deg: still in view
    static readonly Vector2[] Tight = { new Vector2(0.5f, 1.0f) };     // ~63 deg: last resort for tight corners

    /// Finds a spot in front of the visitor where the guide's whole body fits, there is floor
    /// under it, and the visitor can see it (no wall in between). It keeps to the side it is
    /// already on: every comfortable spot on that side, and its nearer wide spot, are tried before
    /// any on the other side, so looking around next to a plinth or in a corridor doesn't make it
    /// hop across.
    bool FindSpot(out Vector3 result)
    {
        Vector3 hp = head.position;
        var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
        Vector3 fwd = yaw * Vector3.forward, right = yaw * Vector3.right;
        Vector3 basePos = new Vector3(hp.x, FloorY(), hp.z);

        float side = 1f;                                           // right by default (and before it is placed)
        if (docent != null && docent.position.sqrMagnitude > 0.0001f &&
            Vector3.Dot(docent.position - basePos, right) < 0f) side = -1f;

        Comfy[0] = new Vector2(followDistance, sideOffset);

        // Own side first - including its nearer, wider spot - before hopping across to the other
        // side (which next to a plinth means a jump across it); the tight spots come last.
        // With the panel open, first look only at the further spots so the panel isn't in your face.
        for (int pass = open ? 0 : 1; pass < 2; pass++)
        {
            for (int group = 0; group < 6; group++)
            {
                Vector2[] list = group < 2 ? Comfy : group < 4 ? Near : Tight;
                float sgn = (group % 2 == 0) ? side : -side;
                foreach (var s in list)
                {
                    if (pass == 0 && s.x < 1.35f) continue;
                    Vector3 c = basePos + fwd * s.x + right * (s.y * sgn);
                    if (!LineClear(hp, c + Vector3.up * 1.2f) || !SpotFree(c)) continue;
                    if (pass == 0 && !PanelFree(c, hp)) continue;           // the open panel must not cut through anything
                    result = c;
                    return true;
                }
            }
        }
        result = basePos;
        return false;
    }

    /// With the panel open: would the panel (which hangs between Haku and you) fit if Haku stood
    /// here, without cutting through a plinth, an exhibit, a wall or a door?
    bool PanelFree(Vector3 feet, Vector3 hp)
    {
        Vector3 toHead = new Vector3(hp.x - feet.x, 0f, hp.z - feet.z);
        if (toHead.sqrMagnitude < 0.01f) return false;
        Vector3 n = toHead.normalized;
        Vector3 centre = new Vector3(feet.x, FloorY() + PanelHeight, feet.z) + n * PanelOut;
        int k = Physics.OverlapBoxNonAlloc(centre, new Vector3(0.67f, 0.67f, 0.03f), overlap,
                                           Quaternion.LookRotation(-n, Vector3.up), ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < k; i++)
            if (Blocks(overlap[i])) return false;
        return true;
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

        // Real floor just below (ignoring the guide's own body, e.g. while it pops back in).
        int m = Physics.RaycastNonAlloc(new Vector3(feet.x, y + 1f, feet.z), Vector3.down, buf, 1.6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < m; i++)
            if (Blocks(buf[i].collider) && buf[i].distance < best) best = buf[i].distance;
        if (best == float.MaxValue) return false;
        return (y + 1f - best) < y + 0.3f;
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
        int n = Physics.CapsuleCastNonAlloc(a, b, BodyRadius + 0.05f, d / len, buf, len, ~0, QueryTriggerInteraction.Ignore);
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
        // A thin sphere rather than a ray, so the crack between two closed door leaves doesn't count.
        int n = Physics.SphereCastNonAlloc(a, 0.08f, d / len, buf, len, ~0, QueryTriggerInteraction.Ignore);
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
                bool listening = voice != null && (voice.IsRecording || voice.IsBusy);
                if (!busy && !listening) StartCoroutine(Ask(b.question));
                break;
        }
    }

    void SetOpen(bool value)
    {
        open = value;
        panel.gameObject.SetActive(value && !hidden);              // LateUpdate keeps it in step with Haku
        if (!value) return;

        if (warmProblem != null && !warming) nextRetry = 0f;      // try Ollama again right away
        bool listening = voice != null && (voice.IsRecording || voice.IsBusy);
        if (!busy && !listening) ShowIdle();                      // otherwise a status is on show
    }

    /// What the panel shows when nothing is happening: a problem if Ollama can't be reached,
    /// "waking up" while the model loads, otherwise the last answer or the greeting.
    string IdleText()
    {
        if (LoadingSlowly())     return Waking;                 // Ollama is there and the model is loading
        if (warmProblem != null) return "Sorry - I can't reach my thinking cap right now: " + warmProblem;
        if (!ready)              return Waking;                 // first load of this Play
        return lastAnswer ?? Greeting;
    }

    // Only say "waking up" once a load has taken over a second - a quick failure (e.g. model not
    // installed) then never flashes "waking up" over its error message.
    bool LoadingSlowly() { return warming && probeOk && (warmProblem == null || unreachableProblem) && Time.time - loadStart > 1f; }

    void ShowIdle()             { idle = true;  SetReply(IdleText()); }
    void ShowStatus(string s)   { idle = false; statusText = s; SetReply(s); }

    void SetReply(string text)
    {
        if (replyText != null) replyText.text = Wrap(text, 44, 12);   // 12 lines x ~0.046 m fit between y=+0.50 and the buttons at -0.07
    }

    // ============================================================ Ollama

    string Url(string path) { return ollamaUrl.TrimEnd('/') + path; }

    /// Loads the model into memory now, so the first real question is not a 30+ second wait.
    IEnumerator WarmUp()
    {
        warming = true;

        // 1) Is Ollama there at all? (quick)  2) Load the model (up to ~40 s from cold).
        using (var probe = UnityWebRequest.Get(Url("/api/version")))
        {
            probe.timeout = 5;
            yield return probe.SendWebRequest();
            if (probe.result != UnityWebRequest.Result.Success)
            {
                warmProblem = "Ollama is not answering at " + ollamaUrl + " (" + probe.error + "). Is it running?";
                unreachableProblem = true;
                nextRetry = Time.time + 10f;
                Debug.LogWarning("[MuseumGuide] " + warmProblem);
                warming = false;
                if (open && idle) ShowIdle();
                yield break;
            }
        }
        probeOk     = true;                                        // reachable: the panel says "waking up" if the load is slow
        loadStart   = Time.time;
        wakingShown = false;

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
                unreachableProblem = www.result == UnityWebRequest.Result.ConnectionError;
                nextRetry = Time.time + 10f;                       // not straight away after a slow failure
                Debug.LogWarning("[MuseumGuide] Ollama warm-up failed: " + warmProblem);
            }
        }
        warming = false;
        probeOk = false;
        if (open && idle) ShowIdle();                              // greeting/answer, or a fresh problem - never a stale one
    }

    IEnumerator Ask(string question, bool spoken = false)
    {
        busy = true;
        string shortQ = question.Length > 34 ? question.Substring(0, 34).TrimEnd() + "..." : question;   // You: "...": <= 44 chars
        string asked = spoken ? "You: \"" + shortQ + "\"\n" : "";     // one line, so the answer fits
        ShowStatus(asked + "Hmm, let me think...");

        var msgs = new List<Msg> { new Msg("system", SystemPrompt) };
        msgs.AddRange(history);
        msgs.Add(new Msg("user", "CONTEXT:\n" + BuildContext() + "\n\nVISITOR: " + question));

        var req = new ChatReq
        {
            model = model, messages = msgs.ToArray(), stream = false, think = false, keep_alive = keepAlive,
            options = new Opts { num_predict = maxTokens, temperature = temperature },
        };

        string answer = null, problem = null;
        bool unreachable = false;
        using (var www = Post("/api/chat", JsonUtility.ToJson(req), 180))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                problem = Explain(www);
                unreachable = www.result == UnityWebRequest.Result.ConnectionError;   // vs. Ollama answered with an error
            }
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
            warmProblem = null;                                  // it clearly works now
            lastAnswer = asked + answer;
            ShowIdle();
            if (voice != null) voice.Speak(answer);
        }
        else
        {
            Debug.LogWarning("[MuseumGuide] " + problem);
            if (unreachable)
            {
                // The panel says Ollama can't be reached, and it is retried in the background
                // until it works again (then the greeting comes back - no stale answer).
                warmProblem = problem;
                unreachableProblem = true;
                lastAnswer  = null;
                nextRetry   = Time.time + 2f;
            }
            else
            {
                // Ollama answered with an error, or the reply was unusable: say so until the next question.
                lastAnswer = asked + "Sorry, I lost my train of thought: " + (problem ?? "no answer") + ".";
            }
            ShowIdle();
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
            if (it.transform.parent != null && it.transform.parent.GetComponentInParent<Inspectable>() != null) continue;   // a part, e.g. the jar's lid
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

    /// Word-wrap to `width` characters (keeping the text's own line breaks) and keep at most `maxLines` lines.
    static string Wrap(string s, int width, int maxLines)
    {
        var sb = new StringBuilder();
        int lines = 0;
        foreach (var para in s.Split('\n'))
        {
            if (++lines > maxLines) { sb.Append("..."); break; }
            if (lines > 1) sb.Append('\n');
            int col = 0;
            bool cut = false;
            foreach (var w in para.Split(' '))
            {
                if (col + w.Length > width && col > 0)
                {
                    if (++lines > maxLines) { sb.Append("..."); cut = true; break; }
                    sb.Append('\n'); col = 0;
                }
                else if (col > 0) { sb.Append(' '); col++; }
                sb.Append(w);
                col += w.Length;
            }
            if (cut) break;
        }
        return sb.ToString();
    }
}
