#if UNITY_EDITOR
using System.IO;
using System.Net;
using UnityEditor;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;

[InitializeOnLoad]
public static class SurfLauncherConnection
{
    static SurfLauncherConnection()
    {
        EditorApplication.delayCall += Apply;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Apply();
        };
    }

    [MenuItem("SURF/更新启动器 ROS 地址")]
    public static void Apply()
    {
        string path = Path.Combine(Application.dataPath, "../.surf-ros-ip.txt");
        if (!File.Exists(path)) return;
        string ip = File.ReadAllText(path).Trim();
        if (!IPAddress.TryParse(ip, out _)) return;
        // Change only scene instances in memory; never rewrite saved scenes or
        // the ROS prefab. Apply before Play so Awake connects to the current IP.
        foreach (var connection in Resources.FindObjectsOfTypeAll<ROSConnection>())
        {
            if (EditorUtility.IsPersistent(connection) || !connection.gameObject.scene.IsValid()) continue;
            connection.RosIPAddress = ip;
            connection.RosPort = 10000;
        }
        Debug.Log("[SURF] ROS address prepared: " + ip + ":10000");
    }
}
#endif
