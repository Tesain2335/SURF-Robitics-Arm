using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

/// <summary>
/// 读取 VIVE Focus 3 右手柄 Grip 值，并发布 0~1 的抓握比例到 ROS2。
///
/// ratio = 0：手柄未按，夹爪打开
/// ratio = 1：手柄完全按下，夹爪闭合
/// </summary>
[DefaultExecutionOrder(150)]
public sealed class ViveGripperPublisher : MonoBehaviour
{
    public PiperControlMode controls;
    [Header("ROS2")]
    [SerializeField]
    private string topicName = "/unity/gripper_ratio";

    [Header("VIVE 右手柄输入")]
    [SerializeField]
    private string gripBindingPath =
        "<XRController>{RightHand}/grip";

    [Tooltip("部分设备的 Grip 只有 0 和 1；这仍然可以用于开/关控制。")]
    [SerializeField]
    private bool invertGrip = false;

    [Header("发布设置")]
    [Range(1f, 60f)]
    [SerializeField]
    private float publishRateHz = 30f;

    [Range(0f, 0.2f)]
    [SerializeField]
    private float inputDeadzone = 0.02f;

    [SerializeField]
    private bool showDebugLog = true;

    private ROSConnection ros;
    private InputAction gripAction;

    private float nextPublishTime;
    private float nextLogTime;

    private void Awake()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<Float32Msg>(topicName,queue_size:1,latch:false);

        // 直接创建一个 Value 类型 Input Action。
        gripAction = new InputAction(
            name: "ViveRightGrip",
            type: InputActionType.Value,
            binding: gripBindingPath
        );
    }

    private void OnEnable()
    {
        gripAction?.Enable();
    }

    private void OnDisable()
    {
        gripAction?.Disable();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextPublishTime)
        {
            return;
        }

        nextPublishTime =
            Time.unscaledTime + 1f / Mathf.Max(1f, publishRateHz);

        float rawGrip = gripAction.ReadValue<float>();
        rawGrip = Mathf.Clamp01(rawGrip);

        // 消除手柄松开时的微小输入噪声。
        float ratio;
        if (rawGrip <= inputDeadzone)
        {
            ratio = 0f;
        }
        else
        {
            ratio = Mathf.InverseLerp(
                inputDeadzone,
                1f,
                rawGrip
            );
        }

        if (invertGrip)
        {
            ratio = 1f - ratio;
        }

        if(controls&&!controls.TryGripper(ratio,out ratio))return;
        ros.Publish(
            topicName,
            new Float32Msg(ratio)
        );

        if (showDebugLog && Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + 0.5f;
            Debug.Log(
                $"[VIVE Gripper] Grip ratio = {ratio:F3}"
            );
        }
    }

    private void OnDestroy()
    {
        gripAction?.Dispose();
    }
}
