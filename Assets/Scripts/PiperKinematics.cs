using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

// URDF joint coordinates in radians; Unity coordinates only inside FK/IK.
public sealed class PiperKinematics
{
    public readonly Vector3[] origin = new Vector3[6], axis = new Vector3[6];
    public readonly Quaternion[] zero = new Quaternion[6];
    public readonly float[] lower = new float[6], upper = new float[6];
    static float Number(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    static Vector3 Triple(string s) { var a = s.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray(); return new Vector3(a[0],a[1],a[2]); }
    public static Vector3 RosToUnity(Vector3 v) => new Vector3(-v.y, v.z, v.x);
    public PiperKinematics(string xml)
    {
        var document = XDocument.Parse(xml);
        for (int i=0;i<6;i++)
        {
            var j=document.Root.Elements("joint").Single(x=>(string)x.Attribute("name")=="joint"+(i+1));
            var o=j.Element("origin"); var r=Triple((string)o.Attribute("rpy"));
            origin[i]=RosToUnity(Triple((string)o.Attribute("xyz")));
            zero[i]=Quaternion.AngleAxis(-r.z*Mathf.Rad2Deg,Vector3.up)*Quaternion.AngleAxis(r.y*Mathf.Rad2Deg,Vector3.right)*Quaternion.AngleAxis(-r.x*Mathf.Rad2Deg,Vector3.forward);
            axis[i]=-RosToUnity(Triple((string)j.Element("axis").Attribute("xyz"))).normalized;
            lower[i]=Number((string)j.Element("limit").Attribute("lower"));
            upper[i]=Number((string)j.Element("limit").Attribute("upper"));
        }
    }
    public bool Valid(float[] q) => q!=null && q.Length==6 && Enumerable.Range(0,6).All(i=>!float.IsNaN(q[i])&&!float.IsInfinity(q[i])&&q[i]>=lower[i]-1e-5f&&q[i]<=upper[i]+1e-5f);
    public void Forward(float[] q, out Vector3 tip, out Quaternion rotation, Vector3[] pivots=null, Vector3[] axes=null)
    {
        tip=Vector3.zero; rotation=Quaternion.identity;
        for(int i=0;i<6;i++)
        {
            tip+=rotation*origin[i]; rotation*=zero[i];
            if(pivots!=null)pivots[i]=tip;
            if(axes!=null)axes[i]=rotation*axis[i];
            rotation*=Quaternion.AngleAxis(q[i]*Mathf.Rad2Deg,axis[i]);
        }
    }
    public float ReadAngle(Quaternion local,int i,out float offAxisDegrees)
    {
        var d=Quaternion.Inverse(zero[i])*local;
        float projection=Vector3.Dot(new Vector3(d.x,d.y,d.z),axis[i]);
        float angle=Mathf.DeltaAngle(0,2*Mathf.Atan2(projection,d.w)*Mathf.Rad2Deg);
        if(angle<lower[i]*Mathf.Rad2Deg-0.01f && angle+360<=upper[i]*Mathf.Rad2Deg+0.01f)angle+=360;
        offAxisDegrees=Quaternion.Angle(d,Quaternion.AngleAxis(angle,axis[i]));
        return angle*Mathf.Deg2Rad;
    }
    public bool Solve(Vector3 target,float[] seed,out float[] result,out float error,int lockedJointMask=0)
    {
        result=(float[])seed.Clone(); var pivots=new Vector3[6];var axes=new Vector3[6];
        // Position task; wrist orientation is not constrained in this mode.
        for(int iteration=0;iteration<100;iteration++)
        {
            Forward(result,out var tip,out var rotation,pivots,axes);
            var e=target-tip; if(e.magnitude<0.001f)break;
            var jac=new Vector3[6];var a=Matrix4x4.zero;
            for(int i=0;i<6;i++)
            {
                jac[i]=(lockedJointMask&(1<<i))!=0?Vector3.zero:Vector3.Cross(axes[i],tip-pivots[i]);
                for(int row=0;row<3;row++)for(int col=0;col<3;col++)a[row,col]+=jac[i][row]*jac[i][col];
            }
            for(int i=0;i<3;i++)a[i,i]+=0.0001f;
            a[3,3]=1;var step=a.inverse.MultiplyVector(e);
            for(int i=0;i<6;i++)result[i]=Mathf.Clamp(result[i]+Mathf.Clamp(Vector3.Dot(jac[i],step),-0.12f,0.12f),lower[i],upper[i]);
        }
        Forward(result,out var finalTip,out var finalRotation);
        error=Vector3.Distance(target,finalTip);
        return Valid(result)&&error<=0.003f;
    }
}
