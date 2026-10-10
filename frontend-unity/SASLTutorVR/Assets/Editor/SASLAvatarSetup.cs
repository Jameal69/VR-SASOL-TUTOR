using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu: SASL > Set Up Avatar
///
/// One click puts the X Bot avatar into MainClassroom, ready for the demo:
///   - X Bot and the Idle animation imported as Humanoid, Idle set to loop
///   - avatar placed facing the Main Camera, camera framed at chest height
///   - AvatarController (Idle) assigned, so no T-pose
///   - a ResultText on screen and AvatarResultReactor wired to the classifier
///   - a simple classroom backdrop built from Unity primitives (no external
///     assets, so no licence questions; swap for a proper model later)
///
/// Safe to run again: it reuses what's already there. Nothing is saved
/// automatically: check the Game view, then Ctrl+S to keep it.
/// </summary>
public static class SASLAvatarSetup
{
    const string ScenePath = "Assets/Scenes/MainClassroom.unity";
    const string XBotPath = "Assets/Models/X Bot.fbx";
    const string IdlePath = "Assets/Animations/X Bot@Idle.fbx";
    const string ControllerPath = "Assets/Animations/AvatarController.controller";
    const string AvatarName = "Avatar (X Bot)";
    const string CanvasName = "ResultCanvas";
    const string TextName = "ResultText";
    const string ClassroomName = "Classroom";
    const string MaterialsFolder = "Assets/Materials/Classroom";

    [MenuItem("SASL/Set Up Avatar")]
    public static void SetUp()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("SASL", "Stop Play mode first, then run Set Up Avatar again.", "OK");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            scene = EditorSceneManager.OpenScene(ScenePath);
        }

        // 1. Import settings: Humanoid rigs (needed for the Idle and the head bone), looping Idle.
        if (!MakeHumanoid(XBotPath, loop: false) || !MakeHumanoid(IdlePath, loop: true)) return;

        // 2. Animator controller with Idle as the default state.
        var idleClip = AssetDatabase.LoadAllAssetsAtPath(IdlePath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(ControllerPath, idleClip);
        }
        else
        {
            var stateMachine = controller.layers[0].stateMachine;
            var state = stateMachine.defaultState != null
                ? stateMachine.defaultState
                : stateMachine.states.Select(s => s.state).FirstOrDefault();
            if (state != null && state.motion == null)
            {
                state.motion = idleClip;
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
            }
        }

        // 3. The avatar, at the origin facing +Z (towards the camera below).
        var avatar = GameObject.Find(AvatarName);
        if (avatar == null)
        {
            // MainClassroom already has a plain "X Bot" placed in it: reuse that
            // one instead of creating a second avatar inside it.
            var existing = GameObject.Find("X Bot");
            if (existing != null && PrefabUtility.GetCorrespondingObjectFromSource(existing) == AssetDatabase.LoadAssetAtPath<GameObject>(XBotPath))
            {
                Undo.RecordObject(existing, "Rename avatar");
                existing.name = AvatarName;
                avatar = existing;
            }
        }
        if (avatar == null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(XBotPath);
            avatar = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            avatar.name = AvatarName;
            Undo.RegisterCreatedObjectUndo(avatar, "Create avatar");
        }
        Undo.RecordObject(avatar.transform, "Place avatar");
        avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var animator = avatar.GetComponent<Animator>();
        if (animator == null) animator = Undo.AddComponent<Animator>(avatar);
        Undo.RecordObject(animator, "Set up animator");
        animator.runtimeAnimatorController = controller;
        animator.avatar = AssetDatabase.LoadAllAssetsAtPath(XBotPath).OfType<Avatar>().FirstOrDefault();
        animator.applyRootMotion = false;
        // The avatar is a prefab instance of X Bot.fbx: record these as overrides,
        // otherwise they're lost on save / entering Play mode (back to T-pose).
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        PrefabUtility.RecordPrefabInstancePropertyModifications(avatar.transform);

        // 4. Camera in front of the avatar at chest height, avatar filling most of the frame.
        var camera = Camera.main;
        if (camera == null) camera = Object.FindFirstObjectByType<Camera>();
        if (camera != null)
        {
            Undo.RecordObject(camera.transform, "Frame avatar");
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.3f, 1.8f), Quaternion.Euler(4f, 180f, 0f));
        }

        // 5. Result text on screen, and the reactor that drives it.
        var text = FindOrCreateResultText();
        var reactor = avatar.GetComponent<AvatarResultReactor>();
        if (reactor == null) reactor = Undo.AddComponent<AvatarResultReactor>(avatar);
        Undo.RecordObject(reactor, "Wire avatar reactor");
        reactor.classifier = Object.FindFirstObjectByType<GestureClassifierClient>();
        reactor.resultText = text;

        // 6. Room around the avatar.
        BuildClassroom();

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = avatar;

        string warning = reactor.classifier == null
            ? "\n\nWarning: no GestureClassifierClient found in the scene, so the avatar won't react."
            : "";
        Debug.Log("[SASLAvatarSetup] Avatar set up in " + ScenePath + warning);
        EditorUtility.DisplayDialog("SASL",
            "Avatar set up.\n\nCheck the Game view (avatar idle, text at the bottom), then press Ctrl+S to save the scene." + warning,
            "OK");
    }

    /// <summary>
    /// Floor, walls, a whiteboard and a desk behind the avatar. The avatar stands
    /// at the origin facing +Z and the camera looks back along -Z, so everything
    /// here sits at negative Z. Rebuilt from scratch each time it runs.
    /// </summary>
    [MenuItem("SASL/Rebuild Classroom Backdrop")]
    public static void BuildClassroom()
    {
        var old = GameObject.Find(ClassroomName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var room = new GameObject(ClassroomName);
        Undo.RegisterCreatedObjectUndo(room, "Build classroom");

        var floor = ClassroomMaterial("Floor", new Color(0.62f, 0.60f, 0.57f));
        var wall = ClassroomMaterial("Wall", new Color(0.80f, 0.86f, 0.92f));
        var board = ClassroomMaterial("Whiteboard", new Color(0.96f, 0.96f, 0.94f));
        var frame = ClassroomMaterial("Frame", new Color(0.25f, 0.27f, 0.30f));
        var wood = ClassroomMaterial("Wood", new Color(0.55f, 0.38f, 0.24f));

        //                   name           position                     size (x, y, z)              material
        Box(room, "Floor",       new Vector3(0f, -0.05f, -0.5f),  new Vector3(8f, 0.1f, 7f),       floor);
        Box(room, "Back Wall",   new Vector3(0f, 1.75f, -2.6f),   new Vector3(8f, 3.5f, 0.1f),     wall);
        Box(room, "Left Wall",   new Vector3(-4f, 1.75f, -0.5f),  new Vector3(0.1f, 3.5f, 4.3f),   wall);
        Box(room, "Right Wall",  new Vector3(4f, 1.75f, -0.5f),   new Vector3(0.1f, 3.5f, 4.3f),   wall);
        Box(room, "Board Frame", new Vector3(1.9f, 1.7f, -2.53f), new Vector3(2.3f, 1.3f, 0.04f),  frame);
        Box(room, "Whiteboard",  new Vector3(1.9f, 1.7f, -2.50f), new Vector3(2.2f, 1.2f, 0.04f),  board);
        Box(room, "Desk Top",    new Vector3(-2f, 0.74f, -1.4f),  new Vector3(1.4f, 0.06f, 0.7f),  wood);
        foreach (float x in new[] { -2.6f, -1.4f })
            foreach (float z in new[] { -1.65f, -1.15f })
                Box(room, "Desk Leg", new Vector3(x, 0.36f, z), new Vector3(0.06f, 0.72f, 0.06f), wood);

        EditorSceneManager.MarkSceneDirty(room.scene);
    }

    static void Box(GameObject parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent.transform, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(box.GetComponent<Collider>());   // decoration only
    }

    // Saved as assets so the scene keeps its colours after it's reopened.
    static Material ClassroomMaterial(string name, Color colour)
    {
        if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "Classroom");

        string path = MaterialsFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", colour);   // URP Lit
        material.color = colour;                   // Standard fallback
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    static bool MakeHumanoid(string path, bool loop)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("SASL", "Missing " + path + ". Pull the latest branch and try again.", "OK");
            return false;
        }

        bool changed = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            changed = true;
        }

        if (loop)
        {
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                if (clip.loopTime) continue;
                clip.loopTime = true;
                // Bake root motion into the pose so the avatar stays in place.
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                changed = true;
            }
            importer.clipAnimations = clips;
        }

        if (changed) importer.SaveAndReimport();
        return true;
    }

    static Text FindOrCreateResultText()
    {
        var canvasObject = GameObject.Find(CanvasName);
        if (canvasObject == null)
        {
            canvasObject = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create result canvas");
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        var existing = canvasObject.transform.Find(TextName);
        if (existing != null) return existing.GetComponent<Text>();

        // Bottom centre, so it doesn't overlap GestureClassifierClient's status panel (top left).
        var textObject = new GameObject(TextName, typeof(RectTransform), typeof(Text), typeof(Outline));
        Undo.RegisterCreatedObjectUndo(textObject, "Create result text");
        textObject.transform.SetParent(canvasObject.transform, false);

        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 60f);
        rect.sizeDelta = new Vector2(1400f, 120f);

        var text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 56;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = "Sign: Hello";

        var outline = textObject.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);

        return text;
    }
}
