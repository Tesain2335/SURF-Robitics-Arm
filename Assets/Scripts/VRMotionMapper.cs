using UnityEngine;
using UnityEngine.XR;

[DefaultExecutionOrder(50)]
public class VRMotionMapper : MonoBehaviour
{
    public Transform vrController, ikTarget, trackingOrigin, robotBase;
    public PiperConstrainedIK solver;
    public float motionScale=1f;
    public bool TrackingValid { get; private set; }
    bool calibrated;
    Vector3 handOrigin, targetOrigin;
    public static Vector3 MapDelta(Vector3 hand,Vector3 reference,Vector3 tip,float scale) => tip+(hand-reference)*scale;
    void OnDisable(){TrackingValid=false;calibrated=false;}
    public void Rebase(){TrackingValid=false;calibrated=false;}
    void LateUpdate()
    {
        if(solver&&solver.controls&&solver.controls.KeyboardMode){TrackingValid=false;calibrated=false;return;}
        var device=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        TrackingValid=device.isValid&&device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked)&&tracked&&device.TryGetFeatureValue(CommonUsages.devicePosition,out _);
        if(!TrackingValid||!solver||!solver.Ready||!ikTarget||!robotBase||!trackingOrigin)
        { TrackingValid=false;calibrated=false;return; }
        device.TryGetFeatureValue(CommonUsages.devicePosition,out Vector3 hand);
        if(!calibrated)
        {
            handOrigin=hand;
            solver.Model.Forward(solver.angles,out targetOrigin,out _);
            calibrated=true;
        }
        // Tracking-local right/up/forward maps to robot-base-local right/up/forward.
        var controls=solver.controls;
        if(controls&&controls.publisher&&controls.publisher.FeedbackAligned&&(Mathf.Abs(controls.WristAxis)>0||Mathf.Abs(controls.RollAxis)>0))
        {
            var q=PiperControlMode.WristJog(solver.Model,solver.angles,controls.WristAxis,controls.RollAxis,Time.unscaledDeltaTime,controls.wristDegreesPerSecond,out var shift);
            solver.Apply(q);
            // Let the tip follow the wrist's natural arc, rather than making position IK undo it.
            targetOrigin+=shift;
        }
        ikTarget.position=robotBase.TransformPoint(MapDelta(hand,handOrigin,targetOrigin,motionScale));
    }
}
