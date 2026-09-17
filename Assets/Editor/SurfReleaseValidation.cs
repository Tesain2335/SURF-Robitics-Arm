using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class SurfReleaseValidation
{
    [MenuItem("SURF/Validate portable package (offline)")]
    public static void Run()
    {
        try
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play first");
            var model=new PiperKinematics(File.ReadAllText("Assets/PiperRobot/piper_description.urdf"));
            var q=new float[]{.1f,.8f,-1.1f,.2f,.3f,-.1f};model.Forward(q,out var target,out _);
            if(!model.Solve(target,q,out var solution,out var error)||error>.003f)throw new Exception("FK/IK failure");
            // Preview scene loading does not replace or save the user's current scene.
            var scene=EditorSceneManager.OpenPreviewScene("Assets/OutdoorsScene.unity");
            try
            {
                int missing=0,solvers=0,overlays=0;
                foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))
                {
                    missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                    if(t.GetComponent<PiperConstrainedIK>())solvers++;
                    if(t.GetComponent<SURF.Risk.HeadsetSafetyWarningOverlay>())overlays++;
                }
                if(missing!=0||solvers!=1||overlays!=1)throw new Exception($"Scene missing={missing}, IK={solvers}, temperature overlays={overlays}");
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
            File.WriteAllText("Library/surf-release-validation.txt","PASS: imported portable project, scene script references, unique IK and temperature UI, URDF FK/IK. No Play or hardware commands.");
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText("Library/surf-release-validation.txt","FAIL: "+e);Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);}
    }
}
