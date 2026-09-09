using UnityEngine;

// Put this in:  Assets/Scripts/Inspectable.cs
//
// Marks an object as something the player can pick up and inspect.
// Add it to any exhibit and fill in the name and description in the Inspector.

public class Inspectable : MonoBehaviour
{
    public string displayName = "Artefact";

    [TextArea(2, 6)]
    public string description = "";

    [Tooltip("How far in front of the camera the object floats while being inspected.")]
    public float holdDistance = 0.55f;

    [Tooltip("Rotation the object snaps to when picked up, so it faces the player nicely.")]
    public Vector3 holdRotation = new Vector3(0f, 0f, 0f);
}
