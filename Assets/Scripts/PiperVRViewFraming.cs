using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

// Repositions only XR Origin. Tracked camera/hand local poses and robot coordinates remain unchanged.
[DefaultExecutionOrder(20)]
public sealed class PiperVRViewFraming : MonoBehaviour
{
    public Transform xrOrigin,head,robot;
    public float minimumDistance=1.2f;
    public bool autoFrameOnFirstTracking=true;
    public bool framed;
    float trackingSince=-1;
    bool buttonKnown,buttonDown;
    public static Vector3 FrameTranslation(Vector3 eye,Vector3 forward,Bounds bounds,float minDistance)
    {
        float distance=Mathf.Max(minDistance,bounds.extents.magnitude/Mathf.Sin(25*Mathf.Deg2Rad));
        var horizontal=Vector3.ProjectOnPlane(forward,Vector3.up);
        if(horizontal.sqrMagnitude<.0001f)horizontal=Vector3.forward;
        // Use a level viewpoint slightly above the arm, even if the user is looking down.
        // Never rotate the tracked camera or place the viewer beneath the robot's floor.
        return bounds.center-horizontal.normalized*distance+Vector3.up*.15f-eye;
    }
    public bool FrameRobot()
    {
        if(!xrOrigin||!head||!robot||!head.IsChildOf(xrOrigin)||robot.IsChildOf(xrOrigin))return false;
        bool found=false;var bounds=new Bounds(robot.position+Vector3.up*.3f,Vector3.one*.6f);
        foreach(var renderer in robot.GetComponentsInChildren<Renderer>())
        {
            if(!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;
            if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);
        }
        xrOrigin.position+=FrameTranslation(head.position,head.forward,bounds,minimumDistance);
        framed=true;
        return true;
    }
    void LateUpdate()
    {
        var hmd=InputDevices.GetDeviceAtXRNode(XRNode.Head);
        bool tracked=hmd.isValid&&hmd.TryGetFeatureValue(CommonUsages.isTracked,out bool t)&&t
            &&hmd.TryGetFeatureValue(CommonUsages.devicePosition,out _)
            &&hmd.TryGetFeatureValue(CommonUsages.deviceRotation,out _);
        if(hmd.TryGetFeatureValue(CommonUsages.userPresence,out bool worn)&&!worn)tracked=false;
        if(!tracked){trackingSince=-1;buttonKnown=false;return;}
        if(trackingSince<0)trackingSince=Time.unscaledTime;
        if(autoFrameOnFirstTracking&&!framed&&Time.unscaledTime-trackingSince>=.5f)FrameRobot();
        var right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if(right.isValid&&right.TryGetFeatureValue(CommonUsages.secondaryButton,out bool b))
        {
            if(buttonKnown&&b&&!buttonDown)FrameRobot();
            buttonKnown=true;buttonDown=b;
        }
        else buttonKnown=false;
        if(Application.isFocused&&Keyboard.current!=null&&Keyboard.current.f8Key.wasPressedThisFrame)FrameRobot();
    }
}
