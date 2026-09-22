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
            CheckTemperatureLabels();
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
            File.WriteAllText("Library/surf-release-validation.txt","PASS: imported portable project, scene script references, unique IK and temperature UI, URDF FK/IK, 12 temperature label viewport checks. No Play or hardware commands.");
            if(Application.isBatchMode)EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText("Library/surf-release-validation.txt","FAIL: "+e);Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);}
    }
    static void CheckTemperatureLabels()
    {
        var root = new GameObject("Label validation", typeof(RectTransform));
        try
        {
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            labelObject.transform.SetParent(root.transform, false);
            var label = labelObject.GetComponent<UnityEngine.UI.Text>();
            var type = typeof(SURF.Risk.HeadsetSafetyWarningOverlay).GetNestedType("ZoneUi", System.Reflection.BindingFlags.NonPublic);
            var ui = Activator.CreateInstance(type, new object[]{root.GetComponent<RectTransform>(), label, new UnityEngine.UI.Image[0]});
            foreach(float width in new[]{320f,720f,1280f})
            foreach(var rect in new[]{new Rect(0,0,9,9), new Rect(width/2-10,350,9,9),new Rect(-width/2,-360,9,9),new Rect(-50,330,100,50)})
            {
                type.GetMethod("SetRect").Invoke(ui,new object[]{rect,new Vector2(width,720)});
                var r=label.rectTransform;
                var pos=rect.position+r.anchoredPosition;
                if(r.sizeDelta.x<200 || pos.x < -width/2 || pos.y < -360 || pos.x+r.sizeDelta.x>width/2 || pos.y+r.sizeDelta.y>360)
                    throw new Exception("Temperature label clipped or too narrow");
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}
