from launch import LaunchDescription
from launch_ros.actions import Node
import os
from ament_index_python.packages import get_package_share_directory

def generate_launch_description():
    # 自动查找配置文件路径
    config = os.path.join(
        get_package_share_directory('agx_arm_ros'),
        'config',
        'piper_servo_config.yaml'
    )

    return LaunchDescription([
        # 1. 启动核心控制节点 (使用 ROS2 MoveIt Servo 组件)
        Node(
            package='moveit_servo',
            executable='servo_node',
            name='servo_server',
            output='screen',
            parameters=[config],
            remappings=[
                ('/joint_states', '/control/joint_states'),
                ('/status', '/servo_status')
            ]
        ),
        
        # 2. 启动我们自定义的 Python 零延迟桥接脚本
        # 确保 unity_servo.py 已经在 colcon_ws 根目录下
        Node(
            package='agx_arm_ros', 
            executable='unity_servo.py',
            output='screen'
        )
    ])
