using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SurfLatencySetup
{
    static SurfLatencySetup(){EditorApplication.update+=Apply;}
    static void Apply()
    {
        const string request="Library/surf-latency.request";
        if(!File.Exists(request)||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)return;
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/OutdoorsScene.unity")return;
        int count=0;
        foreach(var root in scene.GetRootGameObjects())
            foreach(var p in root.GetComponentsInChildren<IkToRosPublisher>(true))
            {
                if(!p.solver)continue;
                Undo.RecordObject(p,"Reduce teleoperation latency");
                Undo.RecordObject(p.solver,"Reduce target rate limiting");
                p.publishHz=60f;p.solver.jointSpeedLimit=3f;
                EditorUtility.SetDirty(p);EditorUtility.SetDirty(p.solver);count++;
            }
        if(count!=1)return;
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))return;
        File.WriteAllText("Library/surf-latency.result","Saved: publishHz=60, jointSpeedLimit=3 rad/s; hardware speed configured by launcher.");
        File.Delete(request);
    }
}
