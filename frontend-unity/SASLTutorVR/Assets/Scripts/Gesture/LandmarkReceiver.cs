using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>

/// Setup: attach this script to any GameObject in the scene (an empty
/// GameObject called "LandmarkReceiver" is fine). Run landmark_streamer.py
/// FIRST, then press Play in Unity, matching the order in ml-service/README.md.
/// </summary>
public class LandmarkReceiver : MonoBehaviour
{
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private volatile bool keepRunning = true;
    private volatile string latestMessage = "";
    private volatile bool hasNewMessage = false;
 
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
            Debug.Log("[LandmarkReceiver] Connected to Python landmark streamer.");
 
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
                    latestMessage = line;
                    hasNewMessage = true;
                    text = text.Substring(newlineIndex + 1);
                }
                lineBuffer.Clear();
                lineBuffer.Append(text);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[LandmarkReceiver] Connection error: " + e.Message);
        }
    }
 
    void Update()
    {
        if (hasNewMessage)
        {
            Debug.Log("[LandmarkReceiver] Frame received: " + latestMessage);
            hasNewMessage = false;
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
 









































