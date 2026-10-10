using System.Collections;
using UnityEngine;

/// <summary>
/// Makes the avatar demonstrate signs:
///   - when a sign is selected (keys 1-4), it shows that sign
///   - press D to see it again
///   - after a NO MATCH it shows the correct sign again
///
/// Signs play on the "Signing" Animator layer, which only covers the right arm
/// and hand, so the rest of the body keeps its Idle. Clips come from a
/// SignLibrary. SASL > Build Sign Animations sets all of this up.
/// </summary>
[RequireComponent(typeof(Animator))]
public class AvatarSignPlayer : MonoBehaviour
{
    public SignLibrary library;

    [Tooltip("The scene's GestureClassifierClient. Found automatically if left empty.")]
    public GestureClassifierClient classifier;

    [Tooltip("Empty clip in the Signing layer's Sign state; swapped for the real sign at runtime.")]
    public AnimationClip placeholderClip;

    public string layerName = "Signing";
    public string stateName = "Sign";

    public float blendSeconds = 0.25f;
    [Tooltip("Minimum time a sign is held, so static handshapes are visible long enough.")]
    public float holdSeconds = 1.5f;
    [Tooltip("Wait this long after a NO MATCH (so the head shake finishes) before demonstrating.")]
    public float demoAfterNoMatchDelay = 1.3f;

    public bool demoWhenSignSelected = true;
    public bool demoAfterNoMatch = true;

    Animator animator;
    AnimatorOverrideController overrides;
    int layer = -1;
    Coroutine playing;

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    void Start()
    {
        if (classifier == null)
            classifier = FindFirstObjectByType<GestureClassifierClient>();

        layer = animator.GetLayerIndex(layerName);
        if (layer < 0 || library == null || placeholderClip == null)
        {
            Debug.LogWarning("[AvatarSignPlayer] Not set up (missing Signing layer, library or placeholder). Run SASL > Build Sign Animations.");
            enabled = false;
            return;
        }

        // One override controller for the whole session; only the clip in the
        // Sign slot changes per demonstration.
        overrides = new AnimatorOverrideController(animator.runtimeAnimatorController);
        animator.runtimeAnimatorController = overrides;
        animator.SetLayerWeight(layer, 0f);

        if (classifier != null)
        {
            classifier.TargetChanged += OnTargetChanged;
            classifier.ResultReceived += OnResult;
        }
    }

    void OnDestroy()
    {
        if (classifier != null)
        {
            classifier.TargetChanged -= OnTargetChanged;
            classifier.ResultReceived -= OnResult;
        }
    }

    void Update()
    {
        if (DemoKeyPressedThisFrame() && classifier != null)
            Play(classifier.expectedSignName);
    }

    void OnTargetChanged(string sign)
    {
        if (demoWhenSignSelected) Play(sign);
    }

    void OnResult(string predicted, float confidence, bool isMatch)
    {
        if (!isMatch && demoAfterNoMatch && classifier != null)
            Play(classifier.expectedSignName, demoAfterNoMatchDelay);
    }

    /// <summary>Demonstrates a sign. Returns false if there's no animation for it yet.</summary>
    public bool Play(string sign, float delaySeconds = 0f)
    {
        if (!enabled) return false;
        var clip = library.Find(sign);
        if (clip == null)
        {
            Debug.Log("[AvatarSignPlayer] No animation for '" + sign + "' yet.");
            return false;
        }

        if (playing != null) StopCoroutine(playing);
        playing = StartCoroutine(PlayRoutine(clip, delaySeconds));
        return true;
    }

    IEnumerator PlayRoutine(AnimationClip clip, float delaySeconds)
    {
        if (delaySeconds > 0f) yield return new WaitForSeconds(delaySeconds);

        overrides[placeholderClip] = clip;
        animator.Play(stateName, layer, 0f);

        yield return Fade(animator.GetLayerWeight(layer), 1f);
        yield return new WaitForSeconds(Mathf.Max(holdSeconds, clip.length - blendSeconds));
        yield return Fade(1f, 0f);
        playing = null;
    }

    IEnumerator Fade(float from, float to)
    {
        for (float t = 0f; t < blendSeconds; t += Time.deltaTime)
        {
            animator.SetLayerWeight(layer, Mathf.Lerp(from, to, t / blendSeconds));
            yield return null;
        }
        animator.SetLayerWeight(layer, to);
    }

    bool DemoKeyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && keyboard.dKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.D);
#endif
    }
}
