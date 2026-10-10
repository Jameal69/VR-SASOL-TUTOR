using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes the avatar react to classifier results: a nod on MATCH, a head shake
/// on NO MATCH, and writes the result into a UI Text in front of the avatar.
///
/// Put it on the avatar (needs a Humanoid Animator for the head bone).
/// SASL > Set Up Avatar adds it and wires classifier + resultText for you.
/// The reaction is done in code on top of whatever the Animator plays (Idle),
/// so no extra animation files are needed.
/// </summary>
[RequireComponent(typeof(Animator))]
public class AvatarResultReactor : MonoBehaviour
{
    [Tooltip("The scene's GestureClassifierClient. Found automatically if left empty.")]
    public GestureClassifierClient classifier;

    [Tooltip("UI Text that shows the sign to perform and the MATCH / NO MATCH result.")]
    public Text resultText;

    public float reactionSeconds = 1.2f;
    public float nodDegrees = 15f;
    public float shakeDegrees = 20f;

    static readonly Color MatchColour = new Color(0.35f, 1f, 0.35f);
    static readonly Color NoMatchColour = new Color(1f, 0.4f, 0.4f);
    static readonly Color RetryColour = new Color(1f, 0.85f, 0.3f);

    Animator animator;
    Transform head;
    Quaternion headRestLocal;
    float reactionStart = -100f;
    bool reactionIsNod;

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    void OnEnable()
    {
        if (classifier == null)
            classifier = FindFirstObjectByType<GestureClassifierClient>();

        if (classifier != null)
        {
            classifier.ResultReceived += OnResult;
            classifier.TargetChanged += ShowPrompt;
            classifier.RetryRequested += OnRetry;
        }
        else
        {
            Debug.LogWarning("[AvatarResultReactor] No GestureClassifierClient in the scene, so the avatar won't react.");
        }
    }

    void OnDisable()
    {
        if (classifier != null)
        {
            classifier.ResultReceived -= OnResult;
            classifier.TargetChanged -= ShowPrompt;
            classifier.RetryRequested -= OnRetry;
        }
    }

    void Start()
    {
        if (animator.isHuman)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) headRestLocal = head.localRotation;
        }
        else
        {
            Debug.LogWarning("[AvatarResultReactor] Avatar rig is not Humanoid, so it can't nod or shake. Run SASL > Set Up Avatar.");
        }

        if (classifier != null) ShowPrompt(classifier.expectedSignName);
    }

    void ShowPrompt(string sign)
    {
        if (resultText == null) return;
        resultText.text = "Sign: " + sign;
        resultText.color = Color.white;
    }

    // Poor tracking isn't the learner's mistake: show the hint, no head shake.
    void OnRetry(string message)
    {
        if (resultText == null) return;
        resultText.text = message;
        resultText.color = RetryColour;
    }

    void OnResult(string predicted, float confidence, bool isMatch)
    {
        reactionStart = Time.time;
        reactionIsNod = isMatch;

        if (resultText == null) return;
        int percent = Mathf.RoundToInt(confidence * 100f);
        resultText.text = isMatch
            ? "MATCH: " + classifier.expectedSignName + " (" + percent + "%)"
            : "NO MATCH (detected: " + predicted + ", " + percent + "%)";
        resultText.color = isMatch ? MatchColour : NoMatchColour;
    }

    void LateUpdate()
    {
        if (head == null) return;

        // Without an animation playing nothing resets the head each frame, so
        // start from the rest pose to stop the offsets from piling up.
        if (animator.runtimeAnimatorController == null || !animator.enabled)
            head.localRotation = headRestLocal;

        float t = (Time.time - reactionStart) / reactionSeconds;
        if (t < 0f || t > 1f) return;

        float envelope = Mathf.Sin(t * Mathf.PI);                               // ease in and out
        float wave = Mathf.Sin(t * Mathf.PI * (reactionIsNod ? 4f : 6f));       // 2 nods or 3 shakes
        float angle = wave * envelope * (reactionIsNod ? nodDegrees : shakeDegrees);

        // Rotate around the character's own axes so it works whatever the bone axes are.
        Vector3 axis = reactionIsNod ? transform.right : transform.up;
        head.rotation = Quaternion.AngleAxis(angle, axis) * head.rotation;
    }
}
