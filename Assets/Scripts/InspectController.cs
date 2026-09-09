using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Put this in:  Assets/Scripts/InspectController.cs   (replaces the old one)
// It goes on the PlayerCamera.
//
// Handles two kinds of interaction with the same E key:
//   Inspectable  -> pick it up, turn it over with the mouse, E/Esc to put back
//   DoorInteract -> swing the door open or closed

public class InspectController : MonoBehaviour
{
    public float reach = 3.5f;
    public float rotateSpeed = 4f;

    Camera cam;
    Inspectable  target;       // exhibit the crosshair is over
    DoorInteract doorTarget;   // door the crosshair is over
    Inspectable  held;         // exhibit currently in hand

    Transform  homeParent;
    Vector3    homePos;
    Quaternion homeRot;
    Collider[] heldColliders;
    MonoBehaviour playerMove;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        playerMove = transform.root.GetComponent("SimpleFPS") as MonoBehaviour;
    }

    void Update()
    {
        if (held == null) LookForTarget();
        else              HoldUpdate();
    }

    void LookForTarget()
    {
        target = null;
        doorTarget = null;

        RaycastHit hit;
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out hit,
                            reach, ~0, QueryTriggerInteraction.Ignore))
        {
            target = hit.collider.GetComponentInParent<Inspectable>();
            if (target == null)
                doorTarget = hit.collider.GetComponentInParent<DoorInteract>();
        }

        if (!InteractPressed()) return;

        if (target != null)          Pick(target);
        else if (doorTarget != null) doorTarget.Toggle();
    }

    void HoldUpdate()
    {
        Vector2 d = LookDelta() * rotateSpeed;
        held.transform.Rotate(cam.transform.up,    -d.x, Space.World);
        held.transform.Rotate(cam.transform.right,  d.y, Space.World);

        if (InteractPressed() || CancelPressed()) PutBack();
    }

    void Pick(Inspectable item)
    {
        held = item;

        var t = item.transform;
        homeParent = t.parent;
        homePos    = t.position;
        homeRot    = t.rotation;

        heldColliders = item.GetComponentsInChildren<Collider>();
        foreach (var c in heldColliders) c.enabled = false;

        t.SetParent(cam.transform, true);
        t.localPosition    = new Vector3(0f, 0f, item.holdDistance);
        t.localEulerAngles = item.holdRotation;

        if (playerMove != null) playerMove.enabled = false;
    }

    void PutBack()
    {
        var t = held.transform;
        t.SetParent(homeParent, true);
        t.position = homePos;
        t.rotation = homeRot;

        foreach (var c in heldColliders) c.enabled = true;

        if (playerMove != null) playerMove.enabled = true;
        held = null;
    }

    // ---------------------------------------------------------------- UI

    void OnGUI()
    {
        var mid = new GUIStyle(GUI.skin.label);
        mid.alignment = TextAnchor.MiddleCenter;
        mid.normal.textColor = Color.white;
        mid.fontSize = 17;

        var small = new GUIStyle(mid);
        small.fontSize = 13;
        small.wordWrap = true;

        GUI.Label(new Rect(Screen.width * 0.5f - 10f, Screen.height * 0.5f - 12f, 20f, 24f), "+", mid);

        if (held != null)
        {
            GUI.Label(new Rect(0f, Screen.height - 130f, Screen.width, 26f), held.displayName, mid);
            GUI.Label(new Rect(Screen.width * 0.22f, Screen.height - 100f, Screen.width * 0.56f, 60f),
                      held.description, small);
            GUI.Label(new Rect(0f, Screen.height - 34f, Screen.width, 22f),
                      "move mouse to rotate     ·     E or Esc to put it back", small);
        }
        else if (target != null)
        {
            GUI.Label(new Rect(0f, Screen.height * 0.5f + 26f, Screen.width, 26f),
                      "[ E ]   inspect " + target.displayName, mid);
        }
        else if (doorTarget != null)
        {
            GUI.Label(new Rect(0f, Screen.height * 0.5f + 26f, Screen.width, 26f),
                      "[ E ]   " + doorTarget.Prompt(), mid);
        }
    }

    // ------------------------------------------- input, either system

#if ENABLE_LEGACY_INPUT_MANAGER
    bool    InteractPressed() { return Input.GetKeyDown(KeyCode.E); }
    bool    CancelPressed()   { return Input.GetKeyDown(KeyCode.Escape); }
    Vector2 LookDelta()       { return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")); }

#elif ENABLE_INPUT_SYSTEM
    bool InteractPressed() { var k = Keyboard.current; return k != null && k.eKey.wasPressedThisFrame; }
    bool CancelPressed()   { var k = Keyboard.current; return k != null && k.escapeKey.wasPressedThisFrame; }
    Vector2 LookDelta()
    {
        var m = Mouse.current;
        return m == null ? Vector2.zero : m.delta.ReadValue() * 0.05f;
    }

#else
    bool    InteractPressed() { return false; }
    bool    CancelPressed()   { return false; }
    Vector2 LookDelta()       { return Vector2.zero; }
#endif
}
