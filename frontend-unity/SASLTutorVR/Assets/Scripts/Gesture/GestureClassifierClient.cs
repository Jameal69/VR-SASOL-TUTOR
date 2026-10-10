using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Shape of the backend's /api/gestures/classify response. JsonUtility ignores any extra fields in the backend returns, so this only lists what we display.

/// </summary>
[Serializable]
public class GestureClassifyResponse
{
    public string predicted_sign;
    public float confidence_score;
    // Set by the backend when tracking was too poor to classify
    // ("body_not_visible" / "hand_not_visible" / "too_short"). Empty for a normal result.
    public string retry_reason;
}

/// <summary>Shape of the backend's /api/curriculum/catalog response (lesson browser).</summary>
[Serializable]
public class CatalogSign
{
    public string curriculum_item_id;
    public string sign_name;
    public string description;
}

[Serializable]
public class CatalogLesson
{
    public string lesson_id;
    public string title;
    public CatalogSign[] signs;
}

[Serializable]
public class CatalogResponse
{
    public CatalogLesson[] lessons;
}

/// <summary>
/// Connects to landmark_streamer.py's socket, buffers hand frames while the
/// player is "recording" an attempt, sends the sequence to the real backend's
/// /api/gestures/classify endpoint, and shows the result ON SCREEN (Game view)
/// as well as in the Console.
///
/// Controls (Play mode, Game view focused): SPACE to start/stop recording.
///
/// IMPORTANT: this script opens its own connection to port 5052, the same one
/// LandmarkReceiver uses. Only ONE of them should be enabled at a time.
/// Untick LandmarkReceiver in the Inspector when using this script.
///
/// Lesson browser (Play mode): the lessons and signs are loaded from the
/// backend's /api/curriculum/catalog when Play starts, so no IDs need typing.
///   Left/Right arrows (or [ and ])  previous / next lesson
///   Up/Down arrows                  previous / next sign in the lesson
///   1-9                             jump to that sign in the lesson
/// If the backend isn't reachable, the built-in Lesson 1 list is used.
/// The Inspector fields below set the starting sign.
/// </summary>
public class GestureClassifierClient : MonoBehaviour
{
    // Fallback when the backend's catalog can't be loaded (ids from the live curriculum_items table).
    static List<CatalogLesson> BuiltInLessons() => new List<CatalogLesson>
    {
        new CatalogLesson
        {
            lesson_id = "", title = "Lesson 1: Introduction",
            signs = new[]
            {
                new CatalogSign { curriculum_item_id = "701d037f-2703-47a5-a09b-5eb3effa08f5", sign_name = "Hello" },
                new CatalogSign { curriculum_item_id = "fab6a4b5-d6ef-4166-afad-3aaa8503daed", sign_name = "Goodbye" },
                new CatalogSign { curriculum_item_id = "a99dfe87-1cbd-4700-a4da-ce2947b58ac3", sign_name = "Please" },
                new CatalogSign { curriculum_item_id = "8160b4f7-752e-4e60-b0f6-9565b890c080", sign_name = "Thank You" },
            },
        },
    };

    /// <summary>Fired on the main thread when the backend answers: (predicted sign, confidence 0-1, is MATCH).</summary>
    public event Action<string, float, bool> ResultReceived;

    /// <summary>Fired when the target sign changes (lesson browser keys), with the new sign name.</summary>
    public event Action<string> TargetChanged;

    /// <summary>Fired instead of ResultReceived when tracking was too poor to judge the attempt, with a message for the user.</summary>
    public event Action<string> RetryRequested;

    [Tooltip("Real curriculum_item_id for the sign you're testing (from /api/curriculum/lessons in /docs).")]
    public string curriculumItemId = "701d037f-2703-47a5-a09b-5eb3effa08f5";

    [Tooltip("Name of the sign being tested, e.g. Hello, Goodbye, Please, Thank You. Used for the on-screen prompt and match check.")]
    public string expectedSignName = "Hello";

    private const string BackendUrl = "http://localhost:8000/api/gestures/classify";
    private const string CatalogUrl = "http://localhost:8000/api/curriculum/catalog";

    // Lesson browser state (main thread only).
    private List<CatalogLesson> lessons = BuiltInLessons();
    private bool catalogFromBackend = false;
    private int lessonIndex = 0;
    private int signIndex = 0;

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private volatile bool keepRunning = true;
    private volatile bool streamConnected = false;
    private volatile bool isRecording = false;

    private readonly object bufferLock = new object();
    private List<string> rawFrameBuffer = new List<string>();

    // On-screen display state (only touched on the main thread).
    private string statusText = "Waiting for camera stream...";
    private string resultText = "";
    private bool? lastWasMatch = null;

    void Start()
    {
        receiveThread = new Thread(ConnectAndListen);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        FindCurrentSign();
        StartCoroutine(LoadCatalog());
    }

    System.Collections.IEnumerator LoadCatalog()
    {
        using (var request = UnityWebRequest.Get(CatalogUrl))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[GestureClassifierClient] Couldn't load lessons from the backend (" + request.error + "), using built-in Lesson 1.");
                yield break;
            }

            CatalogResponse catalog = null;
            try { catalog = JsonUtility.FromJson<CatalogResponse>(request.downloadHandler.text); }
            catch (Exception e) { Debug.LogWarning("[GestureClassifierClient] Couldn't read the lesson catalog: " + e.Message); }

            var loaded = new List<CatalogLesson>();
            if (catalog != null && catalog.lessons != null)
                foreach (var lesson in catalog.lessons)
                    if (lesson.signs != null && lesson.signs.Length > 0) loaded.Add(lesson);
            if (loaded.Count == 0) yield break;

            lessons = loaded;
            catalogFromBackend = true;
            FindCurrentSign();
            int signCount = 0;
            foreach (var lesson in lessons) signCount += lesson.signs.Length;
            Debug.Log("[GestureClassifierClient] Loaded " + lessons.Count + " lessons, " + signCount + " signs from the backend.");
        }
    }

    // Points the browser at the sign set in the Inspector (curriculumItemId), if it's in the list.
    void FindCurrentSign()
    {
        for (int l = 0; l < lessons.Count; l++)
            for (int s = 0; s < lessons[l].signs.Length; s++)
                if (lessons[l].signs[s].curriculum_item_id == curriculumItemId)
                {
                    lessonIndex = l;
                    signIndex = s;
                    expectedSignName = lessons[l].signs[s].sign_name;
                    return;
                }
    }

    void ConnectAndListen()
    {
        try
        {
            client = new TcpClient("127.0.0.1", 5052);
            stream = client.GetStream();
            streamConnected = true;
            Debug.Log("[GestureClassifierClient] Connected to Python landmark streamer.");

            byte[] buffer = new byte[4096];
            StringBuilder lineBuffer = new StringBuilder();

            while (keepRunning)
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                lineBuffer.Append(chunk);

                string text = lineBuffer.ToString();
                int newlineIndex;
                while ((newlineIndex = text.IndexOf('\n')) >= 0)
                {
                    string line = text.Substring(0, newlineIndex);
                    if (isRecording)
                    {
                        lock (bufferLock)
                        {
                            rawFrameBuffer.Add(line);
                        }
                    }
                    text = text.Substring(newlineIndex + 1);
                }
                lineBuffer.Clear();
                lineBuffer.Append(text);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[GestureClassifierClient] Connection error: " + e.Message);
        }
        finally
        {
            streamConnected = false;
        }
    }

    bool SpacePressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // Project uses the new Input System only; the old Input class would throw.
        return UnityEngine.InputSystem.Keyboard.current != null
            && UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    // Key names shared by the new Input System's Key and the old KeyCode
    // (LeftArrow, RightArrow, UpArrow, DownArrow, LeftBracket, RightBracket).
    bool PressedThisFrame(string keyName)
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null
            && keyboard[(UnityEngine.InputSystem.Key)Enum.Parse(typeof(UnityEngine.InputSystem.Key), keyName)].wasPressedThisFrame;
#else
        return Input.GetKeyDown((KeyCode)Enum.Parse(typeof(KeyCode), keyName));
#endif
    }

    // Returns 1-9 if that number key was pressed this frame, otherwise 0.
    int SignKeyPressedThisFrame()
    {
        for (int i = 1; i <= 9; i++)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return 0;
            if (keyboard[UnityEngine.InputSystem.Key.Digit1 + (i - 1)].wasPressedThisFrame
                || keyboard[UnityEngine.InputSystem.Key.Numpad1 + (i - 1)].wasPressedThisFrame)
                return i;
#else
            if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                return i;
#endif
        }
        return 0;
    }

    // Selects a sign by lesson and position; both wrap around.
    void SelectSign(int lesson, int sign)
    {
        if (lessons.Count == 0) return;
        lessonIndex = (lesson % lessons.Count + lessons.Count) % lessons.Count;
        var signs = lessons[lessonIndex].signs;
        signIndex = (sign % signs.Length + signs.Length) % signs.Length;

        curriculumItemId = signs[signIndex].curriculum_item_id;
        expectedSignName = signs[signIndex].sign_name;
        lastWasMatch = null;
        resultText = "";
        statusText = "Switched to " + expectedSignName + ". Press SPACE to start recording";
        Debug.Log("[GestureClassifierClient] Target sign: " + lessons[lessonIndex].title + " / " + expectedSignName);
        TargetChanged?.Invoke(expectedSignName);
    }

    void HandleLessonBrowserKeys()
    {
        if (isRecording) return;

        if (PressedThisFrame("RightArrow") || PressedThisFrame("RightBracket")) SelectSign(lessonIndex + 1, 0);
        else if (PressedThisFrame("LeftArrow") || PressedThisFrame("LeftBracket")) SelectSign(lessonIndex - 1, 0);
        else if (PressedThisFrame("DownArrow")) SelectSign(lessonIndex, signIndex + 1);
        else if (PressedThisFrame("UpArrow")) SelectSign(lessonIndex, signIndex - 1);
        else
        {
            int signKey = SignKeyPressedThisFrame();
            if (signKey > 0 && signKey <= lessons[lessonIndex].signs.Length)
                SelectSign(lessonIndex, signKey - 1);
        }
    }

    void Update()
    {
        HandleLessonBrowserKeys();

        if (!isRecording && !streamConnected)
        {
            statusText = "Camera stream NOT connected. Start landmark_streamer.py first, then press Play.";
        }

        if (SpacePressedThisFrame())
        {
            if (!streamConnected)
            {
                Debug.LogWarning("[GestureClassifierClient] Not connected to the camera stream yet.");
                return;
            }

            if (!isRecording)
            {
                lock (bufferLock) { rawFrameBuffer.Clear(); }
                isRecording = true;
                lastWasMatch = null;
                resultText = "";
                statusText = "RECORDING... perform the sign, then press SPACE to stop";
                Debug.Log("[GestureClassifierClient] Recording started...");
            }
            else
            {
                isRecording = false;
                statusText = "Sending to backend...";
                Debug.Log("[GestureClassifierClient] Recording stopped, sending to backend...");
                SendCapturedSequence();
            }
        }
        else if (!isRecording && streamConnected && statusText.StartsWith("Camera stream NOT"))
        {
            statusText = "Ready. Press SPACE to start recording";
        }
        else if (!isRecording && streamConnected && statusText.StartsWith("Waiting"))
        {
            statusText = "Ready. Press SPACE to start recording";
        }
    }

    void SendCapturedSequence()
    {
        List<string> framesCopy;
        lock (bufferLock)
        {
            framesCopy = new List<string>(rawFrameBuffer);
        }

        if (framesCopy.Count < 3)
        {
            Debug.LogWarning("[GestureClassifierClient] Not enough frames captured, try again.");
            statusText = "Not enough frames captured, try again (press SPACE)";
            return;
        }

        // Each buffered line is already valid JSON, e.g. {"t":..,"left_hand":[...],"right_hand":[...]}  matching the backend's LandmarkFrame schema exactly, so we can just wrap them.
        
        StringBuilder json = new StringBuilder();
        json.Append("{");
        json.Append("\"session_id\":\"unity-live-test\",");
        json.Append("\"curriculum_item_id\":\"" + curriculumItemId + "\",");
        json.Append("\"landmark_sequence\":[");
        json.Append(string.Join(",", framesCopy));
        json.Append("]}");

        StartCoroutine(PostToBackend(json.ToString()));
    }

    System.Collections.IEnumerator PostToBackend(string jsonBody)
    {
        var request = new UnityWebRequest(BackendUrl, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string body = request.downloadHandler.text;
            Debug.Log("[GestureClassifierClient] Backend response: " + body);
            HandleResponse(body);
        }
        else
        {
            Debug.LogError("[GestureClassifierClient] Request failed: " + request.error + " | " + request.downloadHandler.text);
            statusText = "Request failed: " + request.error + " (is the backend running?)";
        }

        request.Dispose();
    }

    void HandleResponse(string body)
    {
        GestureClassifyResponse parsed = null;
        try
        {
            parsed = JsonUtility.FromJson<GestureClassifyResponse>(body);
        }
        catch (Exception e)
        {
            Debug.LogError("[GestureClassifierClient] Could not parse response: " + e.Message);
        }

        if (parsed == null || string.IsNullOrEmpty(parsed.predicted_sign))
        {
            resultText = "No sign recognised";
            lastWasMatch = false;
            statusText = "Done. Press SPACE to try again";
            ResultReceived?.Invoke("unknown", 0f, false);
            return;
        }

        if (!string.IsNullOrEmpty(parsed.retry_reason))
        {
            // Not a wrong sign: the camera couldn't see well enough to judge it.
            string message;
            switch (parsed.retry_reason)
            {
                case "hand_not_visible":
                    message = "Couldn't see your hand clearly. Keep your signing hand in view and try again.";
                    break;
                case "too_short":
                    message = "That was too short. Hold the sign for about 1-2 seconds, then press SPACE.";
                    break;
                case "sign_not_trained":
                    message = "This sign can't be checked yet (no reference recordings). Press D to watch the avatar sign it.";
                    break;
                default:
                    message = "Couldn't see you clearly. Keep your head and both shoulders in view and try again.";
                    break;
            }
            resultText = message;
            lastWasMatch = null;
            statusText = "Press SPACE to try again";
            Debug.Log("[GestureClassifierClient] Retry requested: " + parsed.retry_reason);
            RetryRequested?.Invoke(message);
            return;
        }

        int percent = Mathf.RoundToInt(parsed.confidence_score * 100f);
        resultText = "Detected: " + parsed.predicted_sign + " (" + percent + "%)";
        lastWasMatch = Normalise(parsed.predicted_sign) == Normalise(expectedSignName);
        statusText = "Done. Press SPACE to try again";
        ResultReceived?.Invoke(parsed.predicted_sign, parsed.confidence_score, lastWasMatch.Value);
    }

    static string Normalise(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.ToLowerInvariant().Replace(" ", "").Replace("_", "");
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 26;
        style.fontStyle = FontStyle.Bold;
        style.wordWrap = true;
        style.normal.textColor = Color.white;

        GUIStyle small = new GUIStyle(style) { fontSize = 18, fontStyle = FontStyle.Normal };
        var lesson = lessons[lessonIndex];
        string lessonLine = lesson.title + "   (sign " + (signIndex + 1) + " of " + lesson.signs.Length + ")"
            + (catalogFromBackend ? "" : "   [backend offline: Lesson 1 only]");

        GUI.Box(new Rect(10, 10, 880, 250), GUIContent.none);
        GUI.Label(new Rect(25, 15, 850, 28), lessonLine, small);
        GUI.Label(new Rect(25, 43, 850, 24), "Left/Right: lesson    Up/Down or 1-9: sign    D: watch the avatar    SPACE: record", small);
        GUI.Label(new Rect(25, 75, 850, 40), "Sign to perform: " + expectedSignName, style);
        GUI.Label(new Rect(25, 115, 850, 70), statusText, style);

        if (!string.IsNullOrEmpty(resultText))
        {
            GUI.Label(new Rect(25, 190, 850, 35), resultText, style);
        }

        if (lastWasMatch.HasValue)
        {
            style.normal.textColor = lastWasMatch.Value ? Color.green : Color.red;
            GUI.Label(new Rect(25, 220, 850, 35), lastWasMatch.Value ? "MATCH" : "NO MATCH", style);
        }
    }

    void OnDestroy()
    {
        keepRunning = false;
        stream?.Close();
        client?.Close();
        receiveThread?.Join(500);
    }
}
