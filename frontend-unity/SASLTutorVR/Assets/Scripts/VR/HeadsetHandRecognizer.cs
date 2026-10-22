using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.XR.Hands;

/// <summary>
/// EXPERIMENT: recognise a sign from the Quest 3's own hand tracking instead
/// of the webcam. Separate from the normal pipeline: the webcam, the streamer
/// and the backend are unchanged, and nothing happens unless this component is
/// in the scene (SASL > Experiments > Add Headset Hand Recognition).
///
/// G on the keyboard starts / stops an attempt (your hands are busy signing,
/// so someone else presses it, or reach for the keyboard). The attempt is sent
/// to the same backend endpoint as the webcam attempts, for the sign currently
/// selected on the board, and the result shows on the board. Each attempt is
/// also saved to Logs/headset_attempts/ so they can become headset references
/// later if the webcam references don't match well.
///
/// The Quest gives 26 joints per hand in metres. They're converted to the 21
/// MediaPipe points, in the order and axis directions the webcam uses (as if a
/// camera stood in front of you looking at you), so the backend can't tell the
/// difference.
/// </summary>
public class HeadsetHandRecognizer : MonoBehaviour
{
    public GestureClassifierClient classifier;

    [Tooltip("Frames per second sent to the backend; the webcam runs at about 30.")]
    public float sampleRate = 30f;

    [Tooltip("Webcam width / height. MediaPipe divides x by the width and y by the height, so y comes out stretched by this much.")]
    public float webcamAspect = 640f / 480f;

    const string BackendUrl = "http://localhost:8000/api/gestures/classify";

    // MediaPipe's 21 hand points, in its order, as Quest joints.
    static readonly XRHandJointID[] MediaPipeOrder =
    {
        XRHandJointID.Wrist,
        XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip,
        XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip,
        XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip,
        XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip,
        XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip,
    };

    /// <summary>Text for the status board.</summary>
    public string Report { get; private set; } = "Headset hands (experiment): G to record";

    XRHandSubsystem hands;
    Transform head;
    readonly List<string> frames = new List<string>();
    bool recording;
    float startTime, nextSample;

    void Start()
    {
        if (classifier == null) classifier = FindAnyObjectByType<GestureClassifierClient>();
        if (Camera.main != null) head = Camera.main.transform;
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
        {
            if (!recording) Begin();
            else Finish();
        }

        if (!recording || Time.time < nextSample) return;
        nextSample += 1f / sampleRate;

        if (hands == null || !hands.running) hands = HandTrackingProbe.FindHandSubsystem();
        if (hands == null || head == null) return;

        frames.Add("{\"t\":" + (Time.time - startTime).ToString("0.###", CultureInfo.InvariantCulture) +
                   ",\"left_hand\":" + HandJson(hands.leftHand) +
                   ",\"right_hand\":" + HandJson(hands.rightHand) + "}");
        Report = $"Headset hands: RECORDING {frames.Count} frames... G to stop";
    }

    void Begin()
    {
        if (HandTrackingProbe.FindHandSubsystem() == null)
        {
            Report = "Headset hands: hand tracking not available (hand tracking on in the headset? controllers down?)";
            return;
        }
        frames.Clear();
        recording = true;
        startTime = nextSample = Time.time;
    }

    void Finish()
    {
        recording = false;
        if (frames.Count < 3)
        {
            Report = "Headset hands: not enough frames, try again (G)";
            return;
        }

        string sign = classifier != null ? classifier.expectedSignName : "unknown";
        string itemId = classifier != null ? classifier.curriculumItemId : "";
        string body = "{\"session_id\":\"unity-headset-test\",\"curriculum_item_id\":\"" + itemId +
                      "\",\"landmark_sequence\":[" + string.Join(",", frames) + "]}";
        Save(sign, body);
        Report = $"Headset hands: checking {sign}...";
        StartCoroutine(Post(sign, body));
    }

    // One hand as 21 [x, y, z] points, or [] when it isn't tracked.
    string HandJson(XRHand hand)
    {
        if (!hand.isTracked) return "[]";

        // A pretend webcam in front of you, facing you: x = towards your left,
        // y = down, z = away from the camera (into you). Head turn only, no tilt.
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Transform space = head.parent;   // joint poses are relative to the tracking space

        var sb = new StringBuilder("[");
        for (int i = 0; i < MediaPipeOrder.Length; i++)
        {
            if (!hand.GetJoint(MediaPipeOrder[i]).TryGetPose(out Pose pose)) return "[]";
            Vector3 p = space != null ? space.TransformPoint(pose.position) : pose.position;
            float x = -Vector3.Dot(p, right);
            float y = -Vector3.Dot(p, Vector3.up) * webcamAspect;
            float z = -Vector3.Dot(p, forward);
            if (i > 0) sb.Append(',');
            sb.Append('[').Append(x.ToString("0.#####", CultureInfo.InvariantCulture))
              .Append(',').Append(y.ToString("0.#####", CultureInfo.InvariantCulture))
              .Append(',').Append(z.ToString("0.#####", CultureInfo.InvariantCulture)).Append(']');
        }
        return sb.Append(']').ToString();
    }

    IEnumerator Post(string sign, string body)
    {
        using (var request = new UnityWebRequest(BackendUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Report = "Headset hands: backend error (" + request.error + ")";
                yield break;
            }

            GestureClassifyResponse parsed = null;
            try { parsed = JsonUtility.FromJson<GestureClassifyResponse>(request.downloadHandler.text); }
            catch (Exception) { }
            if (parsed == null)
                Report = "Headset hands: couldn't read the backend's answer";
            else if (!string.IsNullOrEmpty(parsed.retry_reason))
                Report = $"Headset hands: {sign}: try again ({parsed.retry_reason})";
            else
                Report = $"Headset hands: {sign} -> {parsed.predicted_sign} {parsed.confidence_score * 100f:0}%" +
                         (parsed.predicted_sign == sign ? "  MATCH" : "  NO MATCH");
            Debug.Log("[HeadsetHandRecognizer] " + Report);
        }
    }

    void Save(string sign, string body)
    {
        try
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "headset_attempts");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{sign}_{DateTime.Now:yyyyMMdd_HHmmss}.json"), body);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[HeadsetHandRecognizer] Couldn't save the attempt: " + e.Message);
        }
    }
}
