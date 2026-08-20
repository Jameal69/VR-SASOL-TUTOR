/*
Setup:
1. Create an empty GameObject in Unity (e.g. name it "LandmarkReceiver")
   and attach this script to it.
2. Run landmark_streamer.py first, outside Unity, and wait for
   "Waiting for Unity to connect...".
3. Press Play. Watch the Console - JSON should stream in and update as
   your hand moves in front of the webcam.

Throwaway proof-of-concept: only logs the data, doesn't use it yet.
Parsing it into something usable (driving an avatar, etc.) is next.

Why a background thread: socket reads block, so reading directly on
Unity's main thread would freeze the game. This reads on a separate
Thread and queues messages; Update() drains the queue each frame.
*/



using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class LandmarkReceiver : MonoBehaviour
{
    private const string Host = "127.0.0.1";
    private const int Port = 5052;

    private TcpClient _client;
    private Thread _receiveThread;
    private volatile bool _running;

    // Thread-safe queue: background thread writes, Update() reads and logs
    // (Unity API calls aren't allowed off the main thread).
    private readonly ConcurrentQueue<string> _messageQueue = new ConcurrentQueue<string>();

    void Start()
    {
        _running = true;
        _receiveThread = new Thread(ReceiveLoop);
        _receiveThread.IsBackground = true;
        _receiveThread.Start();
    }

    private void ReceiveLoop()
    {
        try
        {
            _client = new TcpClient();
            Debug.Log($"Connecting to landmark streamer at {Host}:{Port} ...");
            _client.Connect(Host, Port);
            Debug.Log("Connected to landmark streamer.");

            using (var stream = _client.GetStream())
            using (var reader = new StreamReader(stream))
            {
                while (_running)
                {
                    string line = reader.ReadLine(); // blocks until a line arrives
                    if (line == null)
                    {
                        // Python side closed the connection.
                        break;
                    }
                    _messageQueue.Enqueue(line);
                }
            }
        }
        catch (Exception e)
        {
            // Logged on the background thread via Debug.LogError
            Debug.LogError($"Landmark socket error: {e.Message}");
        }
    }

    void Update()
    {
        // Drain whatever arrived since the last frame.
        while (_messageQueue.TryDequeue(out string message))
        {
            Debug.Log(message);
        }
    }

    void OnApplicationQuit()
    {
        _running = false;
        _client?.Close();
    }
}
