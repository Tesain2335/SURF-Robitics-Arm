# Release validation (2026-09-17)

- Windows launcher rebuilt from the included Launcher.cs with .NET Framework csc.
- PowerShell installer parsed without syntax errors; all shell entry/install/CAN scripts passed bash -n.
- Actual ROS message classes: 20 bridge protection unit tests passed, without ROS initialization or hardware commands.
- New temporary Ubuntu workspace: all five vendored ROS packages compiled using colcon (agx_arm_msgs, agx_arm_description, ros_tcp_endpoint, agx_arm_ctrl, agx_arm_moveit). ROS-TCP-Endpoint emitted an upstream setuptools script-dir deprecation warning.
- Unity 6000.4.11f1 fresh import passed: OutdoorsScene has no missing scripts, exactly one IK solver and temperature overlay, and URDF FK/IK validation passed. Unity did not enter Play or issue hardware commands.
- Second physical PC, headset setup and real hardware motion were not exercised for this publication. Initial apt/rosdep/pip installation on a clean OS is not equivalent to the successful local fresh-workspace build.

To rerun bridge protection tests inside Ubuntu after setup:

```bash
source /opt/ros/humble/setup.bash
BRIDGE_UNDER_TEST="$PWD/ros/unity_piper_bridge.py" python3 tests/test_bridge_input_guard.py
```

To check the imported Unity project without Play: SURF > Validate portable package (offline).
The result is saved locally to Library/surf-release-validation.txt. This checks scene script references and FK/IK, not a hardware safety certification.

Latency CSV helper: `tools/analyze_teleop_latency.py PATH_TO_CSV` estimates small move-and-hold response/arrival from the Unity local-clock target and real-feedback FK logger. Its 3 synthetic analysis tests pass (`python tests/test_latency_analysis.py`). This is not the excluded standalone A/B experiment. Unity latency files live under Application.persistentDataPath/SURF-latency. Output includes feedback return delay and excludes headset-internal tracking latency; link6 origin is not a calibrated tool tip.

Full independent FK fixture generation is available in `tools/generate_kinematics_cases.py` (requires numpy). Run it from Python, then select SURF > Test kinematics offline in Unity. Fixtures/results go into Library, never into public raw experiment data.
