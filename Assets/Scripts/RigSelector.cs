using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

// Put this in:  Assets/Scripts/RigSelector.cs
//
// The scene contains two rigs:
//   - the VR rig (XR Origin)            -> used when a headset is running
//   - the desktop 'Player' (WASD, E)    -> used in the Editor with no headset
//
// On Start this checks whether an XR display is actually running and switches
// on exactly one of them. The Quest build therefore uses the VR rig, and pressing
// Play in the Editor gives you a walkable, E-to-interact desktop player.

public class RigSelector : MonoBehaviour
{
    public GameObject xrRig;
    public GameObject desktopRig;

    [Tooltip("Tick to use the desktop rig even when a headset is connected (e.g. recording the demo video).")]
    public bool forceDesktop = false;

    void Start()
    {
        bool vr = !forceDesktop && XRRunning();

        if (xrRig != null)      xrRig.SetActive(vr);
        if (desktopRig != null) desktopRig.SetActive(!vr);

        Debug.Log(vr
            ? "[RigSelector] Headset detected - using the VR rig."
            : "[RigSelector] No headset - using the desktop Player (WASD, mouse, E to interact).");
    }

    static bool XRRunning()
    {
        if (XRSettings.isDeviceActive) return true;

        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays)
            if (d.running) return true;

        return false;
    }
}
