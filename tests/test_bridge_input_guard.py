"""Offline tests: actual ROS message classes, no ROS init, DDS or hardware."""
import importlib.util
import os
from pathlib import Path
from types import SimpleNamespace
import unittest
from unittest.mock import Mock

source = Path(os.environ.get('BRIDGE_UNDER_TEST',
              str(Path(__file__).resolve().parents[1] / 'bridge/unity_piper_bridge.py')))
spec = importlib.util.spec_from_file_location('bridge_under_test', source)
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)


class InputProtectionTests(unittest.TestCase):
    def setUp(self):
        self.time = 10.0
        self.guard = bridge.InputGuard(now=lambda: self.time)
        self.names = list(bridge.ARM_NAMES)
        self.angles = [0.1, 0.2, -0.3, 0.4, 0.5, 0.6]

    def arm(self):
        self.assertTrue(self.guard.accept_arm(self.names, self.angles))

    def test_startup_never_commands_defaults(self):
        for _ in range(100):
            self.time += 0.1
            self.assertEqual(self.guard.command(), ([], [], []))

    def test_arm_only_does_not_open_gripper(self):
        self.arm()
        self.assertEqual(self.guard.command(), (self.names, self.angles, [0.0] * 6))

    def test_gripper_only_does_not_zero_arm(self):
        self.guard.accept_gripper(1.0)
        self.assertEqual(self.guard.command(), (['gripper'], [0.0], [3.0]))

    def test_named_angles_reordered(self):
        self.guard.accept_arm(self.names[::-1], self.angles[::-1])
        self.assertEqual(self.guard.command()[1], self.angles)

    def test_full_open_close_cycle_has_state_specific_force(self):
        for ratio, width, force in [(0.0, 0.1, 0.5), (1.0, 0.0, 3.0), (0.0, 0.1, 0.5)]:
            self.time += 0.3
            self.guard.accept_gripper(ratio)
            self.assertEqual(self.guard.command(), (['gripper'], [width], [force]))

    def test_partial_duplicate_unknown_and_unnamed_rejected(self):
        cases = [([], self.angles), (self.names[:-1], self.angles[:-1]),
                 (self.names, self.angles[:-1]),
                 (self.names[:-1] + ['joint1'], self.angles),
                 (self.names[:-1] + ['wrong'], self.angles),
                 (self.names + ['gripper'], self.angles + [0.05])]
        for names, positions in cases:
            with self.subTest(names=names, positions=positions):
                self.arm()
                self.assertFalse(self.guard.accept_arm(names, positions))
                self.assertEqual(self.guard.command()[0], [])

    def test_nonfinite_arm_invalidates_cache(self):
        for value in [float('nan'), float('inf'), float('-inf')]:
            self.arm()
            self.assertFalse(self.guard.accept_arm(self.names, [value] * 6))
            self.assertEqual(self.guard.command()[0], [])

    def test_timeout_at_boundary_and_recovery(self):
        self.arm()
        self.time = 10.499
        self.assertEqual(self.guard.command()[0], self.names)
        self.time = 10.5
        self.assertEqual(self.guard.command()[0], [])
        self.arm()
        self.assertEqual(self.guard.command()[1], self.angles)

    def test_gripper_traffic_cannot_keep_arm_alive(self):
        self.arm()
        for _ in range(6):
            self.time += 0.1
            self.guard.accept_gripper(0.0)
        self.assertEqual(self.guard.command()[0], ['gripper'])

    def test_arm_traffic_cannot_keep_gripper_alive(self):
        self.guard.accept_gripper(0.0)
        for _ in range(6):
            self.time += 0.1
            self.arm()
        self.assertEqual(self.guard.command()[0], self.names)

    def test_identical_grip_samples_refresh_timeout(self):
        for _ in range(25):
            self.time += 0.1
            self.guard.accept_gripper(1.0)
            self.assertEqual(self.guard.command()[1], [0.0])

    def test_hysteresis_initial_band_never_assumes_open(self):
        for value in [0.25, 0.5, 0.75]:
            self.guard.accept_gripper(value)
            self.assertEqual(self.guard.command()[0], [])

    def test_hysteresis_band_preserves_live_state(self):
        self.guard.accept_gripper(1.0)
        for _ in range(10):
            self.time += 0.1
            self.guard.accept_gripper(0.5)
        self.assertEqual(self.guard.command()[1], [0.0])

    def test_debounce_retained(self):
        self.guard.accept_gripper(0.0)
        self.time += 0.1
        self.guard.accept_gripper(1.0)
        self.assertEqual(self.guard.command()[1], [0.1])
        self.time += 0.11
        self.guard.accept_gripper(1.0)
        self.assertEqual(self.guard.command()[1], [0.0])

    def test_invalid_grip_invalidates_cache(self):
        for value in [float('nan'), float('inf'), float('-inf'), -0.1, 1.1]:
            self.guard.accept_gripper(0.0)
            self.assertFalse(self.guard.accept_gripper(value))
            self.assertEqual(self.guard.command()[0], [])

    def test_stale_grip_cannot_revive_in_middle_band(self):
        self.guard.accept_gripper(1.0)
        self.time += 1.0
        # Includes a callback arriving before the next watchdog tick.
        self.guard.accept_gripper(0.5)
        self.assertEqual(self.guard.command()[0], [])
        self.guard.accept_gripper(0.0)
        self.assertEqual(self.guard.command()[1], [0.1])

    def test_all_inputs_expire(self):
        self.arm()
        self.guard.accept_gripper(1.0)
        self.time += 0.6
        self.assertEqual(self.guard.command(), ([], [], []))

    def test_invalid_channel_does_not_remove_other_valid_channel(self):
        self.arm()
        self.guard.accept_gripper(1.0)
        self.guard.accept_arm([], [])
        self.assertEqual(self.guard.command()[0], ['gripper'])

    def test_invalid_timeout_rejected(self):
        for value in [0, -1, float('nan'), float('inf')]:
            with self.assertRaises(ValueError):
                bridge.InputGuard(timeout=value)

    def test_message_output_and_watchdog_no_network(self):
        # Call production callbacks and output adapter with a fake publisher.
        # No rclpy.init() or Node construction occurs anywhere in this suite.
        node = SimpleNamespace(
            guard=self.guard, dry_run=False, last_channels=(),
            command_pub=Mock(), get_logger=Mock(return_value=Mock()),
            get_clock=Mock(return_value=SimpleNamespace(now=lambda: SimpleNamespace(
                to_msg=lambda: bridge.JointState().header.stamp))),
            _warn_rejected=Mock(), last_dry_log=float('-inf'))
        bridge.UnityPiperBridge.publish_command(node)
        node.command_pub.publish.assert_not_called()
        arm = bridge.JointState()
        arm.name = self.names[::-1]
        arm.position = self.angles[::-1]
        grip = bridge.Float32()
        grip.data = 1.0
        bridge.UnityPiperBridge.arm_callback(node, arm)
        bridge.UnityPiperBridge.gripper_callback(node, grip)
        bridge.UnityPiperBridge.publish_command(node)
        msg = node.command_pub.publish.call_args.args[0]
        self.assertEqual(list(msg.name), self.names + ['gripper'])
        self.assertEqual(list(msg.position), self.angles + [0.0])
        self.assertEqual(list(msg.effort), [0.0] * 6 + [3.0])
        self.time += 1.0
        bridge.UnityPiperBridge.publish_command(node)
        self.assertEqual(node.command_pub.publish.call_count, 1)
        self.arm()
        node.dry_run = True
        bridge.UnityPiperBridge.publish_command(node)
        self.assertEqual(node.command_pub.publish.call_count, 1)


if __name__ == '__main__':
    unittest.main(verbosity=2)
