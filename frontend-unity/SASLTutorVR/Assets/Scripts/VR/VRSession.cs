using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// Starts VR when the VR scene starts, and stops it when the scene ends.
/// VR is NOT started automatically for the whole project (SASL > Set Up VR Scene
/// turns that off), so the desktop MainClassroom keeps working as before.
///
/// Recentring: in VR your real position in the room counts, so if you stand
/// away from the middle of your play area the avatar and boards end up off to
/// the side. Shortly after VR starts (and whenever Recenter() is called: Y on
/// the left controller or R on the keyboard), the view is moved so you stand
/// at the standing spot, facing the avatar.
///
/// No headset connected? The scene still runs: the camera is placed at standing
/// eye height so you see the same view, and the keyboard controls still work.
/// </summary>
public class VRSession : MonoBehaviour
{
    public XROrigin origin;

    [Tooltip("What you should face after recentring (the avatar).")]
    public Transform lookTarget;

    [Tooltip("Where you stand after recentring (height comes from your real head height).")]
    public Vector3 standingSpot = new Vector3(0f, 0f, 1.6f);

    [Tooltip("Eye height used when no headset is connected.")]
    public float fallbackEyeHeight = 1.6f;

    bool started;

    void Awake()
    {
        // The desktop info panel is drawn onto the screen, which in a headset
        // means one eye only. The VR status board shows the same information.
        var classifier = FindFirstObjectByType<GestureClassifierClient>();
        if (classifier != null) classifier.showDesktopPanel = false;
    }

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

        // Give head tracking a moment to report a real position, then recentre.
        yield return new WaitForSeconds(0.75f);
        Recenter();
    }

    /// <summary>Moves the view so you stand at the standing spot, facing the avatar.</summary>
    public void Recenter()
    {
        if (origin == null || origin.Camera == null) return;
        Transform head = origin.Camera.transform;

        if (lookTarget != null)
        {
            Vector3 toTarget = lookTarget.position - head.position;
            Vector3 forward = head.forward;
            toTarget.y = 0f;
            forward.y = 0f;
            if (toTarget.sqrMagnitude > 1e-4f && forward.sqrMagnitude > 1e-4f)
                origin.RotateAroundCameraUsingOriginUp(Vector3.SignedAngle(forward, toTarget, Vector3.up));
        }

        origin.MoveCameraToWorldLocation(new Vector3(standingSpot.x, head.position.y, standingSpot.z));
        Debug.Log("[VRSession] Recentred: standing in front of the avatar.");
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
