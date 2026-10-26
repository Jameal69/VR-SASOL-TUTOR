using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Used by the "Start SASL Tutor" launcher (option 1: Run with VR):
///   Unity.exe -projectPath ... -executeMethod SASLLauncher.StartVR
/// Opens the VR scene and enters Play mode once the editor has finished loading.
/// The launcher points OpenXR at the Oculus runtime before starting Unity.
/// </summary>
public static class SASLLauncher
{
    const string VRScenePath = "Assets/Scenes/VRClassroom.unity";

    public static void StartVR()
    {
        EditorApplication.delayCall += OpenAndPlay;
    }

    static void OpenAndPlay()
    {
        // Wait until scripts and assets have finished importing/compiling.
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += OpenAndPlay;
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(VRScenePath) == null)
        {
            Debug.LogError("[SASLLauncher] " + VRScenePath + " not found.");
            return;
        }

        if (EditorSceneManager.GetActiveScene().path != VRScenePath)
            EditorSceneManager.OpenScene(VRScenePath);

        Debug.Log("[SASLLauncher] Opened VRClassroom, entering Play mode.");
        EditorApplication.isPlaying = true;
    }
}
