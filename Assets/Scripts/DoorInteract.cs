using UnityEngine;

// Put this in:  Assets/Scripts/DoorInteract.cs
//
// Goes on a door HINGE (an empty object at the jamb). The visible leaf is a
// child, offset sideways, so rotating the hinge swings the door properly.

public class DoorInteract : MonoBehaviour
{
    public string doorName  = "Door";
    public float  openAngle = 95f;    // negative swings the other way
    public float  speed     = 260f;   // degrees per second
    public bool   isOpen    = false;

    float closedY;
    float targetY;

    void Awake()
    {
        closedY = transform.localEulerAngles.y;
        targetY = closedY + (isOpen ? openAngle : 0f);
    }

    public void Toggle()
    {
        isOpen  = !isOpen;
        targetY = closedY + (isOpen ? openAngle : 0f);
    }

    public string Prompt()
    {
        return (isOpen ? "close " : "open ") + doorName;
    }

    void Update()
    {
        var e = transform.localEulerAngles;
        float y = Mathf.MoveTowardsAngle(e.y, targetY, speed * Time.deltaTime);
        if (!Mathf.Approximately(y, e.y))
            transform.localEulerAngles = new Vector3(e.x, y, e.z);
    }
}
