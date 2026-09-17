using UnityEngine;

[DefaultExecutionOrder(100)]
public class PiperConstrainedIK : MonoBehaviour
{
    public TextAsset urdf;
    public Transform robotBase, target;
    public Transform[] joints=new Transform[6];
    public bool followTarget=true;
    public float jointSpeedLimit=3.0f;
    public float[] angles=new float[6];
    public float targetError;
    public bool targetReachable;
    public PiperControlMode controls;
    public PiperKinematics Model { get; private set; }
    public bool Ready { get; private set; }
    public bool CommandValid => Ready && (controls&&controls.KeyboardMode ? controls.KeyboardValid : targetReachable && (!mapper || mapper.TrackingValid));
    VRMotionMapper mapper;
    public void Initialize()
    {
        Ready=false;
        if(!urdf||!robotBase||joints.Length!=6)return;
        Model=new PiperKinematics(urdf.text);
        for(int i=0;i<6;i++)if(!joints[i]||joints[i].parent!=(i==0?robotBase:joints[i-1]))return;
        if(!Model.Valid(angles))return;
        mapper=target?target.GetComponent<VRMotionMapper>():null;
        Apply(angles);Ready=true;
    }
    void Awake(){Initialize();}
    public void Apply(float[] q)
    {
        if(Model==null||!Model.Valid(q))return;
        for(int i=0;i<6;i++)
        {
            angles[i]=q[i]; joints[i].localPosition=Model.origin[i];
            joints[i].localRotation=Model.zero[i]*Quaternion.AngleAxis(q[i]*Mathf.Rad2Deg,Model.axis[i]);
        }
    }
    void LateUpdate()
    {
        if(controls&&controls.KeyboardMode)return;
        if(!Ready||!followTarget||!target||(mapper&&!mapper.TrackingValid)){targetReachable=false;return;}
        targetReachable=Model.Solve(robotBase.InverseTransformPoint(target.position),angles,out var desired,out targetError,controls?controls.VrLockedJointMask:0);
        if(!targetReachable)return;
        // A stalled render frame must not produce a large target jump on recovery.
        float step=jointSpeedLimit*Mathf.Min(Time.deltaTime,0.05f);
        for(int i=0;i<6;i++)desired[i]=Mathf.MoveTowards(angles[i],desired[i],step);
        Apply(desired);
    }
}
