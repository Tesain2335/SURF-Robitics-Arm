using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class SurfKinematicsTests
{
    [InitializeOnLoadMethod]
    static void Schedule(){EditorApplication.delayCall+=()=>{if(File.Exists("Library/surf-test-kinematics.request")&&!EditorApplication.isPlayingOrWillChangePlaymode){Run();File.Delete("Library/surf-test-kinematics.request");}};}
    [Serializable] public class Case { public float[] q;public Vector3 tip,basisX,basisY,basisZ; }
    [Serializable] public class Cases { public Case[] cases; }
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    [MenuItem("SURF/Test kinematics offline")]
    public static void Run()
    {
        var log=new StringBuilder();
        try
        {
            var model=new PiperKinematics(File.ReadAllText("Assets/PiperRobot/piper_description.urdf"));
            var cases=JsonUtility.FromJson<Cases>(File.ReadAllText("Library/surf-fk-cases.json"));
            float maxFk=0,maxAngle=0;
            foreach(var c in cases.cases)
            {
                model.Forward(c.q,out var tip,out var rotation);
                Check(Vector3.Distance(rotation*PiperKinematics.RosToUnity(Vector3.right),c.basisX)<1e-5f,"ROS orientation X mismatch");
                Check(Vector3.Distance(rotation*PiperKinematics.RosToUnity(Vector3.up),c.basisY)<1e-5f,"ROS orientation Y mismatch");
                Check(Vector3.Distance(rotation*PiperKinematics.RosToUnity(Vector3.forward),c.basisZ)<1e-5f,"ROS orientation Z mismatch");
                maxFk=Mathf.Max(maxFk,Vector3.Distance(tip,c.tip));
                Check(Vector3.Distance(tip,c.tip)<0.00001f,"Independent ROS FK mismatch");
                for(int i=0;i<6;i++)
                {
                    var local=model.zero[i]*Quaternion.AngleAxis(c.q[i]*Mathf.Rad2Deg,model.axis[i]);
                    float actual=model.ReadAngle(local,i,out var residual);
                    float error=Mathf.Abs(actual-c.q[i]);maxAngle=Mathf.Max(maxAngle,error);
                    Check(error<0.00001f,"Angle mismatch joint "+(i+1)+": "+actual+" vs "+c.q[i]);
                    Check(residual<0.1f,"Unexpected off-axis rotation");
                }
            }
            log.AppendLine("PASS: "+cases.cases.Length+" independent ROS FK position AND orientation cases, max position error m="+maxFk+", max angle error rad="+maxAngle);
            for(int i=0;i<6;i++)
            {
                var off=model.zero[i]*Quaternion.AngleAxis(10,Vector3.right);
                model.ReadAngle(off,i,out float residual);Check(residual>9,"Off-axis rejection");
            }
            log.AppendLine("PASS: wrong-axis rotation detected on all six joints");
            var seed=new float[6];float worst=0;
            for(int n=1;n<=60;n++)
            {
                float t=n/60f;
                var known=new[]{0.3f*t,0.8f*t,-1.0f*t,0.2f*t,0.3f*t,0f};
                model.Forward(known,out var target,out _);
                bool solved=model.Solve(target,seed,out var solution,out var error);
                Check(solved,"Reachable trajectory failed at "+n+", error="+error);
                Check(model.Valid(solution),"Limits violated");seed=solution;worst=Mathf.Max(worst,error);
            }
            log.AppendLine("PASS: 60 reachable trajectory samples; worst IK error m="+worst+"; all within joint limits");
            Check(!model.Solve(new Vector3(3,3,3),seed,out _,out _),"Unreachable target accepted");
            log.AppendLine("PASS: unreachable target rejected");
            Check(!model.Valid(new[]{0f,-0.5f,0f,0f,0f,0f}),"Invalid J2 accepted");
            Check(!model.Valid(new[]{0f,0f,float.NaN,0f,0f,0f}),"NaN accepted");
            var home=new Vector3(0,.2f,.06f);var hand=new Vector3(1,1,1);
            Check(Vector3.Distance(VRMotionMapper.MapDelta(hand,hand,home,1),home)<1e-6f,"First frame jump");
            foreach(var axis in new[]{Vector3.right,Vector3.up,Vector3.forward})
                Check(Vector3.Distance(VRMotionMapper.MapDelta(hand+axis*.05f,hand,home,1),home+axis*.05f)<1e-6f,"Hand mapping mismatch");
            log.AppendLine("PASS: limits/NaN guard and first-frame/XYZ relative mapping");
            var solver=UnityEngine.Object.FindFirstObjectByType<PiperConstrainedIK>();
            if(solver&&!solver.Ready)solver.Initialize();
            Check(solver&&solver.Ready,"Scene solver not initialized");
            var saved=(float[])solver.angles.Clone();
            try
            {
                solver.Apply(seed);model.Forward(seed,out var expected,out _);
                Check(Vector3.Distance(solver.robotBase.InverseTransformPoint(solver.joints[5].position),expected)<0.00001f,"Actual hierarchy FK mismatch");
                var publisher=solver.GetComponent<IkToRosPublisher>();
                Check(publisher&&publisher.solver==solver&&!publisher.publishCommands,"Hardware publisher should be off during validation");
                if(solver.target)
                {
                    Check(solver.target.parent==solver.robotBase,"Target still follows controller parent");
                    foreach(var body in solver.robotBase.GetComponentsInChildren<ArticulationBody>())
                        Check(!new SerializedObject(body).FindProperty("m_Enabled").boolValue,"Physics writer still active");
                    foreach(var b in solver.robotBase.root.GetComponentsInChildren<Behaviour>())
                        if(b is Animator||b.GetType().Namespace=="UnityEngine.Animations.Rigging")Check(!b.enabled,"Animation writer still active");
                    foreach(var b in solver.target.GetComponents<Behaviour>())if(b is UnityEngine.Animations.IConstraint)Check(!b.enabled,"Old target constraint still active");
                }
            }
            finally {solver.Apply(saved);}
            log.AppendLine("PASS: actual scene transforms match FK; ROS publishing disabled");
            log.AppendLine("ALL PASS");
        }
        catch(Exception e){log.AppendLine("FAIL: "+e);Debug.LogException(e);}
        File.WriteAllText("Library/surf-kinematics-tests.txt",log.ToString());
        Debug.Log("[SURF] "+log);
    }
}
