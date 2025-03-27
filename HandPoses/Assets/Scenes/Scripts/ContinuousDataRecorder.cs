using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;
using System;

public class ContinuousDataRecorder : MonoBehaviour
{
    public Transform leftHand;  // Assign 'Left_Hand' in Inspector
    public Transform rightHand; // Assign 'Right_Hand' in Inspector

    [Tooltip("How many frames to wait between saving data points.")]
    [Range(1, 100)] // Ensure a reasonable range
    public int frameInterval = 10; // Set the interval (e.g., save every 10 frames)

    private List<Transform> leftHandJoints = new List<Transform>();
    private List<Transform> rightHandJoints = new List<Transform>();

    private string projectPath;
    private int frameCount = 0; // Counter for frames

    void Start()
    {
        // Set path to "Gestures/Continuous" relative to Assets folder
        projectPath = Path.Combine(Application.dataPath, "..", "Gestures", "Continuous");

        // Create directories for left and right hand if they don't exist
        Directory.CreateDirectory(Path.Combine(projectPath, "Left"));
        Directory.CreateDirectory(Path.Combine(projectPath, "Right"));

        Debug.Log($"Saving continuous data to: {projectPath} every {frameInterval} frames.");

        // Auto-populate joint lists
        if (leftHand != null)
        {
            leftHandJoints = GetHandJoints(leftHand);
            if (leftHandJoints.Count == 0)
            {
                Debug.LogWarning("No valid joints found under the assigned Left Hand transform.");
            }
        }
        else
        {
            Debug.LogWarning("Left Hand transform is not assigned in the Inspector.");
        }

        if (rightHand != null)
        {
            rightHandJoints = GetHandJoints(rightHand);
            if (rightHandJoints.Count == 0)
            {
                Debug.LogWarning("No valid joints found under the assigned Right Hand transform.");
            }
        }
        else
        {
            Debug.LogWarning("Right Hand transform is not assigned in the Inspector.");
        }

        // Ensure frameInterval is at least 1 to avoid issues
        if (frameInterval < 1)
        {
            frameInterval = 1;
            Debug.LogWarning("Frame interval cannot be less than 1. Setting to 1.");
        }
    }

    void Update()
    {
        // Increment the frame counter
        frameCount++;

        // Check if the counter has reached the desired interval
        if (frameCount >= frameInterval)
        {
            // Reset the counter
            frameCount = 0;

            // Save data for both hands
            SaveHandData("Left", leftHand, leftHandJoints);
            SaveHandData("Right", rightHand, rightHandJoints);
        }
    }

    void SaveHandData(string handName, Transform wrist, List<Transform> handJoints)
    {
        // Check if wrist is assigned and if there are any joints to save
        if (wrist == null || handJoints == null || handJoints.Count == 0)
        {
            // Don't log every frame interval if the hand isn't set up, Start() already warned.
            // Consider adding a flag if you want to log this repeatedly.
            // Debug.LogWarning($"Skipping save for {handName} hand - Wrist or joints not properly set up.");
            return;
        }

        // Use a precise timestamp for the filename, but consistent timestamp within the file for this frame
        string fileTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff"); // Millisecond precision for unique filenames
        float frameTimestamp = Time.time; // Use Unity's frame time for data consistency within the file

        string folderPath = Path.Combine(projectPath, handName); // "Gestures/Continuous/Left" or "Right"
        string fileName = $"{handName}_{fileTimestamp}.csv";
        string filePath = Path.Combine(folderPath, fileName);

        StringBuilder sb = new StringBuilder();
        // Header describes the data columns
        sb.AppendLine("Timestamp,Hand,Joint,PositionX,PositionY,PositionZ,RotationX,RotationY,RotationZ,RotationW");

        // --- Record Wrist Data ---
        Vector3 wristPos = wrist.position;
        Quaternion wristRot = wrist.rotation;
        sb.AppendLine($"{frameTimestamp},{handName},Wrist,{wristPos.x},{wristPos.y},{wristPos.z},{wristRot.x},{wristRot.y},{wristRot.z},{wristRot.w}");

        // --- Record Joint Data ---
        foreach (var joint in handJoints)
        {
            if (joint == null) continue; // Safety check in case a joint was destroyed

            Vector3 pos = joint.position;
            Quaternion rot = joint.rotation;
            sb.AppendLine($"{frameTimestamp},{handName},{joint.name},{pos.x},{pos.y},{pos.z},{rot.x},{rot.y},{rot.z},{rot.w}");
        }

        try
        {
            // Write the collected data to the CSV file
            File.WriteAllText(filePath, sb.ToString());
            // Optional: Reduce log frequency if needed, maybe log only every N saves
            // Debug.Log($"Saved {handName} hand data to {filePath}");
        }
        catch (IOException ex)
        {
            Debug.LogError($"Error writing file {filePath}: {ex.Message}");
            // Handle potential file access errors (e.g., disk full, permissions)
        }
        catch (Exception ex)
        {
            Debug.LogError($"An unexpected error occurred while saving {handName} data: {ex.Message}");
        }
    }

    List<Transform> GetHandJoints(Transform handRoot)
    {
        List<Transform> joints = new List<Transform>();
        if (handRoot == null) return joints; // Return empty list if root is null

        // Use GetComponentsInChildren<Transform>(true) to include inactive objects if needed,
        // but false is usually sufficient unless joints are being deactivated.
        foreach (Transform joint in handRoot.GetComponentsInChildren<Transform>())
        {
            // Exclude the root itself and potentially unwanted "Def" (deformation?) bones if they exist
            if (joint != handRoot && !joint.name.EndsWith("Def", StringComparison.OrdinalIgnoreCase)) // More robust check
            {
                joints.Add(joint);
            }
        }
        return joints;
    }
}