using System;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

/// <summary>
/// Menu: SASL > Set Up VR Scene
///
/// Makes Assets/Scenes/VRClassroom.unity: a copy of MainClassroom (avatar,
/// classroom, recognition, sign demos) set up for a Quest 3 via Quest Link:
///   - XR rig (headset camera + both controllers) standing in front of the avatar
///   - status board in the room, result text above the avatar (screen text
///     isn't visible in a headset)
///   - controller buttons: A record, B watch the avatar, thumbstick lesson/sign
///   - OpenXR on for Windows with Quest controller profiles, but VR only starts
///     inside this scene (VRSession), so MainClassroom stays a desktop scene.
///
/// MainClassroom itself is not changed. Safe to run again.
/// </summary>
public static class SASLVRSetup
{
    const string DesktopScenePath = "Assets/Scenes/MainClassroom.unity";
    const string VRScenePath = "Assets/Scenes/VRClassroom.unity";
    const string RigName = "XR Origin (VR)";

    [MenuItem("SASL/Set Up VR Scene")]
    public static void SetUp()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("SASL", "Stop Play mode first, then run Set Up VR Scene again.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string xrNote = ConfigureXR();

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(VRScenePath) == null && !AssetDatabase.CopyAsset(DesktopScenePath, VRScenePath))
        {
            EditorUtility.DisplayDialog("SASL", "Couldn't copy " + DesktopScenePath + ".", "OK");
            return;
        }
        var scene = EditorSceneManager.OpenScene(VRScenePath);

        var classifier = UnityEngine.Object.FindFirstObjectByType<GestureClassifierClient>();
        var signPlayer = UnityEngine.Object.FindFirstObjectByType<AvatarSignPlayer>();

        var rig = BuildRig();
        PlaceResultTextAboveAvatar();
        BuildStatusBoard(classifier);

        var input = rig.GetComponent<VRControllerInput>();
        if (input == null) input = rig.AddComponent<VRControllerInput>();
        input.classifier = classifier;
        input.signPlayer = signPlayer;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        EditorUtility.DisplayDialog("SASL",
            "VR scene ready: Assets/Scenes/VRClassroom.unity (MainClassroom is unchanged).\n\n" + xrNote +
            "\n\nTest without a headset: press Play, you see the room from standing height and the keyboard works.\n" +
            "With the Quest 3: start Quest Link first, put the headset on, then Play.\n" +
            "A = record, B = watch the avatar, thumbstick = lesson / sign.",
            "OK");
    }

    // ------------------------------------------------------------------ XR settings

    static string ConfigureXR()
    {
        var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (general == null || general.Manager == null)
            return "XR settings for Windows don't exist yet: open Edit > Project Settings > XR Plug-in Management once, " +
                   "click the Windows tab, tick OpenXR, untick 'Initialize XR on Startup', then run this again.";

        // VR only inside the VR scene (VRSession starts it), not for every scene.
        general.InitManagerOnStart = false;
        EditorUtility.SetDirty(general);
        bool loaderOk = XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone);

        string profiles = "";
        var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
        if (openxr != null)
        {
            foreach (var type in new[] { typeof(MetaQuestTouchPlusControllerProfile), typeof(OculusTouchControllerProfile) })
            {
                var feature = openxr.GetFeature(type);
                if (feature == null) continue;
                feature.enabled = true;
                profiles += (profiles.Length > 0 ? ", " : "") + type.Name.Replace("ControllerProfile", "");
            }
            EditorUtility.SetDirty(openxr);
        }
        AssetDatabase.SaveAssets();

        return (loaderOk ? "OpenXR is on for Windows" : "Couldn't switch OpenXR on automatically: tick it in Project Settings > XR Plug-in Management > Windows") +
               (profiles.Length > 0 ? " (controllers: " + profiles + ")." : ".");
    }

    // ------------------------------------------------------------------ rig

    static GameObject BuildRig()
    {
        var existing = GameObject.Find(RigName);
        if (existing != null) return existing;

        // The desktop camera stays in the file but switched off; the headset camera takes over.
        var desktopCamera = GameObject.Find("Main Camera");
        if (desktopCamera != null)
        {
            desktopCamera.tag = "Untagged";
            desktopCamera.SetActive(false);
        }

        // Stand 1.6 m in front of the avatar (which faces +Z), looking at it.
        var rig = new GameObject(RigName);
        rig.transform.SetPositionAndRotation(new Vector3(0f, 0f, 1.6f), Quaternion.Euler(0f, 180f, 0f));
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(rig.transform, false);

        var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        cameraObject.transform.SetParent(offset.transform, false);
        var camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        cameraObject.AddComponent<AudioListener>();
        var headPose = cameraObject.AddComponent<TrackedPoseDriver>();
        headPose.positionInput = Action("<XRHMD>/centerEyePosition", "Vector3");
        headPose.rotationInput = Action("<XRHMD>/centerEyeRotation", "Quaternion");
        headPose.trackingStateInput = Action("<XRHMD>/trackingState", "Integer");

        AddController(offset.transform, "Left Controller", "{LeftHand}");
        AddController(offset.transform, "Right Controller", "{RightHand}");

        var origin = rig.AddComponent<XROrigin>();
        origin.Origin = rig;
        origin.CameraFloorOffsetObject = offset;
        origin.Camera = camera;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = 1.6f;

        rig.AddComponent<VRSession>().origin = origin;
        Undo.RegisterCreatedObjectUndo(rig, "Create XR rig");
        return rig;
    }

    static void AddController(Transform parent, string name, string hand)
    {
        var controller = new GameObject(name);
        controller.transform.SetParent(parent, false);
        var pose = controller.AddComponent<TrackedPoseDriver>();
        pose.positionInput = Action($"<XRController>{hand}/devicePosition", "Vector3");
        pose.rotationInput = Action($"<XRController>{hand}/deviceRotation", "Quaternion");
        pose.trackingStateInput = Action($"<XRController>{hand}/trackingState", "Integer");

        // A small marker so you can see where your controllers are.
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Marker";
        marker.transform.SetParent(controller.transform, false);
        marker.transform.localScale = Vector3.one * 0.05f;
        UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
    }

    static InputActionProperty Action(string binding, string controlType) =>
        new InputActionProperty(new InputAction(binding: binding, expectedControlType: controlType));

    // ------------------------------------------------------------------ UI in the room

    // The big MATCH / NO MATCH line, as a sign floating above the avatar.
    static void PlaceResultTextAboveAvatar()
    {
        var canvasObject = GameObject.Find("ResultCanvas");
        if (canvasObject == null) return;
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler != null) UnityEngine.Object.DestroyImmediate(scaler);  // only for screen-space canvases

        var rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1600f, 160f);
        rect.localScale = Vector3.one * 0.001f;   // 1.6 m wide
        rect.SetPositionAndRotation(new Vector3(0f, 2.05f, 0.1f), Quaternion.Euler(0f, 180f, 0f));

        var text = canvasObject.transform.Find("ResultText");
        if (text != null)
        {
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        }
    }

    // Lesson, sign, status and controls on a board to the learner's right of the avatar.
    static void BuildStatusBoard(GestureClassifierClient classifier)
    {
        if (GameObject.Find("VR Status Board") != null) return;

        var board = new GameObject("VR Status Board", typeof(RectTransform), typeof(Canvas));
        board.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = board.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(900f, 720f);
        rect.localScale = Vector3.one * 0.001f;   // 0.9 m x 0.72 m
        // Learner stands at z = 1.6 facing -Z, so their right is -X. Turned slightly towards them.
        rect.SetPositionAndRotation(new Vector3(-0.85f, 1.35f, 0.35f), Quaternion.Euler(0f, 160f, 0f));

        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(board.transform, false);
        Stretch(background.GetComponent<RectTransform>(), 0f);
        background.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 0.85f);

        var textObject = new GameObject("Status Text", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(board.transform, false);
        Stretch(textObject.GetComponent<RectTransform>(), 36f);
        var text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 34;
        text.color = Color.white;
        text.alignment = TextAnchor.UpperLeft;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.text = "Lesson / sign appear here in Play mode";

        var panel = board.AddComponent<VRStatusPanel>();
        panel.classifier = classifier;
        panel.text = text;
        Undo.RegisterCreatedObjectUndo(board, "Create VR status board");
    }

    static void Stretch(RectTransform rect, float padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }
}
