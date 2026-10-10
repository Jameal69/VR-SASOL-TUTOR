using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Sign animations for the avatar.
///
/// SASL > Build Sign Animations
///   1. Generates one clip per sign from the reference recordings in
///      ml-service/references_raw/: the average recorded hand shape, with the
///      right arm raised into signing space. Good for STATIC signs (most letters
///      and numbers). Moving signs (greetings, J, Z) get a static placeholder
///      until a hand-made clip exists.
///   2. Collects hand-made clips from Assets/Animations/Signs/Handmade/
///      (e.g. Blender FBX exports, file named after the sign: hello.fbx). A
///      hand-made clip always wins over a generated one.
///   3. Adds the "Signing" layer (right arm + hand only) to AvatarController and
///      an AvatarSignPlayer to the avatar in the open scene.
///
/// SASL > Render Sign Previews
///   Saves a picture of every sign to SignPreviews/ (next to Assets), so signs
///   can be checked against RealSASL without opening the animation tools.
///
/// How a pose is made: a hidden copy of X Bot is posed by pointing each finger
/// bone along the recorded finger direction (rig-independent), then Unity's
/// HumanPoseHandler turns that pose into Humanoid muscle values for the clip.
/// </summary>
public static class SASLSignAnimations
{
    const string ControllerPath = "Assets/Animations/AvatarController.controller";
    const string XBotPath = "Assets/Models/X Bot.fbx";
    const string IdlePath = "Assets/Animations/X Bot@Idle.fbx";
    const string SignsRoot = "Assets/Animations/Signs";
    const string GeneratedDir = SignsRoot + "/Generated";
    const string HandmadeDir = SignsRoot + "/Handmade";
    const string PlaceholderPath = SignsRoot + "/SignPlaceholder.anim";
    const string MaskPath = SignsRoot + "/RightArmMask.mask";
    const string LibraryPath = SignsRoot + "/SignLibrary.asset";
    const string LayerName = "Signing";
    const string StateName = "Sign";
    const float ClipSeconds = 1f;

    // Webcam frames are 4:3; MediaPipe x and z are in image-width units, y in height units.
    const float ImageAspect = 4f / 3f;

    static string ReferencesDir =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "ml-service", "references_raw"));

    static string PreviewDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "SignPreviews"));

    // Where the right hand goes, relative to the right shoulder, in metres
    // (x = avatar's right, y = up, z = forward). First approximations from the
    // lesson descriptions; to be checked against RealSASL.
    static readonly Dictionary<string, Vector3> SignLocations = new Dictionary<string, Vector3>
    {
        { "hello",     new Vector3( 0.14f,  0.20f, 0.14f) },  // beside the head
        { "goodbye",   new Vector3( 0.14f,  0.08f, 0.32f) },  // in front, shoulder height
        { "please",    new Vector3(-0.14f, -0.16f, 0.24f) },  // chest
        { "thank_you", new Vector3(-0.06f,  0.06f, 0.30f) },  // just below and in front of the chin
    };
    static readonly Vector3 DefaultLocation = new Vector3(0.10f, 0.02f, 0.30f);  // fingerspelling space

    // Signs that move: their generated clip is only a placeholder.
    static readonly HashSet<string> MovingSigns = new HashSet<string> { "hello", "goodbye", "please", "thank_you", "j", "z" };

    // ------------------------------------------------------------------ menu

    [MenuItem("SASL/Build Sign Animations")]
    public static void BuildFromMenu()
    {
        var report = Build(attachToOpenScene: true);
        EditorUtility.DisplayDialog("SASL", report + "\n\nPress Ctrl+S to save the scene, then Play: keys 1-4 show a sign, D repeats it.", "OK");
    }

    [MenuItem("SASL/Render Sign Previews")]
    public static void RenderPreviewsFromMenu()
    {
        int count = RenderPreviews();
        EditorUtility.RevealInFinder(PreviewDir);
        Debug.Log("[SASLSignAnimations] Rendered " + count + " previews to " + PreviewDir);
    }

    // Headless entry point: build (without touching the scene) and render previews.
    public static void BatchBuildAndPreview()
    {
        Debug.Log("[SASLSignAnimations] " + Build(attachToOpenScene: false));
        CheckCurveNamesAgainstIdle();
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { GeneratedDir }))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
            Debug.Log($"[SASLSignAnimations] CHECK clip '{clip.name}': humanMotion={clip.humanMotion}, curves={AnimationUtility.GetCurveBindings(clip).Length}, length={clip.length:0.00}s");
        }
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Debug.Log("[SASLSignAnimations] CHECK controller layers: " + string.Join(", ", controller.layers.Select(l => $"{l.name}(mask={(l.avatarMask != null ? l.avatarMask.name : "none")}, states={l.stateMachine.states.Length})")));
        Debug.Log("[SASLSignAnimations] Rendered " + RenderPreviews() + " previews to " + PreviewDir);
    }

    // ------------------------------------------------------------------ build

    public static string Build(bool attachToOpenScene)
    {
        EnsureFolder("Assets/Animations", "Signs");
        EnsureFolder(SignsRoot, "Generated");
        EnsureFolder(SignsRoot, "Handmade");

        var generated = GenerateClips(out var skipped);
        var library = BuildLibrary(out int handmadeCount);
        var placeholder = EnsurePlaceholder();
        var mask = EnsureMask();
        EnsureSigningLayer(placeholder, mask);
        AssetDatabase.SaveAssets();

        string attached = "";
        if (attachToOpenScene)
            attached = AttachPlayer(library, placeholder) ? "\nAvatarSignPlayer added to the avatar." : "\nNo avatar in the open scene (open MainClassroom and run again).";

        string skippedText = skipped.Count > 0 ? "\nSkipped: " + string.Join(", ", skipped) : "";
        return $"Generated {generated.Count} clip(s) from recordings, found {handmadeCount} hand-made clip(s); " +
               $"library has {library.entries.Count} sign(s).{skippedText}{attached}";
    }

    static List<string> GenerateClips(out List<string> skipped)
    {
        skipped = new List<string>();
        var done = new List<string>();
        if (!Directory.Exists(ReferencesDir))
        {
            skipped.Add("no recordings folder at " + ReferencesDir);
            return done;
        }

        using (var rig = new PosingRig())
        {
            foreach (var signDir in Directory.GetDirectories(ReferencesDir).OrderBy(d => d))
            {
                string sign = Path.GetFileName(signDir);
                var shape = AverageHandShape(Directory.GetFiles(signDir, "*.npy"));
                if (shape == null) { skipped.Add(sign + " (no usable right-hand frames)"); continue; }

                var location = SignLocations.TryGetValue(sign, out var loc) ? loc : DefaultLocation;
                var muscles = rig.Pose(shape, location);

                var clip = new AnimationClip { name = sign };
                foreach (var (index, value) in muscles)
                {
                    var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), ClipCurveName(HumanTrait.MuscleName[index]));
                    AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, ClipSeconds, value));
                }
                string path = $"{GeneratedDir}/{sign}.anim";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(clip, path);
                done.Add(sign + (MovingSigns.Contains(sign) ? " (placeholder: moving sign)" : ""));
            }
        }
        return done;
    }

    static SignLibrary BuildLibrary(out int handmadeCount)
    {
        var library = AssetDatabase.LoadAssetAtPath<SignLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<SignLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        library.entries.Clear();

        var bySign = new Dictionary<string, SignLibrary.Entry>();
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { GeneratedDir }))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip != null) bySign[SignLibrary.Normalise(clip.name)] = new SignLibrary.Entry { sign = clip.name, clip = clip };
        }

        handmadeCount = 0;
        foreach (var guid in AssetDatabase.FindAssets("", new[] { HandmadeDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string sign = Path.GetFileNameWithoutExtension(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) continue;
            bySign[SignLibrary.Normalise(sign)] = new SignLibrary.Entry { sign = sign, clip = clip, handmade = true };
            handmadeCount++;
        }

        library.entries.AddRange(bySign.Values.OrderBy(e => e.sign));
        EditorUtility.SetDirty(library);
        return library;
    }

    static AnimationClip EnsurePlaceholder()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(PlaceholderPath);
        if (clip != null) return clip;
        clip = new AnimationClip { name = "SignPlaceholder" };
        AssetDatabase.CreateAsset(clip, PlaceholderPath);
        return clip;
    }

    static AvatarMask EnsureMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, MaskPath);
        }
        for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++)
            mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.RightFingers);
        EditorUtility.SetDirty(mask);
        return mask;
    }

    static void EnsureSigningLayer(AnimationClip placeholder, AvatarMask mask)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) throw new Exception("Missing " + ControllerPath);
        if (controller.layers.Any(l => l.name == LayerName)) return;

        controller.AddLayer(LayerName);
        var layers = controller.layers;
        var layer = layers[layers.Length - 1];
        layer.avatarMask = mask;
        layer.defaultWeight = 0f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        var empty = layer.stateMachine.AddState("Empty");
        var sign = layer.stateMachine.AddState(StateName);
        sign.motion = placeholder;
        layer.stateMachine.defaultState = empty;
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
    }

    static bool AttachPlayer(SignLibrary library, AnimationClip placeholder)
    {
        var reactor = UnityEngine.Object.FindFirstObjectByType<AvatarResultReactor>();
        if (reactor == null) return false;
        var avatar = reactor.gameObject;
        var player = avatar.GetComponent<AvatarSignPlayer>();
        if (player == null) player = Undo.AddComponent<AvatarSignPlayer>(avatar);
        Undo.RecordObject(player, "Set up sign player");
        player.library = library;
        player.placeholderClip = placeholder;
        player.classifier = UnityEngine.Object.FindFirstObjectByType<GestureClassifierClient>();
        EditorSceneManager.MarkSceneDirty(avatar.scene);
        return true;
    }

    // ------------------------------------------------------- recordings -> shape

    /// <summary>
    /// The typical right-hand shape of a sign: 21 landmarks relative to the
    /// wrist, scaled to hand size, as the median over the middle half of every
    /// recording (where the sign is held). Image axes: x right, y down, z away.
    /// </summary>
    static Vector3[] AverageHandShape(IEnumerable<string> files)
    {
        var samples = new List<Vector3[]>();
        foreach (var file in files)
        {
            float[,] data;
            try { data = NpyReader.ReadFloat2D(file); }
            catch (Exception e) { Debug.LogWarning("[SASLSignAnimations] Skipping " + file + ": " + e.Message); continue; }
            if (data.GetLength(1) != 126) continue;

            var present = new List<int>();
            for (int f = 0; f < data.GetLength(0); f++)
                for (int c = 63; c < 126; c++)
                    if (data[f, c] != 0f) { present.Add(f); break; }
            if (present.Count < 4) continue;

            for (int k = present.Count / 4; k < present.Count * 3 / 4; k++)
            {
                int f = present[k];
                var pts = new Vector3[21];
                for (int j = 0; j < 21; j++)
                    pts[j] = new Vector3(data[f, 63 + j * 3] * ImageAspect, data[f, 63 + j * 3 + 1], data[f, 63 + j * 3 + 2] * ImageAspect);
                float size = (pts[9] - pts[0]).magnitude;
                if (size < 1e-5f) continue;
                for (int j = 20; j >= 0; j--) pts[j] = (pts[j] - pts[0]) / size;
                samples.Add(pts);
            }
        }
        if (samples.Count == 0) return null;

        var shape = new Vector3[21];
        for (int j = 0; j < 21; j++)
            shape[j] = new Vector3(Median(samples.Select(s => s[j].x)), Median(samples.Select(s => s[j].y)), Median(samples.Select(s => s[j].z)));
        return shape;
    }

    static float Median(IEnumerable<float> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : 0.5f * (sorted[n / 2 - 1] + sorted[n / 2]);
    }

    // ------------------------------------------------------- shape -> muscles

    /// <summary>A hidden X Bot used to turn a hand shape into Humanoid muscle values.</summary>
    sealed class PosingRig : IDisposable
    {
        readonly GameObject root;
        readonly Animator animator;
        readonly HumanPoseHandler handler;
        readonly Dictionary<Transform, Quaternion> restPose = new Dictionary<Transform, Quaternion>();

        // MediaPipe landmark chains (joint indices from base to tip) per finger,
        // matched to the Humanoid finger bones.
        static readonly (HumanBodyBones bone, int from, int to)[] FingerSegments =
        {
            (HumanBodyBones.RightThumbProximal, 1, 2),   (HumanBodyBones.RightThumbIntermediate, 2, 3),   (HumanBodyBones.RightThumbDistal, 3, 4),
            (HumanBodyBones.RightIndexProximal, 5, 6),   (HumanBodyBones.RightIndexIntermediate, 6, 7),   (HumanBodyBones.RightIndexDistal, 7, 8),
            (HumanBodyBones.RightMiddleProximal, 9, 10), (HumanBodyBones.RightMiddleIntermediate, 10, 11), (HumanBodyBones.RightMiddleDistal, 11, 12),
            (HumanBodyBones.RightRingProximal, 13, 14),  (HumanBodyBones.RightRingIntermediate, 14, 15),  (HumanBodyBones.RightRingDistal, 15, 16),
            (HumanBodyBones.RightLittleProximal, 17, 18), (HumanBodyBones.RightLittleIntermediate, 18, 19), (HumanBodyBones.RightLittleDistal, 19, 20),
        };

        public PosingRig()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(XBotPath);
            root = UnityEngine.Object.Instantiate(model);
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            animator = root.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                throw new Exception("X Bot is not set up as Humanoid. Run SASL > Set Up Avatar first.");
            animator.Rebind();  // outside Play mode the bone lookup needs an initialised Animator
            handler = new HumanPoseHandler(animator.avatar, root.transform);
            foreach (var t in root.GetComponentsInChildren<Transform>()) restPose[t] = t.localRotation;
        }

        Transform Bone(HumanBodyBones b) => animator.GetBoneTransform(b);

        /// <summary>Image-space direction -> world direction for an avatar facing the camera.</summary>
        Vector3 World(Vector3 image) => root.transform.TransformDirection(new Vector3(-image.x, -image.y, -image.z));

        public List<(int index, float value)> Pose(Vector3[] shape, Vector3 handOffset)
        {
            foreach (var kv in restPose) kv.Key.localRotation = kv.Value;
            var t = root.transform;

            // Arm: simple two-bone IK so the wrist lands at the sign's location.
            var upper = Bone(HumanBodyBones.RightUpperArm);
            var lower = Bone(HumanBodyBones.RightLowerArm);
            var hand = Bone(HumanBodyBones.RightHand);
            Vector3 shoulder = upper.position;
            Vector3 target = shoulder + t.right * handOffset.x + t.up * handOffset.y + t.forward * handOffset.z;
            float a = (lower.position - upper.position).magnitude;
            float b = (hand.position - lower.position).magnitude;
            Vector3 toTarget = target - shoulder;
            float d = Mathf.Clamp(toTarget.magnitude, 0.05f, a + b - 0.001f);
            Vector3 dir = toTarget.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(-t.up * 0.35f + t.right * 0.2f - t.forward * 0.05f, dir).normalized;
            float angle = Mathf.Acos(Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f));
            Vector3 elbow = shoulder + (dir * Mathf.Cos(angle) + pole * Mathf.Sin(angle)) * a;
            Aim(upper, lower.position, elbow);
            Aim(lower, hand.position, shoulder + dir * d);

            // Palm: match the recorded hand's orientation (fingers direction + across the knuckles).
            Vector3 targetFingers = World(shape[9] - shape[0]);
            Vector3 targetAcross = World(shape[5] - shape[17]);
            Vector3 avatarFingers = Bone(HumanBodyBones.RightMiddleProximal).position - hand.position;
            Vector3 avatarAcross = Bone(HumanBodyBones.RightIndexProximal).position - Bone(HumanBodyBones.RightLittleProximal).position;
            hand.rotation = Frame(targetFingers, targetAcross) * Quaternion.Inverse(Frame(avatarFingers, avatarAcross)) * hand.rotation;

            // Fingers: point every bone along the recorded segment, base to tip.
            foreach (var (bone, from, to) in FingerSegments)
            {
                var tr = Bone(bone);
                if (tr == null || tr.childCount == 0) continue;
                var child = tr.GetChild(0);
                float length = (child.position - tr.position).magnitude;
                Aim(tr, child.position, tr.position + World(shape[to] - shape[from]).normalized * length);
            }

            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            var result = new List<(int, float)>();
            for (int i = 0; i < HumanTrait.MuscleCount; i++)
                if (IsRightArmOrHand(HumanTrait.MuscleName[i])) result.Add((i, pose.muscles[i]));
            return result;
        }

        static Quaternion Frame(Vector3 along, Vector3 across) =>
            Quaternion.LookRotation(along.normalized, Vector3.Cross(along, across).normalized);

        static void Aim(Transform bone, Vector3 childPosition, Vector3 targetPosition)
        {
            bone.rotation = Quaternion.FromToRotation(childPosition - bone.position, targetPosition - bone.position) * bone.rotation;
        }

        public void Dispose()
        {
            handler?.Dispose();
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static bool IsRightArmOrHand(string muscle) =>
        Regex.IsMatch(muscle, @"^Right (Shoulder|Arm|Forearm|Hand|Thumb|Index|Middle|Ring|Little) ");

    /// <summary>
    /// Humanoid clips name finger muscles differently from HumanTrait:
    /// "Right Index 1 Stretched" -> "RightHand.Index.1 Stretched", "Right Index Spread" -> "RightHand.Index.Spread".
    /// Body muscles keep their name ("Right Arm Down-Up").
    /// </summary>
    static string ClipCurveName(string muscle)
    {
        var m = Regex.Match(muscle, @"^(Left|Right) (Thumb|Index|Middle|Ring|Little) (.+)$");
        return m.Success ? $"{m.Groups[1].Value}Hand.{m.Groups[2].Value}.{m.Groups[3].Value}" : muscle;
    }

    static int MuscleIndexForCurve(string curveName)
    {
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
            if (ClipCurveName(HumanTrait.MuscleName[i]) == curveName) return i;
        return -1;
    }

    // Confirms our curve names are the ones Unity uses, by comparing with the imported Mixamo Idle clip.
    static void CheckCurveNamesAgainstIdle()
    {
        var idle = AssetDatabase.LoadAllAssetsAtPath(IdlePath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (idle == null) { Debug.LogWarning("[SASLSignAnimations] CHECK: no Idle clip to compare with."); return; }
        var idleNames = new HashSet<string>(AnimationUtility.GetCurveBindings(idle).Select(b => b.propertyName));
        var ours = Enumerable.Range(0, HumanTrait.MuscleCount).Select(i => HumanTrait.MuscleName[i]).Where(IsRightArmOrHand).Select(ClipCurveName).ToList();
        var missing = ours.Where(n => !idleNames.Contains(n)).ToList();
        Debug.Log(missing.Count == 0
            ? $"[SASLSignAnimations] CHECK PASS: all {ours.Count} right arm/hand curve names match Unity's Humanoid names."
            : $"[SASLSignAnimations] CHECK FAIL: {missing.Count} names not used by Unity: {string.Join(", ", missing.Take(8))}");
    }

    // ------------------------------------------------------------------ previews

    public static int RenderPreviews()
    {
        var library = AssetDatabase.LoadAssetAtPath<SignLibrary>(LibraryPath);
        if (library == null || library.entries.Count == 0) return 0;
        Directory.CreateDirectory(PreviewDir);

        // A separate preview scene, so the open scene (classroom, avatar) isn't in the picture.
        var previewScene = EditorSceneManager.NewPreviewScene();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(XBotPath);
        var avatar = UnityEngine.Object.Instantiate(model);
        avatar.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(avatar, previewScene);
        avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var animator = avatar.GetComponent<Animator>();
        animator.Rebind();
        var handler = new HumanPoseHandler(animator.avatar, avatar.transform);

        // Start from the Idle pose so the rest of the body looks natural.
        var basePose = new HumanPose();
        handler.GetHumanPose(ref basePose);
        var idle = AssetDatabase.LoadAllAssetsAtPath(IdlePath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (idle != null) ApplyClipMuscles(idle, 0f, basePose.muscles);

        var cameraObject = new GameObject("SignPreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.82f, 0.86f, 0.9f);
        camera.fieldOfView = 30f;
        camera.transform.SetPositionAndRotation(new Vector3(0f, 1.35f, 2.2f), Quaternion.Euler(0f, 180f, 0f));
        camera.scene = previewScene;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
        var lightObject = new GameObject("SignPreviewLight") { hideFlags = HideFlags.HideAndDontSave };
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(30f, 160f, 0f);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, previewScene);

        // Outside Play mode a SkinnedMeshRenderer can keep showing its first pose,
        // so each pose is baked into a plain mesh and that is what gets rendered.
        var bakes = new List<(SkinnedMeshRenderer source, Mesh mesh)>();
        foreach (var smr in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var holder = new GameObject("Baked " + smr.name) { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, previewScene);
            var mesh = new Mesh();
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            holder.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
            smr.enabled = false;
            bakes.Add((smr, mesh));
        }

        var texture = new RenderTexture(640, 640, 24);
        var image = new Texture2D(640, 640, TextureFormat.RGB24, false);
        camera.targetTexture = texture;
        int count = 0;
        try
        {
            foreach (var entry in library.entries.Where(e => e.clip != null))
            {
                var pose = basePose;
                pose.muscles = (float[])basePose.muscles.Clone();
                ApplyClipMuscles(entry.clip, entry.clip.length * 0.5f, pose.muscles);
                handler.SetHumanPose(ref pose);
                avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var (source, mesh) in bakes)
                {
                    source.BakeMesh(mesh);
                    mesh.RecalculateBounds();
                }

                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 640, 640), 0, 0);
                image.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(PreviewDir, entry.sign + (entry.handmade ? "" : "_generated") + ".png"), image.EncodeToPNG());
                count++;
            }
        }
        finally
        {
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(lightObject);
            handler.Dispose();
            foreach (var (_, mesh) in bakes) UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(avatar);
            EditorSceneManager.ClosePreviewScene(previewScene);  // also removes the baked holders
        }
        return count;
    }

    static void ApplyClipMuscles(AnimationClip clip, float time, float[] muscles)
    {
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            int index = MuscleIndexForCurve(binding.propertyName);
            if (index >= 0) muscles[index] = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(time);
        }
    }

    // ------------------------------------------------------------------ helpers

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    /// <summary>Minimal reader for the .npy files our recorder writes (2-D float32/float64, little-endian).</summary>
    static class NpyReader
    {
        public static float[,] ReadFloat2D(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.ReadByte() != 0x93 || new string(reader.ReadChars(5)) != "NUMPY") throw new Exception("not a .npy file");
                byte major = reader.ReadByte();
                reader.ReadByte();
                int headerLength = major == 1 ? reader.ReadUInt16() : (int)reader.ReadUInt32();
                string header = new string(reader.ReadChars(headerLength));
                if (header.Contains("'fortran_order': True")) throw new Exception("fortran order not supported");
                var shape = Regex.Match(header, @"'shape': \((\d+), (\d+)\)");
                if (!shape.Success) throw new Exception("expected a 2-D array");
                int rows = int.Parse(shape.Groups[1].Value), cols = int.Parse(shape.Groups[2].Value);
                bool isDouble = header.Contains("'<f8'");
                if (!isDouble && !header.Contains("'<f4'")) throw new Exception("expected float32/float64");

                var data = new float[rows, cols];
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        data[r, c] = isDouble ? (float)reader.ReadDouble() : reader.ReadSingle();
                return data;
            }
        }
    }
}
