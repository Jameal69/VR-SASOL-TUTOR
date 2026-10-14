using System;
using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

/// <summary>
/// Test tool: does the Quest 3's own hand tracking keep up during signs,
/// especially ones made close to the face (Hello at the forehead, Thank You at
/// the chin)? That decides whether recognition could move from the webcam to
/// the headset.
///
/// - Draws your tracked hand joints as small dots (left blue, right orange).
/// - Board line: live "right hand tracked X% over the last 3 s".
/// - Measures an attempt while one is being recorded (webcam pipeline), or
///   while measuring is toggled with H on the keyboard (you can't press
///   controller buttons while your hands are being tracked).
/// - Each measurement is logged to Logs/hand_tracking_probe.csv (next to Assets).
///
/// Needs: hand tracking on in the headset, controllers put down.
/// Added by SASL > Add Hand Tracking Probe.
/// </summary>
public class HandTrackingProbe : MonoBehaviour
{
    public XROrigin origin;
    public GestureClassifierClient classifier;
    public bool showJoints = true;
    public float jointSize = 0.012f;

    static readonly XRHandJointID[] Joints =
    {
        XRHandJointID.Wrist, XRHandJointID.Palm,
        XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
        XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
        XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
        XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
        XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip,
    };

    /// <summary>Text for the status board.</summary>
    public string Report { get; private set; } = "Hand tracking: starting...";

    XRHandSubsystem hands;
    Transform trackingSpace;
    readonly Dictionary<XRHandJointID, Transform>[] dots = { new Dictionary<XRHandJointID, Transform>(), new Dictionary<XRHandJointID, Transform>() };

    // Live window (last 3 seconds) for the right hand.
    readonly Queue<(float time, bool tracked)> recent = new Queue<(float, bool)>();

    // Current measurement.
    bool manualMeasuring, wasMeasuring;
    int frames, rightFrames, leftFrames, rightDropouts;
    bool rightWasTracked;
    float closestToHead;
    string lastAttempt = "";

    void Start()
    {
        if (origin == null) origin = FindFirstObjectByType<XROrigin>();
        if (classifier == null) classifier = FindFirstObjectByType<GestureClassifierClient>();
        trackingSpace = origin != null && origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : transform;
    }

    void Update()
    {
        if (KeyPressedThisFrame()) manualMeasuring = !manualMeasuring;

        if (hands == null || !hands.running)
        {
            hands = FindHandSubsystem();
            if (hands == null)
            {
                Report = "Hand tracking: not available. Is hand tracking on in the headset, and are the controllers put down?";
                SetDotsVisible(0, false);
                SetDotsVisible(1, false);
                return;
            }
        }

        XRHand left = hands.leftHand, right = hands.rightHand;
        if (showJoints)
        {
            DrawHand(0, left, new Color(0.3f, 0.6f, 1f));
            DrawHand(1, right, new Color(1f, 0.6f, 0.2f));
        }

        // Live window.
        recent.Enqueue((Time.time, right.isTracked));
        while (recent.Count > 0 && recent.Peek().time < Time.time - 3f) recent.Dequeue();
        int trackedRecently = 0;
        foreach (var sample in recent) if (sample.tracked) trackedRecently++;
        string live = $"Right hand tracked {100 * trackedRecently / Mathf.Max(1, recent.Count)}% (last 3 s)";

        // Measurement window: while an attempt is recorded, or toggled with H.
        bool measuring = manualMeasuring || (classifier != null && classifier.IsRecording);
        if (measuring && !wasMeasuring) BeginMeasurement();
        if (measuring) Accumulate(left, right);
        if (!measuring && wasMeasuring) EndMeasurement();
        wasMeasuring = measuring;

        Report = live + (measuring ? "   [measuring... H to stop]" : "   (H: measure)") +
                 (lastAttempt.Length > 0 ? "\n" + lastAttempt : "");
    }

    void BeginMeasurement()
    {
        frames = rightFrames = leftFrames = rightDropouts = 0;
        rightWasTracked = true;
        closestToHead = float.PositiveInfinity;
    }

    void Accumulate(XRHand left, XRHand right)
    {
        frames++;
        if (left.isTracked) leftFrames++;
        if (right.isTracked)
        {
            rightFrames++;
            if (origin != null && origin.Camera != null &&
                right.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist))
            {
                float distance = Vector3.Distance(trackingSpace.TransformPoint(wrist.position), origin.Camera.transform.position);
                closestToHead = Mathf.Min(closestToHead, distance);
            }
        }
        if (rightWasTracked && !right.isTracked) rightDropouts++;
        rightWasTracked = right.isTracked;
    }

    void EndMeasurement()
    {
        if (frames == 0) return;
        string sign = classifier != null ? classifier.expectedSignName : "?";
        int rightPct = 100 * rightFrames / frames, leftPct = 100 * leftFrames / frames;
        string closest = float.IsInfinity(closestToHead) ? "n/a" : $"{closestToHead * 100f:0} cm";
        lastAttempt = $"Last ({sign}): right {rightPct}%, {rightDropouts} dropout(s), left {leftPct}%, closest to head {closest}";
        Debug.Log("[HandTrackingProbe] " + lastAttempt + $", {frames} frames");

        try
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "hand_tracking_probe.csv"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            bool isNew = !File.Exists(path);
            using (var writer = new StreamWriter(path, append: true))
            {
                if (isNew) writer.WriteLine("time,sign,frames,right_tracked_pct,right_dropouts,left_tracked_pct,closest_to_head_cm");
                string cm = float.IsInfinity(closestToHead) ? "" : (closestToHead * 100f).ToString("0");
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{sign},{frames},{rightPct},{rightDropouts},{leftPct},{cm}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[HandTrackingProbe] Couldn't write the CSV log: " + e.Message);
        }
    }

    static XRHandSubsystem FindHandSubsystem()
    {
        var settings = XRGeneralSettings.Instance;
        if (settings != null && settings.Manager != null && settings.Manager.activeLoader != null)
        {
            var fromLoader = settings.Manager.activeLoader.GetLoadedSubsystem<XRHandSubsystem>();
            if (fromLoader != null && fromLoader.running) return fromLoader;
        }
        var all = new List<XRHandSubsystem>();
        SubsystemManager.GetSubsystems(all);
        foreach (var subsystem in all)
            if (subsystem.running) return subsystem;
        return null;
    }

    void DrawHand(int side, XRHand hand, Color colour)
    {
        if (!hand.isTracked) { SetDotsVisible(side, false); return; }
        foreach (var id in Joints)
        {
            if (!dots[side].TryGetValue(id, out var dot))
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(sphere.GetComponent<Collider>());
                sphere.name = (side == 0 ? "L " : "R ") + id;
                sphere.transform.SetParent(trackingSpace, false);
                sphere.transform.localScale = Vector3.one * jointSize;
                sphere.GetComponent<Renderer>().material.color = colour;
                dot = sphere.transform;
                dots[side][id] = dot;
            }
            bool hasPose = hand.GetJoint(id).TryGetPose(out Pose pose);
            dot.gameObject.SetActive(hasPose);
            if (hasPose) dot.localPosition = pose.position;
        }
    }

    void SetDotsVisible(int side, bool visible)
    {
        foreach (var dot in dots[side].Values)
            if (dot != null) dot.gameObject.SetActive(visible);
    }

    static bool KeyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && keyboard.hKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.H);
#endif
    }
}
