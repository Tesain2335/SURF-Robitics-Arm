using UnityEngine;
using UnityEngine.InputSystem;

// Key pairs and 45 deg/s jog speed follow Unity-display-for-SURF grasp-physics,
// commit 9c9f73f. This routes through our URDF/ROS stack, not articulation drives.
[DefaultExecutionOrder(40)]
public sealed class PiperControlMode : MonoBehaviour
{
    public enum InputMode { VR, Keyboard }
    [SerializeField] InputMode mode=InputMode.VR;
    public PiperConstrainedIK solver;
    public IkToRosPublisher publisher;
    public float degreesPerSecond=45f;
    public float wristDegreesPerSecond=30f;
    public float stickDeadzone=0.2f;
    public float WristAxis { get; private set; }
    public float RollAxis { get; private set; }
    // Position IK uses the shoulder/elbow; wrist joints retain their commanded angles.
    public int VrLockedJointMask => KeyboardMode?0:56;
    bool controllerSeen,primaryWasDown,stickCentered;
    float nextControllerToggle;
    public InputMode Mode => mode;
    public bool KeyboardMode => mode==InputMode.Keyboard;
    public bool KeyboardValid => KeyboardMode&&Application.isFocused&&Keyboard.current!=null&&!waitForRelease;
    bool waitForRelease,gripValid,waitVrGrip;
    bool invalidateGrip;
    float gripRatio,vrGripBaseline;
    bool haveVrBaseline;
    static readonly Key[] Positive={Key.A,Key.W,Key.F,Key.T,Key.Y,Key.E};
    static readonly Key[] Negative={Key.D,Key.S,Key.R,Key.G,Key.H,Key.Q};
    public void SetMode(InputMode next)
    {
        if(next==mode)return;
        mode=next;waitForRelease=true;gripValid=false;invalidateGrip=true;
        waitVrGrip=true;haveVrBaseline=false;
        stickCentered=false;WristAxis=0;RollAxis=0;
        publisher?.RequestRealign();
        if(solver&&solver.target)solver.target.GetComponent<VRMotionMapper>()?.Rebase();
    }
    void OnApplicationFocus(bool focused)
    {
        if(!focused&&KeyboardMode){waitForRelease=true;gripValid=false;invalidateGrip=true;waitVrGrip=true;haveVrBaseline=false;publisher?.RequestRealign();}
    }
    public static float[] Jog(PiperKinematics model,float[] current,float[] direction,float dt,float speed)
    {
        var q=(float[])current.Clone();
        for(int i=0;i<6;i++)q[i]=Mathf.Clamp(q[i]+Mathf.Clamp(direction[i],-1,1)*speed*Mathf.Deg2Rad*Mathf.Clamp(dt,0,.05f),model.lower[i],model.upper[i]);
        return q;
    }
    void Update()
    {
        var device=UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
        bool tracked=device.isValid&&device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool t)&&t;
        bool buttonValid=device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton,out bool primary);
        bool stickValid=device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis,out Vector2 stick);
        SampleController(tracked&&buttonValid,primary,stickValid?stick:Vector2.zero,Time.unscaledTime);
        var keys=Keyboard.current;
        if(!Application.isFocused||keys==null)return;
        if(keys.tabKey.wasPressedThisFrame){SetMode(KeyboardMode?InputMode.VR:InputMode.Keyboard);return;}
        bool pressed=keys.oKey.isPressed||keys.pKey.isPressed;
        var direction=new float[6];
        for(int i=0;i<6;i++)
        {
            direction[i]=(keys[Positive[i]].isPressed?1:0)-(keys[Negative[i]].isPressed?1:0);
            pressed|=keys[Positive[i]].isPressed||keys[Negative[i]].isPressed;
        }
        if(waitForRelease){if(!pressed)waitForRelease=false;return;}
        if(!KeyboardMode)return;
        if(keys.oKey.isPressed!=keys.pKey.isPressed)
        {gripRatio=keys.pKey.isPressed?1:0;gripValid=true;}
        if(!solver||!solver.Ready||!publisher||!publisher.FeedbackAligned)return;
        solver.Apply(Jog(solver.Model,solver.angles,direction,Time.unscaledDeltaTime,degreesPerSecond));
        solver.Model.Forward(solver.angles,out var tip,out _);
        if(solver.target)solver.target.position=solver.robotBase.TransformPoint(tip);
    }
    public void SampleController(bool tracked,bool primary,Vector2 stick,float now)
    {
        WristAxis=0;RollAxis=0;
        if(!tracked){controllerSeen=false;stickCentered=false;return;}
        if(!controllerSeen){controllerSeen=true;primaryWasDown=primary;}
        else if(primary&&!primaryWasDown&&now>=nextControllerToggle)
        {SetMode(KeyboardMode?InputMode.VR:InputMode.Keyboard);nextControllerToggle=now+0.35f;}
        primaryWasDown=primary;
        if(stick.magnitude<=stickDeadzone)stickCentered=true;
        if(!KeyboardMode&&stickCentered)
        {
            WristAxis=FilterStick(stick.y,stickDeadzone);
            RollAxis=FilterStick(stick.x,stickDeadzone);
        }
    }
    public static float FilterStick(float axis,float deadzone)
    {
        if(float.IsNaN(axis)||float.IsInfinity(axis)||Mathf.Abs(axis)<=deadzone)return 0;
        return Mathf.Sign(axis)*Mathf.Clamp01((Mathf.Abs(axis)-deadzone)/(1-deadzone));
    }
    public static float[] WristJog(PiperKinematics model,float[] current,float axis,float dt,float speed,out Vector3 tipDelta)
        => WristJog(model,current,axis,0,dt,speed,out tipDelta);
    public static float[] WristJog(PiperKinematics model,float[] current,float pitch,float roll,float dt,float speed,out Vector3 tipDelta)
    {
        var directions=new float[6];directions[4]=pitch;directions[5]=roll;
        var next=Jog(model,current,directions,dt,speed);
        model.Forward(current,out var before,out _);model.Forward(next,out var after,out _);
        tipDelta=after-before;return next;
    }
    public bool TryGripper(float vrRatio,out float ratio)
        => RouteGripper(vrRatio,Application.isFocused,out ratio);
    public bool RouteGripper(float vrRatio,bool focused,out float ratio)
    {
        ratio=0;
        if(invalidateGrip){invalidateGrip=false;ratio=float.NaN;return true;}
        if(KeyboardMode&&!focused)return false;
        if(KeyboardMode){ratio=gripRatio;return gripValid&&!waitForRelease;}
        if(waitVrGrip)
        {
            if(!haveVrBaseline){vrGripBaseline=vrRatio;haveVrBaseline=true;return false;}
            if(Mathf.Abs(vrRatio-vrGripBaseline)<.2f)return false;
            waitVrGrip=false;
        }
        ratio=vrRatio;return true;
    }
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12,12,490,180),GUI.skin.box);
        GUILayout.Label(KeyboardMode?"SURF | Keyboard control":"SURF | VR controller");
        if(GUILayout.Button("Tab / Right A - Switch keyboard / VR"))SetMode(KeyboardMode?InputMode.VR:InputMode.Keyboard);
        GUILayout.Label("J1 A/D | J2 W/S | J3 R/F | J4 T/G | J5 Y/H | J6 Q/E");
        GUILayout.Label("O: open  /  P: close | Release keys after switching");
        GUILayout.Label("VR stick: up/down = J5 | left/right = J6 gripper roll");
        if(publisher)GUILayout.Label(publisher.controlStatus);
        GUILayout.EndArea();
    }
}
