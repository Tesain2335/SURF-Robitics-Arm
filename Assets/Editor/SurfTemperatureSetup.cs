using System;
using System.IO;
using System.Linq;
using SURF.Risk;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SurfTemperatureSetup
{
    const string Request = "Library/surf-temperature.request";
    static SurfTemperatureSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SceneManager.GetActiveScene().path != "Assets/OutdoorsScene.unity") return;
        File.Delete(Request);
        try { Install(); }
        catch (Exception ex) { File.WriteAllText("Library/surf-temperature.result", "FAIL: " + ex); Debug.LogException(ex); }
    }
    [MenuItem("SURF/Install Temperature Warning Demo")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play before installing.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/OutdoorsScene.unity") throw new Exception("Open OutdoorsScene first.");
        var roots = scene.GetRootGameObjects();
        var xr = roots.SelectMany(x => x.GetComponentsInChildren<Component>(true))
            .Single(x => x && x.GetType().FullName == "Unity.XR.CoreUtils.XROrigin");
        var camera = new SerializedObject(xr).FindProperty("m_Camera").objectReferenceValue as Camera;
        var solver = roots.SelectMany(x => x.GetComponentsInChildren<PiperConstrainedIK>(true)).Single();
        if (!camera || !solver.robotBase) throw new Exception("Missing XR camera or robot base.");
        var root = roots.SingleOrDefault(x => x.name == "SURF Temperature Demo");
        if (root) throw new Exception("Temperature demo already exists; edit its existing zones in Inspector.");
        var shader = Shader.Find("HDRP/Lit");
        if (!shader) shader = Shader.Find("Standard");
        if (!shader) throw new Exception("No compatible material shader.");
        if (!AssetDatabase.IsValidFolder("Assets/SurfRisk")) AssetDatabase.CreateFolder("Assets", "SurfRisk");
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/SurfRisk/NeutralDemo.mat");
        if (!material)
        {
            material = new Material(shader);
            material.color = new Color(.55f, .58f, .62f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", material.color);
            AssetDatabase.CreateAsset(material, "Assets/SurfRisk/NeutralDemo.mat");
        }
        root = new GameObject("SURF Temperature Demo");
        Undo.RegisterCreatedObjectUndo(root, "Install SURF temperature demo");
        var zones = new SafetyTemperatureZone[4];
        string[] labels = { "Safe Workpiece", "Warm Equipment", "Hot Surface", "Critical Heat Source" };
        float[] temperatures = { 25, 50, 85, 130 };
        for (int i = 0; i < 4; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = labels[i] + " (SIM)";
            cube.transform.SetParent(root.transform, false);
            cube.transform.position = solver.robotBase.TransformPoint(new Vector3((i - 1.5f) * .30f, .10f, -.20f));
            cube.transform.localScale = new Vector3(.14f, .16f, .14f);
            cube.GetComponent<Renderer>().sharedMaterial = material;
            // These objects illustrate temperature only, with no physical interaction.
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            zones[i] = cube.AddComponent<SafetyTemperatureZone>();
            zones[i].Configure(labels[i], temperatures[i], 2f, .45f);
        }
        var overlay = root.AddComponent<HeadsetSafetyWarningOverlay>();
        overlay.SetCamera(camera);
        overlay.SetZones(zones);
        EditorUtility.SetDirty(overlay);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed.");
        File.WriteAllText("Library/surf-temperature.result", "PASS: 4 simulated zones (25/50/85/130 C); XR camera=" + camera.name +
            "; namespaced world-space overlay; no demo colliders; control scripts and robot pose unchanged. Scene saved; Play remains stopped.");
    }
}
