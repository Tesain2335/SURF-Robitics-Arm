#!/usr/bin/env bash
# Run inside Ubuntu 22.04 after ROS 2 Humble has been installed. Does not enable hardware.
set -euo pipefail
source /etc/os-release
[[ "$ID" == ubuntu && "$VERSION_ID" == 22.04 ]] || { echo 'Requires Ubuntu 22.04 / ROS 2 Humble'; exit 1; }
test -f /opt/ros/humble/setup.bash || { echo 'Install ROS 2 Humble desktop first, then rerun.'; exit 1; }
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
surf_ws="${1:-$HOME/surf_ws}"
[[ "$surf_ws" = /* ]] || { echo 'Workspace must be an absolute Linux path'; exit 1; }
if [[ -e "$surf_ws/src" ]]; then
  echo "Refusing to overwrite an existing workspace: $surf_ws. Choose a NEW absolute path or update it manually."
  exit 1
fi
sudo apt-get update
sudo apt-get install -y python3-pip python3-colcon-common-extensions python3-rosdep can-utils ros-humble-rmw-cyclonedds-cpp ros-humble-moveit
python3 -m pip install --user 'pyAgxArm==1.0.0' 'python-can==4.6.1'
if [[ ! -f /etc/ros/rosdep/sources.list.d/20-default.list ]]; then sudo rosdep init; fi
rosdep update --rosdistro humble
mkdir -p "$surf_ws/src" "$surf_ws/piper_launcher"
cp -a "$repo/ros/src/." "$surf_ws/src/"
cp "$repo/ros/unity_piper_bridge.py" "$surf_ws/"
cp "$repo/launcher/backend/"*.py "$repo/launcher/backend/"*.sh "$surf_ws/piper_launcher/"
chmod +x "$surf_ws/piper_launcher/"*.sh
set +u
source /opt/ros/humble/setup.bash
set -u
cd "$surf_ws"
rosdep install --from-paths src --ignore-src -r -y --rosdistro humble
colcon build --symlink-install
echo "Installed $surf_ws. No USB attached, motors enabled, or ROS services launched."
