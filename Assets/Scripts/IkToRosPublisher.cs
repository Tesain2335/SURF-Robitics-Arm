using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

[DefaultExecutionOrder(200)]
public class IkToRosPublisher : MonoBehaviour
{
    public string topicName="unity_joint_angles";
    public Transform joint1,joint2,joint3,joint4,joint5,joint6;
    public PiperConstrainedIK solver;
    [Tooltip("Send only after fresh real feedback has aligned the model and VR tracking is valid.")]
    public bool publishCommands=false;
    public string feedbackTopic="/feedback/joint_states";
    public float feedbackTimeout=0.5f;
    public string controlStatus="Waiting for real joint feedback";
    public float publishHz=60f;
    public float maxOffAxisDegrees=1f;
    ROSConnection ros;
    float nextPublish;
    float feedbackTime=float.NegativeInfinity;
    float[] feedback;
    bool aligned;
    public bool FeedbackAligned => aligned&&feedback!=null&&Time.realtimeSinceStartup-feedbackTime<feedbackTimeout;
    public void RequestRealign()
    {
        aligned=false;
        if(ros!=null&&publishCommands)ros.Publish(topicName,new JointStateMsg());
    }
    System.IO.StreamWriter latencyLog;
    float nextLog, nextFlush;
    float[] measuredAngles;
    bool logFailed;
    void Start()
    {
        ros=ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<JointStateMsg>(topicName,queue_size:1,latch:false);
        ros.Subscribe<JointStateMsg>(feedbackTopic,ReceiveFeedback);
    }
    public static bool DecodeFeedback(JointStateMsg msg,PiperKinematics model,out float[] q)
    {
        q=new float[6];
        if(model==null||msg.name==null||msg.position==null||msg.name.Length!=msg.position.Length)return false;
        for(int i=0;i<6;i++)
        {
            string name="joint"+(i+1);int index=System.Array.IndexOf(msg.name,name);
            if(index<0||System.Array.LastIndexOf(msg.name,name)!=index)return false;
            double value=msg.position[index];
            // Measured zero offsets can lie just outside URDF bounds. Accept up to
            // 0.05 rad, then seed the solver at the nearest valid limit, never expand limits.
            if(double.IsNaN(value)||double.IsInfinity(value)||value<model.lower[i]-0.05||value>model.upper[i]+0.05)return false;
            q[i]=Mathf.Clamp((float)value,model.lower[i],model.upper[i]);
        }
        return true;
    }
    void ReceiveFeedback(JointStateMsg msg)
    {
        if(!solver||!solver.Ready||!DecodeFeedback(msg,solver.Model,out var q))
        {feedback=null;aligned=false;return;}
        feedback=q;feedbackTime=Time.realtimeSinceStartup;
        measuredAngles=new float[6];
        for(int i=0;i<6;i++)measuredAngles[i]=(float)msg.position[System.Array.IndexOf(msg.name,"joint"+(i+1))];
    }
    void OnDisable(){aligned=false;feedback=null;}
    void OnDestroy(){latencyLog?.Dispose();}
    void RecordLatency()
    {
        if(logFailed)return;
        try{WriteLatencySample();}
        catch(System.Exception error)
        {logFailed=true;Debug.LogWarning("[SURF] Latency logging disabled: "+error.Message);}
    }
    void WriteLatencySample()
    {
        float now=Time.realtimeSinceStartup;
        if(now<nextLog||!solver||!solver.Ready||!solver.target||measuredAngles==null)return;
        nextLog=now+0.02f;
        if(latencyLog==null)
        {
            string folder=System.IO.Path.Combine(Application.persistentDataPath,"SURF-latency");
            System.IO.Directory.CreateDirectory(folder);
            string file=System.IO.Path.Combine(folder,"teleop-"+System.DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".csv");
            latencyLog=new System.IO.StreamWriter(file);
            latencyLog.WriteLine("unity_time_s,target_x,target_y,target_z,feedback_x,feedback_y,feedback_z,feedback_age_s,command_valid");
            Debug.Log("[SURF] Latency samples: "+file);
        }
        var target=solver.robotBase.InverseTransformPoint(solver.target.position);
        solver.Model.Forward(measuredAngles,out var actual,out _);
        latencyLog.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:F6},{1:F6},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6},{8}",
            now,target.x,target.y,target.z,actual.x,actual.y,actual.z,now-feedbackTime,
            publishCommands&&aligned&&solver.CommandValid&&feedback!=null&&now-feedbackTime<feedbackTimeout?1:0));
        if(now>=nextFlush){latencyLog.Flush();nextFlush=now+1f;}
    }
    void LateUpdate()
    {
        RecordLatency();
        if(!publishCommands){aligned=false;controlStatus="Arm publishing disabled";return;}
        if(!solver||!solver.Ready){controlStatus="IK not initialized";return;}
        if(feedback==null||Time.realtimeSinceStartup-feedbackTime>=feedbackTimeout)
        {aligned=false;controlStatus="Waiting for fresh real joint feedback";return;}
        if(!aligned)
        {
            solver.Apply(feedback);
            if(solver.target)
            {
                solver.Model.Forward(solver.angles,out var tip,out _);
                solver.target.position=solver.robotBase.TransformPoint(tip);
                var mapper=solver.target.GetComponent<VRMotionMapper>();
                if(mapper)mapper.Rebase();
            }
            aligned=true;controlStatus="Real pose aligned; waiting for tracked hand";
            return;
        }
        if(!solver.CommandValid){controlStatus="Waiting for tracked hand / reachable IK target";return;}
        if(Time.unscaledTime<nextPublish)return;
        var joints=new[]{joint1,joint2,joint3,joint4,joint5,joint6};
        var values=new float[6];
        for(int i=0;i<6;i++)
        {
            if(!joints[i])return;
            values[i]=solver.Model.ReadAngle(joints[i].localRotation,i,out float residual);
            if(residual>maxOffAxisDegrees)return;
        }
        if(!solver.Model.Valid(values))return;
        if(ros==null){ros=ROSConnection.GetOrCreateInstance();ros.RegisterPublisher<JointStateMsg>(topicName,queue_size:1,latch:false);}
        var msg=new JointStateMsg();
        msg.name=new[]{"joint1","joint2","joint3","joint4","joint5","joint6"};
        msg.position=System.Array.ConvertAll(values,x=>(double)x);
        ros.Publish(topicName,msg);
        controlStatus="Publishing six joints";
        // Preserve cadence across render frames; never catch up by sending a burst.
        nextPublish=Mathf.Max(nextPublish+1f/Mathf.Max(1f,publishHz),Time.unscaledTime);
    }
}
