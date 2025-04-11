using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[System.Serializable]
public class ArmPoint
{
    public float x;
    public float y;
    public float z;
    public float visibility;
}

[System.Serializable]
public class ArmData
{
    public ArmPoint shoulder;
    public ArmPoint wrist;
}

[System.Serializable]
public class ArmTracking
{
    public double timestamp;
    public ArmData left_arm;
    public ArmData right_arm;
}

public class RigidArmController : MonoBehaviour
{
    [Header("WebSocket Connection")]
    [SerializeField] private string serverUrl = "ws://localhost:8765";
    [SerializeField] private bool autoConnect = true;
    [SerializeField] private float reconnectDelay = 5f;
    
    [Header("Rigid Arms")]
    [SerializeField] private Transform leftArmCuboid;
    [SerializeField] private Transform rightArmCuboid;
    [SerializeField] private Transform leftShoulder;
    [SerializeField] private Transform rightShoulder;
    
    [Header("Settings")]
    [SerializeField] private float armLength = 1.0f;
    [SerializeField] private float positionScale = 2.0f;
    [SerializeField] private float smoothing = 0.2f;
    [SerializeField] private bool debugMode = true;

    [Header("Axis Mapping")]
    [SerializeField] private bool invertXAxis = false;
    [SerializeField] private bool invertYAxis = true; // Currently inverted
    [SerializeField] private bool invertZAxis = false;
    [SerializeField] private bool swapYZ = false;     // Try this if up/down is wrong
    
    // Connection variables
    private ClientWebSocket webSocket;
    private bool isConnected = false;
    private string connectionStatus = "Disconnected";
    private string latencyMs = "N/A";
    private float lastDataReceived;
    
    // Target positions and rotations
    private Vector3 leftArmTargetDir = Vector3.forward;
    private Vector3 rightArmTargetDir = Vector3.forward;
    
    void Start()
    {
        // Initialize arm positions
        InitializeArms();
        
        if (autoConnect)
            StartCoroutine(ConnectToServerCoroutine());
    }
    
    void Update()
    {
        // Apply smoothed updates to arm rotations
        UpdateArmPositions();
    }
    
    private void InitializeArms()
    {
        // Don't modify the scale since it's already set correctly
        // Just ensure arms are initially at shoulder positions
        if (leftArmCuboid != null && leftShoulder != null)
        {
            leftArmCuboid.position = leftShoulder.position;
            // Keep existing rotation
        }
        
        if (rightArmCuboid != null && rightShoulder != null)
        {
            rightArmCuboid.position = rightShoulder.position;
            // Keep existing rotation
        }
    }
    
    private IEnumerator ConnectToServerCoroutine()
    {
        connectionStatus = "Connecting...";
        
        webSocket = new ClientWebSocket();
        Uri serverUri = new Uri(serverUrl);
        
        var tokenSource = new CancellationTokenSource();
        
        bool reconnect = false;
        
        // Wait for connection outside of try-catch
        Task connectTask = null;
        
        try {
            connectTask = webSocket.ConnectAsync(serverUri, tokenSource.Token);
        }
        catch (Exception e) {
            Debug.LogError("WebSocket connection error: " + e.Message);
            connectionStatus = "Error: " + e.Message;
            reconnect = true;
            connectTask = null;
        }
        
        // Wait for task to complete if it was started
        if (connectTask != null) {
            while (!connectTask.IsCompleted)
                yield return null;
            
            if (webSocket.State == WebSocketState.Open) {
                isConnected = true;
                connectionStatus = "Connected";
                Debug.Log("Connected to server: " + serverUrl);
                lastDataReceived = Time.time;
                
                StartCoroutine(ReceiveLoopCoroutine());
            }
            else {
                reconnect = true;
            }
        }
        
        // Reconnect if needed (outside try-catch)
        if (reconnect && autoConnect) {
            yield return new WaitForSeconds(reconnectDelay);
            StartCoroutine(ConnectToServerCoroutine());
        }
    }
    
    private IEnumerator ReceiveLoopCoroutine()
    {
        var buffer = new byte[8192];
        var tokenSource = new CancellationTokenSource();
        
        while (isConnected && webSocket.State == WebSocketState.Open)
        {
            var segment = new ArraySegment<byte>(buffer);
            Task<WebSocketReceiveResult> receiveTask = null;
            WebSocketReceiveResult result = null;
            bool error = false;
            
            try {
                receiveTask = webSocket.ReceiveAsync(segment, tokenSource.Token);
            }
            catch (Exception e) {
                Debug.LogError("WebSocket error starting receive: " + e.Message);
                isConnected = false;
                connectionStatus = "Error: " + e.Message;
                error = true;
            }
            
            // Wait for receive task completion outside try block
            if (receiveTask != null && !error) {
                while (!receiveTask.IsCompleted)
                    yield return null;
                
                try {
                    result = receiveTask.Result;
                }
                catch (Exception e) {
                    Debug.LogError("WebSocket error completing receive: " + e.Message);
                    isConnected = false;
                    connectionStatus = "Error: " + e.Message;
                    error = true;
                }
            }
            
            // Process the result outside try block
            if (result != null && !error) {
                if (result.MessageType == WebSocketMessageType.Text) {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessMessage(message);
                    lastDataReceived = Time.time;
                }
                else if (result.MessageType == WebSocketMessageType.Close) {
                    isConnected = false;
                    connectionStatus = "Disconnected";
                    Debug.Log("WebSocket connection closed");
                    error = true;
                }
            }
            
            // Handle reconnection outside try-catch
            if (error) {
                if (autoConnect) {
                    yield return new WaitForSeconds(reconnectDelay);
                    StartCoroutine(ConnectToServerCoroutine());
                }
                yield break;
            }
            
            // Check for timeout
            if (Time.time - lastDataReceived > 5f && lastDataReceived > 0) {
                Debug.LogWarning("No data received for 5 seconds. Reconnecting...");
                isConnected = false;
                
                if (autoConnect) {
                    yield return new WaitForSeconds(reconnectDelay);
                    StartCoroutine(ConnectToServerCoroutine());
                }
                yield break;
            }
        }
    }
    
    private void ProcessMessage(string message)
    {
        try {
            ArmTracking armData = JsonConvert.DeserializeObject<ArmTracking>(message);
            ProcessArmData(armData);
            
            // Calculate latency
            double serverTimestamp = armData.timestamp;
            double currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            double latency = (currentTime - serverTimestamp) * 1000; // Convert to milliseconds
            latencyMs = latency.ToString("F1") + " ms";
        }
        catch (Exception e) {
            Debug.LogError("Error parsing arm data: " + e.Message);
        }
    }
    
    private void ProcessArmData(ArmTracking armData)
    {
        // Process left arm data
        if (armData.left_arm != null && leftArmCuboid != null && leftShoulder != null)
        {
            // Compute direction from shoulder to wrist
            Vector3 shoulderPos = new Vector3(
                armData.left_arm.shoulder.x,
                armData.left_arm.shoulder.y, 
                armData.left_arm.shoulder.z
            ) * positionScale;
            
            Vector3 wristPos = new Vector3(
                armData.left_arm.wrist.x,
                armData.left_arm.wrist.y, 
                armData.left_arm.wrist.z
            ) * positionScale;
            
            // Calculate arm direction
            Vector3 direction = wristPos - shoulderPos;
            if (direction.magnitude > 0.01f)
            {
                leftArmTargetDir = direction.normalized;
            }
        }
        
        // Process right arm data
        if (armData.right_arm != null && rightArmCuboid != null && rightShoulder != null)
        {
            // Compute direction from shoulder to wrist
            Vector3 shoulderPos = new Vector3(
                armData.right_arm.shoulder.x,
                armData.right_arm.shoulder.y, 
                armData.right_arm.shoulder.z
            ) * positionScale;
            
            Vector3 wristPos = new Vector3(
                armData.right_arm.wrist.x,
                armData.right_arm.wrist.y, 
                armData.right_arm.wrist.z
            ) * positionScale;
            
            // Calculate arm direction
            Vector3 direction = wristPos - shoulderPos;
            if (direction.magnitude > 0.01f)
            {
                rightArmTargetDir = direction.normalized;
            }
        }
    }

        // Add this helper method to transform coordinates with configurable options
    private Vector3 TransformCoordinates(float x, float y, float z)
    {
        // Apply inversions based on settings
        float xOut = invertXAxis ? -x : x;
        float yOut = invertYAxis ? -y : y;
        float zOut = invertZAxis ? -z : z;
        
        // Optionally swap Y and Z axes
        if (swapYZ)
        {
            float temp = yOut;
            yOut = zOut;
            zOut = temp;
        }
        
        return new Vector3(xOut, yOut, zOut) * positionScale;
    }
    
    private void UpdateArmPositions()
    {
        // Update left arm
        if (leftArmCuboid != null && leftShoulder != null)
        {
            // Position at shoulder
            leftArmCuboid.position = leftShoulder.position;
            
            // Rotation to point in direction
            Quaternion targetRotation = Quaternion.LookRotation(leftArmTargetDir);
            leftArmCuboid.rotation = Quaternion.Slerp(
                leftArmCuboid.rotation,
                targetRotation,
                Time.deltaTime * (1f / smoothing)
            );
        }
        
        // Update right arm
        if (rightArmCuboid != null && rightShoulder != null)
        {
            // Position at shoulder
            rightArmCuboid.position = rightShoulder.position;
            
            // Rotation to point in direction
            Quaternion targetRotation = Quaternion.LookRotation(rightArmTargetDir);
            rightArmCuboid.rotation = Quaternion.Slerp(
                rightArmCuboid.rotation,
                targetRotation,
                Time.deltaTime * (1f / smoothing)
            );
        }
    }
    
    void OnGUI()
    {
        if (debugMode)
        {
            GUI.Box(new Rect(10, 10, 250, 70), "Arm Tracking Status");
            GUI.Label(new Rect(20, 30, 240, 20), "Status: " + connectionStatus);
            GUI.Label(new Rect(20, 50, 240, 20), "Latency: " + latencyMs);
        }
    }
    
    void OnApplicationQuit()
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            // Close WebSocket connection when the application quits
            var tokenSource = new CancellationTokenSource();
            webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Application closing", tokenSource.Token);
        }
    }

    
}