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
// on exactly one of them. The XR display can come up a moment AFTER the first
// frame (Quest Link, Quest launch), so if no headset is seen at Start this keeps
// looking for a few seconds and switches to the VR rig as soon as one appears.
// The VR rig gets VRInteractor (controller pointing, trigger interaction, stick walking) at that point.

public class RigSelector : MonoBehaviour
{
    public GameObject xrRig;
    public GameObject desktopRig;

    [Tooltip("Tick to use the desktop rig even when a headset is connected (e.g. recording the demo video).")]
    public bool forceDesktop = false;

    [Tooltip("How long to keep waiting for a headset that was not running at Start.")]
    public float waitForHeadset = 20f;

    bool usingVR;
    float waited;
    bool refreshLogged;
    int refreshTries;
    float nextRefresh;

    void Start()
    {
        Apply(!forceDesktop && XRRunning());
    }

    void Update()
    {
        if (usingVR) { LogRefreshRate(); return; }
        if (forceDesktop || waited > waitForHeadset) return;
        waited += Time.unscaledDeltaTime;
        if (XRRunning()) Apply(true);
    }

    void Apply(bool vr)
    {
        usingVR = vr;

        if (xrRig != null)      xrRig.SetActive(vr);
        if (desktopRig != null) desktopRig.SetActive(!vr);

        // Controller pointing, trigger interaction and stick locomotion.
        // InspectController is keyboard-only and sits on the (now inactive) desktop rig.
        if (vr && xrRig != null && xrRig.GetComponent<VRInteractor>() == null)
            xrRig.AddComponent<VRInteractor>();

        Debug.Log(vr
            ? "[RigSelector] Headset detected - using the VR rig."
            : "[RigSelector] No headset (yet) - using the desktop Player (WASD, mouse, E to interact).");
    }

    /// Log the refresh rate the headset is actually running at. This Unity version has no
    /// API to CHANGE it, so 72 Hz has to be set on the headset (see below) - this just
    /// reports what you got. Retries briefly because the display is not always ready at once.
    void LogRefreshRate()
    {
        if (refreshLogged || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        if (++refreshTries > 20) { refreshLogged = true; return; }

        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays)
        {
            float hz;
            if (d.running && d.TryGetDisplayRefreshRate(out hz))
            {
                Debug.Log("[RigSelector] Display refresh rate: " + hz + " Hz" +
                          (Mathf.Abs(hz - 72f) < 0.5f ? "." : " (not 72 - set it in the headset's settings)."));
                refreshLogged = true;
                return;
            }
        }
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
