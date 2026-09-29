using System;
using System.Collections.Generic;
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
//   Trigger      (either hand, pointing at it)
//                  Inspectable  -> pick it up; turn it with your wrist
//                  DoorInteract -> open / close
//                  teleport pad -> jump there, facing the exhibit
//   Trigger or Grip while holding -> put it back
//   X (left)     show / hide the small debug readout in front of your face
//
// Each controller is read TWO independent ways and the results are merged:
//   1. Input System  <XRController>{LeftHand}/...   (gives the proper aim/pointer pose)
//   2. UnityEngine.XR.InputDevices                   (works whatever layouts are registered)
// so if one path is missing on a given runtime (Quest Link vs. standalone Quest) the other
// still drives the hand.

[DisallowMultipleComponent]
public class VRInteractor : MonoBehaviour
{
    public float reach       = 4f;
    public float moveSpeed   = 2f;
    public float snapAngle   = 30f;
    public float padSnapDist = 0.5f;   // aim this close to a pad and it still counts
    public bool  showDebug   = true;   // readout in front of the face; X button toggles

    const float Press = 0.6f;

    // ============================================================ hand

    class Hand
    {
        public string     name;
        public XRNode     node;
        public Transform  root;
        public LineRenderer ray;
        public InputAction pos, rot, pointPos, pointRot, trigger, grip, stick;
        public XRDevice dev;
        float nextDeviceLookup;

        public bool  present;              // some source is giving us this controller
        public bool  viaInputSystem, viaXrDevice;
        public float trig, gripVal;
        public Vector2 stickVal;
        public bool  triggerDown, gripDown, primaryDown;
        float prevTrig, prevGrip; bool prevPrimary;

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
            if (root != null) root.gameObject.SetActive(present && havePose);

            triggerDown = trig >= Press && prevTrig < Press;
            gripDown    = gripVal >= Press && prevGrip < Press;
            primaryDown = xrPrimary && !prevPrimary;
            prevTrig = trig; prevGrip = gripVal; prevPrimary = xrPrimary;
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
    Transform[] pads = new Transform[0];
    string    lastError = "";
    string    rayInfoL = "-", rayInfoR = "-";

    Inspectable held;
    Hand        heldBy;
    Transform   homeParent;
    Vector3     homePos;
    Quaternion  homeRot;
    Collider[]  heldColliders;

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

        try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }

        var padRoot = GameObject.Find("TeleportAnchors");
        if (padRoot != null)
        {
            var list = new List<Transform>();
            foreach (Transform c in padRoot.transform) list.Add(c);
            pads = list.ToArray();
        }

        // Reuse a material already in the scene (the glowing pad one) so its shader is
        // guaranteed to be in the build; Shader.Find alone can be stripped on device.
        Renderer src = pads.Length > 0 ? pads[0].GetComponent<Renderer>() : null;
        if (src == null) src = FindFirstObjectByType<MeshRenderer>();
        if (src != null && src.sharedMaterial != null) tint = new Material(src.sharedMaterial);
        else
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            tint = new Material(s);
        }

        Transform parent = head.parent != null ? head.parent : rig;
        left  = MakeHand(parent, "LeftHand",  XRNode.LeftHand,  "Left Controller");
        right = MakeHand(parent, "RightHand", XRNode.RightHand, "Right Controller");
        label = MakeText("VR Label", rig, 0.01f, 48, TextAnchor.LowerCenter);
        hud   = MakeText("VR Debug",  head, 0.0035f, 48, TextAnchor.UpperLeft);
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
        h.ray.sharedMaterial = Tinted(Color.white);
        return h;
    }

    TextMesh MakeText(string name, Transform parent, float size, int fontSize, TextAnchor anchor)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        var tm = g.AddComponent<TextMesh>();
        tm.characterSize = size;
        tm.fontSize      = fontSize;
        tm.anchor        = anchor;
        tm.alignment     = anchor == TextAnchor.UpperLeft ? TextAlignment.Left : TextAlignment.Center;
        tm.color         = Color.white;
        if (font != null) { tm.font = font; g.GetComponent<Renderer>().sharedMaterial = font.material; }
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
                else
                {
                    prompt   = Wrap(held.displayName + "\n" + held.description, 34) +
                               "\n\nTrigger or grip: put it back";
                    promptAt = held.transform.position + Vector3.up * 0.3f;
                }
                left.ray.enabled = right.ray.enabled = false;
            }
            else
            {
                string pl, pr; Vector3 al, ar;
                rayInfoL = Interact(left,  out pl, out al);
                rayInfoR = Interact(right, out pr, out ar);
                if (pr != null)      { prompt = pr; promptAt = ar; }
                else if (pl != null) { prompt = pl; promptAt = al; }
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

    // ============================================================ interaction

    /// Returns a short description of what the ray is on (for the debug readout).
    string Interact(Hand h, out string prompt, out Vector3 at)
    {
        prompt = null; at = Vector3.zero;
        if (!h.root.gameObject.activeSelf) return "no pose";

        Vector3 o = h.root.position, d = h.root.forward, end = o + d * reach;
        Inspectable  item = null;
        DoorInteract door = null;
        Transform    arrive = null;
        Color col = Color.white;
        string seen = "nothing";

        // Nearest hit that is not the rig's own walking capsule.
        RaycastHit hit = default(RaycastHit);
        bool got = false;
        var hits = Physics.RaycastAll(o, d, reach, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var x in hits)
        {
            if (x.collider == cc) continue;
            if (x.collider.transform.IsChildOf(rig)) continue;
            if (x.distance < best) { best = x.distance; hit = x; got = true; }
        }

        if (got)
        {
            end = hit.point;
            seen = hit.collider.name;
            item = hit.collider.GetComponentInParent<Inspectable>();
            if (item == null) door = hit.collider.GetComponentInParent<DoorInteract>();
            if (item == null && door == null) arrive = PadNear(hit);
        }

        if (item != null)        { prompt = "Trigger: inspect " + item.displayName; col = Color.green; seen += " [item]"; }
        else if (door != null)   { prompt = "Trigger: " + door.Prompt();            col = Color.green; seen += " [door]"; }
        else if (arrive != null) { prompt = "Trigger: teleport here";               col = new Color(0.3f, 0.8f, 1f); seen += " [pad]"; }
        at = end;

        h.ray.enabled = true;
        h.ray.SetPosition(0, o);
        h.ray.SetPosition(1, end);
        Paint(h.ray.sharedMaterial, col);

        if (h.triggerDown)
        {
            if (item != null)        Pick(h, item);
            else if (door != null)   door.Toggle();
            else if (arrive != null) TeleportTo(arrive);
        }
        return seen;
    }

    /// The pad itself, or the nearest pad to a floor hit - pads are small and a ray
    /// that lands 20 cm off should still count.
    Transform PadNear(RaycastHit hit)
    {
        Transform t = hit.collider.transform;
        if (t.parent != null && t.parent.name == "TeleportAnchors") return t.Find("Arrive");

        Transform best = null;
        float bestD = padSnapDist;
        foreach (var p in pads)
        {
            Vector3 dv = p.position - hit.point; dv.y = 0f;
            if (dv.magnitude < bestD) { bestD = dv.magnitude; best = p; }
        }
        return best != null ? best.Find("Arrive") : null;
    }

    void TeleportTo(Transform arrive)
    {
        float yaw = Mathf.DeltaAngle(head.eulerAngles.y, arrive.eulerAngles.y);
        cc.enabled = false;
        rig.RotateAround(head.position, Vector3.up, yaw);
        Vector3 d = arrive.position - head.position;
        d.y = 0f;
        rig.position += d;
        cc.enabled = true;
    }

    void Pick(Hand h, Inspectable item)
    {
        held   = item;
        heldBy = h;

        var t = item.transform;
        homeParent = t.parent;
        homePos    = t.position;
        homeRot    = t.rotation;

        heldColliders = item.GetComponentsInChildren<Collider>();
        foreach (var c in heldColliders) c.enabled = false;

        t.SetParent(h.root, true);
        t.localPosition    = new Vector3(0f, 0f, Mathf.Max(0.35f, item.holdDistance * 0.6f));
        t.localEulerAngles = item.holdRotation;
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

        // capsule tracks the head so you cannot walk your head through a wall
        Vector3 hl = rig.InverseTransformPoint(head.position);
        float h = Mathf.Clamp(hl.y, 0.6f, 2.2f);
        cc.height = h;
        cc.center = new Vector3(hl.x, h * 0.5f + cc.skinWidth, hl.z);

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
        if (string.IsNullOrEmpty(text)) { label.gameObject.SetActive(false); return; }

        label.gameObject.SetActive(true);
        label.text = text;

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

    void UpdateHud()
    {
        if (hud == null) return;
        hud.gameObject.SetActive(showDebug);
        if (!showDebug) return;
        hud.text =
            "VRInteractor (X = hide)\n" +
            "L " + Hs(left)  + "\n  ray: " + rayInfoL + "\n" +
            "R " + Hs(right) + "\n  ray: " + rayInfoR + "\n" +
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
