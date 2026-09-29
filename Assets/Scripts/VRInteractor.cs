using System;
using UnityEngine;
using UnityEngine.InputSystem;
using XRDevice       = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRUsages       = UnityEngine.XR.CommonUsages;
using XRNode         = UnityEngine.XR.XRNode;

// Put this in:  Assets/Scripts/VRInteractor.cs
//
// The headset counterpart of InspectController (keyboard + mouse only, lives on the
// desktop Player, so it does nothing in VR). RigSelector adds this to the XR rig when
// a headset is running - there is nothing to wire up in the scene.
//
//   Left stick   walk (relative to where you are looking)
//   Right stick  snap turn
//   T1 = trigger, G1 = grip, X = left X button (the tags shown in the on-screen prompts)
//   T1           (either hand, pointing at it)
//                  Inspectable  -> pick it up; turn it with your wrist
//                  DoorInteract -> open / close
//   T1 or G1 while holding -> put it back
//   X (left)     show / hide the small debug readout (includes fps)
//
// Each controller is read TWO independent ways and the results are merged:
//   1. Input System  <XRController>{LeftHand}/...   (gives the proper aim/pointer pose)
//   2. UnityEngine.XR.InputDevices                   (works whatever layouts are registered)
//
// PERFORMANCE: this runs every frame on a phone-class CPU/GPU, so the per-frame path
// allocates nothing (NonAlloc raycasts, cached strings, TextMesh rebuilt only when its
// text changes, debug readout refreshed four times a second).

[DisallowMultipleComponent]
public class VRInteractor : MonoBehaviour
{
    public float reach     = 4f;
    public float moveSpeed = 2f;
    public float snapAngle = 30f;
    public bool  showDebug = true;   // readout in front of the face; X button toggles

    const float Press = 0.6f;

    // ============================================================ hand

    class Hand
    {
        public string     name;
        public XRNode     node;
        public Transform  root;
        public LineRenderer ray;
        public Material   rayMat;
        public Color      rayColor = Color.white;
        public InputAction pos, rot, pointPos, pointRot, trigger, grip, stick;
        public XRDevice   dev;
        float nextDeviceLookup;

        public bool  present;              // some source is giving us this controller
        public bool  viaInputSystem, viaXrDevice;
        public float trig, gripVal;
        public Vector2 stickVal;
        public bool  triggerDown, gripDown, primaryDown;
        float prevTrig, prevGrip; bool prevPrimary;

        // what the ray is on; the prompt string is cached so nothing allocates per frame
        public Collider     seenCol;
        public Inspectable  item;
        public DoorInteract door;
        public bool   doorWasOpen;
        public string prompt;
        public Vector3 aimPoint;

        public static InputAction Make(string n, string type, params string[] paths)
        {
            var a = new InputAction(n, InputActionType.Value, expectedControlType: type);
            foreach (var p in paths) a.AddBinding(p);
            a.Enable();
            return a;
        }

        public void CreateActions(string usage)
        {
            string d = "<XRController>{" + usage + "}/";
            pos      = Make(name + " pos",      "Vector3",    d + "devicePosition");
            rot      = Make(name + " rot",      "Quaternion", d + "deviceRotation");
            pointPos = Make(name + " pointPos", "Vector3",    d + "pointerPosition");
            pointRot = Make(name + " pointRot", "Quaternion", d + "pointerRotation");
            trigger  = Make(name + " trigger",  "Axis",       d + "trigger");
            grip     = Make(name + " grip",     "Axis",       d + "grip");
            stick    = Make(name + " stick",    "Vector2",    d + "thumbstick", d + "joystick");
        }

        public void Dispose()
        {
            foreach (var a in new[] { pos, rot, pointPos, pointRot, trigger, grip, stick })
                if (a != null) a.Dispose();
        }

        static bool Has(InputAction a) { return a != null && a.controls.Count > 0; }
        static bool ValidQ(Quaternion q) { return q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > 0.5f; }

        public void Poll()
        {
            // ---- XR device (refresh handle now and then; it goes stale on reconnect)
            if (!dev.isValid && Time.unscaledTime >= nextDeviceLookup)
            {
                nextDeviceLookup = Time.unscaledTime + 0.5f;
                dev = XRInputDevices.GetDeviceAtXRNode(node);
            }
            viaXrDevice = dev.isValid;

            float xrTrig = 0f, xrGrip = 0f; Vector2 xrStick = Vector2.zero;
            bool xrPrimary = false;
            Vector3 xrPos = Vector3.zero; Quaternion xrRot = Quaternion.identity;
            bool xrPose = false;
            if (viaXrDevice)
            {
                dev.TryGetFeatureValue(XRUsages.trigger, out xrTrig);
                dev.TryGetFeatureValue(XRUsages.grip, out xrGrip);
                dev.TryGetFeatureValue(XRUsages.primary2DAxis, out xrStick);
                dev.TryGetFeatureValue(XRUsages.primaryButton, out xrPrimary);
                bool p = dev.TryGetFeatureValue(XRUsages.devicePosition, out xrPos);
                bool r = dev.TryGetFeatureValue(XRUsages.deviceRotation, out xrRot);
                xrPose = p && r && ValidQ(xrRot);
            }

            // ---- Input System
            viaInputSystem = Has(pos) || Has(pointPos) || Has(trigger);
            float isTrig = Has(trigger) ? trigger.ReadValue<float>() : 0f;
            float isGrip = Has(grip)    ? grip.ReadValue<float>()    : 0f;
            Vector2 isStick = Has(stick) ? stick.ReadValue<Vector2>() : Vector2.zero;

            Vector3 p3 = Vector3.zero; Quaternion q = Quaternion.identity; bool havePose = false;
            if (Has(pointPos) && Has(pointRot))
            {
                p3 = pointPos.ReadValue<Vector3>(); q = pointRot.ReadValue<Quaternion>();
                havePose = ValidQ(q);
            }
            if (!havePose && Has(pos) && Has(rot))
            {
                p3 = pos.ReadValue<Vector3>(); q = rot.ReadValue<Quaternion>();
                havePose = ValidQ(q);
            }
            if (!havePose && xrPose) { p3 = xrPos; q = xrRot; havePose = true; }

            present  = viaInputSystem || viaXrDevice;
            trig     = Mathf.Max(isTrig, xrTrig);
            gripVal  = Mathf.Max(isGrip, xrGrip);
            stickVal = isStick.sqrMagnitude > xrStick.sqrMagnitude ? isStick : xrStick;

            if (havePose && root != null) root.SetLocalPositionAndRotation(p3, q);
            bool show = present && havePose;
            if (root != null && root.gameObject.activeSelf != show) root.gameObject.SetActive(show);

            triggerDown = trig >= Press && prevTrig < Press;
            gripDown    = gripVal >= Press && prevGrip < Press;
            primaryDown = xrPrimary && !prevPrimary;
            prevTrig = trig; prevGrip = gripVal; prevPrimary = xrPrimary;
        }

        public void SetRayColor(Color c)
        {
            if (c == rayColor) return;
            rayColor = c;
            Paint(rayMat, c);
        }
    }

    // ============================================================ state

    Transform  rig, head;
    CharacterController cc;
    Hand left, right;
    Material  tint;
    TextMesh  label, hud;
    Font      font;
    bool      turnLatched;
    string    lastError = "";

    readonly RaycastHit[] hitBuf = new RaycastHit[16];

    string labelText = "";
    float  nextHud;
    float  fpsTimer, worstFrame, fps, worstMs;
    int    fpsFrames;

    readonly System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem> displays =
        new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
    float displayHz;
    float nextDisplayHz;

    float   lastCapH = -1f;
    Vector3 lastCapC;

    Inspectable held;
    Hand        heldBy;
    Transform   homeParent;
    Vector3     homePos;
    Quaternion  homeRot;
    Collider[]  heldColliders;
    string      heldText;
    Vector3     heldLocalPos;
    float       heldRadius;

    // ============================================================ setup

    void Awake()
    {
        try { Setup(); }
        catch (Exception e)
        {
            lastError = "setup: " + e.Message;
            Debug.LogException(e);
        }
    }

    void Setup()
    {
        rig = transform;
        var cam = GetComponentInChildren<Camera>(true);
        head = cam != null ? cam.transform : (Camera.main != null ? Camera.main.transform : null);
        if (head == null) throw new Exception("no camera under the XR rig");

        // Walls are solid: walk with a capsule that follows the head, not by moving the root.
        cc = GetComponent<CharacterController>();
        if (cc == null) cc = gameObject.AddComponent<CharacterController>();
        cc.radius = 0.25f;
        cc.stepOffset = 0.3f;
        cc.slopeLimit = 45f;

        font = MuseumText.BuiltinFont();

        // Reuse a material already in the scene so its shader is guaranteed to be in the
        // build; Shader.Find alone can be stripped on device.
        Renderer src = FindAnyObjectByType<MeshRenderer>();
        if (src != null && src.sharedMaterial != null) tint = new Material(src.sharedMaterial);
        else
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            tint = new Material(s);
        }
        tint.mainTexture = null;

        Transform parent = head.parent != null ? head.parent : rig;
        left  = MakeHand(parent, "LeftHand",  XRNode.LeftHand,  "Left Controller");
        right = MakeHand(parent, "RightHand", XRNode.RightHand, "Right Controller");
        label = MakeText("VR Label", rig, 0.01f, 48, TextAnchor.LowerCenter, false);
        label.gameObject.SetActive(false);
        hud   = MakeText("VR Debug",  head, 0.0035f, 48, TextAnchor.UpperLeft, true);
        hud.transform.localPosition = new Vector3(-0.30f, 0.16f, 0.85f);
        hud.transform.localRotation = Quaternion.identity;
        hud.color = new Color(1f, 1f, 0.4f);
    }

    Hand MakeHand(Transform parent, string usage, XRNode node, string name)
    {
        var h = new Hand { name = name, node = node };

        h.root = new GameObject(name).transform;
        h.root.SetParent(parent, false);
        h.root.gameObject.SetActive(false);   // shown once the controller reports a pose

        try { h.CreateActions(usage); }
        catch (Exception e) { lastError = "input actions: " + e.Message; Debug.LogException(e); }

        // stand-in controller model (a small capsule pointing forward)
        var model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        model.name = "Model";
        Destroy(model.GetComponent<Collider>());
        model.transform.SetParent(h.root, false);
        model.transform.localScale = new Vector3(0.035f, 0.055f, 0.035f);
        model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        model.transform.localPosition = new Vector3(0f, 0f, 0.03f);
        model.GetComponent<Renderer>().sharedMaterial = Tinted(new Color(0.85f, 0.85f, 0.9f));

        var rayGo = new GameObject("Ray");
        rayGo.transform.SetParent(h.root, false);
        h.ray = rayGo.AddComponent<LineRenderer>();
        h.ray.positionCount = 2;
        h.ray.startWidth = 0.008f;
        h.ray.endWidth   = 0.004f;
        h.ray.useWorldSpace = true;
        h.ray.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        h.ray.receiveShadows = false;
        h.rayMat = Tinted(Color.white);
        h.ray.sharedMaterial = h.rayMat;
        return h;
    }

    TextMesh MakeText(string name, Transform parent, float size, int fontSize, TextAnchor anchor, bool overlay)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        var tm = g.AddComponent<TextMesh>();
        tm.characterSize = size;
        tm.fontSize      = fontSize;
        tm.anchor        = anchor;
        tm.alignment     = anchor == TextAnchor.UpperLeft ? TextAlignment.Left : TextAlignment.Center;
        tm.color         = Color.white;
        if (font != null) tm.font = font;
        // Unity's default text material ignores depth and shows through walls. The label is
        // depth-tested; the debug readout is head-locked so it stays on top.
        if (overlay) MuseumText.ApplyOverlay(tm); else MuseumText.Apply(tm);
        return tm;
    }

    Material Tinted(Color c)
    {
        var m = new Material(tint);
        Paint(m, c);
        return m;
    }

    static void Paint(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor"))     m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color"))         m.SetColor("_Color", c);
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c);
    }

    void OnDestroy()
    {
        if (left != null)  left.Dispose();
        if (right != null) right.Dispose();
    }

    // ============================================================ frame

    void Update()
    {
        if (left == null || right == null || head == null) return;
        Frame();
        try
        {
            left.Poll();
            right.Poll();
            if (left.primaryDown) showDebug = !showDebug;

            Locomote();

            string prompt = null;
            Vector3 promptAt = Vector3.zero;

            if (held != null)
            {
                if (heldBy.triggerDown || heldBy.gripDown || left.gripDown || right.gripDown) PutBack();
                else { prompt = heldText; promptAt = held.transform.position + Vector3.up * 0.3f; }
                left.ray.enabled = right.ray.enabled = false;
            }
            else
            {
                Interact(left);
                Interact(right);
                if (right.prompt != null)     { prompt = right.prompt; promptAt = right.aimPoint; }
                else if (left.prompt != null) { prompt = left.prompt;  promptAt = left.aimPoint; }
            }

            ShowLabel(prompt, promptAt);
        }
        catch (Exception e)
        {
            lastError = e.GetType().Name + ": " + e.Message;
            Debug.LogException(e);
        }
        UpdateHud();
    }

    /// Frame-rate meter: average fps and the worst single frame over the last second.
    void Frame()
    {
        float dt = Time.unscaledDeltaTime;
        fpsFrames++;
        fpsTimer += dt;
        if (dt > worstFrame) worstFrame = dt;
        if (fpsTimer >= 1f)
        {
            fps = fpsFrames / fpsTimer;
            worstMs = worstFrame * 1000f;
            fpsFrames = 0; fpsTimer = 0f; worstFrame = 0f;
        }
    }

    // ============================================================ interaction

    void Interact(Hand h)
    {
        if (!h.root.gameObject.activeSelf)
        {
            h.prompt = null; h.seenCol = null; h.item = null; h.door = null;
            return;
        }

        Vector3 o = h.root.position, d = h.root.forward, end = o + d * reach;

        // Nearest hit that is not the rig's own walking capsule.
        int n = Physics.RaycastNonAlloc(o, d, hitBuf, reach, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        int bi = -1;
        for (int i = 0; i < n; i++)
        {
            var c = hitBuf[i].collider;
            if (c == cc || c.transform.IsChildOf(rig)) continue;
            if (hitBuf[i].distance < best) { best = hitBuf[i].distance; bi = i; }
        }

        Collider     col  = null;
        Inspectable  item = null;
        DoorInteract door = null;
        if (bi >= 0)
        {
            col = hitBuf[bi].collider;
            end = hitBuf[bi].point;
            if (col != h.seenCol || h.item != null || h.door != null)
            {
                item = col.GetComponentInParent<Inspectable>();
                if (item == null) door = col.GetComponentInParent<DoorInteract>();
            }
            else { item = h.item; door = h.door; }
        }
        h.seenCol = col;

        // Rebuild the prompt string only when the target (or a door's state) changes.
        if (item != h.item || door != h.door || (door != null && door.isOpen != h.doorWasOpen))
        {
            h.item = item; h.door = door;
            h.doorWasOpen = door != null && door.isOpen;
            if (item != null)      h.prompt = "[T1]  inspect " + item.displayName;
            else if (door != null) h.prompt = "[T1]  " + door.Prompt();
        }
        if (item == null && door == null) h.prompt = null;   // never leave a prompt up with nothing under the ray
        h.aimPoint = end;

        h.ray.enabled = true;
        h.ray.SetPosition(0, o);
        h.ray.SetPosition(1, end);
        h.SetRayColor(h.prompt != null ? Color.green : Color.white);

        if (h.triggerDown)
        {
            if (item != null)      Pick(h, item);
            else if (door != null) door.Toggle();
        }
    }

    void Pick(Hand h, Inspectable item)
    {
        held   = item;
        heldBy = h;
        ClearTargets();
        heldText = Wrap(item.displayName + "\n" + item.description, 34) + "\n\n[T1] or [G1]  put it back";

        var t = item.transform;
        homeParent = t.parent;
        homePos    = t.position;
        homeRot    = t.rotation;

        heldColliders = item.GetComponentsInChildren<Collider>();
        foreach (var c in heldColliders) c.enabled = false;

        t.SetParent(h.root, true);
        heldLocalPos       = new Vector3(0f, 0f, Mathf.Max(0.35f, item.holdDistance * 0.6f));
        t.localPosition    = heldLocalPos;
        t.localEulerAngles = item.holdRotation;

        // How far from its centre the item reaches, so the clamp in LateUpdate keeps all of it clear of walls.
        var rs = item.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            heldRadius = Mathf.Clamp(b.extents.magnitude, 0.08f, 0.6f);
        }
        else heldRadius = 0.15f;
    }

    void PutBack()
    {
        var t = held.transform;
        t.SetParent(homeParent, true);
        t.position = homePos;
        t.rotation = homeRot;
        foreach (var c in heldColliders) c.enabled = true;
        held = null;
        heldBy = null;
        ClearTargets();                    // otherwise the old "Trigger: inspect ..." prompt outlives the item
    }

    /// The item rides on the hand, and a hand can go through a wall or below the floor.
    /// After the hand has been posed for the frame, pull the item back inside the museum:
    /// if anything solid is between the head and the item, put it on the head's side of that
    /// surface; and never let it sink into the floor.
    void LateUpdate()
    {
        if (held == null || heldBy == null || head == null) return;

        Vector3 want = heldBy.root.TransformPoint(heldLocalPos);
        Vector3 from = head.position;
        Vector3 dir  = want - from;
        float   dist = dir.magnitude;

        if (dist > 0.001f)
        {
            dir /= dist;
            int n = Physics.RaycastNonAlloc(from, dir, hitBuf, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bi = -1;
            for (int i = 0; i < n; i++)
            {
                var c = hitBuf[i].collider;
                if (c == cc || c.transform.IsChildOf(rig)) continue;
                if (hitBuf[i].distance < best) { best = hitBuf[i].distance; bi = i; }
            }
            if (bi >= 0)
            {
                float margin = Mathf.Min(heldRadius, best * 0.5f);      // never more than half way back to the head
                want = hitBuf[bi].point - dir * margin;
            }
        }

        // Floor: the rig root is floor level. Keep the lowest reach of the item above it.
        float floorY = rig.position.y + Mathf.Min(heldRadius, 0.35f);
        if (want.y < floorY) want.y = floorY;

        held.transform.position = want;
    }

    /// Forget what each ray was on, including the cached prompt text.
    void ClearTargets()
    {
        foreach (var h in new[] { left, right })
        {
            h.item = null; h.door = null; h.seenCol = null; h.prompt = null;
        }
    }

    // ============================================================ locomotion

    void Locomote()
    {
        Vector2 move = left.stickVal;
        Vector2 turn = right.stickVal;

        // snap turn, once per flick
        if (Mathf.Abs(turn.x) > 0.7f)
        {
            if (!turnLatched)
            {
                turnLatched = true;
                cc.enabled = false;
                rig.RotateAround(head.position, Vector3.up, Mathf.Sign(turn.x) * snapAngle);
                cc.enabled = true;
            }
        }
        else if (Mathf.Abs(turn.x) < 0.4f) turnLatched = false;

        // capsule tracks the head so you cannot walk your head through a wall.
        // Only touch the collider when it has actually moved a bit - resizing it every
        // frame makes physics rebuild it for nothing.
        Vector3 hl = rig.InverseTransformPoint(head.position);
        float h = Mathf.Clamp(hl.y, 0.6f, 2.2f);
        Vector3 c = new Vector3(hl.x, h * 0.5f + cc.skinWidth, hl.z);
        if (Mathf.Abs(h - lastCapH) > 0.01f || (c - lastCapC).sqrMagnitude > 0.0001f)
        {
            cc.height = h;
            cc.center = c;
            lastCapH = h; lastCapC = c;
        }

        if (move.sqrMagnitude < 0.04f) return;

        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up);
        fwd.Normalize();
        Vector3 rgt = Vector3.Cross(Vector3.up, fwd);

        Vector3 dir = fwd * move.y + rgt * move.x;
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        cc.Move(dir * moveSpeed * Time.deltaTime);
    }

    // ============================================================ text

    void ShowLabel(string text, Vector3 at)
    {
        if (label == null) return;
        if (string.IsNullOrEmpty(text))
        {
            if (label.gameObject.activeSelf) label.gameObject.SetActive(false);
            return;
        }

        if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
        if (!ReferenceEquals(text, labelText))     // prompts are cached strings, so a reference test is enough
        {
            labelText = text;
            label.text = text;                     // rebuilding a TextMesh is not free - only on change
        }

        Vector3 toHead = head.position - at;
        float dist = Mathf.Clamp(toHead.magnitude, 0.6f, 6f);
        var t = label.transform;
        t.position   = at + Vector3.up * 0.08f;
        t.rotation   = Quaternion.LookRotation(-toHead.normalized, Vector3.up);   // forward points away from the viewer, which is how TextMesh reads
        t.localScale = Vector3.one * Mathf.Clamp(dist / 1.5f, 0.6f, 2.5f);
    }

    static string Hs(Hand h)
    {
        return (h.present ? "on" : "OFF") + " IS:" + (h.viaInputSystem ? "y" : "n") + " XR:" + (h.viaXrDevice ? "y" : "n") +
               "  trig " + h.trig.ToString("0.0") + " grip " + h.gripVal.ToString("0.0") +
               "  stick " + h.stickVal.x.ToString("0.0") + "," + h.stickVal.y.ToString("0.0");
    }

    static string Seen(Hand h)
    {
        if (h.seenCol == null) return "nothing";
        return h.seenCol.name + (h.item != null ? " [item]" : h.door != null ? " [door]" : "");
    }

    void UpdateHud()
    {
        if (hud == null) return;
        if (hud.gameObject.activeSelf != showDebug) hud.gameObject.SetActive(showDebug);
        if (!showDebug || Time.unscaledTime < nextHud) return;
        nextHud = Time.unscaledTime + 0.25f;    // 4 Hz: this string building is the only garbage left

        if (Time.unscaledTime >= nextDisplayHz)
        {
            nextDisplayHz = Time.unscaledTime + 2f;
            displayHz = 0f;
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
            {
                float hz;
                if (d.running && d.TryGetDisplayRefreshRate(out hz)) { displayHz = hz; break; }
            }
        }

        hud.text =
            fps.ToString("0") + " fps of " + (displayHz > 0f ? displayHz.ToString("0") : "?") + " Hz   worst frame " + worstMs.ToString("0.0") + " ms   [X] hide\n" +
            "L " + Hs(left)  + "\n  ray: " + Seen(left) + "\n" +
            "R " + Hs(right) + "\n  ray: " + Seen(right) + "\n" +
            (held != null ? "holding: " + held.displayName + "\n" : "") +
            (lastError.Length > 0 ? "ERR " + lastError : "");
    }

    static string Wrap(string s, int width)
    {
        var outp = new System.Text.StringBuilder();
        foreach (var para in s.Split('\n'))
        {
            int col = 0;
            foreach (var w in para.Split(' '))
            {
                if (col + w.Length > width && col > 0) { outp.Append('\n'); col = 0; }
                else if (col > 0)                       { outp.Append(' '); col++; }
                outp.Append(w);
                col += w.Length;
            }
            outp.Append('\n');
        }
        return outp.ToString().TrimEnd('\n');
    }
}
