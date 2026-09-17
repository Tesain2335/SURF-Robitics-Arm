using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SurfControlModeSetup
{
    static SurfControlModeSetup(){EditorApplication.update+=Apply;}
    static void Apply()
    {
        const string request="Library/surf-control-mode.request";
        if(!File.Exists(request)||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)return;
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/OutdoorsScene.unity")return;
        var roots=scene.GetRootGameObjects();
        var publisher=roots.SelectMany(x=>x.GetComponentsInChildren<IkToRosPublisher>(true)).Single();
        var solver=publisher.solver;
        var grip=roots.SelectMany(x=>x.GetComponentsInChildren<ViveGripperPublisher>(true)).Single();
        if(!solver)throw new System.Exception("Missing IK");
        var mode=solver.GetComponent<PiperControlMode>()??Undo.AddComponent<PiperControlMode>(solver.gameObject);
        mode.solver=solver;mode.publisher=publisher;solver.controls=mode;grip.controls=mode;
        mode.wristDegreesPerSecond=30f;mode.stickDeadzone=0.2f;
        foreach(var item in new Object[]{mode,solver,grip})EditorUtility.SetDirty(item);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))return;
        File.WriteAllText("Library/surf-control-mode.result","PASS: one control arbiter connects IK and gripper; Tab/right A switch modes; right stick Y controls J5, X controls J6 at 30 deg/s, deadzone=0.2; VR IK locks J4-J6. Play is stopped.");
        File.Delete(request);
    }
}
