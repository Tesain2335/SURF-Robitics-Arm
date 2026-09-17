using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SurfArmPublishingSetup
{
    static SurfArmPublishingSetup(){EditorApplication.update+=ApplyRequest;}
    static void ApplyRequest()
    {
        const string request="Library/surf-enable-feedback-arm.request";
        if(!File.Exists(request)||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)return;
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/OutdoorsScene.unity")return;
        int count=0;
        foreach(var root in scene.GetRootGameObjects())
            foreach(var publisher in root.GetComponentsInChildren<IkToRosPublisher>(true))
            {
                Undo.RecordObject(publisher,"Enable feedback-aligned arm publishing");
                publisher.publishCommands=true;
                EditorUtility.SetDirty(publisher);count++;
            }
        if(count!=1){File.WriteAllText("Library/surf-enable-feedback-arm.result","ERROR: expected one publisher, got "+count);File.Delete(request);return;}
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))return;
        File.WriteAllText("Library/surf-enable-feedback-arm.result","Enabled one publisher; pose alignment and fresh feedback required before sending.");
        File.Delete(request);
    }
}
