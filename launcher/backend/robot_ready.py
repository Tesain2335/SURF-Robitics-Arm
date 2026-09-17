"""Only invoked after explicit LIVE button confirmation, never in develop mode."""
import math
import json
import sys
import time
import rclpy
from sensor_msgs.msg import JointState
from std_srvs.srv import SetBool

rclpy.init()
node = rclpy.create_node('surf_launcher_readiness')
feedback = []
positions = {}


def received(msg):
    data = dict(zip(msg.name, msg.position))
    if all(f'joint{i}' in data and math.isfinite(data[f'joint{i}']) for i in range(1, 7)):
        feedback[:] = [time.monotonic()]
        positions.clear()
        positions.update(data)


def service(name):
    client = node.create_client(SetBool, name)
    if not client.wait_for_service(timeout_sec=5):
        raise RuntimeError(name + ' 服务不可用')
    request = SetBool.Request()
    request.data = True
    future = client.call_async(request)
    rclpy.spin_until_future_complete(node, future, timeout_sec=18)
    if not future.done() or future.result() is None or not future.result().success:
        raise RuntimeError(name + ' 未成功；请检查示教状态、供电和驱动日志')


def wait_new_feedback(timeout=3.0):
    """Require a callback after the service, allowing publishing to resume."""
    previous = feedback[-1] if feedback else None
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        rclpy.spin_once(node, timeout_sec=0.1)
        if feedback and feedback[-1] != previous and time.monotonic() - feedback[-1] < 1:
            return
    raise RuntimeError('使能后 3 秒内未恢复有效六轴反馈，未打开控制入口；请查看驱动日志')


try:
    subscription = node.create_subscription(JointState, '/feedback/joint_states', received, 1)
    deadline = time.monotonic() + 18
    while not feedback and time.monotonic() < deadline:
        rclpy.spin_once(node, timeout_sec=0.2)
    if not feedback:
        raise RuntimeError('未收到六轴真实反馈，未使能机械臂')
    if '--check-only' in sys.argv:
        print(json.dumps({'feedback': True, 'positions': positions, 'enable_requested': False}))
    else:
        service('/enable_agx_arm')
        wait_new_feedback()
        service('/control_enable')
        print('Real feedback and enable service confirmed')
finally:
    node.destroy_node()
    rclpy.shutdown()
