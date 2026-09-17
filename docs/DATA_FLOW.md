# Runtime data flow

VR tracked local position -> relative target -> PiperKinematics position IK -> IkToRosPublisher
-> /unity_joint_angles (sensor_msgs/JointState; six named joints; radians)
-> ROS TCP endpoint -> unity_piper_bridge.py
-> /control/joint_states -> agx_arm_ctrl -> pyAgxArm -> CAN/Piper.

Piper driver -> /feedback/joint_states -> Unity initial pose alignment / feedback freshness guard.
Grip or keyboard O/P -> ViveGripperPublisher -> /unity/gripper_ratio (std_msgs/Float32)
-> same bridge -> named gripper control.

Published target settings: Unity joints 60Hz, gripper 30Hz, bridge 50Hz, feedback/input freshness 0.5s.
These are configured rates, not measured end-to-end latency. ROS uses CycloneDDS, localhost-only, domain 0;
Unity connects to WSL TCP port 10000 through the launcher-provided IP. Permit only the intended local machine/network access.

Keyboard/VR share one control mode; wrist J5/J6 are joystick jog axes with original URDF limits.
Position IK does not constrain full world-space wrist orientation. Actual joint feedback is used for startup alignment
and validity checks; the visual model is not a complete independent calibrated physical-state digital twin.

The launcher starts software in dry-run by default. Explicit live confirmation starts/inspects/enables Piper.
Temperature simulation is display-only. Hardware cannot be substituted by synthetic feedback in a real experiment.
