# Components and reproducibility

- Unity project: current working Assets/Packages/ProjectSettings, Unity 6000.4.11f1. Project originally based on repository Tesain2335/My-project-2, commit 28396de.
- ROS-TCP-Connector: Unity-Technologies/ROS-TCP-Connector, c27f00c6cf750d2d0564349b3039d19aa3925e7c (resolved Git package hash).
- URDF-Importer: Unity-Technologies/URDF-Importer, 90f353e4352aae4df52fa2c05e49b804631d2a63 (resolved Git package hash).
- agx_arm_ros: https://github.com/agilexrobotics/agx_arm_ros commit c73d33f2ab377447261423f1b881bd89c6663627. Local display.rviz edits and piper_servo_config.yaml/servo_demo.launch.py are preserved. See vendored LICENSE and individual package licenses.
- agx_arm_urdf: https://github.com/agilexrobotics/agx_arm_urdf commit f6642ce0d7872c686f29c99e9e10cd23d1d49313. Meshes/description present in the actual workspace are vendored; not Git submodules. Preserve LICENSE.
- ROS-TCP-Endpoint: Unity-Technologies/ROS-TCP-Endpoint, installed source snapshot package version 0.7.0, Apache-2.0. This installation came from an archive without .git; no commit SHA is asserted. Exact files are recorded in ros-source-manifest.json.
- Python SDK dependency pyAgxArm 1.0.0, installed from PyPI; upstream metadata declares LGPL-3.0-only. python-can 4.6.1 is installed as a dependency. No SDK binary is copied into this repository.
- Temperature UI adapted from xxStevexxx/Unity-display-for-SURF Temperature-Warning-Display commit a298c2d6917f8f496cd31cbe677d549450e8b545. Authorship reference preserved in scripts. Keyboard control follows grasp-physics key conventions but uses this project's Transform/IK control path.

The source snapshot manifest records SHA256 per vendored ROS file. Ubuntu apt dependency resolution is not an OS-image lock; install time updates may differ. ROS and Unity machine-generated cache/build files, personal configs, logs, videos, research documents and offline comparison experiment are excluded.
