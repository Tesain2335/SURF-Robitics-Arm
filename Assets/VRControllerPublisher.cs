using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

public class VRControllerPublisher : MonoBehaviour
{
    private ROSConnection ros;
    public string topicName = "target_arm_pose"; 

    [Header("追踪目标（VR右手柄）")]
    public Transform targetTransform;

    [Header("运动缩放比例")]
    public float scaleFactor = 0.3f; // 强烈建议保持 0.3，实现微操

    [Header("机械臂基准待机坐标")]
    // 这是 AgileX Piper 在正前方的一个安全、舒展的物理坐标
    public Vector3 robotHomePosition = new Vector3(0.2f, 0.0f, 0.2f);

    // 相对位移算法核心变量
    private Vector3 initialControllerPos;
    private bool isTracking = false;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PoseMsg>(topicName);
        if (targetTransform == null) targetTransform = this.transform;
    }

    void Update()
    {
        // 【核心离合器机制】按下空格键，记录当前手柄位置作为"零点"，并开启跟随
        // 后续我们将把这个键绑定到 VR 手柄的侧边扳机 (Grip) 上
        if (Input.GetKeyDown(KeyCode.Space))
        {
            initialControllerPos = targetTransform.position;
            isTracking = true;
            Debug.Log("🟢 【离合器激活】已重置手柄零点，机械臂开始跟随！");
        }

        // 松开空格键（或按其他键），停止发送，机械臂原地待命
        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            isTracking = false;
            Debug.Log("🔴 【离合器断开】机械臂已锁定待命。");
        }

        // 每 10 帧发送一次，防止数据堵塞
        if (isTracking && Time.frameCount % 10 == 0)
        {
            PublishPose();
        }
    }

    void PublishPose()
    {
        PoseMsg msg = new PoseMsg();

        // 1. 计算手柄相对于你按下空格键那一瞬间的【物理位移量 Delta】
        Vector3 deltaPos = targetTransform.position - initialControllerPos;

        // 2. 坐标系直觉映射 (Unity 左手系 -> ROS 右手系) + 比例缩放
        float deltaRosX = deltaPos.z * scaleFactor;  // 往前推
        float deltaRosY = -deltaPos.x * scaleFactor; // 往左移
        float deltaRosZ = deltaPos.y * scaleFactor;  // 往上抬

        // 3. 将你的相对位移，叠加到机械臂的【安全待机坐标】上
        msg.position.x = robotHomePosition.x + deltaRosX;
        msg.position.y = robotHomePosition.y + deltaRosY;
        msg.position.z = robotHomePosition.z + deltaRosZ;

        // 4. 【关键修复】强行锁定机械臂夹爪姿态，使其始终朝前，防止奇异点扭曲
        // 我们发送一个标准的四元数，代表不产生偏转
        msg.orientation.x = 0;
        msg.orientation.y = 0;
        msg.orientation.z = 0;
        msg.orientation.w = 1;

        ros.Publish(topicName, msg);
    }
}