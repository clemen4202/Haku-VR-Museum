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

    [Tooltip("Display refresh rate to request from the headset, in Hz (Quest 3 offers 72, 80, 90, 120). 0 = leave at the system default.")]
    public float refreshRate = 72f;

    bool usingVR;
    float waited;
    bool refreshDone;
    int refreshTries;
    float nextRefresh;

    void Start()
    {
        Apply(!forceDesktop && XRRunning());
    }

    void Update()
    {
        if (usingVR) { RequestRefreshRate(); return; }
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

    /// Ask the headset for a fixed refresh rate. The display is not always ready to
    /// accept the request on the first frame, so retry twice a second for ~10 s.
    void RequestRefreshRate()
    {
        if (refreshDone || refreshRate <= 0f || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;

        if (++refreshTries > 20)
        {
            refreshDone = true;
            Debug.LogWarning("[RigSelector] Could not set the display refresh rate to " + refreshRate +
                             " Hz (this runtime may not support changing it - normal over Quest Link).");
            return;
        }

        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays)
        {
            if (!d.running) continue;

            float now;
            if (d.TryGetDisplayRefreshRate(out now) && Mathf.Abs(now - refreshRate) < 0.5f)
            {
                Debug.Log("[RigSelector] Display already at " + now + " Hz.");
                refreshDone = true;
                return;
            }
            if (d.TrySetDisplayRefreshRate(refreshRate))
            {
                Debug.Log("[RigSelector] Requested " + refreshRate + " Hz display refresh.");
                refreshDone = true;
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
