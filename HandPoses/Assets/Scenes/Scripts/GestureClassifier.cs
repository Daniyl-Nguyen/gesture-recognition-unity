using System.Collections;
using System.Collections.Generic; // Need this for List
using UnityEngine;
using UnityEngine.Networking; // Need this for UnityWebRequest
using System; // Need this for StringComparison, Exception, Buffer.BlockCopy
using System.Text; // Need this for Encoding
using TMPro; // Need this for TextMeshProUGUI

public class GestureClassifier : MonoBehaviour
{
    // --- Data Structures for Networking ---

    // Request structure for the FLAT array sent TO the server
    [System.Serializable]
    public class FlatDataRequest
    {
        public float[] data; // Combined L/R hand data (e.g., 294 floats)
    }

    // Response structure received FROM the server (server still sends nested)
    [System.Serializable]
    public class ClassificationResponse
    {
        public HandResponse left_hand;
        public HandResponse right_hand;

        [System.Serializable]
        public class HandResponse
        {
            public string classification;
            public float confidence;
            public string error; // Optional: Add error field if server sends it
        }
    }

    // --- Inspector Variables ---
    [Header("Networking")]
    [Tooltip("URL of the Python classification server endpoint.")]
    [SerializeField] private string serverUrl = "http://127.0.0.1:8000/classify_data"; // Ensure this matches server

    [Header("Hand Tracking")]
    [Tooltip("Assign the root transform of the Left Hand skeleton.")]
    [SerializeField] private Transform leftHand;  // Assign 'Left_Hand' GameObject root
    [Tooltip("Assign the root transform of the Right Hand skeleton.")]
    [SerializeField] private Transform rightHand; // Assign 'Right_Hand' GameObject root

    [Tooltip("How many frames to wait between sending data for classification.")]
    [Range(1, 100)]
    [SerializeField] private int frameInterval = 10;

    [Tooltip("Minimum confidence score received from server to consider the classification valid for display/action.")]
    [Range(0f, 1f)]
    [SerializeField] private float minConfidence = 0.7f; // Threshold used for display ONLY

    [Header("UI Display")]
    [Tooltip("Optional: Assign a TextMeshPro UI element to display classification results.")]
    [SerializeField] private TextMeshProUGUI classificationTextDisplay; // Assign Text (TMP) object

    [Header("VR Setup")]
    [Tooltip("Assign the HMD or main camera transform for relative coordinate calculation.")]
    [SerializeField] private Transform hmdTransform; // Assign HMD or main camera transform

    // --- Private Variables ---
    private List<Transform> leftHandJoints = new List<Transform>();
    private List<Transform> rightHandJoints = new List<Transform>();
    private int frameCount = 0;
    private bool isRequestPending = false;

    [Tooltip("Total number of transforms per hand (e.g., 1 wrist + 20 joints = 21) your ML model expects.")]
    [SerializeField] private int expectedTransformsPerHand = 21;
    private int floatsPerHand;      // Calculated in Start: expectedTransformsPerHand * 7
    private int combinedDataLength; // Calculated in Start: floatsPerHand * 2

    // --- Unity Lifecycle Methods ---
    void Start()
    {
        // Calculate data lengths based on Inspector setting
        floatsPerHand = expectedTransformsPerHand * 7; // 7 floats per transform (posXYZ, rotXYZW)
        combinedDataLength = floatsPerHand * 2; // Total floats for both hands

        if (hmdTransform == null)
        {
            Debug.LogError($"[{nameof(GestureClassifier)}] HMD Transform is not assigned! GestureClassifier disabled.");
            enabled = false; // Disable component if prerequisite is missing
            return;
        }

        if (classificationTextDisplay != null)
        {
            classificationTextDisplay.text = "Waiting for gesture...";
        }
        else
        {
            Debug.LogWarning($"[{nameof(GestureClassifier)}] Classification Text Display not assigned. Results won't be shown in UI.");
        }


        // Initialize joints (consider adding sorting/filtering here if needed for your model)
        InitializeHandJoints(leftHand, ref leftHandJoints, "Left");
        InitializeHandJoints(rightHand, ref rightHandJoints, "Right");

        // Validate required joint counts immediately after initialization
        ValidateJointCount(leftHand, leftHandJoints, "Left");
        ValidateJointCount(rightHand, rightHandJoints, "Right");

        Debug.Log($"[{nameof(GestureClassifier)}] Initialized. Sending {combinedDataLength} floats ({expectedTransformsPerHand} transforms/hand) every {frameInterval} frames to {serverUrl}");
    }

    void Update()
    {
        // Don't run if disabled (e.g., HMD missing)
        if (!enabled) return;

        frameCount++;
        // Check if interval reached and no request is pending
        if (frameCount >= frameInterval && !isRequestPending)
        {
            frameCount = 0; // Reset counter

            // Capture data for both hands
            float[] leftHandData = CaptureHandData(leftHand, leftHandJoints);
            float[] rightHandData = CaptureHandData(rightHand, rightHandJoints);

            // Only proceed if BOTH hands provided valid data arrays
            // (CaptureHandData returns null if capture fails or joint count is wrong)
            if (leftHandData != null && rightHandData != null)
            {
                // Combine the data into a single flat array
                float[] combinedData = CombineHandData(leftHandData, rightHandData);

                if (combinedData != null)
                {
                    // Send the combined data via network request
                    StartCoroutine(PostGestureData(combinedData));
                }
                // Error logged within CombineHandData if lengths somehow mismatch
            }
            // else: Silently skip sending if one or both hands failed capture this frame
        }
    }

    // --- Hand Initialization and Validation ---

    void InitializeHandJoints(Transform handRoot, ref List<Transform> jointList, string handName)
    {
        jointList.Clear(); // Ensure list is empty before populating
        if (handRoot == null)
        {
            // Warning if the Transform is not assigned in the Inspector
            Debug.LogWarning($"[{nameof(GestureClassifier)}] {handName} Hand transform not assigned in the Inspector.");
            return; // Exit if no root transform provided
        }

        // Temporary list to hold all child transforms
        List<Transform> allJoints = new List<Transform>();

        // Get all child transforms first (excluding the root itself)
        foreach (Transform joint in handRoot.GetComponentsInChildren<Transform>(true)) // Include inactive children
        {
            // Add all children EXCEPT the root transform itself
            if (joint != handRoot)
            {
                allJoints.Add(joint);
            }
        }

        // FILTERING: Modify this section based on your specific hand model
        // Example 1: Filter out transforms with "_Def" or "Helper" in their names
        foreach (Transform joint in allJoints)
        {
            if (!joint.name.EndsWith("_Def", StringComparison.OrdinalIgnoreCase) &&
                !joint.name.Contains("Helper", StringComparison.OrdinalIgnoreCase) &&
                !joint.name.Contains("Collider", StringComparison.OrdinalIgnoreCase) &&
                !joint.name.Contains("Mesh", StringComparison.OrdinalIgnoreCase))
            {
                jointList.Add(joint);
            }
        }

        // After filtering, sort to ensure consistent ordering
        // (critical if your ML model expects joints in a specific order)
        jointList.Sort((t1, t2) => t1.name.CompareTo(t2.name));

        // If we still have too many joints after filtering, take only the first expectedTransformsPerHand-1
        int requiredJointsInList = expectedTransformsPerHand - 1;
        if (jointList.Count > requiredJointsInList)
        {
            Debug.LogWarning($"[{nameof(GestureClassifier)}] {handName} Hand still has too many joints ({jointList.Count}) after filtering. Taking only the first {requiredJointsInList}.");
            while (jointList.Count > requiredJointsInList)
            {
                jointList.RemoveAt(jointList.Count - 1); // Remove from the end (lowest priority after sorting)
            }
        }

        Debug.Log($"[{nameof(GestureClassifier)}] Found {jointList.Count} filtered joint transforms for {handName} Hand under '{handRoot.name}'.");
    }

    // Validates if the correct number of joints (excluding wrist) were found.
    // Called from Start() to give early feedback on setup issues.
    void ValidateJointCount(Transform handRoot, List<Transform> joints, string handName)
    {
        // Only validate if the hand root transform is actually assigned
        if (handRoot == null) return;

        // Calculate the number of joints expected *in the list* (total transforms - 1 for the root)
        int requiredJointsInList = expectedTransformsPerHand - 1;

        // Check if the number of joints found MATCHES the exact requirement
        if (joints.Count != requiredJointsInList)
        {
            // Log a critical error if the count doesn't match. This usually means the hierarchy is wrong or expectedTransformsPerHand is set incorrectly.
            Debug.LogError($"CRITICAL SETUP ERROR ({handName} Hand): Found {joints.Count} child transforms under '{handRoot.name}', but expected exactly {requiredJointsInList} (for {expectedTransformsPerHand} total transforms including root). Data capture/sending will likely fail or be incorrect. Check the hierarchy and the '{nameof(expectedTransformsPerHand)}' value ({expectedTransformsPerHand}).");
            // Consider potentially disabling the component here if this is fatal:
            // enabled = false;
        }
    }


    // --- Data Capture ---

    // Captures data for a SINGLE hand into a float array (size floatsPerHand)
    float[] CaptureHandData(Transform wrist, List<Transform> handJoints)
    {
        // Basic prerequisite check
        if (wrist == null) return null; // Don't attempt capture if root isn't assigned
        // HMD transform checked in Start()

        // Check if the provided joint list has the correct number of elements (N-1)
        int requiredJointsInList = expectedTransformsPerHand - 1;
        if (handJoints == null || handJoints.Count != requiredJointsInList)
        {
            // This error should have been caught by ValidateJointCount in Start,
            // but double-check here to prevent proceeding with bad data.
            // Don't log spam every frame, the Start error is sufficient.
            return null;
        }

        // Create data array for this single hand
        float[] poseData = new float[floatsPerHand]; // e.g., 147 floats
        int index = 0; // Current position in the poseData array

        try
        {
            // 1. Add the wrist/root transform data first
            AddTransformData(wrist, poseData, ref index);

            // 2. Add data for the required number of joints from the list
            //    Processes joints in the order they appear in handJoints list
            //    (which is hierarchy order unless sorting was added in InitializeHandJoints)
            foreach (Transform joint in handJoints) // Loop through exactly N-1 joints
            {
                if (joint == null) // Defensive check for null entries in the list
                {
                    Debug.LogWarning($"[{nameof(GestureClassifier)}] Null joint found in list for {wrist.name} during capture. Filling its 7 data slots with 0f.");
                    // Fill 7 floats with 0 to maintain array structure if a joint is missing/null
                    for (int j = 0; j < 7; ++j)
                    {
                        if (index < poseData.Length) poseData[index++] = 0f;
                    }
                    continue; // Skip AddTransformData for this null joint
                }
                // Add this joint's data
                AddTransformData(joint, poseData, ref index);
            }

            // 3. Final check: Ensure the array was filled exactly to the expected length
            if (index != poseData.Length)
            {
                // This indicates a logic error (e.g., loop count wrong, null handling issue)
                Debug.LogError($"[{nameof(GestureClassifier)}] CaptureHandData ({wrist.name}): Final index ({index}) does not match expected array length ({poseData.Length}). Aborting capture for this frame.");
                return null; // Return null for incomplete/incorrectly sized data
            }

            // If all checks passed and array is full
            return poseData; // Success

        }
        catch (Exception e) // Catch potential errors during data access or calculation
        {
            Debug.LogError($"[{nameof(GestureClassifier)}] Exception during CaptureHandData for {wrist.name}: {e.Message}\n{e.StackTrace}");
            return null; // Return null on error
        }
    }

    // Adds ONE transform's position and rotation data (7 floats) to the data array
    // Uses HMD-relative coordinates by default.
    void AddTransformData(Transform t, float[] dataArray, ref int index)
    {
        // Check array bounds before writing to prevent IndexOutOfRangeException
        // This check should ideally never fail if calling logic is correct, but acts as a safeguard.
        if (index + 6 >= dataArray.Length)
        {
            // Throw exception because this indicates a serious logic flaw upstream
            throw new IndexOutOfRangeException($"[{nameof(GestureClassifier)}] AddTransformData: Attempted to write past array bounds for '{t?.name ?? "NULL"}'. Index={index}, Length={dataArray.Length}");
        }

        // --- Coordinate System Selection ---
        // Option 1: HMD-Relative Coordinates (Default based on previous code)
        Vector3 relativePos = hmdTransform.InverseTransformPoint(t.position);
        Quaternion relativeRot = Quaternion.Inverse(hmdTransform.rotation) * t.rotation;

        // Option 2: World Coordinates (Uncomment if your model was trained on world data)
        // Vector3 relativePos = t.position;
        // Quaternion relativeRot = t.rotation;
        // ---

        // Add the 7 float values to the array
        dataArray[index++] = relativePos.x;
        dataArray[index++] = relativePos.y;
        dataArray[index++] = relativePos.z;
        dataArray[index++] = relativeRot.x;
        dataArray[index++] = relativeRot.y;
        dataArray[index++] = relativeRot.z;
        dataArray[index++] = relativeRot.w;
    }

    // --- Data Combination ---

    // Combines left and right hand data arrays into a single flat array
    float[] CombineHandData(float[] leftData, float[] rightData)
    {
        // Validate input array lengths before attempting to combine
        if (leftData == null || rightData == null || leftData.Length != floatsPerHand || rightData.Length != floatsPerHand)
        {
            Debug.LogError($"[{nameof(GestureClassifier)}] Cannot combine hand data: Incorrect input lengths or null array. Left={leftData?.Length ?? -1} (Expected {floatsPerHand}), Right={rightData?.Length ?? -1} (Expected {floatsPerHand})");
            return null; // Return null if validation fails
        }

        // Create the combined array with the total expected length
        float[] combined = new float[combinedDataLength]; // e.g., 294 floats

        // Use Buffer.BlockCopy for efficient memory copying
        // It operates on bytes, so multiply float counts by sizeof(float)
        int floatSizeInBytes = sizeof(float);
        int bytesPerHand = floatsPerHand * floatSizeInBytes;

        // Copy left hand data to the beginning of the combined array
        Buffer.BlockCopy(leftData, 0, combined, 0, bytesPerHand);

        // Copy right hand data immediately following the left hand data
        Buffer.BlockCopy(rightData, 0, combined, bytesPerHand, bytesPerHand);

        return combined; // Return the successfully combined array
    }


    // --- Networking ---

    // Coroutine to send the combined flat gesture data array to the server
    private IEnumerator PostGestureData(float[] combinedData)
    {
        // Prevent overlapping requests
        isRequestPending = true;

        // Create the request object using the FlatDataRequest class
        FlatDataRequest requestData = new FlatDataRequest { data = combinedData };
        string jsonData = JsonUtility.ToJson(requestData); // Serialize to {"data": [....]}

        // Optional: Log the JSON being sent (can be very long and impact performance)
        // Debug.Log($"[{nameof(GestureClassifier)}] Sending JSON ({combinedData.Length} floats) to {serverUrl}");
        // if (combinedData.Length < 50) Debug.Log(jsonData); // Log only if short

        // Use 'using' for automatic disposal of the UnityWebRequest
        using (UnityWebRequest request = new UnityWebRequest(serverUrl, "POST"))
        {
            // Encode the JSON string to UTF8 bytes
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

            // Configure the request: upload JSON data, download response buffer
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            // Set necessary headers
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            // Optional: Set a timeout (e.g., 10 seconds)
            request.timeout = 10;

            // Send the request and wait for the response
            yield return request.SendWebRequest();

            // --- Process the Server Response ---
            string responseJson = request.downloadHandler?.text; // Get response text safely

            switch (request.result)
            {
                case UnityWebRequest.Result.ConnectionError:
                    Debug.LogError($"[{nameof(GestureClassifier)}] Network Error: {request.error}. Unable to connect to server at {serverUrl}.");
                    if (classificationTextDisplay != null) classificationTextDisplay.text = "Network Error";
                    break;

                case UnityWebRequest.Result.DataProcessingError:
                    // Error processing data (rare for simple JSON)
                    Debug.LogError($"[{nameof(GestureClassifier)}] Data Processing Error: {request.error}.");
                    if (classificationTextDisplay != null) classificationTextDisplay.text = "Data Error";
                    break;

                case UnityWebRequest.Result.ProtocolError: // HTTP error (4xx, 5xx)
                    Debug.LogError($"[{nameof(GestureClassifier)}] HTTP Error: {request.responseCode} - {request.error}");
                    if (!string.IsNullOrEmpty(responseJson)) // Log server's error message if available
                    {
                        Debug.LogError($"[{nameof(GestureClassifier)}] Server Response Body: {responseJson}");
                    }
                    if (classificationTextDisplay != null) classificationTextDisplay.text = $"Server Error: {request.responseCode}";
                    // Specific handling for 422?
                    if (request.responseCode == 422)
                    {
                        Debug.LogError($"[{nameof(GestureClassifier)}] Received 422 Unprocessable Entity - Server rejected the data format/content. Check server logs and ensure Unity data format matches server expectations.");
                    }
                    break;

                case UnityWebRequest.Result.Success: // Request successful (2xx status code)
                    // Debug.Log($"[{nameof(GestureClassifier)}] Response Received: {responseJson}"); // Log raw success response if needed
                    try
                    {
                        // Attempt to parse the NESTED JSON response from the server
                        ClassificationResponse response = JsonUtility.FromJson<ClassificationResponse>(responseJson);

                        // Check if parsing was successful and expected fields exist
                        if (response != null && response.left_hand != null && response.right_hand != null)
                        {
                            // Update UI using the nested response data
                            if (classificationTextDisplay != null)
                            {
                                // Process left hand result text (handle potential server error message)
                                string leftText = response.left_hand.error ??
                                                  (response.left_hand.classification != null ?
                                                   $"{response.left_hand.classification} ({response.left_hand.confidence:F2})" :
                                                   "N/A");
                                // Process right hand result text
                                string rightText = response.right_hand.error ??
                                                   (response.right_hand.classification != null ?
                                                    $"{response.right_hand.classification} ({response.right_hand.confidence:F2})" :
                                                    "N/A");

                                // Apply confidence threshold logic *for display*
                                // Decide how to display low confidence results or errors
                                string displayLeft = (response.left_hand.error == null && response.left_hand.confidence >= minConfidence)
                                                     ? response.left_hand.classification // Just show name if confident
                                                     : $"(Low Conf/Err: {leftText})"; // Show details if not

                                string displayRight = (response.right_hand.error == null && response.right_hand.confidence >= minConfidence)
                                                      ? response.right_hand.classification // Just show name if confident
                                                      : $"(Low Conf/Err: {rightText})"; // Show details if not

                                classificationTextDisplay.text = $"L: {displayLeft}\nR: {displayRight}";
                            }

                            // Log classification details (even if low confidence)
                            Debug.Log($"[{nameof(GestureClassifier)}] Response Parsed OK: Left='{response.left_hand.classification ?? "null"}' (Conf: {response.left_hand.confidence:F2}, Err: {response.left_hand.error ?? "None"}), Right='{response.right_hand.classification ?? "null"}' (Conf: {response.right_hand.confidence:F2}, Err: {response.right_hand.error ?? "None"})");

                            // ----> TODO: Add your game logic here <----
                            // Example: Trigger actions based on confident classifications
                            // if (response.left_hand.error == null && response.left_hand.confidence >= minConfidence) {
                            //    TriggerLeftHandAction(response.left_hand.classification);
                            // }
                            // if (response.right_hand.error == null && response.right_hand.confidence >= minConfidence) {
                            //    TriggerRightHandAction(response.right_hand.classification);
                            // }
                            // ------------------------------------------
                        }
                        else // JSON parsing seemed successful, but structure was wrong (e.g., missing left_hand)
                        {
                            Debug.LogError($"[{nameof(GestureClassifier)}] Failed to parse expected fields from JSON response. Check server response format. Response text: {responseJson}");
                            if (classificationTextDisplay != null) classificationTextDisplay.text = "Parse Error (Fields)";
                        }
                    }
                    catch (Exception e) // Catch potential errors during JSON parsing or UI update
                    {
                        Debug.LogError($"[{nameof(GestureClassifier)}] Error processing successful server response: {e.Message}\nResponse JSON: {responseJson}\nStackTrace: {e.StackTrace}");
                        if (classificationTextDisplay != null) classificationTextDisplay.text = "Response Process Error";
                    }
                    break; // End of Success case
            }
        } // End of 'using' block (UnityWebRequest is disposed)

        // Allow the next request to be sent
        isRequestPending = false;
    }
}