using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Quest controller buttons for the tutor (the keyboard can't be seen in a headset):
///   A or X          start / stop recording an attempt   (keyboard: SPACE)
///   B               watch the avatar sign it            (keyboard: D)
///   Thumbstick L/R  previous / next lesson              (keyboard: Left/Right)
///   Thumbstick U/D  previous / next sign                (keyboard: Up/Down)
/// The keyboard keeps working too.
/// </summary>
public class VRControllerInput : MonoBehaviour
{
    public GestureClassifierClient classifier;
    public AvatarSignPlayer signPlayer;

    InputAction record;
    InputAction demo;
    InputAction browse;
    bool stickCentered = true;

    void OnEnable()
    {
        if (classifier == null) classifier = FindFirstObjectByType<GestureClassifierClient>();
        if (signPlayer == null) signPlayer = FindFirstObjectByType<AvatarSignPlayer>();

        record = new InputAction("Record", InputActionType.Button);
        record.AddBinding("<XRController>{RightHand}/primaryButton");
        record.AddBinding("<XRController>{LeftHand}/primaryButton");
        record.performed += OnRecord;

        demo = new InputAction("Demo", InputActionType.Button);
        demo.AddBinding("<XRController>{RightHand}/secondaryButton");
        demo.performed += OnDemo;

        browse = new InputAction("Browse", InputActionType.Value, expectedControlType: "Vector2");
        browse.AddBinding("<XRController>{RightHand}/primary2DAxis");
        browse.AddBinding("<XRController>{LeftHand}/primary2DAxis");

        record.Enable();
        demo.Enable();
        browse.Enable();
    }

    void OnDisable()
    {
        record.performed -= OnRecord;
        demo.performed -= OnDemo;
        record.Dispose();
        demo.Dispose();
        browse.Dispose();
    }

    void OnRecord(InputAction.CallbackContext _)
    {
        if (classifier != null) classifier.ToggleRecording();
    }

    void OnDemo(InputAction.CallbackContext _)
    {
        if (classifier != null && signPlayer != null) signPlayer.Play(classifier.expectedSignName);
    }

    // One step per push of the stick: it has to return to the centre before the next step.
    void Update()
    {
        if (classifier == null) return;
        Vector2 stick = browse.ReadValue<Vector2>();
        if (stickCentered && stick.magnitude > 0.7f)
        {
            stickCentered = false;
            if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y))
                classifier.ChangeLesson(stick.x > 0f ? +1 : -1);
            else
                classifier.ChangeSign(stick.y > 0f ? -1 : +1);   // up = previous, like the Up arrow
        }
        else if (stick.magnitude < 0.3f)
        {
            stickCentered = true;
        }
    }
}
