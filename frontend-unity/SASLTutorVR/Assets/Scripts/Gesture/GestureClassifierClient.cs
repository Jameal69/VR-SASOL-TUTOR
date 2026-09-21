using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Extends what LandmarkReceiver proved works: connects to the same Python
/// socket, but instead of just logging raw frames, buffers them while the
/// player is "recording" an attempt, then sends the whole sequence to the
/// real backend's /api/gestures/classify endpoint and logs what comes back.
///
/// This is the actual missing link between "Unity receives hand data" and
/// "Unity knows which sign was performed." Nothing visual yet, that's a
/// separate step, this proves the full loop end to end inside Unity itself.
///
/// Controls (Play mode): SPACE to start/stop recording an attempt.
/// </summary>
public class GestureClassifierClient : MonoBehaviour
{
    [Tooltip("Paste the real curriculum_item_id for the sign you're testing, from GET /api/curriculum/lessons/{lesson_id} in /docs.")]
    public string curriculumItemId = "PASTE_A_REAL_ID_HERE";

    private const string BackendUrl = "http://localhost:8000/api/gestures/classify";

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private volatile bool keepRunning = true;

    private readonly object bufferLock = new object();
    private List<string> rawFrameBuffer = new List<string>();
    private bool isRecording = false;

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
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (!isRecording)
            {
                lock (bufferLock) { rawFrameBuffer.Clear(); }
                isRecording = true;
                Debug.Log("[GestureClassifierClient] Recording started...");
            }
            else
            {
                isRecording = false;
                Debug.Log("[GestureClassifierClient] Recording stopped, sending to backend...");
                SendCapturedSequence();
            }
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
            return;
        }

        // Each buffered line is already valid JSON, e.g. {"t":..,"left_hand":[...],"right_hand":[...]}
        // matching the backend's LandmarkFrame schema exactly, so we just wrap them.
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
            Debug.Log("[GestureClassifierClient] Backend response: " + request.downloadHandler.text);
        }
        else
        {
            Debug.LogError("[GestureClassifierClient] Request failed: " + request.error + " | " + request.downloadHandler.text);
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
