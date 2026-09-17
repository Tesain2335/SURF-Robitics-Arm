#!/usr/bin/env python3
"""Unity -> Piper commands with independent, monotonic input watchdogs.

Defaults to dry-run. A watchdog suppresses new commands; it does not cancel a
target already accepted by the robot or implement an emergency stop.
"""
import math
import time

import rclpy
from rclpy.clock import Clock, ClockType
from rclpy.node import Node
from sensor_msgs.msg import JointState
from std_msgs.msg import Float32


ARM_NAMES = tuple(f"joint{i}" for i in range(1, 7))
# User-confirmed 100 mm gripper. Force uses the installed ROS wrapper's N API.
GRIPPER_OPEN_M = 0.1
GRIPPER_CLOSED_M = 0.0
GRIPPER_OPEN_FORCE_N = 0.5
GRIPPER_CLOSE_FORCE_N = 3.0


class InputGuard:
    """Pure input/state logic, testable without creating a ROS node."""

    def __init__(self, timeout=0.5, now=time.monotonic):
        if not math.isfinite(timeout) or timeout <= 0:
            raise ValueError("input_timeout must be finite and greater than zero")
        self.timeout = timeout
        self.now = now
        self.arm = None
        self.arm_received = None
        self.gripper = None
        self.gripper_received = None
        self.gripper_changed = None
        self.debounce_time = 0.2

    def _fresh(self, received, now):
        return received is not None and 0 <= now - received < self.timeout

    def _clear_gripper(self):
        self.gripper = None
        self.gripper_received = None
        self.gripper_changed = None

    def accept_arm(self, names, positions):
        # Require precisely six distinct, expected names. Reorder explicitly;
        # accepting a partial arm would make the driver default missing axes to 0.
        valid = (len(names) == 6 and len(positions) == 6
                 and len(set(names)) == 6 and set(names) == set(ARM_NAMES)
                 and all(math.isfinite(value) for value in positions))
        if not valid:
            self.arm = None
            self.arm_received = None
            return False
        by_name = dict(zip(names, positions))
        self.arm = [by_name[name] for name in ARM_NAMES]
        self.arm_received = self.now()
        return True

    def accept_gripper(self, ratio):
        now = self.now()
        if not math.isfinite(ratio) or not 0.0 <= ratio <= 1.0:
            self._clear_gripper()
            return False
        if not self._fresh(self.gripper_received, now):
            self._clear_gripper()
        # Every valid sample refreshes liveness, including repeated values and
        # samples in the hysteresis band. No implicit open command at startup.
        self.gripper_received = now
        target = "closed" if ratio > 0.75 else "open" if ratio < 0.25 else None
        if target is None or target == self.gripper:
            return True
        if (self.gripper_changed is not None
                and now - self.gripper_changed < self.debounce_time):
            return True
        self.gripper = target
        self.gripper_changed = now
        return True

    def command(self):
        now = self.now()
        names, positions, efforts = [], [], []
        if self._fresh(self.arm_received, now):
            names.extend(ARM_NAMES)
            positions.extend(self.arm)
            efforts.extend([0.0] * 6)
        else:
            self.arm = None
            self.arm_received = None
        if self._fresh(self.gripper_received, now):
            if self.gripper is not None:
                names.append("gripper")
                closed = self.gripper == "closed"
                positions.append(GRIPPER_CLOSED_M if closed else GRIPPER_OPEN_M)
                efforts.append(GRIPPER_CLOSE_FORCE_N if closed else GRIPPER_OPEN_FORCE_N)
        else:
            self._clear_gripper()
        return names, positions, efforts


class UnityPiperBridge(Node):
    def __init__(self):
        super().__init__("unity_piper_bridge")
        self.declare_parameter("input_timeout", 0.5)
        self.declare_parameter("dry_run", True)
        self.guard = InputGuard(float(self.get_parameter("input_timeout").value))
        self.dry_run = bool(self.get_parameter("dry_run").value)
        self.last_channels = ()
        self.last_warning = {}
        self.last_dry_log = float("-inf")
        self.arm_sub = self.create_subscription(
            JointState, "/unity_joint_angles", self.arm_callback, 1)
        self.gripper_sub = self.create_subscription(
            Float32, "/unity/gripper_ratio", self.gripper_callback, 1)
        # Dry-run does not even create a publisher on the hardware command topic.
        self.command_pub = None if self.dry_run else self.create_publisher(
            JointState, "/control/joint_states", 1)
        self.watchdog_clock = Clock(clock_type=ClockType.STEADY_TIME)
        self.timer = self.create_timer(
            1.0 / 50.0, self.publish_command, clock=self.watchdog_clock)
        self.get_logger().info(
            f"Unity Piper Bridge {'DRY-RUN' if self.dry_run else 'LIVE'}; "
            f"input timeout={self.guard.timeout:.3f}s; "
            f"gripper open={GRIPPER_OPEN_M}m/{GRIPPER_OPEN_FORCE_N}N, "
            f"closed={GRIPPER_CLOSED_M}m/{GRIPPER_CLOSE_FORCE_N}N; waiting for valid inputs")

    def _warn_rejected(self, channel):
        now = time.monotonic()
        if now - self.last_warning.get(channel, float("-inf")) >= 1.0:
            self.get_logger().warning(
                f"Rejected invalid {channel} input; cached command invalidated")
            self.last_warning[channel] = now

    def arm_callback(self, msg):
        if not self.guard.accept_arm(msg.name, msg.position):
            self._warn_rejected("arm")

    def gripper_callback(self, msg):
        if not self.guard.accept_gripper(msg.data):
            self._warn_rejected("gripper")

    def publish_command(self):
        names, positions, efforts = self.guard.command()
        channels = tuple(names)
        if channels != self.last_channels:
            self.get_logger().info(
                "Eligible command joints: " + (", ".join(names) or "none")
                + "; absent/stale/invalid inputs are not commanded")
            self.last_channels = channels
        if not names:
            return
        if self.dry_run:
            now = time.monotonic()
            if now - self.last_dry_log >= 1.0:
                self.get_logger().info(f"[DRY-RUN] {dict(zip(names, positions))}")
                self.last_dry_log = now
            return
        msg = JointState()
        msg.header.stamp = self.get_clock().now().to_msg()
        msg.name = names
        msg.position = positions
        msg.velocity = []
        msg.effort = efforts
        self.command_pub.publish(msg)


def main(args=None):
    rclpy.init(args=args)
    node = None
    try:
        node = UnityPiperBridge()
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        if node is not None:
            node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == "__main__":
    main()
