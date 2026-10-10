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
/// Per sign you test, set BOTH fields in the Inspector:
///   curriculumItemId  (the sign's id from /api/curriculum/lessons)
///   expectedSignName  (what the on-screen prompt and MATCH check use)
/// Or press 1-4 in Play mode to switch between the Lesson 1 signs.
/// </summary>
public class GestureClassifierClient : MonoBehaviour
{
    // Lesson 1 signs for the 1-4 keys (ids from the live curriculum_items table).
    static readonly string[,] Lesson1Signs =
    {
        { "701d037f-2703-47a5-a09b-5eb3effa08f5", "Hello" },
        { "fab6a4b5-d6ef-4166-afad-3aaa8503daed", "Goodbye" },
        { "a99dfe87-1cbd-4700-a4da-ce2947b58ac3", "Please" },
        { "8160b4f7-752e-4e60-b0f6-9565b890c080", "Thank You" },
    };

    /// <summary>Fired on the main thread when the backend answers: (predicted sign, confidence 0-1, is MATCH).</summary>
    public event Action<string, float, bool> ResultReceived;

    /// <summary>Fired when the target sign changes (1-4 keys), with the new sign name.</summary>
    public event Action<string> TargetChanged;

    [Tooltip("Real curriculum_item_id for the sign you're testing (from /api/curriculum/lessons in /docs).")]
    public string curriculumItemId = "701d037f-2703-47a5-a09b-5eb3effa08f5";

    [Tooltip("Name of the sign being tested, e.g. Hello, Goodbye, Please, Thank You. Used for the on-screen prompt and match check.")]
    public string expectedSignName = "Hello";

    private const string BackendUrl = "http://localhost:8000/api/gestures/classify";

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

    // Returns 1-4 if that number key was pressed this frame, otherwise 0.
    int SignKeyPressedThisFrame()
    {
        for (int i = 1; i <= 4; i++)
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

    void SelectSign(int index)
    {
        curriculumItemId = Lesson1Signs[index, 0];
        expectedSignName = Lesson1Signs[index, 1];
        lastWasMatch = null;
        resultText = "";
        statusText = "Switched to " + expectedSignName + ". Press SPACE to start recording";
        Debug.Log("[GestureClassifierClient] Target sign: " + expectedSignName);
        TargetChanged?.Invoke(expectedSignName);
    }

    void Update()
    {
        int signKey = SignKeyPressedThisFrame();
        if (signKey > 0 && !isRecording)
        {
            SelectSign(signKey - 1);
        }

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

        GUI.Box(new Rect(10, 10, 880, 190), GUIContent.none);
        GUI.Label(new Rect(25, 15, 850, 40), "Sign to perform: " + expectedSignName + "   (keys 1-4 to change)", style);
        GUI.Label(new Rect(25, 55, 850, 70), statusText, style);

        if (!string.IsNullOrEmpty(resultText))
        {
            GUI.Label(new Rect(25, 130, 850, 35), resultText, style);
        }

        if (lastWasMatch.HasValue)
        {
            style.normal.textColor = lastWasMatch.Value ? Color.green : Color.red;
            GUI.Label(new Rect(25, 160, 850, 35), lastWasMatch.Value ? "MATCH" : "NO MATCH", style);
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
