using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SurfKinematicsAudit
{
    [InitializeOnLoadMethod]
    static void Schedule() { EditorApplication.delayCall += Run; }

    [MenuItem("SURF/Audit kinematics")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var text = new StringBuilder();
        text.AppendLine("Scene: " + SceneManager.GetActiveScene().path);
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "piper" && t.name != "base_link" && !t.name.StartsWith("link") &&
                !t.name.Contains("Target") && !t.name.Contains("Controller") && !t.name.Contains("IK") &&
                !t.name.Contains("Aim") && t.GetComponent<IkToRosPublisher>() == null) continue;
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            text.AppendLine("\n" + path + " id=" + t.GetInstanceID());
            text.AppendLine("local pos=" + t.localPosition.ToString("F6") + " rot=" + t.localRotation.ToString("F6") +
                " scale=" + t.localScale + " world pos=" + t.position.ToString("F6") + " world rot=" + t.rotation.ToString("F6"));
            foreach (var component in t.GetComponents<Component>())
            {
                if (component == null) continue;
                text.AppendLine(component.GetType().FullName);
                var serialized = new SerializedObject(component);
                var prop = serialized.GetIterator();
                while (prop.NextVisible(true))
                    if (prop.propertyType == SerializedPropertyType.ObjectReference)
                        text.AppendLine("REF " + prop.propertyPath + "=" + (prop.objectReferenceValue ? prop.objectReferenceValue.name + "#" + prop.objectReferenceValue.GetInstanceID() : "NULL"));
                if (component is Behaviour || component is ArticulationBody || component is UnityEngine.Animations.IConstraint)
                    text.AppendLine(EditorJsonUtility.ToJson(component));
            }
        }
        File.WriteAllText(Path.GetFullPath("Library/surf-kinematics-audit.txt"), text.ToString());
        Debug.Log("[SURF] Kinematics audit written to Library/surf-kinematics-audit.txt");
    }
}
