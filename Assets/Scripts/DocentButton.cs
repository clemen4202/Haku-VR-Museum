using UnityEngine;

// Put this in:  Assets/Scripts/DocentButton.cs
//
// Marks a collider as something the guide (MuseumGuide) owns and the controller ray can
// press: the guide character itself ("talk"), or a button on its panel. VRInteractor
// finds it with GetComponentInParent, shows `prompt`, and calls guide.Press(this) on T1.

public class DocentButton : MonoBehaviour
{
    public enum Kind { Talk, Question, Close }

    public Kind         kind;
    public MuseumGuide  guide;
    public string       label;      // text on the button
    public string       question;   // what is asked of the AI (Question buttons)
    public string       prompt;     // "[T1]  ..." line shown while the ray is on it

    public Renderer plate;          // optional: tinted when the ray is on it
    public Color    idle  = new Color(0.16f, 0.20f, 0.30f);
    public Color    hover = new Color(0.25f, 0.55f, 0.35f);

    /// VRInteractor stamps this every frame the ray is on the button.
    [System.NonSerialized] public int hoverFrame = -10;

    bool wasHover;

    void Update()
    {
        if (plate == null) return;
        bool h = Time.frameCount - hoverFrame <= 1;
        if (h == wasHover) return;
        wasHover = h;
        var m = plate.sharedMaterial;
        var c = h ? hover : idle;
        if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
    }
}
