using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;

public static class SurfKinematicsSetup
{
    [InitializeOnLoadMethod]
    static void Schedule(){EditorApplication.delayCall+=ProcessRequest;}
    static void ProcessRequest()
    {
        if(!File.Exists("Library/surf-apply-kinematics.request")||EditorApplication.isPlayingOrWillChangePlaymode)return;
        try { Apply(); File.Delete("Library/surf-apply-kinematics.request"); }
        catch(Exception e){Debug.LogException(e);}
    }
    [MenuItem("SURF/Apply URDF kinematics repair")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stop Play before configuring kinematics.");
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/OutdoorsScene.unity")throw new Exception("Expected OutdoorsScene.");
        var transforms=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).ToArray();
        var robot=transforms.Single(x=>x.name=="piper" && x.parent==null);
        var robotBase=robot.Find("world/base_link");
        var publisher=robot.GetComponentInChildren<IkToRosPublisher>(true);
        var target=transforms.Single(x=>x.name=="Piper_IK_Target"||(x.name=="Target"&&x.parent&&x.parent.name=="Right Controller"));
        var controller=transforms.Single(x=>x.name=="Right Controller");
        // Preserve components and legacy settings for recovery; disable competing writers.
        foreach(var b in robot.GetComponentsInChildren<Behaviour>(true))
            if(b is Animator||b.GetType().Namespace=="UnityEngine.Animations.Rigging"||
                b.GetType().Namespace=="Unity.Robotics.UrdfImporter.Control")
            {Undo.RecordObject(b,"Disable old IK writer");b.enabled=false;EditorUtility.SetDirty(b);}
        foreach(var body in robot.GetComponentsInChildren<ArticulationBody>(true))
        {
            var so=new SerializedObject(body);var enabled=so.FindProperty("m_Enabled");
            if(enabled==null)throw new Exception("ArticulationBody has no enabled field");
            enabled.boolValue=false;so.ApplyModifiedProperties();
        }
        foreach(var b in controller.GetComponents<Behaviour>())
            if(b.GetType().FullName=="UnityEngine.SpatialTracking.TrackedPoseDriver")b.enabled=false;
        foreach(var t in transforms)foreach(var b in t.GetComponents<MonoBehaviour>())
            if(b&&(b.GetType().Name=="VRControllerPublisher"||b.GetType().Name=="UnityToRosAngles"))b.enabled=false;
        foreach(var c in target.GetComponents<Behaviour>())if(c is IConstraint)c.enabled=false;
        Undo.SetTransformParent(target,robotBase,"Detach old offset target");
        target.name="Piper_IK_Target";
        var solver=publisher.GetComponent<PiperConstrainedIK>()??Undo.AddComponent<PiperConstrainedIK>(publisher.gameObject);
        solver.urdf=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/PiperKinematics.xml");
        solver.robotBase=robotBase;solver.target=target;
        var parent=robotBase;
        for(int i=0;i<6;i++){parent=parent.Find("link"+(i+1));if(!parent)throw new Exception("Missing link");solver.joints[i]=parent;}
        solver.angles=new float[6];solver.jointSpeedLimit=1f;
        var mapper=target.GetComponent<VRMotionMapper>()??Undo.AddComponent<VRMotionMapper>(target.gameObject);
        mapper.vrController=controller;mapper.trackingOrigin=controller.parent;mapper.robotBase=robotBase;
        mapper.ikTarget=target;mapper.solver=solver;mapper.motionScale=1f;
        solver.Initialize();if(!solver.Ready)throw new Exception("URDF initialization failed");
        solver.Model.Forward(solver.angles,out var tip,out var orientation);
        target.localPosition=tip;target.localRotation=orientation;
        publisher.joint1=solver.joints[0];publisher.joint2=solver.joints[1];publisher.joint3=solver.joints[2];
        publisher.joint4=solver.joints[3];publisher.joint5=solver.joints[4];publisher.joint6=solver.joints[5];
        publisher.solver=solver;publisher.publishCommands=false;
        EditorUtility.SetDirty(solver);EditorUtility.SetDirty(mapper);EditorUtility.SetDirty(publisher);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Debug.Log("[SURF] URDF kinematics installed; hardware publishing OFF pending physical calibration.");
        SurfKinematicsAudit.Run();
        SurfKinematicsTests.Run();
    }
}
