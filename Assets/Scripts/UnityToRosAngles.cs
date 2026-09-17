using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor; // 引入 ROS 的传感器消息库

public class UnityToRosAngles : MonoBehaviour
{
    ROSConnection ros;
    public string topicName = "unity_joint_angles";

    [Header("手动控制机械臂关节 (单位: 弧度)")]
    [Range(-3.14f, 3.14f)] public float joint1 = 0f;
    [Range(-3.14f, 3.14f)] public float joint2 = 0f;
    [Range(-3.14f, 3.14f)] public float joint3 = 0f;
    [Range(-3.14f, 3.14f)] public float joint4 = 0f;
    [Range(-3.14f, 3.14f)] public float joint5 = 0f;
    [Range(-3.14f, 3.14f)] public float joint6 = 0f;

    void Start()
    {
        // 获取 ROS 通信大门
        ros = ROSConnection.GetOrCreateInstance();
        // 注册发送频道
        ros.RegisterPublisher<JointStateMsg>(topicName);
    }

    void Update()
    {
        // 每一帧都把滑块的数据打包成 ROS 看得懂的格式
        JointStateMsg msg = new JointStateMsg();
        
        // C# 的 float 需要转成 ROS 的 double
        msg.position = new double[] { joint1, joint2, joint3, joint4, joint5, joint6 };
        
        // 发送！
        ros.Publish(topicName, msg);
    }
}