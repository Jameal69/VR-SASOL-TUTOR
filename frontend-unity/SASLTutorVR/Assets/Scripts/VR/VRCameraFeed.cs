using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows what the webcam sees on the VR whiteboard, so someone wearing the
/// headset can check that their hands and shoulders are in view (the streamer's
/// own preview window is on the PC screen, which you can't see in VR).
///
/// Unity can't open the webcam itself while landmark_streamer.py is using it,
/// so the streamer sends its preview (mirrored, with the tracked hand points
/// drawn on it) on a second local port: 4-byte big-endian length + JPEG bytes.
/// Added by SASL > Add Camera View to Whiteboard.
/// </summary>
public class VRCameraFeed : MonoBehaviour
{
    public RawImage image;
    [Tooltip("Optional caption under the picture; shows the connection state.")]
    public Text caption;
    public string host = "127.0.0.1";
    public int port = 5053;

    Thread reader;
    volatile bool running;
    readonly object frameLock = new object();
    byte[] latestFrame;
    bool newFrame;
    volatile bool connected;
    float lastFrameTime = -1f;
    Texture2D texture;

    void OnEnable()
    {
        texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (image != null) image.texture = texture;
        running = true;
        reader = new Thread(ReadLoop) { IsBackground = true, Name = "VRCameraFeed" };
        reader.Start();
    }

    void OnDisable()
    {
        running = false;
        reader?.Join(500);
        if (texture != null) Destroy(texture);
    }

    // Background thread: (re)connect to the streamer and keep only the newest frame.
    void ReadLoop()
    {
        var header = new byte[4];
        while (running)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect(host, port);
                    client.ReceiveTimeout = 3000;
                    var stream = client.GetStream();
                    connected = true;
                    while (running)
                    {
                        ReadExactly(stream, header, 4);
                        int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                        if (length <= 0 || length > 5_000_000) throw new Exception("bad frame length " + length);
                        var data = new byte[length];
                        ReadExactly(stream, data, length);
                        lock (frameLock) { latestFrame = data; newFrame = true; }
                    }
                }
            }
            catch (Exception)
            {
                // Streamer not running yet, or it stopped: try again shortly.
            }
            connected = false;
            if (running) Thread.Sleep(1000);
        }
    }

    static void ReadExactly(NetworkStream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0) throw new Exception("stream closed");
            read += n;
        }
    }

    void Update()
    {
        byte[] frame = null;
        lock (frameLock)
        {
            if (newFrame) { frame = latestFrame; newFrame = false; }
        }
        if (frame != null && texture != null && texture.LoadImage(frame))
        {
            if (image != null && image.texture != texture) image.texture = texture;
            lastFrameTime = Time.time;
        }

        if (caption != null)
        {
            bool live = connected && lastFrameTime >= 0f && Time.time - lastFrameTime < 2f;
            caption.text = live ? "What the camera sees"
                                : "Camera view: waiting for landmark_streamer.py";
        }
        if (image != null) image.color = lastFrameTime >= 0f ? Color.white : new Color(0.15f, 0.17f, 0.22f);
    }
}
