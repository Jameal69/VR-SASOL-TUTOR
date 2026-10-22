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

        var classifier = UnityEngine.Object.FindAnyObjectByType<GestureClassifierClient>();
        var signPlayer = UnityEngine.Object.FindAnyObjectByType<AvatarSignPlayer>();

        var rig = BuildRig();
        PlaceResultTextAboveAvatar();
        BuildStatusBoard(classifier);
        LayoutWhiteboard(rig);

        // Recentring: face the avatar, stand at the standing spot (also re-applied on existing rigs).
        var session = rig.GetComponent<VRSession>();
        var avatar = UnityEngine.Object.FindAnyObjectByType<AvatarResultReactor>();
        if (session != null)
        {
            session.lookTarget = avatar != null ? avatar.transform : null;
            session.standingSpot = new Vector3(ViewerEye.x, 0f, ViewerEye.z);
        }

        var input = rig.GetComponent<VRControllerInput>();
        if (input == null) input = rig.AddComponent<VRControllerInput>();
        input.classifier = classifier;
        input.signPlayer = signPlayer;
        input.session = session;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        EditorUtility.DisplayDialog("SASL",
            "VR scene ready: Assets/Scenes/VRClassroom.unity (MainClassroom is unchanged).\n\n" + xrNote +
            "\n\nTest without a headset: press Play, you see the room from standing height and the keyboard works.\n" +
            "With the Quest 3: start Quest Link first, put the headset on, then Play.\n" +
            "A = record, B = watch the avatar, thumbstick = lesson / sign, Y (or R) = recentre.",
            "OK");
    }

    /// <summary>Adds the hand-tracking test tool (HandTrackingProbe) to the VR scene's rig.</summary>
    [MenuItem("SASL/Add Hand Tracking Probe")]
    public static void AddHandTrackingProbe()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("SASL", "Stop Play mode first.", "OK");
            return;
        }
        if (EditorSceneManager.GetActiveScene().path != VRScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(VRScenePath) == null)
            {
                EditorUtility.DisplayDialog("SASL", "Run SASL > Set Up VR Scene first.", "OK");
                return;
            }
            EditorSceneManager.OpenScene(VRScenePath);
        }

        var rig = GameObject.Find(RigName);
        if (rig == null)
        {
            EditorUtility.DisplayDialog("SASL", "No XR rig in the VR scene. Run SASL > Set Up VR Scene first.", "OK");
            return;
        }
        var probe = rig.GetComponent<HandTrackingProbe>();
        if (probe == null) probe = Undo.AddComponent<HandTrackingProbe>(rig);
        probe.origin = rig.GetComponent<XROrigin>();
        probe.classifier = UnityEngine.Object.FindAnyObjectByType<GestureClassifierClient>();

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("SASL",
            "Hand tracking probe added to VRClassroom.\n\n" +
            "In the headset: switch hand tracking on, put the controllers down, Play.\n" +
            "Your hand joints show as dots; the board shows how much of the time your right hand is tracked.\n" +
            "Press H on the keyboard to start/stop a measurement (e.g. around one Hello). " +
            "Results go to the Console and Logs/hand_tracking_probe.csv.",
            "OK");
    }

    /// <summary>
    /// Adds a panel to the VR whiteboard that shows the webcam picture (sent by
    /// landmark_streamer.py), opposite the status board. Safe to run again.
    /// </summary>
    [MenuItem("SASL/Add Camera View to Whiteboard")]
    public static void AddCameraView()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("SASL", "Stop Play mode first.", "OK");
            return;
        }
        if (EditorSceneManager.GetActiveScene().path != VRScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(VRScenePath) == null)
            {
                EditorUtility.DisplayDialog("SASL", "Run SASL > Set Up VR Scene first.", "OK");
                return;
            }
            EditorSceneManager.OpenScene(VRScenePath);
        }

        var existing = GameObject.Find("VR Camera View");
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var view = new GameObject("VR Camera View", typeof(RectTransform), typeof(Canvas));
        view.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = view.GetComponent<RectTransform>();
        rect.sizeDelta = CameraViewSizePixels;
        rect.localScale = Vector3.one * WallPanelScale;
        // Read from its -Z side like the other whiteboard panels.
        rect.SetPositionAndRotation(CameraViewSpot, Quaternion.Euler(0f, 180f, 0f));

        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(view.transform, false);
        Stretch(background.GetComponent<RectTransform>(), 0f);
        background.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.14f, 0.85f);

        // 4:3 picture at the top, caption underneath.
        var picture = new GameObject("Camera Picture", typeof(RectTransform), typeof(RawImage));
        picture.transform.SetParent(view.transform, false);
        var pictureRect = picture.GetComponent<RectTransform>();
        pictureRect.anchorMin = pictureRect.anchorMax = new Vector2(0.5f, 1f);
        pictureRect.pivot = new Vector2(0.5f, 1f);
        pictureRect.sizeDelta = new Vector2(960f, 720f);
        pictureRect.anchoredPosition = new Vector2(0f, -20f);
        picture.GetComponent<RawImage>().color = new Color(0.15f, 0.17f, 0.22f);   // dark until the first frame arrives

        var captionObject = new GameObject("Caption", typeof(RectTransform), typeof(Text));
        captionObject.transform.SetParent(view.transform, false);
        var captionRect = captionObject.GetComponent<RectTransform>();
        captionRect.anchorMin = new Vector2(0f, 0f);
        captionRect.anchorMax = new Vector2(1f, 0f);
        captionRect.pivot = new Vector2(0.5f, 0f);
        captionRect.sizeDelta = new Vector2(-40f, 90f);
        captionRect.anchoredPosition = new Vector2(0f, 5f);
        var caption = captionObject.GetComponent<Text>();
        caption.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        caption.fontSize = 40;
        caption.color = Color.white;
        caption.alignment = TextAnchor.MiddleCenter;
        caption.text = "Camera view: waiting for landmark_streamer.py";

        var feed = view.AddComponent<VRCameraFeed>();
        feed.image = picture.GetComponent<RawImage>();
        feed.caption = caption;
        Undo.RegisterCreatedObjectUndo(view, "Add camera view");

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("SASL",
            "Camera view added to the whiteboard in VRClassroom (opposite the status board).\n\n" +
            "It shows the webcam picture with the tracked hand points while landmark_streamer.py is running.",
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
            // Multi-pass draws each eye separately. Single Pass Instanced is faster but the
            // in-room boards only appeared in one eye with it; this scene is light enough.
            openxr.renderMode = OpenXRSettings.RenderMode.MultiPass;
            // Foveated rendering requires Single Pass Instanced, so it's switched off.
            foreach (var feature in openxr.GetFeatures())
                if (feature != null && feature.GetType().Name.Contains("Foveat") && feature.enabled)
                    feature.enabled = false;
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

    // VR layout (avatar faces +Z, the learner looks towards -Z, so the learner's right is -X):
    // the avatar stands in front of a big whiteboard on the back wall, like a teacher;
    // the learner stands 2 m in front of the avatar.
    static readonly Vector3 AvatarSpot = new Vector3(0f, 0f, -0.7f);
    static readonly Vector3 ViewerEye = new Vector3(0f, 1.55f, 1.3f);
    const float WallBoardZ = -2.53f;                                        // just in front of the back wall (z = -2.6)
    static readonly Vector3 WallBoardCentre = new Vector3(0f, 2.0f, WallBoardZ);
    static readonly Vector2 WallBoardSize = new Vector2(3.8f, 1.7f);
    // Feedback above the avatar's head; status on the learner's right, away from the
    // avatar's signing (right) hand, which is on the learner's left.
    static readonly Vector3 FeedbackSpot = new Vector3(0f, 2.62f, WallBoardZ + 0.02f);
    static readonly Vector3 StatusSpot = new Vector3(-1.2f, 1.9f, WallBoardZ + 0.02f);
    static readonly Vector2 StatusSizePixels = new Vector2(1000f, 800f);   // x WallPanelScale = 1.3 m x 1.04 m
    const float WallPanelScale = 0.0013f;   // read from about 4 m away
    // Webcam picture on the other side of the board from the status panel.
    static readonly Vector3 CameraViewSpot = new Vector3(1.2f, 1.9f, WallBoardZ + 0.02f);
    static readonly Vector2 CameraViewSizePixels = new Vector2(1000f, 840f);   // x WallPanelScale = 1.3 m x 1.09 m
    const int StatusFontSize = 48;
    const int FeedbackFontSize = 72;

    // Moves the avatar back, puts the big whiteboard behind it and pins both panels onto it.
    static void LayoutWhiteboard(GameObject rig)
    {
        var avatar = UnityEngine.Object.FindAnyObjectByType<AvatarResultReactor>();
        if (avatar != null)
        {
            Undo.RecordObject(avatar.transform, "Move avatar back");
            avatar.transform.SetPositionAndRotation(AvatarSpot, Quaternion.identity);
            PrefabUtility.RecordPrefabInstancePropertyModifications(avatar.transform);
        }

        // The classroom's small corner whiteboard would overlap the big one.
        var classroom = GameObject.Find("Classroom");
        if (classroom != null)
            foreach (var name in new[] { "Whiteboard", "Board Frame" })
            {
                var part = classroom.transform.Find(name);
                if (part != null) part.gameObject.SetActive(false);
            }

        var board = GameObject.Find("VR Whiteboard");
        if (board == null)
        {
            board = new GameObject("VR Whiteboard");
            Undo.RegisterCreatedObjectUndo(board, "Create VR whiteboard");
            AddBox(board, "Frame", "Assets/Materials/Classroom/Frame.mat");
            AddBox(board, "Surface", "Assets/Materials/Classroom/Whiteboard.mat");
        }
        board.transform.SetPositionAndRotation(WallBoardCentre, Quaternion.identity);
        // Sizes are re-applied every run, so changing them above updates an existing board.
        var frame = board.transform.Find("Frame");
        if (frame != null)
        {
            frame.localPosition = new Vector3(0f, 0f, -0.02f);
            frame.localScale = new Vector3(WallBoardSize.x + 0.1f, WallBoardSize.y + 0.1f, 0.03f);
        }
        var surface = board.transform.Find("Surface");
        if (surface != null)
        {
            surface.localPosition = Vector3.zero;
            surface.localScale = new Vector3(WallBoardSize.x, WallBoardSize.y, 0.03f);
        }

        // A world-space canvas is read from its -Z side; the learner looks towards -Z, so turn it 180°.
        var flatOnBoard = Quaternion.Euler(0f, 180f, 0f);
        var result = GameObject.Find("ResultCanvas");
        if (result != null)
        {
            Undo.RecordObject(result.transform, "Pin feedback to whiteboard");
            result.transform.SetPositionAndRotation(FeedbackSpot, flatOnBoard);
            result.transform.localScale = Vector3.one * WallPanelScale;
            var resultText = result.GetComponentInChildren<Text>();
            if (resultText != null) resultText.fontSize = FeedbackFontSize;
        }
        var status = GameObject.Find("VR Status Board");
        if (status != null)
        {
            Undo.RecordObject(status.transform, "Pin status to whiteboard");
            status.transform.SetPositionAndRotation(StatusSpot, flatOnBoard);
            status.transform.localScale = Vector3.one * WallPanelScale;
            ((RectTransform)status.transform).sizeDelta = StatusSizePixels;
            var text = status.GetComponentInChildren<Text>();
            if (text != null) text.fontSize = StatusFontSize;
        }

        // Without a headset (or before recentring) the rig starts at the standing spot.
        if (rig != null)
            rig.transform.SetPositionAndRotation(new Vector3(ViewerEye.x, 0f, ViewerEye.z), Quaternion.Euler(0f, 180f, 0f));
    }

    static void AddBox(GameObject parent, string name, string materialPath)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent.transform, false);
        UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material != null) box.GetComponent<Renderer>().sharedMaterial = material;
    }

    // A world-space canvas is readable from its -Z side, so its forward must point away from the viewer.
    static Quaternion FacingViewer(Vector3 position, bool keepUpright)
    {
        Vector3 away = position - ViewerEye;
        if (keepUpright) away.y = 0f;
        return Quaternion.LookRotation(away, Vector3.up);
    }

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
        var position = new Vector3(0f, 2.05f, 0.1f);   // just above the avatar's head, tilted down towards you
        rect.SetPositionAndRotation(position, FacingViewer(position, keepUpright: false));

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

    // Lesson, sign, status and controls on a board to the learner's right, clear of the avatar.
    static void BuildStatusBoard(GestureClassifierClient classifier)
    {
        // Learner faces -Z, so their right is -X. Far enough aside not to block the avatar's signing arm.
        var position = new Vector3(-1.05f, 1.4f, 0.3f);
        var existing = GameObject.Find("VR Status Board");
        if (existing != null)
        {
            Undo.RecordObject(existing.transform, "Move VR status board");
            existing.transform.SetPositionAndRotation(position, FacingViewer(position, keepUpright: true));
            return;
        }

        var board = new GameObject("VR Status Board", typeof(RectTransform), typeof(Canvas));
        board.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = board.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(900f, 720f);
        rect.localScale = Vector3.one * 0.001f;   // 0.9 m x 0.72 m
        rect.SetPositionAndRotation(position, FacingViewer(position, keepUpright: true));

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
