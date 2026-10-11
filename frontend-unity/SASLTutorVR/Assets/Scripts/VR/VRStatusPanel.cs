using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tutor's status panel as a board in the room. The desktop panel
/// (top-left of the screen) can't be seen inside a headset.
/// </summary>
public class VRStatusPanel : MonoBehaviour
{
    public GestureClassifierClient classifier;
    public Text text;
    [Tooltip("Optional: shows the hand-tracking test line when the probe is in the scene.")]
    public HandTrackingProbe probe;

    void Start()
    {
        if (classifier == null) classifier = FindFirstObjectByType<GestureClassifierClient>();
        if (probe == null) probe = FindFirstObjectByType<HandTrackingProbe>();
    }

    void Update()
    {
        if (classifier == null || text == null) return;

        string result = string.IsNullOrEmpty(classifier.ResultText) ? "" : "\n" + classifier.ResultText;
        string verdict = classifier.LastWasMatch.HasValue ? (classifier.LastWasMatch.Value ? "\nMATCH" : "\nNO MATCH") : "";
        text.text =
            $"{classifier.CurrentLessonTitle}  ({classifier.CurrentSignNumber}/{classifier.CurrentLessonSignCount})\n" +
            $"<size=56><b>Sign: {classifier.expectedSignName}</b></size>\n\n" +
            $"{classifier.StatusText}{result}{verdict}\n\n" +
            "<size=26>A: record    B: watch the avatar    Stick: lesson / sign    Y: recentre</size>" +
            (probe != null ? "\n\n<size=26>" + probe.Report + "</size>" : "");
    }
}
