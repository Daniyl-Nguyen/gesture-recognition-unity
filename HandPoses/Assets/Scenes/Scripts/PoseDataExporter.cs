using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using Oculus.Interaction.Body.PoseDetection;
using System.Collections.Generic;

public class PoseDataExporter : EditorWindow
{
    private BodyPoseData poseAsset;
    private Animator referenceAnimator;
    // Maps "Joints.Array.data[x]" → "Left Thumb Base", etc.
    private Dictionary<int, string> _jointIndexToName = new Dictionary<int, string>();

    [MenuItem("Tools/Body Pose Data Exporter")]
    public static void ShowWindow()
    {
        GetWindow<PoseDataExporter>("Body Pose Data Exporter");
    }

    private void OnGUI()
    {
        GUILayout.Label("Body Pose Data Exporter", EditorStyles.boldLabel);

        poseAsset = EditorGUILayout.ObjectField("Pose Asset", poseAsset, typeof(BodyPoseData), false) as BodyPoseData;
        EditorGUILayout.HelpBox("Assign an Animator component from your character/avatar in the scene.", MessageType.Info);
        referenceAnimator = EditorGUILayout.ObjectField("Reference Animator", referenceAnimator, typeof(Animator), true) as Animator;

        if (GUILayout.Button("Export Pose Data"))
        {
            if (poseAsset != null && referenceAnimator != null)
            {
                ExportPoseData();
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please assign both a pose asset and a reference animator", "OK");
            }
        }
    }

    private void ExportPoseData()
    {
        // Clear our joint index → name lookup each time we export
        _jointIndexToName.Clear();

        var serializedObject = new SerializedObject(poseAsset);
        StringBuilder csv = new StringBuilder();

        // Write some top-level metadata
        csv.AppendLine("Pose Asset Name: " + poseAsset.name);
        csv.AppendLine("Export Date: " + System.DateTime.Now);
        csv.AppendLine();

        // We'll store the categorized data in a dictionary
        Dictionary<string, List<(string propName, string value)>> categorizedData = new Dictionary<string, List<(string propName, string value)>>();

        // Iterate over all serialized properties of the asset
        SerializedProperty iterator = serializedObject.GetIterator();
        while (iterator.Next(true))
        {
            string rawValue = GetFormattedPropertyValue(iterator);
            if (string.IsNullOrEmpty(rawValue))
                continue; // Skip empty/unhandled properties

            // Figure out which category (top-level) this property belongs to
            string category = GetPropertyCategory(iterator.propertyPath);

            // If there's no list for this category yet, create it
            if (!categorizedData.ContainsKey(category))
            {
                categorizedData[category] = new List<(string, string)>();
            }

            // Clean up the property name (e.g., remove "m_", add spaces, etc.)
            string propName = GetCleanPropertyName(iterator.propertyPath);

            // Detect if this is "_id", "_position", "_rotation", etc. from Joints
            int arrayIndex = GetJointArrayIndex(iterator.propertyPath);
            if (arrayIndex >= 0) // We are inside "Joints.Array.data[x]"
            {
                // If it's the joint ID property, store the name in our lookup
                if (propName.ToLower().Contains("id"))
                {
                    if (int.TryParse(rawValue, out int jointId))
                    {
                        string jointName = GetJointName(jointId);
                        _jointIndexToName[arrayIndex] = jointName;
                        // Also append the name to the property name itself
                        propName += $" ({jointName})";
                    }
                }
                else
                {
                    // If it's position, rotation, or scale, 
                    // see if we already know the joint's name for this array index
                    if (_jointIndexToName.ContainsKey(arrayIndex))
                    {
                        propName += $" ({_jointIndexToName[arrayIndex]})";
                    }
                }
            }

            // Add the property to the category list
            categorizedData[category].Add((propName, rawValue));
        }

        // Export each category block into the CSV
        foreach (var keyValue in categorizedData)
        {
            string category = keyValue.Key;
            csv.AppendLine($"=== {category} ===");
            csv.AppendLine("Property Name,Value");

            foreach (var (propName, value) in keyValue.Value)
            {
                csv.AppendLine($"{propName},{value}");
            }
            csv.AppendLine();
        }

        // Save to file
        string filePath = EditorUtility.SaveFilePanel(
            "Save Pose Data",
            "",
            $"{poseAsset.name}_PoseData.csv",
            "csv"
        );

        if (!string.IsNullOrEmpty(filePath))
        {
            File.WriteAllText(filePath, csv.ToString());
            Debug.Log("Pose data exported to: " + filePath);
            EditorUtility.RevealInFinder(filePath);
        }
    }

    ////////////////////////////////////////////////////////////////////////////
    /// Helper Methods
    ////////////////////////////////////////////////////////////////////////////

    // Attempts to extract "x" from "Joints.Array.data[x]"
    private int GetJointArrayIndex(string propertyPath)
    {
        // Example path: "m_joints.Array.data[2]._position"
        // We want to extract the 2
        // Try to find ".Array.data["
        int arrayIdx = -1;
        int arrayPos = propertyPath.IndexOf(".Array.data[");
        if (arrayPos >= 0)
        {
            // Move to the bracket
            int bracketStart = propertyPath.IndexOf('[', arrayPos);
            int bracketEnd = propertyPath.IndexOf(']', bracketStart + 1);
            if (bracketStart >= 0 && bracketEnd > bracketStart)
            {
                string numString = propertyPath.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                int.TryParse(numString, out arrayIdx);
            }
        }
        return arrayIdx;
    }

    private string GetJointName(int jointId)
    {
        // Add or adjust IDs as needed. This is just an example.
        switch (jointId)
        {
            case 11: return "Left Hand";
            case 15: return "Right Hand";

            case 22: return "Left Thumb Base";
            case 23: return "Left Thumb Joint1";
            case 24: return "Left Thumb Joint2";
            case 25: return "Left Thumb Tip";
            case 26: return "Left Index Base";
            case 27: return "Left Index Joint1";
            case 28: return "Left Index Tip";
            case 29: return "Left Middle Base";
            case 30: return "Left Middle Joint1";
            case 31: return "Left Middle Tip";
            case 32: return "Left Ring Base";
            case 33: return "Left Ring Joint1";
            case 34: return "Left Ring Tip";
            case 35: return "Left Pinky Base";
            case 36: return "Left Pinky Joint1";
            case 37: return "Left Pinky Tip";

            case 38: return "Right Thumb Base";
            case 39: return "Right Thumb Joint1";
            case 40: return "Right Thumb Joint2";
            case 41: return "Right Thumb Tip";
            case 42: return "Right Index Base";
            case 43: return "Right Index Joint1";
            case 44: return "Right Index Tip";
            case 45: return "Right Middle Base";
            case 46: return "Right Middle Joint1";
            case 47: return "Right Middle Tip";
            case 48: return "Right Ring Base";
            case 49: return "Right Ring Joint1";
            case 50: return "Right Ring Tip";
            case 51: return "Right Pinky Base";
            case 52: return "Right Pinky Joint1";
            case 53: return "Right Pinky Tip";

            default: return $"Joint {jointId}";
        }
    }

    private string GetFormattedPropertyValue(SerializedProperty prop)
    {
        switch (prop.propertyType)
        {
            case SerializedPropertyType.Integer:
                return prop.intValue.ToString();
            case SerializedPropertyType.Float:
                return prop.floatValue.ToString("F4");
            case SerializedPropertyType.String:
                return prop.stringValue;
            case SerializedPropertyType.Boolean:
                return prop.boolValue.ToString();
            case SerializedPropertyType.Vector3:
                Vector3 v3 = prop.vector3Value;
                return $"({v3.x:F4}, {v3.y:F4}, {v3.z:F4})";
            case SerializedPropertyType.Quaternion:
                Quaternion q = prop.quaternionValue;
                return $"({q.x:F4}, {q.y:F4}, {q.z:F4}, {q.w:F4})";
            default:
                // For other property types, return nothing
                return "";
        }
    }

    private string GetPropertyCategory(string propertyPath)
    {
        // Typically the first segment: e.g., "m_joints" becomes "Joints"
        string[] parts = propertyPath.Split('.');
        if (parts.Length > 0)
        {
            string category = parts[0].TrimStart('m', '_');
            return char.ToUpper(category[0]) + category.Substring(1);
        }
        return "Other";
    }

    private string GetCleanPropertyName(string propertyPath)
    {
        // e.g. "m_joints.Array.data[0]._position" → "_position" → "Position"
        string[] parts = propertyPath.Split('.');
        string name = parts[parts.Length - 1];
        name = name.TrimStart('m', '_');

        // Insert spaces before capital letters
        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
            {
                name = name.Insert(i, " ");
                i++;
            }
        }

        // Capitalize first letter
        if (!string.IsNullOrEmpty(name))
        {
            name = char.ToUpper(name[0]) + name.Substring(1);
        }
        return name;
    }
}