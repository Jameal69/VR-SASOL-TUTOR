using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// Starts VR when the VR scene starts, and stops it when the scene ends.
/// VR is NOT started automatically for the whole project (SASL > Set Up VR Scene
/// turns that off), so the desktop MainClassroom keeps working as before.
///
/// No headset connected? The scene still runs: the camera is placed at standing
/// eye height so you see the same view, and the keyboard controls still work.
/// </summary>
public class VRSession : MonoBehaviour
{
    public XROrigin origin;

    [Tooltip("Eye height used when no headset is connected.")]
    public float fallbackEyeHeight = 1.6f;

    bool started;

    IEnumerator Start()
    {
        var settings = XRGeneralSettings.Instance;
        if (settings == null || settings.Manager == null)
        {
            UseFallback("VR isn't configured; run SASL > Set Up VR Scene");
            yield break;
        }

        if (settings.Manager.activeLoader == null)
            yield return settings.Manager.InitializeLoader();

        if (settings.Manager.activeLoader == null)
        {
            UseFallback("no headset found; is Quest Link running and set as the OpenXR runtime?");
            yield break;
        }

        settings.Manager.StartSubsystems();
        started = true;
        Debug.Log("[VRSession] VR started (" + settings.Manager.activeLoader.name + ").");
    }

    void UseFallback(string reason)
    {
        Debug.Log("[VRSession] Running without a headset (" + reason + "). Showing the view from standing height.");
        if (origin != null && origin.CameraFloorOffsetObject != null)
            origin.CameraFloorOffsetObject.transform.localPosition = new Vector3(0f, fallbackEyeHeight, 0f);
    }

    void OnDestroy()
    {
        if (!started) return;
        var settings = XRGeneralSettings.Instance;
        if (settings == null || settings.Manager == null) return;
        settings.Manager.StopSubsystems();
        settings.Manager.DeinitializeLoader();
    }
}
