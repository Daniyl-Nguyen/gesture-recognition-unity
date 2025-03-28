using System.Collections;
using System.Collections.Generic; // Need this for List
using UnityEngine;
using UnityEngine.Networking; // Need this for UnityWebRequest
using System; // Need this for StringComparison, Exception
using System.Text; // Need this for Encoding
using TMPro; // Need this for TextMeshProUGUI

public class GestureClassifier : MonoBehaviour
{
    // --- Data Structures for Networking ---
    [System.Serializable]
    public class DataRequest
    {
        public float[] data; // This will hold our flattened pose data
    }

    [System.Serializable]
    public class ClassificationResponse
    {
        public string classification;
        public float confidence;
    }

    // --- Inspector Variables ---
    [Header("Networking")]
    [SerializeField] private string serverUrl = "http://127.0.0.1:8000/classify_data"; // Python server endpoint

    [Header("Hand Tracking")]
    [SerializeField] private Transform leftHand;  // Assign 'Left_Hand' GameObject
    [SerializeField] private Transform rightHand; // Assign 'Right_Hand' GameObject

    [Tooltip("How many frames to wait between sending data for classification.")]
    [Range(1, 100)]
    [SerializeField] private int frameInterval = 10;

    [Tooltip("Which hand(s) to classify.")]
    [SerializeField] private HandToClassify handToClassify = HandToClassify.Right;

    [Tooltip("Minimum confidence score to accept a classification.")]
    [Range(0f, 1f)]
    [SerializeField] private float minConfidence = 0.7f;

    [Header("UI Display")]
    [Tooltip("Assign the TextMeshPro UI element here to display the classification result.")]
    [SerializeField] private TextMeshProUGUI classificationTextDisplay; // Assign your Text (TMP) object

    // --- Private Variables ---
    private List<Transform> leftHandJoints = new List<Transform>();
    private List<Transform> rightHandJoints = new List<Transform>();
    private int frameCount = 0; // Counter for frame interval
    private bool isRequestPending = false; // Flag to prevent overlapping requests
    private int expectedTransformsPerHand = 21; // ADJUST THIS based on your model's training input (1 wrist + N joints)

    // Enum to select which hand to track
    public enum HandToClassify
    {
        Left,
        Right,
        Both // Note: Current server likely handles one hand per request
    }

    // --- Unity Lifecycle Methods ---
    void Start()
    {
        Debug.Log($"Gesture Classifier started. Sending to: {serverUrl} every {frameInterval} frames.");

        // Check and initialize UI display
        if (classificationTextDisplay == null)
        {
            Debug.LogWarning($"{nameof(classificationTextDisplay)} is not assigned in the Inspector. Gesture names will not be shown on UI.");
        }
        else
        {
            classificationTextDisplay.text = "Waiting for gesture..."; // Set initial text
        }

        // Auto-populate joint lists and perform validation
        InitializeHandJoints(leftHand, ref leftHandJoints, "Left");
        InitializeHandJoints(rightHand, ref rightHandJoints, "Right");

        // Validate joint counts against expected model input size
        ValidateJointCount(leftHandJoints, "Left", HandToClassify.Left);
        ValidateJointCount(rightHandJoints, "Right", HandToClassify.Right);

        // Ensure frameInterval is valid
        if (frameInterval < 1) frameInterval = 1;
        frameCount = 0; // Initialize frame counter
    }

    void Update()
    {
        frameCount++; // Increment frame counter

        // Check if it's time to send data and no request is already running
        if (frameCount >= frameInterval && !isRequestPending)
        {
            frameCount = 0; // Reset counter

            // Process based on selected hand(s)
            if (handToClassify == HandToClassify.Left || handToClassify == HandToClassify.Both)
            {
                CaptureAndClassifyHand("Left", leftHand, leftHandJoints);
            }

            // Note: If 'Both' is selected, this sends two separate requests in quick succession.
            // Ensure your server/logic can handle this or add a delay/different handling if needed.
            if (handToClassify == HandToClassify.Right || handToClassify == HandToClassify.Both)
            {
                CaptureAndClassifyHand("Right", rightHand, rightHandJoints);
            }
        }
    }

    // --- Hand Initialization and Validation ---

    void InitializeHandJoints(Transform handRoot, ref List<Transform> jointList, string handName)
    {
        if (handRoot != null)
        {
            jointList = GetHandJoints(handRoot);
            Debug.Log($"Found {jointList.Count} joints for the {handName} Hand (excluding wrist).");
            if (jointList.Count == 0)
                Debug.LogWarning($"No valid child joints found under the assigned {handName} Hand transform '{handRoot.name}'. Check hierarchy and naming.");
        }
        else
        {
            Debug.LogWarning($"{handName} Hand transform is not assigned in the Inspector.");
            jointList = new List<Transform>(); // Ensure list is empty
        }
    }

    List<Transform> GetHandJoints(Transform handRoot)
    {
        List<Transform> joints = new List<Transform>();
        if (handRoot == null) return joints;

        foreach (Transform child in handRoot.GetComponentsInChildren<Transform>(true)) // Include inactive
        {
            // Exclude the root itself and common deformation bones. Adjust if needed.
            if (child != handRoot &&
                !child.name.EndsWith("_Def", StringComparison.OrdinalIgnoreCase) &&
                !child.name.EndsWith(".Def", StringComparison.OrdinalIgnoreCase) &&
                 !child.name.Contains("_end", StringComparison.OrdinalIgnoreCase)) // Example: Exclude _end markers if they exist
            {
                joints.Add(child);
            }
        }

        // IMPORTANT: Sort joints alphabetically to ensure consistent order matching training data collection.
        joints.Sort((t1, t2) => t1.name.CompareTo(t2.name));

        return joints;
    }

    void ValidateJointCount(List<Transform> joints, string handName, HandToClassify relevantEnum)
    {
        // Only validate if this hand is supposed to be classified
        if (handToClassify == relevantEnum || handToClassify == HandToClassify.Both)
        {
            // Expected joints = total transforms - 1 (for the wrist)
            int expectedJoints = expectedTransformsPerHand - 1;
            if (joints.Count != expectedJoints)
            {
                Debug.LogError($"Mismatch! Found {joints.Count} joints for {handName} Hand, but model expects {expectedJoints} joints (total {expectedTransformsPerHand} transforms including wrist). Classification may fail or be inaccurate.");
            }
        }
    }


    // --- Data Capture and Classification ---

    void CaptureAndClassifyHand(string handName, Transform wrist, List<Transform> handJoints)
    {
        // Basic check if hand is set up
        if (wrist == null || handJoints == null)
        {
            // This warning is logged in Start, so maybe just return silently here
            // Debug.LogWarning($"Skipping classification for {handName} hand - Not assigned.");
            return;
        }

        // Check if we actually found joints (prevents sending data for an empty hand)
        // We rely on the Start validation for the *correct number* of joints.
        if (handJoints.Count == 0 && (handToClassify == HandToClassify.Both || (handToClassify.ToString() == handName)))
        {
            Debug.LogWarning($"Skipping classification for {handName} hand - No joints found/assigned.");
            return;
        }


        // Calculate expected size and create data array
        int expectedFloatCount = expectedTransformsPerHand * 7; // 7 floats per transform (posXYZ, rotXYZW)
        float[] poseData = new float[expectedFloatCount];
        int currentIndex = 0;

        try
        {
            // --- Add Wrist Data ---
            AddTransformData(wrist, poseData, ref currentIndex);

            // --- Add Joint Data (Sorted Order) ---
            foreach (var joint in handJoints)
            {
                if (joint == null) // Should ideally not happen if GetHandJoints works
                {
                    Debug.LogError($"Null joint encountered in {handName} list while capturing data. Filling with zeros.");
                    for (int i = 0; i < 7; ++i) poseData[currentIndex++] = 0f; // Fill with zeros
                    continue; // Skip to next joint
                }
                AddTransformData(joint, poseData, ref currentIndex);
            }

            // --- Final Size Validation ---
            // Check if we added the correct number of transforms' data
            int transformsProcessed = currentIndex / 7;
            if (transformsProcessed != expectedTransformsPerHand)
            {
                Debug.LogError($"Data array population error for {handName}! Expected data for {expectedTransformsPerHand} transforms ({expectedFloatCount} floats), but got data for {transformsProcessed} transforms ({currentIndex} floats). Aborting send. Check GetHandJoints filtering and sorting.");
                return; // Don't send incorrectly sized data
            }

            // If everything looks okay, start the network request
            StartCoroutine(PostGestureData(poseData, handName));

        }
        catch (Exception e)
        {
            Debug.LogError($"Error during data capture for {handName}: {e.Message}\n{e.StackTrace}");
            // Optionally update UI to show capture error
            if (classificationTextDisplay != null) classificationTextDisplay.text = "Data Capture Error";
        }
    }

    // Helper to add transform data to the float array
    void AddTransformData(Transform t, float[] dataArray, ref int index)
    {
        if (index + 6 >= dataArray.Length)
        {
            Debug.LogError($"Array bounds error trying to add data for transform '{t.name}'. Index: {index}, Array Size: {dataArray.Length}");
            // Fill remaining with zeros to avoid crashing, though data will be wrong
            while (index < dataArray.Length) dataArray[index++] = 0f;
            throw new IndexOutOfRangeException("Attempted to write past the end of the pose data array."); // Stop further processing
        }

        Vector3 pos = t.position;
        Quaternion rot = t.rotation;
        dataArray[index++] = pos.x;
        dataArray[index++] = pos.y;
        dataArray[index++] = pos.z;
        dataArray[index++] = rot.x;
        dataArray[index++] = rot.y;
        dataArray[index++] = rot.z;
        dataArray[index++] = rot.w;
    }


    // --- Networking Coroutine ---
    private IEnumerator PostGestureData(float[] gestureData, string handNameForLog)
    {
        isRequestPending = true; // Prevent new requests while this one is running

        DataRequest requestData = new DataRequest { data = gestureData };
        string jsonData = JsonUtility.ToJson(requestData);

        // Use 'using' block for automatic disposal of UnityWebRequest
        using (UnityWebRequest request = new UnityWebRequest(serverUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10; // Optional: Set a timeout in seconds

            // Send the request and wait for the response
            yield return request.SendWebRequest();

            // --- Process Response ---
            switch (request.result)
            {
                case UnityWebRequest.Result.ConnectionError:
                case UnityWebRequest.Result.DataProcessingError:
                    Debug.LogError($"Network Error sending gesture data ({handNameForLog}): {request.error}");
                    if (classificationTextDisplay != null) classificationTextDisplay.text = "Network Error";
                    break;
                case UnityWebRequest.Result.ProtocolError: // HTTP error (e.g., 404, 500)
                    Debug.LogError($"HTTP Error sending gesture data ({handNameForLog}): {request.responseCode} - {request.error}");
                    if (!string.IsNullOrEmpty(request.downloadHandler?.text)) // Log server message if available
                    {
                        Debug.LogError($"Server Response: {request.downloadHandler.text}");
                    }
                    if (classificationTextDisplay != null) classificationTextDisplay.text = $"HTTP Error: {request.responseCode}";
                    break;
                case UnityWebRequest.Result.Success:
                    string responseJson = request.downloadHandler.text;
                    try
                    {
                        ClassificationResponse response = JsonUtility.FromJson<ClassificationResponse>(responseJson);

                        if (response != null) // Check if parsing was successful
                        {
                            if (response.confidence >= minConfidence)
                            {
                                string displayText = $"Gesture: {response.classification}\nConf: {response.confidence:F2}";
                                Debug.Log($"CLASSIFIED ({handNameForLog}): {response.classification} (Conf: {response.confidence:F2})");

                                // Update UI Text
                                if (classificationTextDisplay != null)
                                {
                                    classificationTextDisplay.text = displayText;
                                }
                                // ----> PLACEHOLDER: Trigger game events based on response.classification <----

                            }
                            else // Confidence below threshold
                            {
                                Debug.Log($"({handNameForLog}) Low Confidence: {response.classification} (Conf: {response.confidence:F2}) - Ignored");
                                // Optionally update UI for low confidence state
                                if (classificationTextDisplay != null)
                                {
                                    // Example: Show low confidence result or clear text
                                    classificationTextDisplay.text = $"Low Conf: {response.classification}";
                                    // classificationTextDisplay.text = "---";
                                }
                            }
                        }
                        else
                        {
                            Debug.LogError($"Failed to parse JSON response, FromJson returned null. Response text: {responseJson}");
                            if (classificationTextDisplay != null) classificationTextDisplay.text = "Parse Error (Null)";
                        }

                    }
                    catch (Exception e) // Catch potential issues during JSON parsing or processing
                    {
                        Debug.LogError($"Error processing server response ({handNameForLog}): {e.Message}\nResponse JSON: {responseJson}\nStackTrace: {e.StackTrace}");
                        if (classificationTextDisplay != null) classificationTextDisplay.text = "Response Process Error";
                    }
                    break;
            }
        } // End of 'using' block - request is disposed here

        isRequestPending = false; // Allow the next request
    }
}