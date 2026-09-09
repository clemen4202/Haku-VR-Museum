using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// IMPORTANT: put this in  Assets/Scripts/SimpleFPS.cs
// NOT in Assets/Editor/ - this one has to run in the game, not the editor.
//
// WASD to walk, mouse to look, Shift to run, Esc to free the cursor.
// Works with either the old Input Manager or the new Input System.

[RequireComponent(typeof(CharacterController))]
public class SimpleFPS : MonoBehaviour
{
    public float walkSpeed = 3.0f;
    public float runSpeed = 6.0f;
    public float mouseSensitivity = 2.0f;
    public float gravity = -18f;

    CharacterController cc;
    Transform cam;
    float pitch;
    float vy;

    void Awake()
    {
        cc  = GetComponent<CharacterController>();
        cam = transform.Find("PlayerCamera");
        LockCursor(true);
    }

    void Update()
    {
        if (EscPressed()) LockCursor(false);
        if (ClickPressed()) LockCursor(true);

        // ---- look ----
        Vector2 look = LookDelta() * mouseSensitivity;
        transform.Rotate(0f, look.x, 0f);
        pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
        if (cam != null) cam.localEulerAngles = new Vector3(pitch, 0f, 0f);

        // ---- move ----
        Vector2 mv = MoveAxis();
        Vector3 dir = transform.right * mv.x + transform.forward * mv.y;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        if (cc.isGrounded && vy < 0f) vy = -2f;
        vy += gravity * Time.deltaTime;

        float speed = RunHeld() ? runSpeed : walkSpeed;
        cc.Move((dir * speed + Vector3.up * vy) * Time.deltaTime);
    }

    void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;
    }

    // ---------------- input, whichever system the project uses ----------------

#if ENABLE_LEGACY_INPUT_MANAGER
    Vector2 MoveAxis()   { return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")); }
    Vector2 LookDelta()  { return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")); }
    bool    RunHeld()    { return Input.GetKey(KeyCode.LeftShift); }
    bool    EscPressed() { return Input.GetKeyDown(KeyCode.Escape); }
    bool    ClickPressed(){ return Input.GetMouseButtonDown(0); }

#elif ENABLE_INPUT_SYSTEM
    Vector2 MoveAxis()
    {
        var k = Keyboard.current;
        if (k == null) return Vector2.zero;
        float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
        float y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
        return new Vector2(x, y);
    }
    Vector2 LookDelta()
    {
        var m = Mouse.current;
        return m == null ? Vector2.zero : m.delta.ReadValue() * 0.05f;
    }
    bool RunHeld()    { var k = Keyboard.current; return k != null && k.leftShiftKey.isPressed; }
    bool EscPressed() { var k = Keyboard.current; return k != null && k.escapeKey.wasPressedThisFrame; }
    bool ClickPressed(){ var m = Mouse.current; return m != null && m.leftButton.wasPressedThisFrame; }

#else
    Vector2 MoveAxis()   { return Vector2.zero; }
    Vector2 LookDelta()  { return Vector2.zero; }
    bool    RunHeld()    { return false; }
    bool    EscPressed() { return false; }
    bool    ClickPressed(){ return false; }
#endif
}
