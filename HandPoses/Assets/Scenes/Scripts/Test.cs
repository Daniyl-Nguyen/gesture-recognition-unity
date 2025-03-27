using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;
using System;
using System.Collections;
using UnityEngine.Networking;
using Newtonsoft.Json;


public class Test : MonoBehaviour
{
    public Transform leftHand;  // Assign 'Left_Hand' GameObject in the Inspector (Wrist)
    public Transform rightHand; // Assign 'Right_Hand' GameObject in the Inspector (Wrist)

    private List<Transform> leftHandJoints = new List<Transform>();
    private List<Transform> rightHandJoints = new List<Transform>();

    public string gestureName = "Gesture"; // Set this in Unity Inspector
    private string projectPath;
    private string currentHand = "Left"; // Default to left hand

    private string apiUrl = "http://localhost:8000/classify_data";

    void Start()
    {
        // Set path within the Unity project
        projectPath = Path.Combine(Application.dataPath, "..", "Gestures", gestureName);

        // Create directories for left and right hand
        Directory.CreateDirectory(Path.Combine(projectPath, "Left"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Right"));

        Debug.Log($"Saving data to: {projectPath}");

        // Auto-populate joint lists, filtering out unnecessary joints
        if (leftHand != null)
        {
            leftHandJoints = GetHandJoints(leftHand);
            Debug.Log($"Left hand joints found: {leftHandJoints.Count}");
        }

        if (rightHand != null)
        {
            rightHandJoints = GetHandJoints(rightHand);
            Debug.Log($"Right hand joints found: {rightHandJoints.Count}");
        }
    }

    void Update()
    {
        // Switch hand (Press 'H')
        if (Input.GetKeyDown(KeyCode.H))
        {
            currentHand = (currentHand == "Left") ? "Right" : "Left";
            Debug.Log($"Switched to {currentHand} hand.");
        }

        // Save hand data (Press 'S')
        if (Input.GetKeyDown(KeyCode.S))
        {
            SaveHandData();
        }

        if(Input.GetKeyDown(KeyCode.D))
        {
            var data = GetHandData();
            StartCoroutine(SendHandData(data));
            Debug.Log("Pressed D");
        }
    }

    void SaveHandData()
    {
        List<Transform> handJoints = (currentHand == "Left") ? leftHandJoints : rightHandJoints;
        Transform wrist = (currentHand == "Left") ? leftHand : rightHand; // Wrist tracking

        if (handJoints == null || handJoints.Count == 0 || wrist == null)
        {
            Debug.LogWarning($"No joints found for {currentHand} hand.");
            return;
        }

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string folderPath = Path.Combine(projectPath, currentHand); // "Gestures/GestureName/Left" or "Right"
        string fileName = $"{gestureName}_{currentHand}_{timestamp}.csv";
        string filePath = Path.Combine(folderPath, fileName);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Timestamp,Hand,Joint,PositionX,PositionY,PositionZ,RotationX,RotationY,RotationZ,RotationW");

        float currentTime = Time.time;

        // Add wrist first
        Vector3 wristPos = wrist.position;
        Quaternion wristRot = wrist.rotation;
        sb.AppendLine($"{currentTime},{currentHand},Wrist,{wristPos.x},{wristPos.y},{wristPos.z},{wristRot.x},{wristRot.y},{wristRot.z},{wristRot.w}");

        // Add joints
        foreach (var joint in handJoints)
        {
            Vector3 pos = joint.position;
            Quaternion rot = joint.rotation;
            sb.AppendLine($"{currentTime},{currentHand},{joint.name},{pos.x},{pos.y},{pos.z},{rot.x},{rot.y},{rot.z},{rot.w}");
        }

        File.WriteAllText(filePath, sb.ToString());
        Debug.Log($"Saved {currentHand} hand data to {filePath}");
    }

    List<Transform> GetHandJoints(Transform handRoot)
    {
        List<Transform> joints = new List<Transform>();
        foreach (Transform joint in handRoot.GetComponentsInChildren<Transform>())
        {
            if (joint != handRoot && !joint.name.Contains("ProximalDef") && !joint.name.Contains("IntermediateDef")) // Remove unwanted joints
                joints.Add(joint);
        }
        return joints;
    }

    List<float> GetHandData()
    {
        List<Transform> handJoints = (currentHand == "Left") ? leftHandJoints : rightHandJoints;
        Transform wrist = (currentHand == "Left") ? leftHand : rightHand; // Wrist tracking

        List<float> handData = new List<float>();

        if (handJoints == null || handJoints.Count == 0 || wrist == null)
        {
            Debug.LogWarning($"No joints found for {currentHand} hand.");
            return handData;
        }

        // Add wrist data first
        Vector3 wristPos = wrist.position;
        Quaternion wristRot = wrist.rotation;
        handData.AddRange(new float[] { wristPos.x, wristPos.y, wristPos.z, wristRot.x, wristRot.y, wristRot.z, wristRot.w });

        // Add joint data
        foreach (var joint in handJoints)
        {
            Vector3 pos = joint.position;
            Quaternion rot = joint.rotation;
            handData.AddRange(new float[] { pos.x, pos.y, pos.z, rot.x, rot.y, rot.z, rot.w });
        }

        return handData;
    }

    public IEnumerator SendHandData(List<float> handData)
    {
        if (handData == null || handData.Count == 0)
        {
            Debug.LogWarning("No hand data to send.");
            yield break;
        }

        // Create JSON request body
        var requestData = new { data = handData };
        string json = JsonConvert.SerializeObject(requestData);

        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = request.downloadHandler.text;
                ClassificationResponse response = JsonConvert.DeserializeObject<ClassificationResponse>(responseText);
                Debug.Log("Classification: " + response.classification);
            }
            else
            {
                Debug.LogError("Error sending hand data: " + request.error);
            }
        }
    }

    private IEnumerator TestAPI()
    {
        using (UnityWebRequest request = UnityWebRequest.Get(apiUrl))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Response: " + request.downloadHandler.text);
            }
            else
            {
                Debug.LogError("Error: " + request.error);
            }
        }
    }

    [System.Serializable]
    public class ClassificationResponse
    {
        public string classification;
    }


}
