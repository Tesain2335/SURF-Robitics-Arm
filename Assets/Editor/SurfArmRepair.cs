using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SurfArmRepair
{
    [Serializable] public class Request { public string id; public long expiresUtcTicks; }
    [Serializable] public class Result { public string id; public bool ok; public string message; }
    const string RequestFile="Library/surf-arm-repair.request.json";
    const string ResultFile="Library/surf-arm-repair.result.json";
    static double nextPoll;
    static SurfArmRepair(){EditorApplication.update+=Poll;}
    public static bool IsFresh(Request request,long now) => request!=null &&
        Guid.TryParseExact(request.id,"N",out _) && request.expiresUtcTicks>now &&
        request.expiresUtcTicks-now<=TimeSpan.FromMinutes(3).Ticks;
    static void Poll()
    {
        if(EditorApplication.timeSinceStartup<nextPoll)return;
        nextPoll=EditorApplication.timeSinceStartup+0.25;
        if(!File.Exists(RequestFile)||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        Request request=null;
        try
        {
            request=JsonUtility.FromJson<Request>(File.ReadAllText(RequestFile));
            if(!IsFresh(request,DateTime.UtcNow.Ticks))throw new Exception("请求已过期或无效，请重新点击修复。");
            // Persist request across domain reload, then repair only the edit-mode scene.
            if(EditorApplication.isPlayingOrWillChangePlaymode)
            {EditorApplication.isPlaying=false;return;}
            var scene=SceneManager.GetActiveScene();
            if(scene.path!="Assets/OutdoorsScene.unity")throw new Exception("请先打开 PIPER 的 OutdoorsScene 场景。");
            string report=RepairConfiguration(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new Exception("场景保存失败，尚未完成修复。");
            Finish(request,true,report);
        }
        catch(Exception error){Finish(request,false,error.Message);}
    }
    static void Finish(Request request,bool ok,string message)
    {
        var result=new Result{id=request?.id??"",ok=ok,message=message};
        File.WriteAllText(ResultFile+".tmp",JsonUtility.ToJson(result));
        if(File.Exists(ResultFile))File.Delete(ResultFile);
        File.Move(ResultFile+".tmp",ResultFile);
        File.Delete(RequestFile);
        Debug.Log("[SURF] 六轴修复："+message);
    }
    public static string RepairConfiguration(Scene scene)
    {
        var roots=scene.GetRootGameObjects();
        var publishers=roots.SelectMany(x=>x.GetComponentsInChildren<IkToRosPublisher>(true)).ToArray();
        var solvers=roots.SelectMany(x=>x.GetComponentsInChildren<PiperConstrainedIK>(true)).ToArray();
        if(publishers.Length!=1||solvers.Length!=1)throw new Exception("预期唯一六轴发布器和 IK 求解器；当前数量不符，未自动选择或删除组件。");
        var p=publishers[0];var s=solvers[0];
        if(!p.gameObject.activeInHierarchy||!s.gameObject.activeInHierarchy)throw new Exception("机械臂或 IK 对象未激活，请检查场景层级。");
        if(!s.urdf||!s.robotBase||!s.target||s.joints==null||s.joints.Length!=6)throw new Exception("IK 的 URDF、基座、目标或六轴引用缺失。");
        for(int i=0;i<6;i++)
            if(!s.joints[i]||s.joints[i].parent!=(i==0?s.robotBase:s.joints[i-1]))throw new Exception("关节层级不符合 URDF：joint"+(i+1));
        var model=new PiperKinematics(s.urdf.text);
        if(!model.Valid(s.angles))throw new Exception("保存的 IK 初始角度无效；未擅自清零。");
        var mapper=s.target.GetComponent<VRMotionMapper>();
        if(!mapper||!mapper.gameObject.activeInHierarchy||!mapper.trackingOrigin)throw new Exception("手柄映射或追踪空间引用缺失/未激活。");
        // Validate every structural prerequisite before making reversible changes.
        Undo.RecordObjects(new UnityEngine.Object[]{p,s,mapper},"SURF repair six-axis publishing");
        p.solver=s;p.enabled=true;p.publishCommands=true;
        p.topicName="unity_joint_angles";p.feedbackTopic="/feedback/joint_states";
        p.feedbackTimeout=0.5f;p.publishHz=60;p.maxOffAxisDegrees=1;
        p.joint1=s.joints[0];p.joint2=s.joints[1];p.joint3=s.joints[2];
        p.joint4=s.joints[3];p.joint5=s.joints[4];p.joint6=s.joints[5];
        s.enabled=true;s.followTarget=true;s.jointSpeedLimit=3;
        var controls=s.GetComponent<PiperControlMode>();
        if(controls)
        {
            Undo.RecordObject(controls,"Restore control mode routing");
            controls.enabled=true;controls.solver=s;controls.publisher=p;s.controls=controls;
            EditorUtility.SetDirty(controls);
        }
        mapper.enabled=true;mapper.solver=s;mapper.robotBase=s.robotBase;mapper.ikTarget=s.target;
        mapper.Rebase();
        foreach(var item in new UnityEngine.Object[]{p,s,mapper})EditorUtility.SetDirty(item);
        return "Unity 已退出 Play，六轴发布/话题/关节引用/IK 跟随已恢复，保留真实反馈对齐和限位保护。";
    }
}
