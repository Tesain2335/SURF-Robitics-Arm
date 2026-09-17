using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SurfVRViewSetup
{
    static SurfVRViewSetup(){EditorApplication.update+=Apply;}
    static void Apply()
    {
        const string request="Library/surf-vr-view.request";
        if(!File.Exists(request)||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)return;
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/OutdoorsScene.unity")return;
        var roots=scene.GetRootGameObjects();
        var xr=roots.SelectMany(x=>x.GetComponentsInChildren<Component>(true)).Single(x=>x&&x.GetType().FullName=="Unity.XR.CoreUtils.XROrigin");
        var serialized=new SerializedObject(xr);
        var camera=serialized.FindProperty("m_Camera").objectReferenceValue as Camera;
        var publisher=roots.SelectMany(x=>x.GetComponentsInChildren<IkToRosPublisher>(true)).Single();
        if(!camera||!publisher.solver||!publisher.solver.robotBase)throw new System.Exception("Missing XR camera or robot base");
        var robot=publisher.solver.robotBase.root;
        if(robot==xr.transform.root)throw new System.Exception("Robot must remain outside XR Origin");
        var view=xr.GetComponent<PiperVRViewFraming>()??Undo.AddComponent<PiperVRViewFraming>(xr.gameObject);
        Undo.RecordObjects(new Object[]{view,xr.transform,camera},"Frame Piper in VR");
        view.xrOrigin=xr.transform;view.head=camera.transform;view.robot=robot;
        view.minimumDistance=1.2f;view.autoFrameOnFirstTracking=true;
        camera.enabled=true;camera.nearClipPlane=.03f;
        int disabled=0;
        foreach(var other in roots.SelectMany(x=>x.GetComponentsInChildren<Camera>(true)))
            if(other!=camera&&other.name=="Main Camera"&&other.enabled)
            {Undo.RecordObject(other,"Use single XR camera");other.enabled=false;EditorUtility.SetDirty(other);disabled++;}
        if(!view.FrameRobot())throw new System.Exception("Could not frame robot");
        // Preview framing is saved, but actual tracked head height is recalibrated on Play.
        view.framed=false;
        foreach(var item in new Object[]{view,xr.transform,camera})EditorUtility.SetDirty(item);
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))return;
        File.WriteAllText("Library/surf-vr-view.result","PASS: XR camera="+camera.name+", duplicate cameras disabled="+disabled+", origin="+xr.transform.position+", auto frame on tracked HMD; B/F8 reframe; robot="+robot.position);
        File.Delete(request);
    }
}
