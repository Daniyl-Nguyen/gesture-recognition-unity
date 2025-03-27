using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class GestureClassifier : MonoBehaviour
{
    [System.Serializable]
    public class DataRequest
    {
        public float[] data;
    }

    [System.Serializable]
    public class ClassificationResponse
    {
        public string classification;
        public float confidence;
    }

    private string serverUrl = "http://127.0.0.1:8000/classify_data";

    // Example method to send gesture data to the server
    public void SendGestureData(float[] gestureData)
    {
        StartCoroutine(PostGestureData(gestureData));
    }

    private IEnumerator PostGestureData(float[] gestureData)
    {
        // Create the data request object
        DataRequest requestData = new DataRequest { data = gestureData };
        string jsonData = JsonUtility.ToJson(requestData);

        // Create a UnityWebRequest for POST
        UnityWebRequest request = new UnityWebRequest(serverUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        // Send the request and wait for a response
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            // Parse the response
            string responseJson = request.downloadHandler.text;
            ClassificationResponse response = JsonUtility.FromJson<ClassificationResponse>(responseJson);

            // Log the classification and confidence
            Debug.Log($"Gesture: {response.classification}, Confidence: {response.confidence}");
        }
        else
        {
            Debug.LogError($"Error: {request.error}");
        }
    }
}